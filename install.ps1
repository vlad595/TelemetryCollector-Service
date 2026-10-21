param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $ApiUrl
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TelemetryCollectorServiceAgent'
$installDir = Join-Path $env:ProgramFiles 'TelemetryCollectorService'
$assetUrl = 'https://github.com/vlad595/TelemetryCollector-Service/releases/latest/download/telemetry-agent-win-x64.zip'

$parsedApiUrl = $null
if (-not ([uri]::TryCreate($ApiUrl, [UriKind]::Absolute, [ref]$parsedApiUrl))) {
    throw 'ApiUrl must be an absolute URL, for example https://api.example.com/metrics.'
}
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installer from an elevated PowerShell terminal (Run as Administrator).'
}

$archive = Join-Path $env:TEMP 'telemetry-agent-win-x64.zip'
$unpackDir = Join-Path $env:TEMP 'telemetry-agent-win-x64'
Invoke-WebRequest -Uri $assetUrl -OutFile $archive
if (Test-Path $unpackDir) { Remove-Item $unpackDir -Recurse -Force }
Expand-Archive -Path $archive -DestinationPath $unpackDir -Force
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path (Join-Path $unpackDir '*') -Destination $installDir -Recurse -Force

$settings = Get-Content (Join-Path $installDir 'appsettings.json') -Raw | ConvertFrom-Json
$settings.ApiUrl = $ApiUrl
$settings | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $installDir 'appsettings.json') -Encoding utf8

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}
New-Service -Name $serviceName -DisplayName 'Telemetry Collector Agent' -BinaryPathName (Join-Path $installDir 'TelemetryCollectorService.Agent.exe') -StartupType Automatic | Out-Null
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
Start-Service -Name $serviceName
Write-Host "Installed and started $serviceName."
