[CmdletBinding()]
param(
    [string]$Device = "",
    [string]$Platform = "P1",
    [string]$Ecu = "7A",
    [UInt64]$StartPin = 0,
    [switch]$Down,
    [switch]$VerboseLog
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoFlasher*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "VolvoFlasher executable not found in $root. Extract the complete Actions package." }

$argsList = @('-d', $Device, '-f', $Platform, '-e', $Ecu, '-p', ('0x{0:X}' -f $StartPin))
if ($VerboseLog) { $argsList += '-v' }
$argsList += 'pin'
if ($Down) { $argsList += '-d' }

Write-Host "Starting VolvoFlasher PIN operation: device='$Device', platform='$Platform', ECU=$Ecu"
Write-Warning "PIN search communicates with a real ECU. Confirm the vehicle, adapter, platform and ECU before continuing."
& $exe.FullName @argsList
exit $LASTEXITCODE
