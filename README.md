# IT Desktop Toolkit — V0.4.4

Windows desktop IT operations toolkit (C# WinForms). This repository contains the complete V0.4.4 source package.

## Build

- Windows 10 / Windows 11 environment.
- Extract/clone the complete repository folder.
- Run `Build_And_Run.bat` to compile and launch, or `Build_Only.bat` to compile only.
- The compiler and framework requirements are checked by the batch scripts. Keep the generated `build` folder and its `Database` subfolder together when copying the app to another PC.

## Included modules

- System and hardware overview, monitor/EDID information
- Network configuration and diagnostics
- Startup and power tools
- Event and fault-code lookup
- SMB sharing modes, diagnostics, share and NTFS permissions
- BAT workflow automation, drag ordering, cross-room environment diagnostics

## Safety / validation

This is the source snapshot labelled V0.4.4. The included `START_HERE.txt` and `版本更新记录_累计.txt` contain detailed version notes and safety limitations. The source package records that Windows 10/11 real-machine build and SMB environment testing were not yet completed for this snapshot. Test on a disposable test machine and test folders before using administrative or sharing functions on production devices.

## Source integrity

`SHA256SUMS.txt` contains hashes for the original source files. The GitHub import package was checked against all entries in that file; added repository files (`README.md` and `.gitignore`) are not part of the original manifest.
