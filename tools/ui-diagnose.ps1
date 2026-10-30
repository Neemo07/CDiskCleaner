# Diagnostic: watch the app while it scans (read-only, no clicking of destructive buttons).
param([int]$Seconds = 150)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class D {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$proc = Start-Process -FilePath "$PSScriptRoot\..\src\CDiskCleaner\bin\Debug\net48\CDiskCleaner.exe" -PassThru
Start-Sleep -Seconds 5
$proc.Refresh()
Write-Host "pid=$($proc.Id) handle=$($proc.MainWindowHandle) title='$($proc.MainWindowTitle)'"

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
Write-Host "top level windows: $($wins.Count)"
for ($i = 0; $i -lt $wins.Count; $i++) {
    Write-Host "  win[$i] name='$($wins[$i].CurrentName)' type=$($wins[$i].Current.ControlType.ProgrammaticName) hwnd=$($wins[$i].CurrentNativeWindowHandle)"
}

$main = $wins[0]
function Find-ById($root, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
$chk = Find-ById $main 'FullScanCheck'
if ($chk) {
    $chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Write-Host "full scan toggled"
}
$start = Find-ById $main 'StartScanButton'
$start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Host "scan started"

for ($i = 0; $i -lt $Seconds / 5; $i++) {
    Start-Sleep -Seconds 5
    $alive = $null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
    if (-not $alive) { Write-Host "t=$($i*5)s PROCESS GONE"; break }
    $proc.Refresh()
    $h = $proc.MainWindowHandle
    $r = New-Object D+RECT
    $ok = [D]::GetWindowRect($h, [ref]$r)
    $isw = [D]::IsWindow($h)
    $grid = Find-ById $main 'ResultsGrid'
    $rows = -1
    if ($grid) {
        $rowCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::DataItem)
        $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rowCond).Count
    }
    $recycle = Find-ById $main 'RecycleButton'
    $enabled = if ($recycle) { $recycle.Current.IsEnabled } else { 'no-button' }
    Write-Host "t=$($i*5)s alive=$alive isWindow=$isw rectOk=$ok size=$($r.R-$r.L)x$($r.B-$r.T) rows=$rows recycleEnabled=$enabled"
}
Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
