# Contributing

Thank you for helping improve Climb and Maintain ACARS. Accessibility, flight-data durability, third-party intellectual property, and a continuously buildable repository are non-negotiable project qualities.

## Before starting

1. Search existing issues and discussions.
2. Use the appropriate issue form for a bug, feature, accessibility problem, or aircraft compatibility problem.
3. For a broad architectural change, open a proposal before investing in implementation.
4. Never attach Microsoft SimConnect binaries, aircraft-vendor files, proprietary SDK documentation, API keys, or unsanitized support bundles.

Small, well-scoped fixes may go directly to a pull request.

## Development setup

- Windows x64
- .NET 10 SDK selected by global.json
- VS Code or another editor capable of invoking the dotnet CLI
- NSIS only when building the installer

Visual Studio, an installed MSFS SDK, Microsoft's managed SimConnect wrapper, and Microsoft native SimConnect binaries are not build prerequisites.

~~~powershell
dotnet restore ClimbAndMaintain.Acars.slnx --locked-mode
dotnet build ClimbAndMaintain.Acars.slnx -c Release --no-restore
dotnet test --solution ClimbAndMaintain.Acars.slnx --configuration Release
~~~

Run dotnet format --verify-no-changes --no-restore before submitting. See [Development](docs/DEVELOPMENT.md) for architecture and test guidance.

## Engineering rules

- Keep Core independent of UI, HTTP, SQLite, and SimConnect.
- Serialize every call into a SimConnect instance through its dispatcher.
- Keep the real simulator adapter compiled in all application configurations.
- Do not introduce SDK-detection MSBuild conditions or a managed-wrapper reference.
- Do not add download code, binaries, fixtures, or installer entries for Microsoft SimConnect libraries.
- Do not swallow exceptions or block the UI thread on simulator, storage, or network operations.
- Do not use async void except required event handlers.
- Keep business logic out of ViewModels.
- Keep profiles declarative, read-only, deterministic, and free of scripts or executable instructions.
- Protect credentials and sanitize logs, tests, screenshots, diagnostics, and support bundles.
- Update documentation and tests with behavior changes.
- Do not leave production-path placeholders simply to make a build pass.

## Runtime interop changes

Changes to the native interop boundary require tests for missing files, invalid PE files, wrong architecture, missing exports, incompatible target selection, load failure, disconnect, and recovery. Compatibility for msfs20 and msfs24 must be evaluated and stored separately. A successful test on one target is not evidence for the other.

Use a project-owned native test shim for automated ABI/loader tests. Microsoft DLLs must never enter the repository or CI.

## Aircraft profiles

Profile pull requests must include:

- simulator target (msfs, msfs20, or msfs24 as appropriate);
- aircraft/developer/version evidence;
- deterministic match rules and priority;
- synthetic input/output fixtures;
- upstream source, license, revision, and original author metadata when adapted;
- standard-SimVar fallback wherever possible; and
- confirmation that no executable/write behavior or copyrighted vendor material is included.

Use an exported, sanitized aircraft diagnostic where possible. See [Aircraft profiles](docs/AIRCRAFT-PROFILES.md).

## Accessibility

New or changed UI must work by keyboard, expose useful UI Automation names/state, preserve visible focus, avoid color-only meaning, support Windows High Contrast, and remain usable at 200% scaling. Prefer native WPF controls. A necessary custom control requires an appropriate AutomationPeer.

## Pull requests

- Keep each pull request focused.
- Explain user-visible behavior and risks.
- Include or update unit, contract, integration, and UI tests as applicable.
- Include documentation and changelog updates for user-visible changes.
- Ensure Release build and tests complete with zero warnings.
- Sign commits if the repository later enables a DCO requirement; no CLA is currently required.

By contributing, you agree that your original contribution is licensed under Apache-2.0. Material you do not own must retain its license and attribution and must be legally redistributable.
