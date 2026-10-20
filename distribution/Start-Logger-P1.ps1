[CmdletBinding()]
param(
    [string]$Device = "",
    [string]$Platform = "P1",
    [string]$Ecu = "7A",
    [UInt32]$Baudrate = 500000,
    [string]$Variables = "",
    [string]$Output = ".\logs\p1.csv",
    [UInt32]$Print = 5,
    [switch]$VerboseLog
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoLogger*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "VolvoLogger executable not found in $root." }
$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$argsList = @('-d', $Device, '-f', $Platform, '-e', $Ecu, '-b', [string]$Baudrate, '-p', [string]$Print, '-o', $Output)
if ($Variables) { $argsList += @('--variables', $Variables) }
if ($VerboseLog) { $argsList += '--verbose' }
& $exe.FullName @argsList
exit $LASTEXITCODE
