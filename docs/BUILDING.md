# Building

## Requirements

- Windows x64
- .NET 10 SDK selected by global.json
- Git
- NSIS for Setup.exe only
- 7-Zip for enumerating and extracting the completed NSIS payload during the release audit

Visual Studio is optional. The supported workflow uses VS Code and the dotnet CLI. An installed MSFS SDK, SimConnect.dll, and Microsoft.FlightSimulator.SimConnect.dll are not required and must not become build inputs.

## Restore, build, and test

~~~powershell
dotnet restore ClimbAndMaintain.Acars.slnx --locked-mode
dotnet format ClimbAndMaintain.Acars.slnx --verify-no-changes --no-restore
dotnet build ClimbAndMaintain.Acars.slnx -c Release --no-restore
dotnet test --solution ClimbAndMaintain.Acars.slnx --configuration Release --no-restore --no-build
~~~

There is no EnableMsfsSdk property. A build that silently omits the real runtime adapter is defective.

## Publish

The release target is self-contained Windows x64 with trimming and single-file publishing disabled:

~~~powershell
dotnet restore src/ClimbAndMaintain.Acars.App/ClimbAndMaintain.Acars.App.csproj --locked-mode --runtime win-x64
dotnet publish src/ClimbAndMaintain.Acars.App/ClimbAndMaintain.Acars.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishTrimmed=false -p:PublishSingleFile=false -o artifacts/publish/win-x64
pwsh scripts/Assert-NoMicrosoftSimConnect.ps1 -Path artifacts/publish/win-x64 -InspectBuildMetadata -RequireApplicationAdapter
~~~

The audit must pass before packaging. The project-owned ClimbAndMaintain.Acars.SimConnect assembly is expected; Microsoft native or managed SimConnect binaries are forbidden.

## Installer

With makensis.exe on PATH:

~~~powershell
makensis.exe /DPRODUCT_VERSION=0.1.0 /DPRODUCT_FILE_VERSION=0.1.0.0 /DPUBLISH_DIR=artifacts\publish\win-x64 /DOUTPUT_DIR=artifacts\release installer\ClimbAndMaintain.Acars.nsi
~~~

The NSIS source excludes Microsoft DLL name prefixes even if a contaminated publish directory is supplied. The release build then extracts the completed installer with 7-Zip and audits its actual payload before hashing it. Both checks are mandatory because defense in depth is intentional.

scripts/Build-Release.ps1 performs the standard sequence and produces the installer plus SHA256SUMS.txt:

~~~powershell
pwsh scripts/Build-Release.ps1 -Version 0.1.0 -CandidateBuild
~~~

The hosted release-candidate workflow pins GitHub Actions to reviewed commit SHAs and verifies the exact NSIS 3.12.0 Chocolatey package hash before installing it. When building locally, obtain NSIS and 7-Zip from trusted sources and record the tool versions used for release evidence.

## Signing

Signing is optional and uses external secrets only for official builds. Contributors and forks can build unsigned artifacts. Never put a certificate, private key, or password in source, settings, logs, or workflow artifacts.

## Reproducibility and packages

Use central package versions and committed lock files. The repository declares `win-x64` as its release runtime so the runtime-specific publish graph is present in those locks; both solution restore and publish restore run in locked mode. A package update requires review of its license, release notes, vulnerability state, and resulting third-party notices.

## Live simulator tests

Build success does not establish runtime compatibility. SimulatorIntegration tests are opt-in. A release requires a completed machine-readable evidence file for both simulator editions; see [Releases](RELEASES.md).
