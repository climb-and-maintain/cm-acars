# Troubleshooting

Start with Diagnostics. Use Copy Diagnostics or Export Support Bundle only after reviewing the sanitized output. Never attach API keys, simulator DLLs, aircraft files, or proprietary SDK documentation.

## Application will not launch

A missing SimConnect library must not prevent launch. If startup fails, the problem is not an acceptable simulator-prerequisite failure. Record the application version, Windows version, exception text, and sanitized log, then use the bug issue form.

## No library was discovered

Automatic discovery is intentionally bounded and may not find every legitimate location.

1. Confirm that you possess a native x64 library from a Microsoft-provided source you are entitled to use.
2. Choose the correct target simulator.
3. Use Browse and select the file manually.
4. Run validation.

The application will not download a DLL for you.

## Wrong architecture or invalid PE image

The Windows x64 application requires a native x64 library. A managed assembly, x86 binary, damaged file, or unrelated DLL will be rejected. Select the correct source file; changing application build settings is not a remedy.

## Missing required exports

The selected file does not expose the minimal native API required by this release. It may be a managed wrapper, an unsupported version, or unrelated. Do not rename DLLs to bypass the check.

## Target mismatch

The library failed the selected simulator's identity or live test. Return to Simulator settings and choose a library intended for that simulator. Passing MSFS 2020 validation never implies MSFS 2024 compatibility, and vice versa.

## Native load failed

Diagnostics should show the Windows load error and whether a dependency was missing. Keep the selected DLL in its original supported location where its required dependencies can be resolved. Do not copy random dependencies beside the application.

## Structural validation passes but connection fails

- Start the exact simulator edition selected in Settings.
- Wait until the simulator is ready to accept external connections.
- Confirm that another simulator edition is not being mistaken for the target.
- Re-run Test connection and inspect the reported simulator identity.
- If the simulator was restarted, allow the provider to move through Recovering.

## Connection works but telemetry is incomplete

Check aircraft identity and Profile Inspector. Universal SimVars should provide basic tracking; enhanced switches may require a matching profile or an aircraft-local option such as PMDG data broadcast. A missing custom value should be shown as unavailable rather than stopping the flight.

Export an aircraft diagnostic and use the aircraft compatibility issue form.

## phpVMS is offline or throttling

The application stores outbound data locally. Diagnostics shows queued positions, events, and logs. For HTTP 429, it honors Retry-After; for timeouts and server errors, it backs off with jitter. Do not repeatedly restart the app to force uploads.

## Unfinished flight after restart

Choose Resume to continue, Inspect to review the saved session, or Discard only when the record is no longer needed. Network or simulator loss must not silently remove it.

## Where to get help

See [SUPPORT.md](../SUPPORT.md) and select the issue form matching the problem.
