# Climb and Maintain ACARS

Climb and Maintain ACARS is an open-source Windows flight-tracking client for Microsoft Flight Simulator 2020, Microsoft Flight Simulator 2024, and phpVMS. It is designed as a durable flight recorder: simulator and network interruptions should degrade gracefully without discarding an active flight.

The project is a native .NET 10 WPF application using System.Waf, a simulator-neutral ACARS core, a data-driven aircraft-profile engine, a durable SQLite outbox, and a first-class phpVMS REST adapter.

> Project status: the repository is under active development toward the v0.1 public beta. Items described as release criteria are not support claims until they are recorded in a published release.

## Screenshot

A release screenshot will be added after the accessible WPF shell and visual states have completed verification. The repository does not use an unrelated mock-up as a substitute for the working application.

## Simulator library policy

The application includes its complete SimConnect adapter in every build, but it does **not** include or download Microsoft SimConnect binaries. It also does not reference Microsoft's managed SimConnect wrapper and does not need the MSFS SDK to compile.

To connect a simulator, the user supplies a compatible native x64 SimConnect library that they obtained from a Microsoft-provided source they are entitled to use. The application provides:

- bounded automatic discovery of local candidates;
- an accessible manual file picker;
- validation before use;
- a live connection test;
- separate configuration and validation for MSFS 2020 and MSFS 2024; and
- diagnostics and troubleshooting when a library is absent or incompatible.

A library validated for one simulator is not assumed to work with the other. Adding a compatible library to an installed copy enables connectivity without recompiling or reinstalling the application. Missing simulator prerequisites never prevent configuration, diagnostics, phpVMS setup, or recorded-flight replay.

See [SimConnect setup](docs/SIMCONNECT.md) and [troubleshooting](docs/TROUBLESHOOTING.md).

## What v0.1 targets

- Generic position, altitude, speed, heading, on-ground, flight-time, distance, fuel, landing, and standard-system telemetry for normally behaving MSFS aircraft.
- Profile-enhanced support for stock aircraft, FlyByWire A32NX, Fenix A319/A320/A321, PMDG 737/777, and selected MSFS 2024 aircraft where verified.
- Native phpVMS authentication, flight selection, PIREP prefile/update/file/cancel, batched positions, events, logs, retry handling, and offline recovery.
- Resume of an unfinished local flight after an application restart.
- System, Light, Dark, and Windows High Contrast appearance with keyboard and Narrator support.
- A normal per-user x64 Windows installer.

Aircraft-specific telemetry is profile-driven. A missing enhanced value does not prevent generic flight tracking.

## Quick start

1. Install the latest ClimbAndMaintain-ACARS-Setup-x64.exe from the Releases page when a public beta is available.
2. Complete the first-run phpVMS connection pages.
3. On the Simulator page, choose MSFS 2020 or MSFS 2024.
4. Review automatically discovered library candidates or select your own compatible native library.
5. Validate the library, start the selected simulator, and run the connection test.
6. Select a phpVMS flight or bid and begin tracking.

No manual JSON editing is required for normal setup. Replay mode remains available without a simulator library or a running simulator.

## Build

The supported build workflow uses the .NET CLI on Windows x64. Visual Studio is not required, and neither an installed MSFS SDK nor any Microsoft SimConnect DLL is a build input.

~~~powershell
dotnet restore ClimbAndMaintain.Acars.slnx --locked-mode
dotnet build ClimbAndMaintain.Acars.slnx -c Release --no-restore
dotnet test --solution ClimbAndMaintain.Acars.slnx --configuration Release --no-restore --no-build
~~~

See [Building](docs/BUILDING.md) and [Development](docs/DEVELOPMENT.md). Release maintainers use scripts/Build-Release.ps1 to create the immutable candidate that is then exercised by the two-simulator evidence gate.

## Documentation

- [Installation](docs/INSTALLATION.md)
- [Configuration](docs/CONFIGURATION.md)
- [phpVMS integration](docs/PHPMVS-INTEGRATION.md)
- [Aircraft compatibility](docs/AIRCRAFT-COMPATIBILITY.md)
- [Aircraft profiles](docs/AIRCRAFT-PROFILES.md)
- [Accessibility](docs/ACCESSIBILITY.md)
- [Privacy](docs/PRIVACY.md)
- [Architecture](ARCHITECTURE.md)
- [Release process](docs/RELEASES.md)
- [Forking and branding](docs/FORKING-AND-BRANDING.md)

## Contributing and support

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. Use the issue forms for bugs, feature requests, accessibility problems, and aircraft compatibility reports. Please do not upload Microsoft or aircraft-vendor DLLs, SDK files, documentation, or other copyrighted assets.

Security vulnerabilities should be reported privately as described in [SECURITY.md](SECURITY.md). General support routes are listed in [SUPPORT.md](SUPPORT.md).

## Accessibility

Accessibility is a release criterion. The interface uses standard WPF controls, meaningful UI Automation metadata, complete keyboard paths, visible focus, text status in addition to color, Windows High Contrast behavior, and System/Light/Dark themes. The release checklist includes Narrator, keyboard traversal, High Contrast, and 200% display scaling.

## License and attribution

Original Climb and Maintain ACARS code is licensed under the [Apache License 2.0](LICENSE). Third-party components and imported profile material retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Climb and Maintain ACARS was originally created by **Climb and Maintain, SPC**.

This is an independent third-party project. It is not affiliated with or endorsed by Microsoft, Asobo Studio, phpVMS, Fenix Simulations, PMDG, or other aircraft developers unless a future written statement explicitly says otherwise.
