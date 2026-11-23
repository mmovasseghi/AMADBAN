# Run on the flash folder BEFORE taking it to the offline server (no internet needed).
param([string]$BundleRoot = '')

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $BundleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    $BundleRoot = (Resolve-Path $BundleRoot).Path
}
$missing = @()

$required = @(
    (Join-Path $BundleRoot 'نصب-آمادبان.bat'),
    (Join-Path $BundleRoot 'install\Install-OneClick.ps1'),
    (Join-Path $BundleRoot 'app\Amadban.exe')
)
foreach ($path in $required) {
    if (-not (Test-Path $path)) { $missing += $path }
}

$pg = Get-ChildItem (Join-Path $BundleRoot 'postgres') -Filter 'postgresql-*-windows-x64.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pg) { $missing += 'postgres\postgresql-*-windows-x64.exe' }

$vc = Join-Path $BundleRoot 'postgres\vcredist\VC_redist.x64.exe'
if (-not (Test-Path $vc)) { $missing += 'postgres\vcredist\VC_redist.x64.exe (recommended)' }

if ($missing.Count -gt 0) {
    Write-Host 'این فلش برای نصب آفلاین کامل نیست:' -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host 'فلش آماده است: نصب-آمادبان.bat را روی سرور با Administrator اجرا کنید.' -ForegroundColor Green
exit 0