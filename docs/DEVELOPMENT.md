# Development

## Principles

Keep the application buildable, testable without a simulator, and tolerant of partial failure. Work in coherent vertical increments rather than isolated UI mock-ups.

The normal development loop never needs Microsoft SimConnect binaries or an installed MSFS SDK. The complete real adapter compiles against original C# native declarations, while automated tests use abstractions, synthetic data, recorded flights, and a project-owned native test shim.

## Solution boundaries

Follow [ARCHITECTURE.md](../ARCHITECTURE.md). Core is simulator- and backend-neutral. Application coordinates use cases. SimConnect, phpVMS, profiles, and infrastructure implement interfaces. App composes services and renders state.

Do not:

- reference WPF, HTTP, SQLite, or native interop from Core;
- call simulator, HTTP, or storage APIs from ViewModels;
- conditionally compile out the real simulator adapter;
- add Microsoft's managed wrapper as a reference;
- use Microsoft DLLs as tests or fixtures; or
- create aircraft behavior through a growing chain of product-name conditionals.

## Suggested implementation gates

### Gate 1: walking skeleton

Establish every project, central configuration, WPF/System.Waf shell, DI, navigation, themes, fake simulator/backend, SQLite initialization, logging, and runnable tests. Restore, build, and test must pass before real integrations expand.

### Gate 2: simulation mode

Complete profiles, session/phase behavior, durable queues, phpVMS contracts, recorded telemetry, UI, and diagnostics. Replay a complete flight and verify emitted backend requests.

### Gate 3: live SimConnect

Use the ordinary published build and the application's runtime library workflow. Test discovery, manual selection, structural validation, loading, connection, aircraft identity, SimVars, LVars, aircraft reload, and simulator restart independently on MSFS 2020 and MSFS 2024.

### Gate 4: release candidate

Run locked restore, clean Release build, tests, publish, distribution audit, NSIS packaging, clean-profile installation, accessibility checks, upgrade, and uninstall. Complete the two-simulator evidence document before tagging.

## Tests

- Unit: pure domain, rules, phase transitions, validation, and error mapping.
- Contract: exact phpVMS JSON and HTTP behavior.
- Integration: SQLite, recovery, queues, replay, and native shim.
- UI: navigation, accessible names, focus, validation, and workflows.
- SimulatorIntegration: opt-in and run only on an appropriate live Windows environment.

Test fixture files must be synthetic or redistributable. Sanitize recordings and never include credentials or proprietary aircraft data.

## Coding practice

Nullable annotations and warnings-as-errors are enabled. Prefer understandable code to dependency-heavy shortcuts. Do not swallow exceptions. Use async Task, reserving async void for required event handlers. Keep simulator operations on one dispatcher and never block the UI thread.

Update user documentation, diagnostics wording, and the changelog whenever behavior changes.

## Working with user-supplied native code

Separate candidate inspection, target-specific validation, native loading, live connection, and telemetry verification. A failure at one stage should produce a typed result and an actionable message. Do not claim a file is safe merely because it has the right exports.

Selection changes must not race an active dispatcher. Ensure outstanding calls have stopped before unload or replacement, and invalidate stored live-test evidence when file identity changes.
