# Configuration

Normal users configure the application through the first-run wizard and Settings. Manual JSON editing is not required.

## First-run sequence

1. Welcome and data-flow explanation.
2. phpVMS base URL.
3. phpVMS API key.
4. Connection test and authenticated pilot.
5. Simulator target, library discovery or selection, validation, and optional live test.
6. System, Light, or Dark appearance and accessibility preferences.
7. Readiness summary.

A simulator library is optional during first run. Skipping it leaves the simulator status as Not available while settings, diagnostics, phpVMS setup, and replay remain usable.

## Simulator settings

MSFS 2020 and MSFS 2024 have independent selections and live-test attestations. The setup UI provides:

- selected absolute library path;
- discovery source and target hints for candidates during the current session;
- structural validation status and messages for the current action; and
- a persisted target-specific live-test record containing file identity, simulator identity, capability, and timestamp.

Changing the selected path clears its displayed verified state when the selection is saved or tested. Every test or live connection validates the current file again; a changed hash, size, or write time no longer matches the stored attestation.

Never point both targets at a file merely because its name looks similar. Each target must pass its own validation and live test. See [SimConnect](SIMCONNECT.md).

## phpVMS settings

- Base URL, HTTPS by default.
- Credential, stored through Windows DPAPI for the current user.
- Optional position-report interval and supported operational preferences.
- Explicit developer-only HTTP allowance with a warning, restricted to localhost and other loopback addresses.

API keys do not belong in settings JSON, logs, screenshots, or support bundles.

## Appearance and accessibility

Appearance choices are System, Light, and Dark. Windows High Contrast overrides decorative styling where necessary. The choice applies without an application restart and is remembered.

The application must not require a mouse. Status is conveyed in text, not color alone.

## Aircraft profiles

Profile precedence is:

1. bundled universal defaults;
2. bundled upstream phpVMS profiles;
3. Climb and Maintain maintained profiles;
4. local user profiles.

Local profiles are stored under:

~~~text
%LocalAppData%\ClimbAndMaintain\ACARS\Profiles
~~~

Only valid declarative profiles participate. Invalid profiles are reported without disabling universal telemetry. See [Aircraft profiles](AIRCRAFT-PROFILES.md).

## Data locations

Program files and user data are separate so upgrades do not erase active flights or settings. Diagnostics displays the effective paths rather than requiring users to infer them.
