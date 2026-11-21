#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Publish Amadban and register the Windows Service "Amadban" for LAN use.
.PARAMETER SourcePath
  Folder containing Amadban.csproj (git checkout or extracted source).
.PARAMETER InstallRoot
  Default C:\Amadban
.PARAMETER LanPort
  TCP port for HTTP (default 8088).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [string]$InstallRoot = 'C:\Amadban',
    [int]$LanPort = 8088
)

$ErrorActionPreference = 'Stop'
$Publish = Join-Path $InstallRoot 'publish'
$Keys = Join-Path $InstallRoot 'keys'
$Config = Join-Path $InstallRoot 'config'
$EnvFile = Join-Path $Config 'service.env'
$LogDir = Join-Path $InstallRoot 'logs'

if (-not (Test-Path $SourcePath)) { throw "SourcePath not found: $SourcePath" }
$csproj = Join-Path $SourcePath 'Amadban.csproj'
if (-not (Test-Path $csproj)) { throw "Amadban.csproj missing under $SourcePath" }

New-Item -ItemType Directory -Force -Path $Publish, $Keys, $Config, $LogDir | Out-Null

if (-not (Test-Path $EnvFile)) {
    $example = Join-Path $PSScriptRoot 'service.env.example'
    if (Test-Path $example) { Copy-Item $example $EnvFile }
    throw "Configure secrets in $EnvFile then run this script again."
}

$envVars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -eq '' -or $line.StartsWith('#')) { return }
  if ($line -match '^([^=]+)=(.*)$') {
        $envVars[$Matches[1].Trim()] = $Matches[2].Trim()
    }
}
if (-not $envVars['AMADBAN_DB_PASSWORD']) { throw 'AMADBAN_DB_PASSWORD is required in service.env' }
if (-not $envVars['AMADBAN_ADMIN_PASSWORD']) { throw 'AMADBAN_ADMIN_PASSWORD is required in service.env' }

$envVars['ASPNETCORE_URLS'] = "http://0.0.0.0:$LanPort"
if (-not $envVars['AMADBAN_KEYS']) { $envVars['AMADBAN_KEYS'] = $Keys }
if (-not $envVars['ASPNETCORE_ENVIRONMENT']) { $envVars['ASPNETCORE_ENVIRONMENT'] = 'Production' }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { throw 'dotnet not found. Install ASP.NET Core Runtime 10.x for Windows x64.' }

Write-Host "Publishing to $Publish ..."
& $dotnet publish $csproj -c Release -o $Publish --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit $LASTEXITCODE" }

$ruleName = 'Amadban HTTP (LAN)'
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if (-not $existing) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $LanPort -Action Allow | Out-Null
    Write-Host "Firewall rule created for port $LanPort"
}

$serviceName = 'Amadban'
$dll = Join-Path $Publish 'Amadban.dll'
$binPath = "`"$dotnet`" `"$dll`""

$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -eq 'Running') { Stop-Service $serviceName -Force }
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $serviceName binPath= $binPath start= auto DisplayName= "Amadban hospital LAN" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'sc.exe create failed' }

$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$envList = $envVars.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
Set-ItemProperty -Path $regPath -Name Environment -Value $envList -Type MultiString -Force

Write-Host 'Starting service...'
Start-Service $serviceName
Start-Sleep -Seconds 4
$status = (Get-Service $serviceName).Status
Write-Host "Service status: $status"
Write-Host "Open from LAN: http://<server-ip>:$LanPort/Login"
if ($status -ne 'Running') {
    throw 'Service did not start. Check Application event log.'
}