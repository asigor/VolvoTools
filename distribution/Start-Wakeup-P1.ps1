[CmdletBinding()]
param(
    [string]$Device = "",
    [string]$Platform = "P1",
    [switch]$VerboseLog
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoFlasher*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "VolvoFlasher executable not found in $root." }
$argsList = @('-d', $Device, '-f', $Platform)
if ($VerboseLog) { $argsList += '-v' }
$argsList += 'wakeup'
& $exe.FullName @argsList
exit $LASTEXITCODE
