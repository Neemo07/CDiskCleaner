# Drives the built app through UI Automation and captures screenshots of the real states.
# ASCII only on purpose: PowerShell 5.1 reads BOM-less scripts as ANSI, so no Arabic literals here.
#   usage: powershell -ExecutionPolicy Bypass -File tools\ui-verify.ps1
#   every destructive button is CANCELLED, nothing is ever deleted by this script.

param(
    [string]$Exe = "$PSScriptRoot\..\src\CDiskCleaner\bin\Debug\net48\CDiskCleaner.exe",
    [string]$OutDir = "$PSScriptRoot\..\docs",
    [int]$ScanTimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Drawing;using System.Text;
public class Cap {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int c);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
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
 public static string Title(IntPtr h) {
   var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString();
 }
 public static string Shot(IntPtr h, string path) {
   RECT r; GetWindowRect(h, out r);
   int w = r.R-r.L, ht = r.B-r.T;
   if (w <= 0 || ht <= 0) return "invalid-rect " + w + "x" + ht;
   // IMPORTANT: PrintWindow returns a horizontally MIRRORED bitmap for right-to-left windows,
   // so the capture has to come from the screen instead.
   using (Bitmap bmp = new Bitmap(w, ht))
   using (Graphics g = Graphics.FromImage(bmp)) {
     g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, ht));
     bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
     return w + "x" + ht + " (screen grab)";
   }
 }
}
"@ -ReferencedAssemblies System.Drawing,System.Windows.Forms

function Find-ById($root, [string]$id, [int]$timeoutMs = 1000) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 150
    }
    return $null
}

function Invoke-Id($root, [string]$id) {
    $el = Find-ById $root $id 4000
    if (-not $el) { Write-Host "  ! element '$id' not found"; return $false }
    try {
        $p = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $p.Invoke()
        return $true
    } catch { Write-Host "  ! invoke '$id' failed: $($_.Exception.Message)"; return $false }
}

function Toggle-Id($root, [string]$id) {
    $el = Find-ById $root $id 4000
    if (-not $el) { Write-Host "  ! element '$id' not found"; return $false }
    try {
        $p = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $p.Toggle()
        return $true
    } catch { Write-Host "  ! toggle '$id' failed: $($_.Exception.Message)"; return $false }
}

# the cached Process.MainWindowHandle can point at a transient window, so always re-read it
function Get-MainHandle($proc) {
    $proc.Refresh()
    $handles = [Cap]::TopWindows($proc.Id)
    if ($handles.Count -gt 0) { return $handles[$handles.Count - 1] }
    return [IntPtr]$proc.MainWindowHandle
}

function Capture($handle, $file) {
    [Cap]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
    Start-Sleep -Milliseconds 600
    Write-Host ("  shot " + $file + ": " + [Cap]::Shot($handle, (Join-Path $OutDir $file)))
    [Cap]::SetWindowPos($handle, [IntPtr](-2), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 5

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$uiaMain = $null
for ($i = 0; $i -lt 30 -and -not $uiaMain; $i++) {
    $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
    if ($wins.Count -gt 0) { $uiaMain = $wins[0] } else { Start-Sleep -Milliseconds 300 }
}
if (-not $uiaMain) { throw "main window not exposed through UI Automation" }
Write-Host "main window found"

# 1) quick scan (full scan off) so the run stays short and read-only
Write-Host "step: switch to quick scan"
Toggle-Id $uiaMain 'FullScanCheck' | Out-Null
Write-Host "step: start scan"
Invoke-Id $uiaMain 'StartScanButton' | Out-Null

# 2) wait until an actionable item is selected (the recycle button exists and is enabled)
$deadline = (Get-Date).AddSeconds($ScanTimeoutSeconds)
$ready = $false
while ((Get-Date) -lt $deadline) {
    $recycle = Find-ById $uiaMain 'RecycleButton' 300
    if ($recycle -and $recycle.Current.IsEnabled) { $ready = $true; break }
    Start-Sleep -Milliseconds 500
}
Write-Host "actionable item ready: $ready"
Start-Sleep -Seconds 2

Capture (Get-MainHandle $proc) 'screenshot-scan-results.png'

# 3) details of the selected item + the confirmation dialog (always CANCELLED)
if ($ready) {
    Capture (Get-MainHandle $proc) 'screenshot-details.png'

    Write-Host "step: open confirm dialog (will cancel)"
    Invoke-Id $uiaMain 'RecycleButton' | Out-Null
    Start-Sleep -Seconds 2
    $mainHandle = Get-MainHandle $proc
    $dialog = $null
    foreach ($h in [Cap]::TopWindows($proc.Id)) {
        if ($h -ne $mainHandle) { $dialog = $h }
    }
    if ($dialog) {
        Write-Host ("dialog found: " + [Cap]::Title($dialog))
        Capture $dialog 'screenshot-confirm.png'
        try {
            $prop = [System.Windows.Automation.AutomationElement]::NativeWindowHandleProperty
            $cond = [System.Windows.Automation.PropertyCondition]::new($prop, [int]$dialog.ToInt32())
            $dlgRoot = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
            if ($dlgRoot) { Invoke-Id $dlgRoot 'CancelButton' | Out-Null }
            else { Write-Host "  ! dialog not reachable through UI Automation" }
        } catch {
            Write-Host "  ! could not cancel through UI Automation: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds 1
        # make sure the modal dialog is gone (nothing is ever confirmed)
        if ([Cap]::TopWindows($proc.Id).Count -gt 1) {
            [Cap]::SetWindowPos($dialog, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
            Start-Sleep -Milliseconds 300
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            Start-Sleep -Seconds 1
        }
    } else {
        Write-Host "  ! confirm dialog not found"
    }
}

# 4) switch to English / LTR and capture
Write-Host "step: switch language"
Invoke-Id $uiaMain 'LanguageButton' | Out-Null
Start-Sleep -Seconds 2
Capture (Get-MainHandle $proc) 'screenshot-main-en.png'

Write-Host "done"
Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
