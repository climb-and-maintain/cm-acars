# Architecture

## Design objective

Climb and Maintain ACARS is an out-of-process Windows desktop flight recorder. Simulator details, aircraft-specific interpretation, backend contracts, persistence, and presentation are separated so a failure or replacement in one area does not corrupt the others.

~~~text
WPF / System.Waf UI
        |
Application orchestration and simulator-neutral ACARS domain
        |-----------------------|------------------------|
Runtime SimConnect adapter   Aircraft profiles       phpVMS backend
        |                                                |
User-supplied native DLL                           Durable SQLite outbox
~~~

MSFS does not know about phpVMS, and phpVMS does not know about SimConnect. Immutable TelemetrySnapshot values are the boundary between simulator acquisition and application behavior.

## Projects and dependency direction

- Core: domain types, units, flight/session concepts, events, and interfaces; BCL only.
- Application: use cases, orchestration, phase engine, and ports; depends on Core.
- SimConnect: runtime native interop and telemetry provider; implements application/core interfaces.
- AircraftProfiles: data-only matching, rules, normalization, and fallback.
- PhpVms: HTTP contracts and backend implementation.
- Infrastructure: SQLite, secrets, logging, file-system services, and recovery.
- App: WPF/System.Waf composition and presentation.

The UI does not directly issue SimConnect, HTTP, SQLite, or business-significant file-system calls. Architecture tests should enforce these directions.

## Runtime SimConnect boundary

The complete simulator adapter is built in every configuration. There is no EnableMsfsSdk property, SDK-path compile condition, managed-wrapper reference, or Microsoft binary build input.

At runtime:

1. Configuration identifies a target: MSFS 2020 or MSFS 2024.
2. Bounded discovery proposes local native-library candidates, or the user selects a file.
3. Non-executing checks establish file presence, PE/x64 architecture, absence of a CLR header, and the required native export surface.
4. The native loader uses an absolute path and safe dependency search, resolves the minimal required export set, and reports structured failures.
5. A live connection test verifies the selected target and records the result for that target only.
6. The provider serializes all native calls through one dispatcher/thread and publishes immutable snapshots.

Library selection and the last live-test attestation are stored separately for msfs20 and msfs24. Structural checks run against the current file and are target-neutral; edition compatibility is established only by the target-specific live handshake. The same physical file, if ever allowed for both, still requires independent live validation. A changed path, hash, size, or write time no longer matches the stored attestation.

A runtime-unavailable provider/state supports graceful degradation but never replaces the real adapter at compile time. Configuration, diagnostics, phpVMS setup, and recorded replay remain available without a library.

## Telemetry and aircraft profiles

The provider collects fast flight data, slower system data, and identity data on load/change. The normalizer first uses universal SimVars, then applies only the fields supplied by the highest-priority matching profile. Unspecified values retain universal fallback behavior.

Profiles are JSON data. They may match aircraft identity and interpret SimVars or LVars, but may not execute scripts, start processes, download content, write simulator variables, or contain arbitrary instructions. Specialist compiled providers remain optional and require a demonstrated technical and licensing need.

## Flight session and outbox

The phase engine uses sustained conditions and hysteresis rather than one-frame transitions. Positions, events, and logs receive durable local IDs and enter a SQLite outbox before network delivery. phpVMS uploads use bounded batches, idempotent retry behavior, Retry-After, and exponential backoff with jitter.

The active session, phase, PIREP identifier, summary, and unsent outbox survive application and network failure. A start intent is committed locally before prefile; its unique phpVMS source marker lets recovery reconcile a committed request whose response was lost without issuing a duplicate POST. Recovery presents Resume, Inspect, and Discard rather than silently deleting a flight.

## Security and accessibility

phpVMS secrets use a DPAPI-backed ISecretStore; logs and support exports are sanitized. Selecting a native library is a trust decision because loading it executes code. See [SECURITY.md](SECURITY.md).

Presentation uses native WPF controls, UI Automation properties, keyboard commands, semantic theme resources, and explicit textual state. Accessibility is tested throughout development and again during release.

## Test boundaries

- Unit: domain, phase, profile, validation, and loader logic.
- Contract: exact phpVMS requests/responses and failure behavior.
- Integration: storage, outbox, recorded end-to-end flights, and a project-owned native ABI shim.
- UI: keyboard, automation names, navigation, and critical workflows.
- SimulatorIntegration: opt-in live tests on actual MSFS installations.

Hosted CI never needs proprietary simulator files. A release requires separate live evidence for both supported simulators using the same published application artifact.
