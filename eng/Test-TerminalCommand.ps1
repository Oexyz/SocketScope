#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagePath = [IO.Path]::GetFullPath($Package)
$comparison = if ($IsWindows) {
    [StringComparison]::OrdinalIgnoreCase
} else {
    [StringComparison]::Ordinal
}
$workspacePrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $packagePath.StartsWith($workspacePrefix, $comparison)) {
    throw 'The terminal-command smoke package must be inside the repository.'
}
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "The package does not exist: $packagePath"
}
if (-not $IsWindows) {
    throw 'This script currently validates the direct Windows executable.'
}
$packageMatch = [regex]::Match(
    [IO.Path]::GetFileName($packagePath),
    '^OeXYZ-SocketScope-v(?<version>\d+\.\d+\.\d+)-win-(?:x64|arm64)\.exe$')
if (-not $packageMatch.Success) {
    throw 'The package name does not match the Windows release policy.'
}
$version = $packageMatch.Groups['version'].Value

$testRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ".cache/terminal-command-smoke/$([Guid]::NewGuid().ToString('N'))"))
if (-not $testRoot.StartsWith($workspacePrefix, $comparison)) {
    throw 'The terminal-command smoke directory escaped the repository.'
}
$binDirectory = Join-Path $testRoot 'bin'
$commandPath = Join-Path $binDirectory 'socketscope.exe'
$dataRoot = Join-Path $testRoot 'data'
$originalProcessPath = [Environment]::GetEnvironmentVariable('PATH', [EnvironmentVariableTarget]::Process)
$desktop = $null
try {
    [IO.Directory]::CreateDirectory($binDirectory) | Out-Null
    Copy-Item -LiteralPath $packagePath -Destination $commandPath
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $commandPath -Algorithm SHA256).Hash) {
        throw 'The workspace-local terminal command does not match the release package.'
    }
    if (@(Get-ChildItem -LiteralPath $binDirectory -File).Count -ne 1) {
        throw 'The terminal command smoke directory does not contain exactly one executable.'
    }

    $processPath = if ([string]::IsNullOrWhiteSpace($originalProcessPath)) {
        $binDirectory
    } else {
        "$binDirectory;$originalProcessPath"
    }
    [Environment]::SetEnvironmentVariable('PATH', $processPath, [EnvironmentVariableTarget]::Process)
    $resolved = Get-Command socketscope -CommandType Application -ErrorAction Stop
    if (-not [IO.Path]::GetFullPath($resolved.Source).Equals($commandPath, $comparison)) {
        throw 'The shell did not resolve socketscope to the workspace-local release executable.'
    }

    $versionOutput = & socketscope --version
    if ($LASTEXITCODE -ne 0 -or -not ($versionOutput -join "`n").Contains("OeXYZ SocketScope $version", [StringComparison]::Ordinal)) {
        throw "The stable terminal command version smoke failed with exit code $LASTEXITCODE."
    }

    $inventoryOutput = & socketscope list --json --no-firewall
    if ($LASTEXITCODE -ne 0) {
        throw "The stable terminal command inventory smoke failed with exit code $LASTEXITCODE."
    }
    $inventory = ($inventoryOutput -join "`n") | ConvertFrom-Json
    if ($null -eq $inventory.observations) {
        throw 'The stable terminal command did not return a SocketScope inventory document.'
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolved.Source
    $startInfo.ArgumentList.Add('gui')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = $testRoot
    $startInfo.Environment['SOCKETSCOPE_DATA_ROOT'] = $dataRoot
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $desktop = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $desktop) {
        throw 'The stable terminal command GUI could not start.'
    }
    [Threading.Tasks.Task]::Delay(2500).GetAwaiter().GetResult()
    if ($desktop.HasExited) {
        throw "The stable terminal command GUI exited early with code $($desktop.ExitCode)."
    }

    [pscustomobject]@{
        Result = 'PASS'
        ResolvedCommand = 'socketscope.exe'
        PackageFiles = 1
        VersionCommand = 'passed'
        InventoryCommand = 'passed'
        GuiCommand = 'passed'
        UserPathChanged = $false
    }
} finally {
    [Environment]::SetEnvironmentVariable('PATH', $originalProcessPath, [EnvironmentVariableTarget]::Process)
    if ($null -ne $desktop) {
        try {
            if (-not $desktop.HasExited) {
                $desktop.Kill($true)
                $desktop.WaitForExit()
            }
        } finally {
            $desktop.Dispose()
        }
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
