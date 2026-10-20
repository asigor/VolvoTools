[CmdletBinding()]
param(
    [string]$Device,
    [string]$Platform,
    [switch]$VerboseLog
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $root 'config\defaults.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw "Missing defaults file: $configPath" }
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (-not $PSBoundParameters.ContainsKey('Device')) { $Device = [string]$config.device }
if (-not $PSBoundParameters.ContainsKey('Platform')) { $Platform = [string]$config.platform }
$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoFlasher*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "VolvoFlasher executable not found in $root." }
$argsList = @('-d', $Device, '-f', $Platform)
if ($VerboseLog) { $argsList += '-v' }
$argsList += 'wakeup'
& $exe.FullName @argsList
exit $LASTEXITCODE
