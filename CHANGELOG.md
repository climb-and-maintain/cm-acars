# Changelog

All notable changes are recorded here. This project follows [Semantic Versioning](https://semver.org/) and uses the structure from [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Production-oriented public-repository structure, strict build configuration, dependency locks, CI, CodeQL, release evidence, and NSIS packaging.
- A .NET 10 WPF/System.Waf ACARS client with flight selection, durable local recovery, profile-driven aircraft telemetry, recorded-flight replay, diagnostics, and accessible first-run/setup workflows.
- An always-built native C# SimConnect adapter that discovers, validates, loads, and tests a user-supplied x64 library at runtime without the MSFS SDK or Microsoft's managed wrapper.
- Independent MSFS 2020 and MSFS 2024 selections, file attestations, live connection tests, and release evidence requirements.
- A simulator-neutral phase/session engine, SQLite outbox, background delivery, and restart recovery.
- A phpVMS v7 adapter for authentication, flight/bid retrieval, prefile/update, position/event/log batches, filing, cancellation, throttling, and retry handling.
- Read-only universal, Fenix, and PMDG profile layers with bounded local profile overrides and Profile Inspector output.
- Accessibility, security, support, governance, contribution, and aircraft-profile policies.

### Security

- Microsoft SimConnect binaries are excluded from source, CI artifacts, publish output, and installers.
- User-selected native libraries are treated as trusted-code inputs and validated before connection attempts.

## [0.1.0-beta.1] - 2026-11-06

### Test candidate

- First installer candidate for controlled Windows x64 acceptance testing; not yet a public beta or a claim of supported simulator compatibility.
- Includes the .NET 10 WPF desktop client, native SimConnect adapter, aircraft-profile engine, phpVMS v7 integration, SQLite outbox, flight recovery, diagnostics, and telemetry replay.
- Windows CI formatting, Release compilation, 181 automated tests, and CodeQL passed before preparing this candidate.

### Validation still required

- Live SimConnect and flight-telemetry validation on MSFS 2024 and, separately, MSFS 2020.
- Installer, first-run, accessibility, phpVMS server integration, and end-to-end flight/PIREP testing with the exact candidate installer.
- User-supplied compatible Microsoft native x64 SimConnect library is required for simulator connectivity; no Microsoft SimConnect binaries are bundled or downloaded.
- Do not publish or redistribute this candidate as a verified release until the release evidence and acceptance checks are complete.

[Unreleased]: https://github.com/climb-and-maintain/cm-acars/commits/main
[0.1.0-beta.1]: https://github.com/climb-and-maintain/cm-acars/compare/main...main
