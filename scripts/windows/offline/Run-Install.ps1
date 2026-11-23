#Requires -RunAsAdministrator
# ASCII launcher for Server 2019: reads Install-OneClick.ps1 as UTF-8, runs as UTF-16.
param(
    [string]$BundleRoot = ''
)

if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $BundleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

$src = Join-Path $PSScriptRoot 'Install-OneClick.ps1'
if (-not (Test-Path -LiteralPath $src)) {
    throw "Missing: $src"
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$text = [System.IO.File]::ReadAllText($src, $utf8)
$tmp = Join-Path $env:TEMP ('amadban-install-' + [guid]::NewGuid().ToString('n') + '.ps1')
$unicode = New-Object System.Text.UnicodeEncoding $false, $true
[System.IO.File]::WriteAllText($tmp, $text, $unicode)

try {
    & $tmp -BundleRoot $BundleRoot
    exit $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}