# Builds the app and produces ONE distributable file: dist\CDiskCleaner.exe
#   usage: powershell -ExecutionPolicy Bypass -File tools\publish-single.ps1
# The app has no NuGet dependencies and all XAML is compiled into the assembly, so the exe
# is self contained. The .exe.config is intentionally NOT shipped: for a net48 assembly the
# framework already uses the new (non legacy) path handling, and the manifest declares
# longPathAware="true", so the switches inside it are redundant.

param(
    [string]$Project = "$PSScriptRoot\..\src\CDiskCleaner\CDiskCleaner.csproj",
    [string]$Dist = "$PSScriptRoot\..\dist",
    [switch]$SkipRunTest,
    [switch]$WithConfig
)

$ErrorActionPreference = 'Stop'
$exeName = 'CDiskCleaner.exe'

Write-Host '1/4 building Release...'
dotnet build $Project -c Release -v:q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$bin = Join-Path (Split-Path (Resolve-Path $Project) -Parent) 'bin\Release\net48'
$source = Join-Path $bin $exeName
if (-not (Test-Path $source)) { throw "missing $source" }

Write-Host '2/4 cleaning dist...'
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

Write-Host '3/4 copying the single file...'
Copy-Item $source (Join-Path $Dist $exeName) -Force
if ($WithConfig) {
    Copy-Item (Join-Path $bin 'CDiskCleaner.exe.config') (Join-Path $Dist 'CDiskCleaner.exe.config') -Force
    Write-Host '    (the sidecar config is included: paths longer than 260 characters are supported)'
} else {
    Write-Host '    (single file only: paths longer than 260 characters will be skipped)'
}

$files = Get-ChildItem $Dist -File
Write-Host ("    files in dist: " + $files.Count)
foreach ($f in $files) { Write-Host ("    - {0} ({1:N0} bytes)" -f $f.Name, $f.Length) }
if (-not $WithConfig -and $files.Count -ne 1) { throw "dist must contain exactly one file" }

if (-not $SkipRunTest) {
    Write-Host '4/4 running the single file from dist to prove it starts on its own...'
    Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    $proc = Start-Process -FilePath (Join-Path $Dist $exeName) -PassThru
    Start-Sleep -Seconds 6
    $proc.Refresh()
    $alive = $null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
    $title = $proc.MainWindowTitle
    $responding = $proc.Responding
    Write-Host ("    alive={0} responding={1} windowTitleLength={2}" -f $alive, $responding, $title.Length)
    Get-Process CDiskCleaner -ErrorAction SilentlyContinue | Stop-Process -Force
    if (-not $alive -or -not $responding) { throw "the single file did not start properly" }
}

$hash = (Get-FileHash (Join-Path $Dist $exeName) -Algorithm SHA256).Hash
$size = (Get-Item (Join-Path $Dist $exeName)).Length
Write-Host ''
Write-Host ("DONE  {0}  ({1:N0} bytes, {2:N2} MB)" -f $exeName, $size, ($size / 1MB))
Write-Host ("SHA256 {0}" -f $hash)
Write-Host ("path  {0}" -f (Join-Path (Resolve-Path $Dist) $exeName))
