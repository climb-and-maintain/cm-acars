# Release process

## Release principles

A release is one coherent, tested artifact. Zero warnings, passing automated checks, usable accessibility, durable recovery, and live evidence on both simulator editions are required.

Microsoft binary redistribution is not the release blocker. The project never bundles or downloads those binaries. The simulator release gate is successful evidence that users can supply and activate compatible libraries at runtime on both MSFS 2020 and MSFS 2024.

## Prepare

1. Resolve all release-blocking issues.
2. Update version, CHANGELOG.md, notices, dependency locks, and support claims.
3. Review dependency and imported-profile licenses.
4. Run formatting, analyzers, unit, contract, integration, and UI checks.
5. Run the Release candidate workflow for the intended version and commit.
6. Download its immutable artifact; the workflow has already run tests, audited publish and packaging inputs, extracted the completed NSIS installer, and audited the actual payload.

## Live simulator evidence

Copy docs/release/simulator-validation-evidence.template.json to:

~~~text
docs/release/evidence/vMAJOR.MINOR.PATCH.json
~~~

Validate it against docs/release/simulator-validation-evidence.schema.json and with:

~~~powershell
pwsh scripts/Test-SimulatorValidationEvidence.ps1 -Path docs/release/evidence/v0.1.0.json -RequireComplete
~~~

Record the candidate workflow run ID, immutable artifact name, source commit, exact installer filename, and SHA-256. Test that downloaded artifact. For **each** simulator target:

- verify automatic discovery behavior;
- verify accessible manual selection;
- validate the appropriate user-supplied native x64 library;
- verify the app did not bundle or download it;
- enable connectivity without recompilation or reinstallation;
- run a live connection test that identifies the correct edition;
- verify aircraft identity, standard SimVars, and LVars;
- reload the aircraft;
- restart the simulator and observe recovery; and
- remove the configured library and verify the application still launches with configuration, diagnostics, phpVMS setup, and replay available.

The evidence file must contain exactly one msfs20 and one msfs24 result. A pass for one does not satisfy the other. Do not commit either tested DLL.

## Accessibility and installation

On a clean Windows user profile or VM, test:

- install and first launch with no simulator library;
- keyboard and Narrator;
- System/Light/Dark and High Contrast;
- 200% scaling and increased text;
- first-run wizard and validation messages;
- phpVMS test flow;
- simulator reconnect;
- upgrade with data preservation; and
- uninstall with both keep-data and remove-data choices.

## Build a candidate

Locally, or equivalently in the Release candidate workflow, run:

~~~powershell
pwsh scripts/Build-Release.ps1 -Version 0.1.0 -CandidateBuild
~~~

Expected release files:

~~~text
ClimbAndMaintain-ACARS-Setup-x64.exe
SHA256SUMS.txt
~~~

A portable ZIP is optional and must pass the same binary audit.

## Publish

1. Confirm the installer hash matches the evidence candidate or update and re-run live evidence if the artifact changed.
2. Commit the evidence file. Do not change source/build inputs after the recorded candidate commit.
3. Tag vMAJOR.MINOR.PATCH. The tag may differ from the candidate commit only by the matching evidence file.
4. The release workflow validates evidence, verifies the candidate workflow identity and source commit, downloads that exact immutable artifact, checks its installer hash, and publishes it without rebuilding.
5. Publish release notes from CHANGELOG.md with verified simulator/aircraft support and known limitations.
6. Retain the committed evidence file as the audit record.

When official signing is configured, it must use repository secrets or an external signing service. The release tooling remains capable of producing unsigned installers for contributors and forks.
