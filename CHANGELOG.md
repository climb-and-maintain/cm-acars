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

[Unreleased]: https://github.com/climb-and-maintain/cm-acars/commits/main
