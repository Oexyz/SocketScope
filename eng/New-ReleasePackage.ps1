#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')]
    [string] $RuntimeIdentifier,

    [ValidateSet('Release')]
    [string] $Configuration = 'Release',

    [string] $OutputDirectory,

    [switch] $RequireClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$comparison = if ($IsWindows) {
    [StringComparison]::OrdinalIgnoreCase
} else {
    [StringComparison]::Ordinal
}

function Assert-WorkspacePath {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $repositoryRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, $comparison)) {
        throw "Release working path is outside the repository: $fullPath"
    }

    return $fullPath
}

function Remove-WorkspaceDirectory {
    param([Parameter(Mandatory)][string] $Path)

    $safePath = Assert-WorkspacePath $Path
    if (Test-Path -LiteralPath $safePath) {
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}

function Find-DotNet {
    if ($env:DOTNET_HOST_PATH -and (Test-Path -LiteralPath $env:DOTNET_HOST_PATH)) {
        return $env:DOTNET_HOST_PATH
    }

    $workspaceHost = if ($IsWindows) {
        Join-Path $repositoryRoot '.tools/dotnet/dotnet.exe'
    } else {
        Join-Path $repositoryRoot '.tools/dotnet-linux/dotnet'
    }
    if (Test-Path -LiteralPath $workspaceHost) {
        return $workspaceHost
    }

    $command = Get-Command dotnet -ErrorAction Stop
    return $command.Source
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

function Add-ZipPackage {
    param(
        [Parameter(Mandatory)][string] $SourceDirectory,
        [Parameter(Mandatory)][string] $PackageRootName,
        [Parameter(Mandatory)][string] $ArchivePath
    )

    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($ArchivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $timestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | Sort-Object FullName) {
                $relative = [IO.Path]::GetRelativePath($SourceDirectory, $file.FullName).Replace('\', '/')
                $entry = $archive.CreateEntry(
                    "$PackageRootName/$relative",
                    [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $entryStream = $entry.Open()
                $input = [IO.File]::OpenRead($file.FullName)
                try {
                    $input.CopyTo($entryStream)
                } finally {
                    $input.Dispose()
                    $entryStream.Dispose()
                }
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Add-TarGzipPackage {
    param(
        [Parameter(Mandatory)][string] $SourceDirectory,
        [Parameter(Mandatory)][string] $PackageRootName,
        [Parameter(Mandatory)][string] $ArchivePath
    )

    $temporaryTar = "$ArchivePath.tar"
    if (Test-Path -LiteralPath $temporaryTar) {
        Remove-Item -LiteralPath $temporaryTar -Force
    }

    $tarStream = [IO.File]::Open($temporaryTar, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [System.Formats.Tar.TarWriter]::new(
            $tarStream,
            [System.Formats.Tar.TarEntryFormat]::Pax,
            $false)
        try {
            $timestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $filesByRelativePath = [Collections.Generic.SortedDictionary[string, IO.FileInfo]]::new(
                [StringComparer]::Ordinal)
            foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File) {
                $relative = [IO.Path]::GetRelativePath(
                    $SourceDirectory,
                    $file.FullName).Replace('\', '/')
                $filesByRelativePath.Add($relative, $file)
            }

            foreach ($pair in $filesByRelativePath.GetEnumerator()) {
                $relative = $pair.Key
                $file = $pair.Value
                $entryName = "$PackageRootName/$relative"
                # PaxTarEntry synthesizes a per-process PaxHeaders.<id> name, which
                # makes otherwise identical archives differ byte-for-byte. Ustar
                # supports this package's bounded paths and has deterministic headers.
                $entry = [System.Formats.Tar.UstarTarEntry]::new(
                    [System.Formats.Tar.TarEntryType]::RegularFile,
                    $entryName)
                $entry.ModificationTime = $timestamp
                $entry.Uid = 0
                $entry.Gid = 0
                $entry.UserName = 'root'
                $entry.GroupName = 'root'
                $entry.Mode = if ($relative -eq 'OeXYZ.SocketScope') {
                    [Convert]::ToInt32('755', 8)
                } else {
                    [Convert]::ToInt32('644', 8)
                }
                $input = [IO.File]::OpenRead($file.FullName)
                try {
                    $entry.DataStream = $input
                    $writer.WriteEntry($entry)
                } finally {
                    $input.Dispose()
                }
            }
        } finally {
            $writer.Dispose()
        }
    } finally {
        $tarStream.Dispose()
    }

    try {
        $input = [IO.File]::OpenRead($temporaryTar)
        $output = [IO.File]::Open($ArchivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $gzip = [IO.Compression.GZipStream]::new(
                $output,
                [IO.Compression.CompressionLevel]::SmallestSize,
                $false)
            try {
                $input.CopyTo($gzip)
            } finally {
                $gzip.Dispose()
            }
        } finally {
            $output.Dispose()
            $input.Dispose()
        }
    } finally {
        if (Test-Path -LiteralPath $temporaryTar) {
            Remove-Item -LiteralPath $temporaryTar -Force
        }
    }
}

if ($RequireClean) {
    $status = & git -C $repositoryRoot status --porcelain=v1 --untracked-files=all
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to determine the Git working-tree state.'
    }
    if ($status) {
        throw 'Official packaging requires a clean Git working tree.'
    }
}

$dotnet = Find-DotNet
$releaseDirectory = if ($OutputDirectory) {
    Assert-WorkspacePath $OutputDirectory
} else {
    Assert-WorkspacePath (Join-Path $repositoryRoot 'artifacts/release')
}
$workRoot = Assert-WorkspacePath (Join-Path $repositoryRoot "artifacts/package-work/$RuntimeIdentifier")
$publishedRoot = Join-Path $workRoot 'published'
$stageBase = Assert-WorkspacePath (Join-Path $repositoryRoot "artifacts/package-root/$RuntimeIdentifier")
$packageRootName = "OeXYZ-SocketScope-v$Version-$RuntimeIdentifier"
$stageRoot = Join-Path $stageBase $packageRootName

Remove-WorkspaceDirectory $workRoot
Remove-WorkspaceDirectory $stageBase
[IO.Directory]::CreateDirectory($publishedRoot) | Out-Null
[IO.Directory]::CreateDirectory($stageRoot) | Out-Null
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null

$sourceState = if (& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all) {
    'dirty'
} else {
    'clean'
}
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $sourceCommit) {
    $sourceCommit = 'uncommitted-worktree'
}

Invoke-DotNet @(
    'restore',
    (Join-Path $repositoryRoot 'OeXYZ.SocketScope.slnx'),
    '--locked-mode')

$desktopProject = Join-Path $repositoryRoot 'src/OeXYZ.SocketScope.Desktop/OeXYZ.SocketScope.Desktop.csproj'
Invoke-DotNet @(
    'publish',
    $desktopProject,
    '--configuration', $Configuration,
    '--runtime', $RuntimeIdentifier,
    '--self-contained', 'true',
    '--no-restore',
    '--output', $publishedRoot,
    "/p:Version=$Version",
    "/p:AssemblyVersion=$Version.0",
    "/p:FileVersion=$Version.0",
    '/p:DebugSymbols=false',
    '/p:DebugType=None',
    '/p:UseAppHost=true',
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true',
    '/p:EnableCompressionInSingleFile=true',
    '/p:PublishTrimmed=false')

$executableName = if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)) {
    'OeXYZ.SocketScope.exe'
} else {
    'OeXYZ.SocketScope'
}
$publishedExecutable = Join-Path $publishedRoot $executableName
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "The single-file publish did not produce '$executableName'."
}
$unexpectedPublishFiles = @(Get-ChildItem -LiteralPath $publishedRoot -Recurse -File | Where-Object {
    $_.Name -ne $executableName -and $_.Extension -notin @('.pdb', '.xml')
})
if ($unexpectedPublishFiles.Count -ne 0) {
    throw "The single-file publish emitted an unexpected companion file: '$($unexpectedPublishFiles[0].Name)'."
}
Copy-Item -LiteralPath $publishedExecutable -Destination (Join-Path $stageRoot $executableName)

$forbiddenPublishFiles = Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Where-Object {
    $_.Extension -in @('.pdb', '.cs', '.csproj', '.user')
}
if ($forbiddenPublishFiles) {
    throw "Unexpected publish file: $($forbiddenPublishFiles[0].FullName)"
}

$isWindowsPackage = $RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)
if (-not $isWindowsPackage) {
    foreach ($fileName in @(
        'README.md',
        'LICENSE',
        'SECURITY.md',
        'CHANGELOG.md',
        'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $fileName) -Destination $stageRoot
    }
    $packagedDocs = Join-Path $stageRoot 'docs'
    [IO.Directory]::CreateDirectory($packagedDocs) | Out-Null
    foreach ($document in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs') -File | Sort-Object Name) {
        Copy-Item -LiteralPath $document.FullName -Destination $packagedDocs
    }
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'docs/images') `
        -Destination (Join-Path $packagedDocs 'images') `
        -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'schemas') -Destination (Join-Path $stageRoot 'schemas') -Recurse
    [IO.Directory]::CreateDirectory((Join-Path $stageRoot 'licenses')) | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'src/OeXYZ.SocketScope.Desktop/Assets/Fonts/OFL-Inter.txt') `
        -Destination (Join-Path $stageRoot 'licenses/OFL-Inter.txt')

    $sdkVersion = (& $dotnet --version).Trim()
    $buildMetadata = [ordered]@{
        schema = 'urn:oexyz:socketscope:build-metadata:1'
        version = $Version
        runtimeIdentifier = $RuntimeIdentifier
        targetFramework = 'net10.0'
        dotnetSdk = $sdkVersion
        sourceRepository = 'https://github.com/Oexyz/SocketScope'
        sourceCommit = $sourceCommit.Trim()
        sourceTreeState = $sourceState
        windowsAuthenticode = 'not-present'
    }
    $metadataJson = $buildMetadata | ConvertTo-Json
    [IO.File]::WriteAllText(
        (Join-Path $stageRoot 'BUILD-METADATA.json'),
        "$metadataJson`n",
        [Text.UTF8Encoding]::new($false))
}

$extension = if ($isWindowsPackage) {
    '.exe'
} else {
    '.tar.gz'
}
$packagePath = Join-Path $releaseDirectory "$packageRootName$extension"
if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

if ($extension -eq '.exe') {
    Copy-Item -LiteralPath (Join-Path $stageRoot $executableName) -Destination $packagePath
} else {
    Add-TarGzipPackage -SourceDirectory $stageRoot -PackageRootName $packageRootName -ArchivePath $packagePath
}

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
[pscustomobject]@{
    Package = $packagePath
    PackageRoot = $stageRoot
    RuntimeIdentifier = $RuntimeIdentifier
    Sha256 = $packageHash
    Size = (Get-Item -LiteralPath $packagePath).Length
}
