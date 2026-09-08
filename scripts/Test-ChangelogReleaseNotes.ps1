[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$fixturePath = Join-Path $PSScriptRoot '../tests/fixtures/release/CHANGELOG.md'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("cm-acars-changelog-test-" + [Guid]::NewGuid().ToString('N'))
$outputPath = Join-Path $temporaryDirectory 'release-notes.md'

try {
    & (Join-Path $PSScriptRoot 'Get-ChangelogReleaseNotes.ps1') `
        -Version '1.2.3' `
        -ChangelogPath $fixturePath `
        -OutputPath $outputPath

    $actual = (Get-Content -LiteralPath $outputPath -Raw).Replace("`r`n", "`n").TrimEnd("`n")
    $expected = @(
        '## [1.2.3] - 2026-09-08'
        ''
        '### Added'
        ''
        '- Candidate-specific release note.'
        '- Known limitation recorded for this candidate.'
    ) -join "`n"

    if ($actual -cne $expected) {
        throw "Extracted release notes did not match the requested changelog section.`nActual:`n$actual"
    }

    $missingVersionWasRejected = $false
    try {
        & (Join-Path $PSScriptRoot 'Get-ChangelogReleaseNotes.ps1') `
            -Version '9.9.9' `
            -ChangelogPath $fixturePath `
            -OutputPath (Join-Path $temporaryDirectory 'missing.md')
    }
    catch {
        $missingVersionWasRejected = $true
    }

    if (-not $missingVersionWasRejected) {
        throw 'The extractor accepted a version that is absent from the changelog.'
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
