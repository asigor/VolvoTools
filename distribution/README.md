# VolvoTools PowerShell shortcuts

These scripts are intended to sit beside the executables downloaded from the GitHub Actions package. They call the command-line options implemented by \`VolvoFlasher\` and \`VolvoLogger\`; they do not change the application itself.

## Examples

\`\`\`powershell
# Equivalent in intent to: VolvoFlasher.exe -d "sm2 pro" -f "P1" pin
.\Start-PinSearch-P1.ps1

# Override the device or ECU when needed
.\Start-PinSearch-P1.ps1 -Device "sm2 pro" -Platform P1 -Ecu 7A -StartPin 0

# Search PINs downward (passes the app's pin -d flag)
.\Start-PinSearch-P1.ps1 -Down

# Wake the CAN network
.\Start-Wakeup-P1.ps1 -Device "sm2 pro"

# Start logger; supply the variables file used by VolvoLogger when available
.\Start-Logger-P1.ps1 -Device "sm2 pro" -Variables ".\data\variables.json" -Output ".\logs\p1.csv"
\`\`\`

## Defaults

\`config/defaults.json\` documents the package defaults: device \`sm2 pro\`, platform \`P1\`, 500000 baud, ECU \`7A\`, PIN search starting at zero and moving upward. The shortcut scripts read these defaults from `config/defaults.json`; pass script parameters to override them for one run.

## Safety and limitations

- The PIN command actively communicates with an ECU. Use only on a vehicle/module you are authorized to diagnose, and confirm the selected device and platform first.
- Flashing an ECU is not included as an automatic shortcut because the input firmware/SBL and target-specific parameters must be chosen explicitly.
- The logger's variables file is application-specific; no placeholder is silently created because an invalid file could make logging fail.
- These scripts require Windows PowerShell 5.1+ or PowerShell 7 on Windows.
