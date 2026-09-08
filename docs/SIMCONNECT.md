# SimConnect runtime setup

## Policy in one sentence

Climb and Maintain ACARS always contains its complete simulator adapter, but never bundles or downloads Microsoft's SimConnect DLLs; the user selects a compatible native x64 library at runtime.

The repository builds without Microsoft's native DLL, managed wrapper, or an installed MSFS SDK. Adding a compatible library to an installed application enables simulator connectivity without recompilation or reinstallation.

## Obtain a library

Use only a library obtained from a Microsoft-provided installation or source that you are entitled to use. This project cannot provide the DLL, mirror it, copy it from another open-source application, or determine whether an arbitrary copy is lawfully obtained.

Do not select:

- a DLL downloaded from an unofficial file-sharing site;
- Microsoft's managed Microsoft.FlightSimulator.SimConnect wrapper;
- an x86 library;
- an aircraft-vendor DLL; or
- a file supplied by another application merely because it has a similar name.

Loading a native DLL executes code in this application's process. Selection is therefore a trust decision as well as a compatibility decision. Store it in a directory you trust and control: Windows may load the native library's sibling dependencies from that directory. Immediately before loading, the app revalidates the selected file while holding a read lease that denies file writes and deletion until the native module is unloaded.

## Configure MSFS 2020

1. Open Simulator settings and choose MSFS 2020.
2. Run automatic discovery to list bounded local candidates, or choose Browse.
3. Review the full path, discovery source, and target hint.
4. Run structural validation.
5. Start MSFS 2020 and run the live connection test.
6. Confirm that the result identifies MSFS 2020 before using it for flight tracking.

## Configure MSFS 2024

Repeat the same process with MSFS 2024 selected. The application stores a separate path and target-specific live-test attestation; structural validation is run against the current selection when requested and again before every load. A file accepted for MSFS 2020 is not automatically accepted for MSFS 2024, even when filenames or exported functions resemble one another.

If one physical file is proposed for both targets, record its structural result independently for each configuration and perform a separate target-specific live validation for each simulator.

## Validation stages

The UI distinguishes these results:

1. **Candidate discovered**: a bounded search found a possible file; nothing about compatibility is yet proven.
2. **File validated**: the file exists, is a native x64 PE image without a CLR header, and exposes the required native API surface. This structural check alone is not edition-specific.
3. **Live test attempted**: Windows loads the selected absolute path and its required dependencies using safe search behavior. Load and export-binding failures are reported separately; a successful load proceeds directly to the handshake.
4. **Connection passed**: the library opened a session with the running target simulator and the reported simulator identity matched.
5. **Core telemetry passed**: the quick connection test received standard core SimVars from that simulator edition. It does not request aircraft identity or profile LVars.
6. **Profile telemetry observed**: live tracking received aircraft identity, and Profile Inspector received the profile's read-only SimVars/LVars. This is recorded during the full release-candidate simulator test, not inferred from the quick connection test.

A structural pass is not a live compatibility guarantee. Changing the selected file invalidates later stages.

## What the application never does

- It does not use an EnableMsfsSdk build switch.
- It does not require MSFS_SDK_PATH to compile.
- It does not reference Microsoft's managed wrapper.
- It does not recursively load every DLL found on the computer.
- It does not download or update a Microsoft DLL.
- It does not copy a selected external DLL into release artifacts or support bundles.
- It does not mark one simulator compatible because the other passed.

## Connection and recovery

All calls into a SimConnect instance are serialized through one dispatcher because the API is not treated as thread-safe. The state sequence supports Unavailable, Disconnected, Connecting, Connected, AircraftLoading, Ready, Tracking, Recovering, and Faulted.

Closing or restarting the simulator must not terminate the application or discard the active flight. The provider enters recovery and retries according to its policy. Aircraft reload causes identity and profile detection to run again.

## Missing prerequisites

When no compatible library is configured, the UI shows Simulator: Not available and explains the next action. You can still:

- configure or test phpVMS;
- inspect diagnostics;
- manage profiles;
- replay recorded telemetry; and
- inspect or recover a saved flight.

See [Troubleshooting](TROUBLESHOOTING.md) for failure-specific guidance.
