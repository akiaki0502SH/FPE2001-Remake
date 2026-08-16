$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$jobs = @(
    @{
        Input = Join-Path $root 'deliverables\FPE_Next_代码实施规格包_v1.0.docx'
        Output = Join-Path $root 'rendered_docx\code_spec_final\FPE_Next_代码实施规格包_v1.0.pdf'
    },
    @{
        Input = Join-Path $root 'deliverables\FPE_Next_UIUX实施规格_v1.0.docx'
        Output = Join-Path $root 'rendered_docx\ui_spec_final\FPE_Next_UIUX实施规格_v1.0.pdf'
    }
)

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    foreach ($job in $jobs) {
        $outDir = Split-Path -Parent $job.Output
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $doc = $word.Documents.Open($job.Input, $false, $true)
        try {
            $doc.Repaginate()
            $doc.ExportAsFixedFormat($job.Output, 17)
        }
        finally {
            $doc.Close($false)
        }
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) | Out-Null
}
