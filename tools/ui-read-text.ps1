# Reads the real text of the path box / list cell through UI Automation (no pixels involved).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$proc = Start-Process -FilePath "$PSScriptRoot\..\src\CDiskCleaner\bin\Debug\net48\CDiskCleaner.exe" -PassThru
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
function Find-ByType($r, [string]$type) {
    $prop = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
    $val = [System.Windows.Automation.ControlType]::$type
    $c = [System.Windows.Automation.PropertyCondition]::new($prop, $val)
    return $r.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)
}

# run a quick scan first so the details pane exists
$chk = Find-ById $main 'FullScanCheck'
if ($chk) { $chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
$start = Find-ById $main 'StartScanButton'
$start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    $r = Find-ById $main 'RecycleButton'
    if ($r -and $r.Current.IsEnabled) { break }
    Start-Sleep -Milliseconds 500
}
Write-Host "scan done"

# every edit / text box in the window
$edits = Find-ByType $main ([System.Windows.Automation.ControlType]::Edit)
Write-Host "edit controls: $($edits.Count)"
for ($i = 0; $i -lt $edits.Count; $i++) {
    $e = $edits[$i]
    $val = ''
    try { $val = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { $val = '<no value pattern>' }
    Write-Host ("  edit[$i] name='" + $e.Current.Name + "' value='" + $val + "'")
}

# the read only text of the window (titles, paths, reasons)
$texts = Find-ByType $main ([System.Windows.Automation.ControlType]::Text)
Write-Host "text controls: $($texts.Count)"
$n = 0
for ($i = 0; $i -lt $texts.Count -and $n -lt 40; $i++) {
    $t = $texts[$i].Current.Name
    if ($t -and $t.Length -gt 12) { Write-Host ("  text: " + $t); $n++ }
}

Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
