[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InstallerPath,

    [string] $SevenZipPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
if (-not [string]::Equals(
        [IO.Path]::GetFileName($installer),
        'ClimbAndMaintain-ACARS-Setup-x64.exe',
        [StringComparison]::Ordinal)) {
    throw 'Installer payload audit accepts only the canonical ClimbAndMaintain-ACARS-Setup-x64.exe filename.'
}

if ([string]::IsNullOrWhiteSpace($SevenZipPath)) {
    $sevenZipCommand = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($null -ne $sevenZipCommand) {
        $SevenZipPath = $sevenZipCommand.Source
    }
    else {
        $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
        $candidate = Join-Path $programFiles '7-Zip/7z.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $SevenZipPath = $candidate
        }
    }
}

if ([string]::IsNullOrWhiteSpace($SevenZipPath) -or
    -not (Test-Path -LiteralPath $SevenZipPath -PathType Leaf)) {
    throw '7z.exe is required to enumerate and extract the final NSIS payload for distribution auditing.'
}

$temporaryRoot = [IO.Path]::GetTempPath()
$extractDirectory = Join-Path $temporaryRoot ("cm-acars-installer-audit-$([Guid]::NewGuid().ToString('N'))")
New-Item -ItemType Directory -Path $extractDirectory | Out-Null
try {
    & $SevenZipPath x -y "-o$extractDirectory" $installer | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip could not extract the NSIS installer (exit code $LASTEXITCODE)."
    }

    & (Join-Path $PSScriptRoot 'Assert-NoMicrosoftSimConnect.ps1') -Path $extractDirectory -InspectBuildMetadata -RequireApplicationAdapter
    Write-Host "Final NSIS payload audit passed for $installer."
}
finally {
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
}
