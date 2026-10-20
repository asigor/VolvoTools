[CmdletBinding()]
param(
    [string]$Device,
    [string]$Platform
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $root 'config\defaults.json'

if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "Missing defaults file: $configPath"
}

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (-not $PSBoundParameters.ContainsKey('Device')) { $Device = [string]$config.device }
if (-not $PSBoundParameters.ContainsKey('Platform')) { $Platform = [string]$config.platform }

$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoFlasher*.exe' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $exe) {
    throw "VolvoFlasher executable not found in $root. Extract the complete GitHub Actions package."
}

Write-Host "Reading CEM PIN using device '$Device' and platform '$Platform'."
Write-Warning "Ensure the adapter is connected to the intended vehicle and CEM."
& $exe.FullName -d $Device -f $Platform pin
exit $LASTEXITCODE
