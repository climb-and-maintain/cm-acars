# Support

Use the narrowest support route so reports reach the right maintainer.

## Bugs

Use the bug issue form for reproducible application failures. Include version, Windows version, expected and actual behavior, and a sanitized support bundle where relevant.

## Aircraft compatibility

Use the aircraft compatibility issue form. Include simulator edition, aircraft and version, selected profile, affected feature, standard-telemetry result, and an exported sanitized aircraft diagnostic. Never upload aircraft files, vendor SDK documentation, or simulator DLLs.

## Accessibility

Use the accessibility issue form for keyboard, Narrator, UI Automation, contrast, High Contrast, scaling, focus, or text-clipping problems. Accessibility defects are release-quality defects.

## Installation and SimConnect setup

Read [Installation](docs/INSTALLATION.md), [SimConnect](docs/SIMCONNECT.md), and [Troubleshooting](docs/TROUBLESHOOTING.md) first. A valid report should state whether the problem occurred during discovery, file validation, native loading, live connection, or telemetry. MSFS 2020 and MSFS 2024 results must be reported separately.

The project cannot provide or download Microsoft DLLs and cannot determine whether a user's copy was lawfully obtained.

## phpVMS server configuration

Read [phpVMS integration](docs/PHPMVS-INTEGRATION.md). Server administrators should reproduce API behavior on a test site, not a production VA. Remove keys and headers from all reports.

## Feature requests

Use the feature issue form and explain the operational problem, accessibility impact, compatibility constraints, and why it belongs in the simulator-neutral core or an adapter.

## Security

Do not file exploitable defects publicly. Follow [SECURITY.md](SECURITY.md).
