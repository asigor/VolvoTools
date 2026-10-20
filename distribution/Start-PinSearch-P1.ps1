[CmdletBinding()]
param(
    [string]$Device,
    [string]$Platform,
    [string]$Ecu,
    [string]$StartPin,
    [switch]$Down,
    [switch]$VerboseLog
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $root 'config\defaults.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw "Missing defaults file: $configPath" }
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (-not $PSBoundParameters.ContainsKey('Device')) { $Device = [string]$config.device }
if (-not $PSBoundParameters.ContainsKey('Platform')) { $Platform = [string]$config.platform }
if (-not $PSBoundParameters.ContainsKey('Ecu')) { $Ecu = [string]$config.ecu }
if (-not $PSBoundParameters.ContainsKey('StartPin')) { $StartPin = [string]$config.pin_start }
if (-not $PSBoundParameters.ContainsKey('Down')) { $Down = ([string]$config.pin_direction -eq 'down') }

$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoFlasher*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "VolvoFlasher executable not found in $root. Extract the complete Actions package." }

$argsList = @('-d', $Device, '-f', $Platform, '-e', $Ecu, '-p', $StartPin)
if ($VerboseLog) { $argsList += '-v' }
$argsList += 'pin'
if ($Down) { $argsList += '-d' }

Write-Host "Starting VolvoFlasher PIN operation: device='$Device', platform='$Platform', ECU=$Ecu, start=$StartPin"
Write-Warning "PIN search communicates with a real ECU. Confirm the vehicle, adapter, platform and ECU before continuing."
& $exe.FullName @argsList
exit $LASTEXITCODE
