# Installation

## Supported package

The primary distribution is ClimbAndMaintain-ACARS-Setup-x64.exe for Windows x64. The installer is self-contained and includes the required .NET runtime. It installs per user under:

~~~text
%LocalAppData%\Programs\ClimbAndMaintain\ACARS
~~~

Application data is stored separately under:

~~~text
%LocalAppData%\ClimbAndMaintain\ACARS
~~~

Administrator rights are not required for normal installation or flight logging.

## Verify the download

Official releases include SHA256SUMS.txt. In PowerShell:

~~~powershell
Get-FileHash .\ClimbAndMaintain-ACARS-Setup-x64.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
~~~

Confirm that the values match before running the installer.

## Install

1. Close a running copy of Climb and Maintain ACARS.
2. Run the setup executable.
3. Choose whether to create a desktop shortcut.
4. Launch the application and complete first-run setup.

The application must launch even when no simulator or SimConnect library is installed. Configuration, diagnostics, phpVMS setup, and replay mode remain available.

## Configure simulator connectivity

Microsoft SimConnect DLLs are not included and the application does not download them. Supply a compatible native x64 library obtained independently from a Microsoft-provided source you are entitled to use.

Configure MSFS 2020 and MSFS 2024 separately. For each simulator:

1. Open the Simulator setup page.
2. Review automatically discovered candidates or choose Browse.
3. Select the library for that simulator.
4. Run Validate.
5. Start the corresponding simulator.
6. Run Test connection.

Passing validation for one edition does not validate the other. See [SimConnect](SIMCONNECT.md).

## Upgrade

Install the newer package over the existing per-user installation. Setup checks for a running application before replacing files. Normal upgrades preserve settings, credentials, profiles, the active-flight record, and queued reports.

Revalidate a configured native library when its path, timestamp, size, or hash changes, and repeat the live test if the simulator or reported SimConnect version changes. An application upgrade never silently substitutes or downloads a library.

## Uninstall

Use Windows Installed apps or the Start menu uninstall shortcut. The uninstaller removes program files and shortcuts and asks whether to remove local application data. Keep local data if you may reinstall or need queued/recovery records.

The uninstaller does not delete a user-selected library outside the application directory.

## Portable archive

A portable ZIP may be published later, but Setup.exe is the supported distribution. Any portable artifact is subject to the same no-bundling rule and must not contain Microsoft SimConnect DLLs.
