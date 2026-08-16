param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Output
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public struct CaptureRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class CaptureNative {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out CaptureRect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
}
"@
Add-Type -AssemblyName System.Drawing

$process = Start-Process -FilePath $Exe -PassThru
try {
    Start-Sleep -Seconds 3
    $shell = New-Object -ComObject WScript.Shell
    [void]$shell.AppActivate($process.Id)
    Start-Sleep -Milliseconds 300
    [CaptureNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [CaptureNative]::MoveWindow($process.MainWindowHandle, 60, 60, 1100, 720, $true) | Out-Null
    Start-Sleep -Milliseconds 500
    $rect = New-Object CaptureRect
    if (-not [CaptureNative]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
        throw "GetWindowRect failed"
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { throw "Invalid window bounds: ${width}x${height}" }
    # Capture a small area outside GetWindowRect so the DWM-owned shadow and
    # rounded-corner clipping can be inspected instead of cropping them away.
    $padding = 16
    $captureWidth = $width + ($padding * 2)
    $captureHeight = $height + ($padding * 2)
    $bitmap = New-Object System.Drawing.Bitmap($captureWidth, $captureHeight)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left - $padding, $rect.Top - $padding, 0, 0, $bitmap.Size)
    $bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Output "captured ${captureWidth}x${captureHeight} (window ${width}x${height}) -> $Output"
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
