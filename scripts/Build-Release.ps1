[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+[.][0-9]+[.][0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $CandidateBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $CandidateBuild) {
    throw 'Build-Release creates an immutable candidate. Pass -CandidateBuild; publish that exact tested candidate through the release workflow.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repositoryRoot 'ClimbAndMaintain.Acars.slnx'
$appProject = Join-Path $repositoryRoot 'src/ClimbAndMaintain.Acars.App/ClimbAndMaintain.Acars.App.csproj'
$publishDirectory = Join-Path $repositoryRoot 'artifacts/publish/win-x64'
$releaseDirectory = Join-Path $repositoryRoot 'artifacts/release'
$installerScript = Join-Path $repositoryRoot 'installer/ClimbAndMaintain.Acars.nsi'
$installerName = 'ClimbAndMaintain-ACARS-Setup-x64.exe'
$installerPath = Join-Path $releaseDirectory $installerName
$checksumsPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'

foreach ($requiredPath in @($solution, $appProject, $installerScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release input does not exist: $requiredPath"
    }
}

foreach ($generatedDirectory in @($publishDirectory, $releaseDirectory)) {
    if (Test-Path -LiteralPath $generatedDirectory) {
        Remove-Item -LiteralPath $generatedDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $generatedDirectory | Out-Null
}

Push-Location $repositoryRoot
try {
    & (Join-Path $PSScriptRoot 'Assert-NoMicrosoftSimConnect.ps1') -Path $repositoryRoot -InspectBuildMetadata

    dotnet clean $solution --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'dotnet clean failed.' }

    dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    dotnet restore $appProject --locked-mode --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'runtime-specific application restore failed.' }

    dotnet format $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet format verification failed.' }

    dotnet build $solution --configuration Release --no-restore -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    dotnet test --solution $solution --configuration Release --no-restore --no-build
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true --no-restore -p:Version=$Version -p:PublishTrimmed=false -p:PublishSingleFile=false --output $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    foreach ($runtimeLegalFile in @(
        'DOTNET_RUNTIME_LICENSE.txt',
        'DOTNET_RUNTIME_THIRD_PARTY_NOTICES.txt',
        'WINDOWS_DESKTOP_RUNTIME_LICENSE.txt'
    )) {
        $runtimeLegalPath = Join-Path $publishDirectory $runtimeLegalFile
        if (-not (Test-Path -LiteralPath $runtimeLegalPath -PathType Leaf)) {
            throw "Self-contained publish is missing required runtime legal file: $runtimeLegalFile"
        }
    }

    & (Join-Path $PSScriptRoot 'Assert-NoMicrosoftSimConnect.ps1') -Path $publishDirectory -InspectBuildMetadata -RequireApplicationAdapter

    $makensisCommand = Get-Command makensis.exe -ErrorAction SilentlyContinue
    $makensisPath = if ($null -ne $makensisCommand) { $makensisCommand.Source } else { $null }
    if ($null -eq $makensisPath) {
        $fallback = Join-Path ${env:ProgramFiles(x86)} 'NSIS/makensis.exe'
        if (Test-Path -LiteralPath $fallback -PathType Leaf) {
            $makensisPath = $fallback
        }
    }
    if ($null -eq $makensisPath) {
        throw 'makensis.exe was not found. Install NSIS or add it to PATH.'
    }

    $numericVersion = ($Version -split '-', 2)[0]
    $fileVersion = "$numericVersion.0"
    & $makensisPath '/V3' "/DPRODUCT_VERSION=$Version" "/DPRODUCT_FILE_VERSION=$fileVersion" "/DPUBLISH_DIR=$publishDirectory" "/DOUTPUT_DIR=$releaseDirectory" $installerScript
    if ($LASTEXITCODE -ne 0) { throw 'NSIS packaging failed.' }

    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "NSIS did not create $installerPath."
    }

    & (Join-Path $PSScriptRoot 'Assert-NsisPayload.ps1') -InstallerPath $installerPath
    & (Join-Path $PSScriptRoot 'Assert-NoMicrosoftSimConnect.ps1') -Path $releaseDirectory -InspectBuildMetadata
    $hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $installerName" | Set-Content -LiteralPath $checksumsPath -Encoding utf8NoBOM
    $releaseFiles = @(Get-ChildItem -LiteralPath $releaseDirectory -File)
    $expectedReleaseNames = @($installerName, 'SHA256SUMS.txt')
    if ($releaseFiles.Count -ne 2 -or @($releaseFiles | Where-Object { $_.Name -notin $expectedReleaseNames }).Count -ne 0) {
        throw 'Release output must contain exactly the canonical installer and SHA256SUMS.txt.'
    }
    Write-Host "Release candidate created: $installerPath"
    Write-Host "SHA-256: $hash"
}
finally {
    Pop-Location
}
