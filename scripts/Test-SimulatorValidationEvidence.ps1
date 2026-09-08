[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [switch] $RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$schemaPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../docs/release/simulator-validation-evidence.schema.json')).Path
try {
    $rawEvidence = Get-Content -LiteralPath $resolvedPath -Raw
    if (-not (Test-Json -Json $rawEvidence -SchemaFile $schemaPath -ErrorAction Stop)) {
        throw 'Evidence does not conform to simulator-validation-evidence.schema.json.'
    }
    $evidence = $rawEvidence | ConvertFrom-Json -Depth 100
}
catch {
    throw "Evidence failed JSON or schema validation: $($_.Exception.Message)"
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-MeaningfulText {
    param(
        [AllowNull()]
        [object] $Value,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $text = [string] $Value
    Assert-Condition ($text.Length -gt 0) "$Name must not be empty."
    if ($RequireComplete) {
        Assert-Condition (-not $text.StartsWith('REPLACE', [StringComparison]::OrdinalIgnoreCase)) "$Name still contains a template placeholder."
    }
}

Assert-Condition ($evidence.schemaVersion -eq '1.0') 'schemaVersion must be 1.0.'
Assert-Condition ([string] $evidence.releaseVersion -match '^[0-9]+[.][0-9]+[.][0-9]+(?:-[0-9A-Za-z.-]+)?$') 'releaseVersion is not valid semantic version text.'
Assert-Condition ([string] $evidence.applicationCommit -match '^[0-9a-f]{40}$') 'applicationCommit must be a lowercase 40-character Git object ID.'
Assert-Condition ([string] $evidence.artifact.sha256 -match '^[0-9a-fA-F]{64}$') 'artifact.sha256 must be a SHA-256 value.'
Assert-MeaningfulText $evidence.artifact.fileName 'artifact.fileName'
Assert-Condition ([string] $evidence.artifact.fileName -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$') 'artifact.fileName contains unsupported characters.'
Assert-Condition ([string] $evidence.candidateWorkflow.workflowRunId -match '^[0-9]+$') 'candidateWorkflow.workflowRunId must contain digits only.'
Assert-MeaningfulText $evidence.candidateWorkflow.artifactName 'candidateWorkflow.artifactName'
Assert-Condition ([string] $evidence.candidateWorkflow.artifactName -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$') 'candidateWorkflow.artifactName contains unsupported characters.'
$expectedArtifactName = "release-candidate-v$($evidence.releaseVersion)-$($evidence.applicationCommit)"
if ($RequireComplete) {
    Assert-Condition ($evidence.candidateWorkflow.artifactName -eq $expectedArtifactName) 'candidateWorkflow.artifactName does not match the immutable release candidate coordinates.'
}

Assert-Condition ($evidence.distributionPolicy.microsoftBinaryBundled -eq $false) 'Microsoft binaries must not be bundled.'
Assert-Condition ($evidence.distributionPolicy.microsoftBinaryDownloadedByApplication -eq $false) 'The application must not download Microsoft binaries.'
Assert-Condition ($evidence.distributionPolicy.managedWrapperReferenced -eq $false) 'The managed SimConnect wrapper must not be referenced.'
Assert-Condition ($evidence.host.architecture -eq 'x64') 'The validation host architecture must be x64.'
Assert-MeaningfulText $evidence.host.windowsEdition 'host.windowsEdition'
Assert-MeaningfulText $evidence.host.windowsBuild 'host.windowsBuild'
Assert-MeaningfulText $evidence.tester.identifier 'tester.identifier'
Assert-MeaningfulText $evidence.attestation 'attestation'
$testedAt = [DateTimeOffset]::MinValue
Assert-Condition ([DateTimeOffset]::TryParse([string] $evidence.testedAtUtc, [ref] $testedAt)) 'testedAtUtc must be an ISO-compatible date and time.'

$simulators = @($evidence.simulators)
Assert-Condition ($simulators.Count -eq 2) 'Evidence must contain exactly two simulator records.'
$targets = @($simulators | ForEach-Object { [string] $_.target })
Assert-Condition (($targets | Where-Object { $_ -eq 'msfs20' }).Count -eq 1) 'Evidence must contain exactly one msfs20 record.'
Assert-Condition (($targets | Where-Object { $_ -eq 'msfs24' }).Count -eq 1) 'Evidence must contain exactly one msfs24 record.'

foreach ($simulator in $simulators) {
    $prefix = [string] $simulator.target
    Assert-MeaningfulText $simulator.simulatorVersion "$prefix.simulatorVersion"
    Assert-MeaningfulText $simulator.library.fileName "$prefix.library.fileName"
    Assert-MeaningfulText $simulator.library.fileVersion "$prefix.library.fileVersion"
    Assert-MeaningfulText $simulator.library.sourceDescription "$prefix.library.sourceDescription"
    Assert-Condition ([string] $simulator.library.sha256 -match '^[0-9a-fA-F]{64}$') "$prefix.library.sha256 must be a SHA-256 value."
    Assert-Condition ($simulator.library.obtainedByUser -eq $true) "$prefix library must be user-obtained."
    Assert-Condition ($simulator.library.bundledByApplication -eq $false) "$prefix library must not be bundled."
    Assert-Condition ($simulator.library.downloadedByApplication -eq $false) "$prefix library must not be downloaded by the application."
    Assert-Condition ($simulator.workflow.reportedTarget -eq $simulator.target) "$prefix live connection reported the wrong simulator target."
    Assert-Condition ($simulator.workflow.enabledWithoutRecompile -eq $true) "$prefix was not enabled without recompilation."
    Assert-Condition ($simulator.workflow.enabledWithoutReinstall -eq $true) "$prefix was not enabled without reinstallation."
    Assert-Condition ($simulator.workflow.applicationRestartRequired -eq $false) "$prefix required an application restart after selecting the user-supplied library."

    $checks = @(
        @{ Name = 'automaticDiscovery'; Value = $simulator.workflow.automaticDiscovery },
        @{ Name = 'manualSelection'; Value = $simulator.workflow.manualSelection },
        @{ Name = 'structuralValidation'; Value = $simulator.workflow.structuralValidation },
        @{ Name = 'connectionTest'; Value = $simulator.workflow.connectionTest },
        @{ Name = 'aircraftIdentity'; Value = $simulator.telemetry.aircraftIdentity },
        @{ Name = 'standardSimVars'; Value = $simulator.telemetry.standardSimVars },
        @{ Name = 'readOnlyLVars'; Value = $simulator.telemetry.readOnlyLVars },
        @{ Name = 'aircraftReload'; Value = $simulator.recovery.aircraftReload },
        @{ Name = 'simulatorRestart'; Value = $simulator.recovery.simulatorRestart }
    )

    foreach ($check in $checks) {
        $validResults = @('pass', 'fail', 'blocked', 'not-run')
        Assert-Condition ($check.Value.result -in $validResults) "$prefix.$($check.Name) has an invalid result."
        if ($RequireComplete) {
            Assert-Condition ($check.Value.result -eq 'pass') "$prefix.$($check.Name) must pass for release."
        }
    }

    Assert-Condition ($simulator.degradedMode.applicationLaunched -eq $true) "$prefix missing-library mode blocked application launch."
    Assert-Condition ($simulator.degradedMode.configurationAvailable -eq $true) "$prefix missing-library mode blocked configuration."
    Assert-Condition ($simulator.degradedMode.diagnosticsAvailable -eq $true) "$prefix missing-library mode blocked diagnostics."
    Assert-Condition ($simulator.degradedMode.phpVmsSetupAvailable -eq $true) "$prefix missing-library mode blocked phpVMS setup."
    Assert-Condition ($simulator.degradedMode.replayAvailable -eq $true) "$prefix missing-library mode blocked replay."
    Assert-Condition ($simulator.degradedMode.result -in @('pass', 'fail', 'blocked', 'not-run')) "$prefix.degradedMode has an invalid result."
    if ($RequireComplete) {
        Assert-Condition ($simulator.degradedMode.result -eq 'pass') "$prefix degraded-mode test must pass for release."
    }
}

if ($RequireComplete) {
    $allZeroCommit = -join ('0' * 40)
    $allZeroHash = -join ('0' * 64)
    Assert-Condition ($evidence.applicationCommit -ne $allZeroCommit) 'applicationCommit still contains the template value.'
    Assert-Condition ($evidence.releaseVersion -ne '0.0.0') 'releaseVersion still contains the template value.'
    Assert-Condition ($evidence.artifact.sha256 -ne $allZeroHash) 'artifact.sha256 still contains the template value.'
    Assert-Condition ($evidence.candidateWorkflow.workflowRunId -ne '0') 'candidateWorkflow.workflowRunId still contains the template value.'
    Assert-Condition ($testedAt -gt [DateTimeOffset]::Parse('2020-01-01T00:00:00Z')) 'testedAtUtc still contains a template or implausible value.'
    Assert-Condition ($testedAt -le [DateTimeOffset]::UtcNow.AddMinutes(5)) 'testedAtUtc is implausibly far in the future.'
    foreach ($simulator in $simulators) {
        Assert-Condition ($simulator.library.sha256 -ne $allZeroHash) "$($simulator.target).library.sha256 still contains the template value."
    }
}

$mode = if ($RequireComplete) { 'release-completeness' } else { 'structural' }
Write-Host "Simulator validation evidence passed $mode checks."
