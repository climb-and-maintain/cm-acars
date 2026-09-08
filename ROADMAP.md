# Roadmap

The roadmap communicates intent, not a promise of dates. Reliability, accessibility, and safe recovery take priority over feature count.

## v0.1 public beta

- Complete WPF/System.Waf shell, simulator-neutral ACARS core, storage, logging, and diagnostics.
- Complete runtime-native SimConnect interop in every build, with no SDK build dependency and no redistributed Microsoft DLL.
- Accessible automatic discovery, manual selection, validation, connection testing, and troubleshooting for user-supplied libraries.
- Independently validate the release workflow on MSFS 2020 and MSFS 2024.
- Generic telemetry plus initial Fenix, PMDG, FlyByWire, stock, and selected iniBuilds profiles.
- phpVMS flight selection, PIREP lifecycle, batched telemetry/events/logs, retry, and offline recovery.
- Recorded-flight end-to-end test harness.
- Per-user x64 installer and complete public project documentation.

## Later 0.x releases

- Expand verified aircraft-profile coverage from sanitized diagnostic reports.
- Refine flight-phase and event behavior using field evidence.
- Add optional specialist providers only where profile/SimVar approaches cannot work and licensing permits them.
- Improve maintainers' release automation and signed official builds.

## Explicitly out of scope

- A map or embedded browser runtime.
- Pilot scoring in the core engine.
- Perfect detection of every aircraft-specific switch.
- Bundling or downloading Microsoft simulator libraries.
- A proprietary PMDG SDK implementation.
