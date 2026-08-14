#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CurrentExecutable,

    [Parameter(Mandatory)]
    [string] $UpdateExecutable,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $CurrentVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $UpdateVersion,

    [switch] $KeepEvidence
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'This direct-EXE self-update smoke currently requires Windows.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The self-update smoke must run as an ordinary, non-elevated user.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$comparison = [StringComparison]::OrdinalIgnoreCase
$workspacePrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

function Resolve-WorkspaceFile {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspacePrefix, $comparison) -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "The self-update test input must be an existing workspace file: $fullPath"
    }

    return $fullPath
}

function Invoke-Version {
    param([Parameter(Mandatory)][string] $Executable)

    $output = & $Executable --version
    if ($LASTEXITCODE -ne 0) {
        throw "Version inspection failed for '$Executable'."
    }

    return ($output -join "`n").Trim()
}

function New-Scenario {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $ExpectedHealthVersion
    )

    $scenarioRoot = Join-Path $evidenceRoot $Name
    $installDirectory = Join-Path $scenarioRoot 'install'
    $updateRoot = Join-Path $scenarioRoot 'updates'
    $session = Join-Path $updateRoot 'session-controlled'
    [IO.Directory]::CreateDirectory($installDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($session) | Out-Null

    $target = Join-Path $installDirectory 'socketscope.exe'
    $asset = Join-Path $session "OeXYZ-SocketScope-v$UpdateVersion-win-x64.exe"
    $incoming = Join-Path $installDirectory '.socketscope.exe.1234567890ab.incoming'
    $backup = Join-Path $installDirectory '.socketscope.exe.1234567890ab.previous'
    Copy-Item -LiteralPath $currentPath -Destination $target
    Copy-Item -LiteralPath $updatePath -Destination $asset
    Copy-Item -LiteralPath $asset -Destination $incoming

    $oldHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    $newHash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    $before = Invoke-Version $target
    & $asset `
        --update-helper `
        --target $target `
        --incoming $incoming `
        --backup $backup `
        --asset $asset `
        --update-root $updateRoot `
        --new-sha256 $newHash `
        --old-sha256 $oldHash `
        --new-version $ExpectedHealthVersion `
        --parent-pid 2147483646 `
        --parent-start-ticks 1 `
        --restart false
    $helperExit = $LASTEXITCODE
    $after = Invoke-Version $target
    $finalHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()

    return [pscustomobject]@{
        Name = $Name
        Before = $before
        After = $after
        HelperExit = $helperExit
        OldHash = $oldHash
        NewHash = $newHash
        FinalHash = $finalHash
        IncomingRemoved = -not (Test-Path -LiteralPath $incoming)
        BackupRemoved = -not (Test-Path -LiteralPath $backup)
        InstallFileCount = @(Get-ChildItem -LiteralPath $installDirectory -File).Count
    }
}

$currentPath = Resolve-WorkspaceFile $CurrentExecutable
$updatePath = Resolve-WorkspaceFile $UpdateExecutable
if ([IO.Path]::GetExtension($currentPath) -ne '.exe' -or
    [IO.Path]::GetExtension($updatePath) -ne '.exe') {
    throw 'The Windows self-update smoke requires two executable release assets.'
}
if ($CurrentVersion -eq $UpdateVersion) {
    throw 'The self-update smoke requires two different versions.'
}

$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ".cache/self-update-evidence/$([Guid]::NewGuid().ToString('N'))"))
if (-not $evidenceRoot.StartsWith($workspacePrefix, $comparison)) {
    throw 'The self-update evidence directory escaped the repository.'
}
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null

try {
    $success = New-Scenario -Name 'success' -ExpectedHealthVersion $UpdateVersion
    if ($success.HelperExit -ne 0 -or
        $success.FinalHash -ne $success.NewHash -or
        -not $success.After.Contains(" $UpdateVersion (", [StringComparison]::Ordinal) -or
        -not $success.IncomingRemoved -or
        -not $success.BackupRemoved -or
        $success.InstallFileCount -ne 1) {
        throw 'The real single-file update scenario failed.'
    }

    $components = $UpdateVersion.Split('.')
    $mismatchVersion = '{0}.{1}.{2}' -f $components[0], $components[1], ([int]$components[2] + 1)
    $rollback = New-Scenario -Name 'rollback' -ExpectedHealthVersion $mismatchVersion
    if ($rollback.HelperExit -ne 77 -or
        $rollback.FinalHash -ne $rollback.OldHash -or
        -not $rollback.After.Contains(" $CurrentVersion (", [StringComparison]::Ordinal) -or
        -not $rollback.IncomingRemoved -or
        -not $rollback.BackupRemoved -or
        $rollback.InstallFileCount -ne 1) {
        throw 'The real single-file rollback scenario failed.'
    }

    [pscustomobject]@{
        Result = 'PASS'
        SuccessTransition = "$CurrentVersion -> $UpdateVersion"
        SuccessExit = $success.HelperExit
        SuccessHashMatched = $success.FinalHash -eq $success.NewHash
        RollbackTrigger = "$UpdateVersion executable declared as $mismatchVersion"
        RollbackExit = $rollback.HelperExit
        PreviousHashRestored = $rollback.FinalHash -eq $rollback.OldHash
        OneExecutableAfterEachScenario = $success.InstallFileCount -eq 1 -and $rollback.InstallFileCount -eq 1
        InstalledCommandName = 'socketscope.exe'
        EvidenceRoot = if ($KeepEvidence) { $evidenceRoot } else { $null }
    }
} finally {
    if (-not $KeepEvidence -and (Test-Path -LiteralPath $evidenceRoot)) {
        $safePath = [IO.Path]::GetFullPath($evidenceRoot)
        if (-not $safePath.StartsWith($workspacePrefix, $comparison)) {
            throw 'Refusing to remove self-update evidence outside the repository.'
        }
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}
