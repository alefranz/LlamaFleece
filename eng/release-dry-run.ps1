using namespace System.IO

param(
    [string]$Tag,
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string[]]$RuntimeId = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [string]$OutputRoot = "artifacts/release-dry-run"
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PathValue)
}

function Get-VersionPrefix {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    [xml]$project = Get-Content $ProjectPath
    $versionPrefix = $project.Project.PropertyGroup.VersionPrefix | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($versionPrefix)) {
        throw "LlamaFleece.csproj is missing VersionPrefix."
    }

    return $versionPrefix.Trim()
}

function Get-ReleaseTarget {
    param([Parameter(Mandatory = $true)][string]$CurrentRuntimeId)

    switch ($CurrentRuntimeId) {
        "win-x64" {
            return @{
                AssetSuffix = "win-x64"
                ArchiveExtension = "zip"
                BinaryFileName = "LlamaFleece.exe"
            }
        }
        "linux-x64" {
            return @{
                AssetSuffix = "linux-x64"
                ArchiveExtension = "tar.gz"
                BinaryFileName = "LlamaFleece"
            }
        }
        "osx-x64" {
            return @{
                AssetSuffix = "macos-x64"
                ArchiveExtension = "tar.gz"
                BinaryFileName = "LlamaFleece"
            }
        }
        "osx-arm64" {
            return @{
                AssetSuffix = "macos-arm64"
                ArchiveExtension = "tar.gz"
                BinaryFileName = "LlamaFleece"
            }
        }
        default {
            throw "Unsupported runtime identifier '$CurrentRuntimeId'."
        }
    }
}

function Copy-ReleasePayload {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$PublishDirectory,
        [Parameter(Mandatory = $true)][string]$StagingDirectory
    )

    Copy-Item (Join-Path $PublishDirectory '*') -Destination $StagingDirectory -Recurse -Force
    Copy-Item (Join-Path $RepoRoot 'README.md') -Destination $StagingDirectory -Force
    Copy-Item (Join-Path $RepoRoot 'LICENSE') -Destination $StagingDirectory -Force
    Copy-Item (Join-Path $RepoRoot 'appsettings.example.json') -Destination $StagingDirectory -Force
    Copy-Item (Join-Path $RepoRoot 'docs') -Destination $StagingDirectory -Recurse -Force
}

function Test-RequiredFiles {
    param(
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$BinaryFileName
    )

    $requiredRelativePaths = @(
        $BinaryFileName,
        'README.md',
        'LICENSE',
        'appsettings.example.json',
        'docs/install.md',
        'docs/configuration.md',
        'docs/screenshots.md'
    )

    $missingPaths = @()

    foreach ($relativePath in $requiredRelativePaths) {
        $fullPath = Join-Path $StagingDirectory $relativePath
        if (-not (Test-Path $fullPath)) {
            $missingPaths += $relativePath
        }
    }

    if ($missingPaths.Count -gt 0) {
        throw "Staged release payload is missing: $($missingPaths -join ', ')"
    }
}

function Get-RelativeMarkdownTargets {
    param([Parameter(Mandatory = $true)][string]$MarkdownPath)

    $content = Get-Content $MarkdownPath -Raw
    $matches = [regex]::Matches($content, '!?:?\[[^\]]*\]\((?<target>[^)\r\n]+)\)')
    $targets = @()

    foreach ($match in $matches) {
        $target = $match.Groups['target'].Value.Trim()

        if ([string]::IsNullOrWhiteSpace($target)) {
            continue
        }

        if ($target.StartsWith('<') -and $target.EndsWith('>')) {
            $target = $target.Substring(1, $target.Length - 2)
        }

        if ($target.StartsWith('#')) {
            continue
        }

        if ($target -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
            continue
        }

        if ($target.StartsWith('//')) {
            continue
        }

        $target = $target.Split('#')[0]
        $target = [Uri]::UnescapeDataString($target)

        if (-not [string]::IsNullOrWhiteSpace($target)) {
            $targets += $target
        }
    }

    return $targets
}

function Test-MarkdownLinks {
    param([Parameter(Mandatory = $true)][string]$StagingDirectory)

    $markdownFiles = @(
        (Join-Path $StagingDirectory 'README.md')
    )

    $markdownFiles += Get-ChildItem (Join-Path $StagingDirectory 'docs') -Filter '*.md' -Recurse |
        Select-Object -ExpandProperty FullName

    $missingLinks = @()

    foreach ($markdownFile in $markdownFiles) {
        $markdownDirectory = Split-Path $markdownFile -Parent

        foreach ($target in Get-RelativeMarkdownTargets -MarkdownPath $markdownFile) {
            $resolvedTarget = [Path]::GetFullPath((Join-Path $markdownDirectory $target))

            if (-not (Test-Path $resolvedTarget)) {
                $relativeMarkdownPath = [Path]::GetRelativePath($StagingDirectory, $markdownFile)
                $missingLinks += "$relativeMarkdownPath -> $target"
            }
        }
    }

    if ($missingLinks.Count -gt 0) {
        throw "Staged release markdown links are broken: $($missingLinks -join '; ')"
    }
}

function New-ReleaseArchive {
    param(
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$ArchiveExtension
    )

    if (Test-Path $ArchivePath) {
        Remove-Item $ArchivePath -Force
    }

    if ($ArchiveExtension -eq 'zip') {
        Compress-Archive -Path $StagingDirectory -DestinationPath $ArchivePath -CompressionLevel Optimal
    }
    else {
        & tar -czf $ArchivePath -C $StagingRoot (Split-Path $StagingDirectory -Leaf)

        if ($LASTEXITCODE -ne 0) {
            throw "tar failed while packaging $ArchivePath."
        }
    }

    if (-not (Test-Path $ArchivePath)) {
        throw "Expected archive '$ArchivePath' was not created."
    }
}

$repoRoot = Resolve-FullPath -PathValue (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'LlamaFleece\LlamaFleece.csproj'
$versionPrefix = Get-VersionPrefix -ProjectPath $projectPath

if (-not $Tag) {
    $Tag = "v$versionPrefix-dryrun"
}

if ($Tag -notmatch '^v\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Tag '$Tag' must match vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease."
}

$resolvedOutputRoot = Resolve-FullPath -PathValue $OutputRoot
$publishRoot = Join-Path $resolvedOutputRoot 'publish'
$releaseRoot = Join-Path $resolvedOutputRoot 'release'
$stagingRoot = Join-Path $resolvedOutputRoot 'staging'

New-Item -ItemType Directory -Force -Path $publishRoot, $releaseRoot, $stagingRoot | Out-Null

$createdArchives = @()

foreach ($currentRuntimeId in $RuntimeId) {
    $releaseTarget = Get-ReleaseTarget -CurrentRuntimeId $currentRuntimeId
    $publishDirectory = Join-Path $publishRoot $currentRuntimeId
    $stagingDirectory = Join-Path $stagingRoot "LlamaFleece-$Tag-$($releaseTarget.AssetSuffix)"
    $archivePath = Join-Path $releaseRoot "LlamaFleece-$Tag-$($releaseTarget.AssetSuffix).$($releaseTarget.ArchiveExtension)"

    if (Test-Path $publishDirectory) {
        Remove-Item $publishDirectory -Recurse -Force
    }

    if (Test-Path $stagingDirectory) {
        Remove-Item $stagingDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $publishDirectory, $stagingDirectory | Out-Null

    Write-Host "Publishing $currentRuntimeId to $publishDirectory"

    $publishArguments = @(
        'publish', $projectPath,
        '--configuration', 'Release',
        '--runtime', $currentRuntimeId,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $publishDirectory
    )

    & dotnet @publishArguments

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Copy-ReleasePayload -RepoRoot $repoRoot -PublishDirectory $publishDirectory -StagingDirectory $stagingDirectory
    Test-RequiredFiles -StagingDirectory $stagingDirectory -BinaryFileName $releaseTarget.BinaryFileName
    Test-MarkdownLinks -StagingDirectory $stagingDirectory
    New-ReleaseArchive -StagingRoot $stagingRoot -StagingDirectory $stagingDirectory -ArchivePath $archivePath -ArchiveExtension $releaseTarget.ArchiveExtension

    $createdArchives += $archivePath
    Write-Host "Validated $currentRuntimeId archive payload at $archivePath"
}

Write-Host ''
Write-Host 'Release dry run completed successfully.'
foreach ($archivePath in $createdArchives) {
    Write-Host " - $archivePath"
}