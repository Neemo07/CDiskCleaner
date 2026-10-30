# Measures the exact geometry of the sidebar elements while the scan state changes.
param([string]$Exe = "$PSScriptRoot\..\src\CDiskCleaner\bin\Debug\net48\CDiskCleaner.exe")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 5

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$main = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

function Find-ById($r, [string]$id) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Report([string]$label) {
    $b = Find-ById $main 'StartScanButton'
    $c = Find-ById $main 'FullScanCheck'
    $btn = if ($b) { $b.Current.BoundingRectangle } else { $null }
    $chk = if ($c) { $c.Current.BoundingRectangle } else { $null }
    $btnTxt = if ($b) { "x=$([int]$btn.X) w=$([int]$btn.Width)" } else { 'n/a' }
    $chkTxt = if ($c) { "x=$([int]$chk.X) w=$([int]$chk.Width)" } else { 'n/a' }
    Write-Host ("{0,-22} startButton {1}   fullScanCheck {2}" -f $label, $btnTxt, $chkTxt)
}

Report 'initial'
$chk = Find-ById $main 'FullScanCheck'
$chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Seconds 1
Report 'quick mode'
$start = Find-ById $main 'StartScanButton'
$start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 800
Report 'scanning'
Start-Sleep -Seconds 4
Report 'scanning later'
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    $r = Find-ById $main 'RecycleButton'
    if ($r -and $r.Current.IsEnabled) { break }
    Start-Sleep -Milliseconds 500
}
Start-Sleep -Seconds 2
Report 'scan finished'
Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
