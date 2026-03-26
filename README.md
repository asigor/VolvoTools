# VolvoTools

Tools for flashing and logging ME7 and ME9 memory with J2534 devices.
Initially written by dream3R from http://nefariousmotorsports.com/forum
for Hilton's Tuning Studio. 
HTS was decompiled and rewritten in C++.
Later was added support of generic J2534 devices not only DiCE
and support of UDS protocol.

Now supports
- ME7 flashing and logging for P80 and P2
- ME9 flashing and logging for P1
- ME9 and other UDS protocols flashing and logging
- Denso ECM flashing and logging for Volvo P3 platform.

Big thanks to rkam for the information and shared data.
Big thanks to jazdw for TP20 protocol explanation.
Big thanks to maZer.GTi for motivation.
Big thanks to prometey1982 for keeping his repo opensource.

## VolvoFlasher CLI profile (ECU/CEM)

`VolvoFlasher` has a Volvo-themed startup header and now supports selecting target module type:

- `--module ECU` (default)
- `--module CEM`

Example for P3 CEM workstream:

```bash
VolvoFlasher -f P3 -m CEM -e 0x42 flash -i your_file.bin
```

Example for classic ECU flashing on P2:

```bash
VolvoFlasher -f P2 -m ECU -e 0x7A flash -i your_file.bin
```


## Installer EXE (single package)

The CI pipeline now generates one Windows installer artifact:

- `VolvoTools_Installer.exe`

Installer is produced with CPack/NSIS and published as workflow artifact.
For tag builds, the same single installer is attached to the GitHub Release.

After installation you can edit GUI defaults in `VolvoToolsGui.config.json`
(placed next to `VolvoToolsGui.exe`) to change default module/platform/baudrate/PIN fields.

