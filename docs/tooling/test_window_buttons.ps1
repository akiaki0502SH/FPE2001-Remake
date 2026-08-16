param([Parameter(Mandatory = $true)][string]$Exe)

Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WindowButtonTestNative {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
}
"@

function Get-Button([System.Windows.Automation.AutomationElement]$Root, [string]$Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)))
    $button = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $button) { throw "Button not found: $Name" }
    return $button
}

function Invoke-Button([System.Windows.Automation.AutomationElement]$Button) {
    $pattern = $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 500
}

$process = Start-Process -FilePath $Exe -PassThru
try {
    $process.WaitForInputIdle(5000) | Out-Null
    Start-Sleep -Seconds 1
    $process.Refresh()
    if ($process.MainWindowHandle -eq 0) { throw "Main window handle unavailable" }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

    $maximize = Get-Button $root '最大化或还原窗口'
    Invoke-Button $maximize
    $process.Refresh()
    if ($process.HasExited) { throw "Process exited during maximize" }
    Write-Output 'maximize: passed'

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $maximize = Get-Button $root '最大化或还原窗口'
    Invoke-Button $maximize
    Write-Output 'restore: passed'

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $minimize = Get-Button $root '最小化窗口'
    Invoke-Button $minimize
    $process.Refresh()
    if ($process.HasExited) { throw "Process exited during minimize" }
    Write-Output 'minimize: passed'

    [WindowButtonTestNative]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    Start-Sleep -Milliseconds 600
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $close = Get-Button $root '关闭窗口'
    Invoke-Button $close
    if (-not $process.WaitForExit(5000)) { throw "Process did not exit after close" }
    Write-Output 'close: passed'
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
