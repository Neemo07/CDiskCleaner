# Captures the window before / during / after a scan so the sidebar width can be compared.
param([string]$Exe = "$PSScriptRoot\..\src\CDiskCleaner\bin\Debug\net48\CDiskCleaner.exe",
      [string]$OutDir = "$PSScriptRoot\..\docs")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Drawing;using System.Text;
public class Cap2 {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 public struct RECT { public int L,T,R,B; }
 public static List<IntPtr> TopWindows(int pid) {
   var list = new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l) {
     uint p; GetWindowThreadProcessId(h, out p);
     if ((int)p == pid && IsWindowVisible(h)) list.Add(h);
     return true;
   }, IntPtr.Zero);
   return list;
 }
 public static string Shot(IntPtr h, string path) {
   RECT r; GetWindowRect(h, out r);
   int w = r.R-r.L, ht = r.B-r.T;
   using (Bitmap bmp = new Bitmap(w, ht))
   using (Graphics g = Graphics.FromImage(bmp)) {
     g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, ht));
     bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
     return w + "x" + ht;
   }
 }
}
"@ -ReferencedAssemblies System.Drawing,System.Windows.Forms

function Get-MainHandle($proc) {
    $proc.Refresh()
    $handles = [Cap2]::TopWindows($proc.Id)
    if ($handles.Count -gt 0) { return $handles[$handles.Count - 1] }
    return [IntPtr]$proc.MainWindowHandle
}
function Grab($proc, $file) {
    $h = Get-MainHandle $proc
    [Cap2]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
    Start-Sleep -Milliseconds 1200
    $null = [Cap2]::Shot($h, (Join-Path $OutDir $file))   # first pass can still catch another window
    Start-Sleep -Milliseconds 300
    $info = [Cap2]::Shot($h, (Join-Path $OutDir $file))
    [Cap2]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
    Write-Host ("  " + $file + ": " + $info)
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
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

Grab $proc 'shot-before-scan.png'
$chk = Find-ById $main 'FullScanCheck'
if ($chk) { $chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
$start = Find-ById $main 'StartScanButton'
$start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Host "scan pressed"
Start-Sleep -Milliseconds 900
Grab $proc 'shot-during-scan.png'
Start-Sleep -Seconds 6
Grab $proc 'shot-after-scan.png'
Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
