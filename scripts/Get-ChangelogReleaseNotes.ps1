[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+[.][0-9]+[.][0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $ChangelogPath = (Join-Path $PSScriptRoot '../CHANGELOG.md'),

    [Parameter(Mandatory)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedChangelog = (Resolve-Path -LiteralPath $ChangelogPath).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$pathComparison = if ($IsWindows) {
    [StringComparison]::OrdinalIgnoreCase
}
else {
    [StringComparison]::Ordinal
}

if ([string]::Equals($resolvedChangelog, $resolvedOutput, $pathComparison)) {
    throw 'OutputPath must not overwrite the changelog.'
}

$lines = @(Get-Content -LiteralPath $resolvedChangelog)
$escapedVersion = [regex]::Escape($Version)
$versionHeading = "^##[ ]+\[$escapedVersion\](?:[ ]+-[ ]+[0-9]{4}-[0-9]{2}-[0-9]{2})?[ ]*$"
$headingIndexes = @(
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match $versionHeading) {
            $index
        }
    }
)

if ($headingIndexes.Count -ne 1) {
    throw "CHANGELOG.md must contain exactly one '## [$Version]' section; found $($headingIndexes.Count)."
}

$sectionStart = $headingIndexes[0]
$sectionEnd = $lines.Count
for ($index = $sectionStart + 1; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^##[ ]+\[') {
        $sectionEnd = $index
        break
    }
}

$notes = [Collections.Generic.List[string]]::new()
for ($index = $sectionStart; $index -lt $sectionEnd; $index++) {
    $notes.Add($lines[$index])
}

while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[0])) {
    $notes.RemoveAt(0)
}
while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[$notes.Count - 1])) {
    $notes.RemoveAt($notes.Count - 1)
}

if ($notes.Count -le 1 -or ($notes | Select-Object -Skip 1 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) {
    throw "The CHANGELOG.md section for version $Version is empty."
}

$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$content = ($notes -join [Environment]::NewLine) + [Environment]::NewLine
Set-Content -LiteralPath $resolvedOutput -Value $content -Encoding utf8NoBOM -NoNewline
