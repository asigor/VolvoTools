[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$InputFile,
    [string]$Device,
    [string]$Platform,
    [string]$Ecu,
    [string]$Sbl,
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
$exe = Get-ChildItem -LiteralPath $root -Filter 'VolvoFlasher*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "VolvoFlasher executable not found in $root." }
Write-Warning "FLASHING WRITES TO ECU MEMORY. Verify the exact vehicle, ECU, firmware and stable power supply before proceeding."
$argsList = @('-d', $Device, '-f', $Platform, '-e', $Ecu)
if ($VerboseLog) { $argsList += '-v' }
$argsList += @('flash', '--input', (Resolve-Path -LiteralPath $InputFile).Path)
if ($Sbl) {
    if (-not (Test-Path -LiteralPath $Sbl -PathType Leaf)) { throw "SBL file not found: $Sbl" }
    $argsList += @('--sbl', (Resolve-Path -LiteralPath $Sbl).Path)
}
& $exe.FullName @argsList
exit $LASTEXITCODE
