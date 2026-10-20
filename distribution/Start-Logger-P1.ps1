[CmdletBinding()]
param(
    [string]$Device,
    [string]$Platform,
    [string]$Ecu,
    [UInt32]$Baudrate,
    [string]$Variables,
    [string]$Output,
    [UInt32]$Print,
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
if (-not $PSBoundParameters.ContainsKey('Baudrate')) { $Baudrate = [UInt32]$config.baudrate }
if (-not $PSBoundParameters.ContainsKey('Variables')) { $Variables = [string]$config.logger.variables }
if (-not $PSBoundParameters.ContainsKey('Output')) { $Output = [string]$config.logger.output }
if (-not $PSBoundParameters.ContainsKey('Print')) { $Print = [UInt32]$config.logger.print }

$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoLogger*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "VolvoLogger executable not found in $root." }
$argsList = @('-d', $Device, '-f', $Platform, '-e', $Ecu, '-b', [string]$Baudrate, '-p', [string]$Print, '-o', $Output)
if ($Variables) { $argsList += @('--variables', $Variables) }
if ($VerboseLog) { $argsList += '--verbose' }
& $exe.FullName @argsList
exit $LASTEXITCODE
