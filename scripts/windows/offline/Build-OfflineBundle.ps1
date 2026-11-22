# Build Amadban offline LAN bundle (run on a machine with .NET SDK — once before USB copy).
param(
    [string]$RepoRoot = '',
    [string]$OutDir = '',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path  # repo root from scripts/windows/offline
}
$csproj = Join-Path $RepoRoot 'Amadban.csproj'
if (-not (Test-Path $csproj)) { throw "Amadban.csproj not found at $RepoRoot" }

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Date -Format 'yyyyMMdd-HHmm')
}
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot "dist/Amadban-Offline-$Version"
}

$appDir = Join-Path $OutDir 'app'
$installDir = Join-Path $OutDir 'install'
$postgresDir = Join-Path $OutDir 'postgres'

New-Item -ItemType Directory -Force -Path $appDir, $installDir, $postgresDir | Out-Null

Write-Host "Publishing self-contained win-x64 to $appDir ..."
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -o $appDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$offlineScripts = Join-Path $PSScriptRoot '*'
Copy-Item -Path $offlineScripts -Destination $installDir -Recurse -Force

$bat = @"
@echo off
chcp 65001 >nul
title نصب آمادبان
echo.
echo   نصب آمادبان روی Windows Server (بدون اینترنت)
echo   لطفاً صبر کنید...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install\Install-OneClick.ps1" -BundleRoot "%~dp0"
if errorlevel 1 (
  echo.
  echo نصب ناموفق بود. پنجره را بخوانید یا Event Viewer را چک کنید.
  pause
  exit /b 1
)
echo.
pause
"@
Set-Content -Path (Join-Path $OutDir 'نصب-آمادبان.bat') -Value $bat -Encoding UTF8

Copy-Item (Join-Path $RepoRoot 'bundle/README.md') (Join-Path $OutDir 'README.txt') -Force -ErrorAction SilentlyContinue
@"
بعد از ساخت بسته، نصب PostgreSQL را اینجا کپی کنید:
  postgres\postgresql-17-windows-x64.exe
(نام فایل می‌تواند postgresql-*-windows-x64.exe باشد)
"@ | Set-Content -Path (Join-Path $postgresDir 'README-POSTGRES.txt') -Encoding UTF8

Write-Host ""
Write-Host "Bundle ready: $OutDir"
Write-Host "Next: copy postgresql-*-windows-x64.exe into $postgresDir"
Write-Host "Then copy the whole folder to USB and run نصب-آمادبان.bat as Administrator on the server."