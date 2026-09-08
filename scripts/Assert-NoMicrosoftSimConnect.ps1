[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [switch] $InspectBuildMetadata,

    [switch] $RequireApplicationAdapter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $Path).Path
$files = @(
    Get-ChildItem -LiteralPath $root -Recurse -Force -File |
        Where-Object {
            $_.FullName -notmatch '[\\/]\.git[\\/]'
        }
)

$forbiddenNamePatterns = @(
    '(?i)^SimConnect.*[.]dll$',
    '(?i)^Microsoft[.]FlightSimulator[.]SimConnect.*[.]dll$'
)

$forbiddenFiles = @(
    foreach ($file in $files) {
        foreach ($pattern in $forbiddenNamePatterns) {
            if ($file.Name -match $pattern) {
                $file.FullName
                break
            }
        }
    }
)

$forbiddenMetadata = @()
if ($InspectBuildMetadata) {
    $metadataExtensions = @(
        '.csproj',
        '.props',
        '.targets',
        '.lock.json',
        '.deps.json',
        '.runtimeconfig.json',
        '.config',
        '.xml'
    )

    foreach ($file in $files) {
        $isMetadata = $false
        foreach ($extension in $metadataExtensions) {
            if ($file.Name.EndsWith($extension, [StringComparison]::OrdinalIgnoreCase)) {
                $isMetadata = $true
                break
            }
        }

        if (-not $isMetadata) {
            continue
        }

        $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
        $containsForbiddenReference = $null -ne $content -and $content -match '(?i)(Microsoft[.]FlightSimulator[.]SimConnect|EnableMsfsSdk|MSFS(?:2020|2024)?_SDK_PATH|<PackageReference[^>]+Include\s*=\s*["''][^"'']*SimConnect)'
        $containsForbiddenLockedPackage = $file.Name.EndsWith(
            '.lock.json',
            [StringComparison]::OrdinalIgnoreCase) -and
            $null -ne $content -and
            $content -match '(?i)"(?!climbandmaintain[.]acars[.]simconnect")[^"]*SimConnect[^"]*"\s*:'
        if ($containsForbiddenReference -or $containsForbiddenLockedPackage) {
            $forbiddenMetadata += $file.FullName
        }
    }
}

$unsafeNsisDirectives = @()
if ($InspectBuildMetadata) {
    foreach ($script in @($files | Where-Object { $_.Name.EndsWith('.nsi', [StringComparison]::OrdinalIgnoreCase) })) {
        foreach ($line in @(Get-Content -LiteralPath $script.FullName)) {
            $directive = $line.Trim()
            if ($directive.Length -eq 0 -or $directive.StartsWith(';', [StringComparison]::Ordinal) -or
                $directive -notmatch '(?i)^File(?:\s|$)') {
                continue
            }

            $hasRecursiveOrWildcardInput = $directive -match '(?i)(?:^|\s)/r(?:\s|$)' -or
                $directive -match '[*?]'
            $excludesNativePrefix = $directive -match '(?i)/x\s+["'']?SimConnect[*][.]dll["'']?'
            $excludesManagedPrefix = $directive -match '(?i)/x\s+["'']?Microsoft[.]FlightSimulator[.]SimConnect[*][.]dll["'']?'
            $includeText = $directive -replace '(?i)/x\s+(?:"[^"]+"|''[^'']+''|\S+)', ''
            $directlyIncludesForbiddenName = $includeText -match '(?i)(?:^|[\\/"''])SimConnect[^\\/"'']*[.]dll(?:["'']|$)' -or
                $includeText -match '(?i)(?:^|[\\/"''])Microsoft[.]FlightSimulator[.]SimConnect[^\\/"'']*[.]dll(?:["'']|$)'

            if ($directlyIncludesForbiddenName -or
                ($hasRecursiveOrWildcardInput -and (-not $excludesNativePrefix -or -not $excludesManagedPrefix))) {
                $unsafeNsisDirectives += "$($script.FullName): $directive"
            }
        }
    }
}

if ($forbiddenFiles.Count -gt 0 -or $forbiddenMetadata.Count -gt 0 -or $unsafeNsisDirectives.Count -gt 0) {
    foreach ($pathFound in $forbiddenFiles) {
        Write-Error "Forbidden Microsoft SimConnect binary name: $pathFound"
    }
    foreach ($pathFound in $forbiddenMetadata) {
        Write-Error "Forbidden SDK conditional or managed-wrapper reference in build metadata: $pathFound"
    }
    foreach ($directiveFound in $unsafeNsisDirectives) {
        Write-Error "Unsafe NSIS File directive can include a Microsoft SimConnect binary: $directiveFound"
    }
    throw 'Distribution audit failed. Microsoft SimConnect binaries, SDK build conditionals, and managed-wrapper references are prohibited.'
}

if ($RequireApplicationAdapter) {
    $adapterFiles = @($files | Where-Object { $_.Name -eq 'ClimbAndMaintain.Acars.SimConnect.dll' })
    if ($adapterFiles.Count -eq 0) {
        throw 'The complete ClimbAndMaintain.Acars.SimConnect adapter is missing from the inspected build output.'
    }
}

Write-Host "Distribution audit passed for $root."
