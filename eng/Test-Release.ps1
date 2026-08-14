#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package,

    [string] $ChecksumsPath,

    [switch] $SkipRuntimeSmoke,

    [switch] $KeepExtracted
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagePath = [IO.Path]::GetFullPath($Package)
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Package does not exist: $packagePath"
}

$match = [regex]::Match(
    [IO.Path]::GetFileName($packagePath),
    '^OeXYZ-SocketScope-v(?<version>\d+\.\d+\.\d+)-(?<rid>(?:win|linux)-(?:x64|arm64))\.(?<format>exe|tar\.gz)$')
if (-not $match.Success) {
    throw 'The package name does not match the immutable updater naming policy.'
}

$version = $match.Groups['version'].Value
$runtimeIdentifier = $match.Groups['rid'].Value
$isDirectWindowsExecutable = $match.Groups['format'].Value -eq 'exe'
if ($isDirectWindowsExecutable -and -not $runtimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)) {
    throw 'A direct executable release asset is valid only for a Windows runtime.'
}
$packageRootName = "OeXYZ-SocketScope-v$version-$runtimeIdentifier"
$extractionBase = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ".cache/release-test/$([Guid]::NewGuid().ToString('N'))"))
$workspacePrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$comparison = if ($IsWindows) {
    [StringComparison]::OrdinalIgnoreCase
} else {
    [StringComparison]::Ordinal
}
if (-not $extractionBase.StartsWith($workspacePrefix, $comparison)) {
    throw 'The release extraction directory escaped the repository.'
}
[IO.Directory]::CreateDirectory($extractionBase) | Out-Null

function Get-SafeDestination {
    param([Parameter(Mandatory)][string] $EntryName)

    $normalized = $EntryName.Replace('\', '/')
    if (-not $normalized -or $normalized.StartsWith('/', [StringComparison]::Ordinal)) {
        throw "Archive entry has an unsafe absolute or empty path: '$EntryName'."
    }
    if ($normalized.Split('/') -contains '..') {
        throw "Archive entry contains path traversal: '$EntryName'."
    }

    $destination = [IO.Path]::GetFullPath((Join-Path $extractionBase $normalized))
    $prefix = $extractionBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $destination.StartsWith($prefix, $comparison)) {
        throw "Archive entry escaped the extraction directory: '$EntryName'."
    }
    return $destination
}

function Expand-ReviewedZip {
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::OpenRead($packagePath)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Read,
            $false)
        try {
            foreach ($entry in $archive.Entries) {
                $unixType = ($entry.ExternalAttributes -shr 16) -band 0xF000
                $windowsAttributes = $entry.ExternalAttributes -band 0xFFFF
                if ($unixType -eq 0xA000 -or ($windowsAttributes -band 0x400) -ne 0) {
                    throw "ZIP contains a link/reparse entry: '$($entry.FullName)'."
                }

                $destination = Get-SafeDestination $entry.FullName
                if ($entry.FullName.EndsWith('/', [StringComparison]::Ordinal)) {
                    [IO.Directory]::CreateDirectory($destination) | Out-Null
                    continue
                }

                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                $input = $entry.Open()
                $output = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try {
                    $input.CopyTo($output)
                } finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Expand-ReviewedTarGzip {
    $stream = [IO.File]::OpenRead($packagePath)
    try {
        $gzip = [IO.Compression.GZipStream]::new(
            $stream,
            [IO.Compression.CompressionMode]::Decompress,
            $false)
        try {
            $reader = [System.Formats.Tar.TarReader]::new($gzip, $false)
            try {
                $previousEntryName = $null
                $deterministicTimestamp = [DateTimeOffset]::new(
                    2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                while ($entry = $reader.GetNextEntry()) {
                    $entryType = $entry.EntryType.ToString()
                    if ($entryType -notin @('RegularFile', 'V7RegularFile', 'Directory')) {
                        throw "TAR contains unsupported/link entry type '$entryType': '$($entry.Name)'."
                    }
                    if ($entry.Format -ne [System.Formats.Tar.TarEntryFormat]::Ustar) {
                        throw "TAR entry is not deterministic USTAR: '$($entry.Name)'."
                    }
                    if ($entry.ModificationTime -ne $deterministicTimestamp -or
                        $entry.Uid -ne 0 -or
                        $entry.Gid -ne 0 -or
                        $entry.UserName -ne 'root' -or
                        $entry.GroupName -ne 'root') {
                        throw "TAR entry has non-deterministic metadata: '$($entry.Name)'."
                    }
                    if ($null -ne $previousEntryName -and
                        [StringComparer]::Ordinal.Compare($previousEntryName, $entry.Name) -ge 0) {
                        throw "TAR entries are not strictly sorted: '$($entry.Name)'."
                    }
                    $previousEntryName = $entry.Name

                    $expectedMode = if ($entry.Name.EndsWith(
                        '/OeXYZ.SocketScope',
                        [StringComparison]::Ordinal)) {
                        [Convert]::ToInt32('755', 8)
                    } else {
                        [Convert]::ToInt32('644', 8)
                    }
                    if ([int]$entry.Mode -ne $expectedMode) {
                        throw "TAR entry has an unexpected mode: '$($entry.Name)'."
                    }

                    $destination = Get-SafeDestination $entry.Name
                    if ($entryType -eq 'Directory') {
                        [IO.Directory]::CreateDirectory($destination) | Out-Null
                        continue
                    }

                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                    $output = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try {
                        if ($entry.DataStream) {
                            $entry.DataStream.CopyTo($output)
                        }
                    } finally {
                        $output.Dispose()
                    }

                    if (-not $IsWindows -and $entry.Mode -ne 0) {
                        [IO.File]::SetUnixFileMode($destination, [IO.UnixFileMode]$entry.Mode)
                    }
                }
            } finally {
                $reader.Dispose()
            }
        } finally {
            $gzip.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)][string] $FileName,
        [string[]] $Arguments = @(),
        [int] $TimeoutSeconds = 30
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start '$FileName'."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "Process timed out: '$FileName'."
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdoutTask.GetAwaiter().GetResult()
            StandardError = $stderrTask.GetAwaiter().GetResult()
        }
    } finally {
        $process.Dispose()
    }
}

function Test-CurrentRuntime {
    $os = if ($IsWindows) { 'win' } elseif ($IsLinux) { 'linux' } else { return $false }
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    return "$os-$architecture" -eq $runtimeIdentifier
}

try {
    $authenticodeStatus = 'not-applicable'
    if ($isDirectWindowsExecutable) {
        $root = $extractionBase
        $application = Join-Path $root 'OeXYZ.SocketScope.exe'
        Copy-Item -LiteralPath $packagePath -Destination $application
    } else {
        Expand-ReviewedTarGzip
        $root = Join-Path $extractionBase $packageRootName
        $application = Join-Path $root 'OeXYZ.SocketScope'
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            throw 'The package does not contain its expected single root directory.'
        }
        $unexpectedRoots = @(Get-ChildItem -LiteralPath $extractionBase -Force | Where-Object {
            $_.Name -ne $packageRootName
        })
        if ($unexpectedRoots.Count -ne 0) {
            throw "Unexpected top-level package entry: '$($unexpectedRoots[0].Name)'."
        }
    }

    if ($isDirectWindowsExecutable) {
        $packageFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File)
        if ($packageFiles.Count -ne 1 -or $packageFiles[0].Name -ne 'OeXYZ.SocketScope.exe') {
            throw 'The Windows release asset must contain exactly one executable and no companion files.'
        }
        if ($IsWindows) {
            $signature = Get-AuthenticodeSignature -FilePath $application
            if ($signature.Status -ne [Management.Automation.SignatureStatus]::NotSigned) {
                throw "The Windows Authenticode status is '$($signature.Status)' instead of the required not-present state."
            }
            $authenticodeStatus = 'not-present'
        } else {
            $authenticodeStatus = 'not-checked (non-Windows host)'
        }
    } else {
        $required = @(
            'OeXYZ.SocketScope',
            'README.md',
            'LICENSE',
            'SECURITY.md',
            'CHANGELOG.md',
            'THIRD_PARTY_NOTICES.md',
            'BUILD-METADATA.json',
            'licenses/OFL-Inter.txt',
            'schemas/report-v1.schema.json',
            'schemas/report-v2.schema.json',
            'schemas/snapshot-v1.schema.json',
            'schemas/settings-v1.schema.json',
            'schemas/settings-v2.schema.json')
        foreach ($relative in $required) {
            if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
                throw "Required package file is missing: '$relative'."
            }
        }
        if (Test-Path -LiteralPath (Join-Path $root 'docs/release-evidence')) {
            throw 'Release evidence is generated after packaging and must not be embedded in the package.'
        }
        $packageFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File)
    }

    $forbidden = Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object {
        $_.Name -in @('SocketScopeData', 'settings.json', 'current.json') -or
        (-not $_.PSIsContainer -and $_.Extension -in @('.pdb', '.cs', '.csproj', '.user')) -or
        $_.FullName.Contains('TestFixture', [StringComparison]::OrdinalIgnoreCase)
    }
    if ($forbidden) {
        throw "Forbidden development/local-data item in package: '$($forbidden[0].FullName)'."
    }

    if (-not $isDirectWindowsExecutable) {
        $metadata = Get-Content -LiteralPath (Join-Path $root 'BUILD-METADATA.json') -Raw | ConvertFrom-Json
        if ($metadata.version -ne $version -or $metadata.runtimeIdentifier -ne $runtimeIdentifier) {
            throw 'Build metadata does not match the package name.'
        }
        if ($metadata.windowsAuthenticode -ne 'not-present') {
            throw 'The package Authenticode status is missing or unexpected.'
        }
    }

    $fontLicensePath = if ($isDirectWindowsExecutable) {
        Join-Path $repositoryRoot 'src/OeXYZ.SocketScope.Desktop/Assets/Fonts/OFL-Inter.txt'
    } else {
        Join-Path $root 'licenses/OFL-Inter.txt'
    }
    $fontLicenseHash = (Get-FileHash -LiteralPath $fontLicensePath -Algorithm SHA256).Hash
    if ($fontLicenseHash -ne '3C8D13D97EA5EB62868833A377034C6BE6A9833E768D32ABEC2A767A8101E4BC') {
        throw 'The packaged Inter license does not match the reviewed owner-provided copy.'
    }
    $assemblyText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($application))
    foreach ($endpoint in @('fonts.googleapis.com', 'fonts.gstatic.com')) {
        if ($assemblyText.Contains($endpoint, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The production desktop assembly contains a forbidden remote font endpoint: $endpoint"
        }
    }

    if ($ChecksumsPath) {
        $checksums = Get-Content -LiteralPath ([IO.Path]::GetFullPath($ChecksumsPath))
        $expectedLine = @($checksums | Where-Object {
            $_ -match "\*$([regex]::Escape([IO.Path]::GetFileName($packagePath)))$"
        })
        if (-not $expectedLine -or $expectedLine.Count -ne 1) {
            throw 'SHA256SUMS does not contain exactly one entry for this package.'
        }
        $expectedHash = ($expectedLine -split '\s+', 2)[0]
        $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
            throw 'The package SHA-256 does not match SHA256SUMS.'
        }
    }

    $runtimeSmoke = 'not-run (different host runtime)'
    if (-not $SkipRuntimeSmoke -and (Test-CurrentRuntime)) {
        $launcher = Invoke-CapturedProcess -FileName $application -Arguments @('--help')
        if ($launcher.ExitCode -ne 0 -or -not $launcher.StandardOutput.Contains('Running without a command', [StringComparison]::Ordinal)) {
            throw "Packaged launcher help smoke failed with exit code $($launcher.ExitCode)."
        }

        $notices = Invoke-CapturedProcess -FileName $application -Arguments @('--notices')
        $noticesValid = $notices.ExitCode -eq 0 -and `
            $notices.StandardOutput.Contains('Inter', [StringComparison]::Ordinal) -and `
            $notices.StandardOutput.Contains('SIL OPEN FONT LICENSE Version 1.1', [StringComparison]::Ordinal)
        if (-not $noticesValid) {
            throw "Packaged embedded-notices smoke failed with exit code $($notices.ExitCode)."
        }

        $cli = Invoke-CapturedProcess -FileName $application -Arguments @('cli', '--language', 'en', '--help')
        if ($cli.ExitCode -ne 0 -or -not $cli.StandardOutput.Contains('Know what listens', [StringComparison]::Ordinal)) {
            throw "Packaged CLI help smoke failed with exit code $($cli.ExitCode)."
        }

        $inventory = Invoke-CapturedProcess `
            -FileName $application `
            -Arguments @('cli', '--language', 'en', 'list', '--json', '--no-firewall')
        if ($inventory.ExitCode -ne 0) {
            throw "Packaged CLI inventory smoke failed with exit code $($inventory.ExitCode)."
        }
        $inventoryDocument = $inventory.StandardOutput | ConvertFrom-Json
        if ($null -eq $inventoryDocument.observations) {
            throw 'Packaged CLI inventory smoke did not return the expected JSON document.'
        }

        $reportPath = Join-Path $extractionBase 'share-safe-report.json'
        $report = Invoke-CapturedProcess `
            -FileName $application `
            -Arguments @(
                'cli',
                '--language',
                'en',
                'report',
                $reportPath,
                '--format',
                'json',
                '--share-safe',
                '--no-firewall')
        if ($report.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            throw "Packaged CLI report smoke failed with exit code $($report.ExitCode)."
        }
        if (-not (Test-Json `
                -LiteralPath $reportPath `
                -SchemaFile (Join-Path $repositoryRoot 'schemas/report-v2.schema.json') `
                -ErrorAction Stop)) {
            throw 'Packaged CLI report does not match report-v2.schema.json.'
        }

        $snapshotPath = Join-Path $extractionBase 'snapshot.json'
        $snapshot = Invoke-CapturedProcess `
            -FileName $application `
            -Arguments @('cli', 'snapshot', 'save', $snapshotPath, '--no-firewall')
        if ($snapshot.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $snapshotPath -PathType Leaf)) {
            throw "Packaged CLI snapshot smoke failed with exit code $($snapshot.ExitCode)."
        }
        if (-not (Test-Json `
                -LiteralPath $snapshotPath `
                -SchemaFile (Join-Path $repositoryRoot 'schemas/snapshot-v1.schema.json') `
                -ErrorAction Stop)) {
            throw 'Packaged CLI snapshot does not match snapshot-v1.schema.json.'
        }

        $updater = Invoke-CapturedProcess -FileName $application -Arguments @('update', '--help')
        if ($updater.ExitCode -ne 0 -or -not $updater.StandardOutput.Contains('explicit confirmation', [StringComparison]::Ordinal)) {
            throw "Packaged updater help smoke failed with exit code $($updater.ExitCode)."
        }

        $shellHelp = Invoke-CapturedProcess -FileName $application -Arguments @('shell', '--help')
        $shellHelpValid = $shellHelp.ExitCode -eq 0 -and `
            $shellHelp.StandardOutput.Contains('socketscope shell install', [StringComparison]::Ordinal) -and `
            $shellHelp.StandardOutput.Contains('socketscope gui', [StringComparison]::Ordinal)
        if (-not $shellHelpValid) {
            throw "Packaged terminal-integration help smoke failed with exit code $($shellHelp.ExitCode)."
        }

        $shellStatus = Invoke-CapturedProcess -FileName $application -Arguments @('shell', 'status')
        $shellStatusValid = $shellStatus.ExitCode -eq 0 -and `
            $shellStatus.StandardOutput.Contains('Installed:', [StringComparison]::Ordinal) -and `
            $shellStatus.StandardOutput.Contains('Available in future terminals:', [StringComparison]::Ordinal)
        if (-not $shellStatusValid) {
            throw "Packaged terminal-integration status smoke failed with exit code $($shellStatus.ExitCode)."
        }

        $dataRoot = Join-Path $extractionBase 'desktop-data'
        $desktopFileName = $application
        $desktopArguments = @()
        if ($IsLinux -and [string]::IsNullOrWhiteSpace($env:DISPLAY)) {
            $xvfbCandidates = @(Get-Command 'xvfb-run' -CommandType Application -ErrorAction SilentlyContinue)
            if ($xvfbCandidates.Count -eq 0) {
                throw 'The Linux desktop package smoke requires DISPLAY or the xvfb-run executable.'
            }
            $desktopFileName = $xvfbCandidates[0].Path
            $desktopArguments = @('-a', $application)
        }
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $desktopFileName
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.WorkingDirectory = $root
        $startInfo.Environment['SOCKETSCOPE_DATA_ROOT'] = $dataRoot
        foreach ($argument in $desktopArguments) {
            $startInfo.ArgumentList.Add($argument)
        }
        if ($IsWindows) {
            $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        }
        $desktop = [Diagnostics.Process]::new()
        $desktop.StartInfo = $startInfo
        try {
            if (-not $desktop.Start()) {
                throw 'The packaged desktop executable did not start.'
            }
            [Threading.Tasks.Task]::Delay(2500).GetAwaiter().GetResult()
            if ($desktop.HasExited) {
                throw "The packaged desktop executable exited early with code $($desktop.ExitCode)."
            }
            if (-not $desktop.CloseMainWindow() -or -not $desktop.WaitForExit(3000)) {
                $desktop.Kill($true)
                $desktop.WaitForExit()
            }
        } finally {
            $desktop.Dispose()
        }
        $runtimeSmoke = 'passed'
    }

    [pscustomobject]@{
        Package = $packagePath
        Version = $version
        RuntimeIdentifier = $runtimeIdentifier
        PackageFiles = $packageFiles.Count
        Sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        WindowsAuthenticode = $authenticodeStatus
        RuntimeSmoke = $runtimeSmoke
        ExtractedTo = if ($KeepExtracted) { $root } else { $null }
    }
} finally {
    if (-not $KeepExtracted -and (Test-Path -LiteralPath $extractionBase)) {
        $safeExtraction = [IO.Path]::GetFullPath($extractionBase)
        if (-not $safeExtraction.StartsWith($workspacePrefix, $comparison)) {
            throw 'Refusing to remove a release-test directory outside the repository.'
        }
        Remove-Item -LiteralPath $safeExtraction -Recurse -Force
    }
}
