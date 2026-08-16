$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$jobs = @(
    @{
        Input = Join-Path $root 'deliverables\FPE2001-Remake_Win11重构分析与架构设计_v2.1.docx'
        Output = Join-Path $root 'rendered_docx\v2.1\architecture\FPE2001-Remake_Win11重构分析与架构设计_v2.1.pdf'
    },
    @{
        Input = Join-Path $root 'deliverables\FPE2001-Remake_代码实施规格包_v2.1.docx'
        Output = Join-Path $root 'rendered_docx\v2.1\code\FPE2001-Remake_代码实施规格包_v2.1.pdf'
    },
    @{
        Input = Join-Path $root 'deliverables\FPE2001-Remake_UIUX实施规格_v2.1.docx'
        Output = Join-Path $root 'rendered_docx\v2.1\uiux\FPE2001-Remake_UIUX实施规格_v2.1.pdf'
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
