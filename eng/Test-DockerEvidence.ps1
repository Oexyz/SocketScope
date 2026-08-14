#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $LinuxApplication,

    [Parameter(Mandatory)]
    [string] $WindowsApplication,

    [string] $OutputPath = 'docs/release-evidence/docker-x64-local.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspacePrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$comparison = [StringComparison]::OrdinalIgnoreCase
$image = 'node@sha256:0557ac14e0d45d02ed563067b82856ca5e7aa3437fa28d98d4350ea9c3d9494a'
$listenerPort = 38123
$containerName = "socketscope-evidence-$([Guid]::NewGuid().ToString('N'))"
$containerCreated = $false

function Resolve-WorkspaceFile {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspacePrefix, $comparison) -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "The Docker evidence input must be an existing workspace file: $fullPath"
    }
    return $fullPath
}

function Resolve-WorkspaceOutput {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspacePrefix, $comparison) -or
        -not [IO.Path]::GetExtension($fullPath).Equals('.json', $comparison)) {
        throw 'Docker evidence output must be a JSON file inside the repository.'
    }
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $parentInfo = [IO.DirectoryInfo]::new($parent)
    if (($parentInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $null -ne $parentInfo.LinkTarget) {
        throw 'A linked or reparse directory cannot hold Docker evidence.'
    }
    return $fullPath
}

function Invoke-Docker {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [switch] $AllowFailure
    )

    $output = & docker @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "Docker failed with exit code ${exitCode}: $(@($output) -join [Environment]::NewLine)"
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function Convert-ApplicationJson {
    param(
        [Parameter(Mandatory)][string] $Application,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    $output = & $Application @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "SocketScope failed with exit code ${LASTEXITCODE}: $(@($output) -join [Environment]::NewLine)"
    }
    return (@($output) -join [Environment]::NewLine) | ConvertFrom-Json -Depth 32
}

$linuxApplication = Resolve-WorkspaceFile $LinuxApplication
$windowsApplication = Resolve-WorkspaceFile $WindowsApplication
$output = Resolve-WorkspaceOutput $OutputPath
if (-not [IO.Path]::GetFileName($windowsApplication).EndsWith('.exe', $comparison)) {
    throw 'The Windows evidence application must be a release executable.'
}

try {
    $null = Invoke-Docker @('image', 'inspect', $image)
    $nodeScript = @"
const net = require('net');
const server = net.createServer(socket => socket.end());
server.listen($listenerPort, '0.0.0.0');
setInterval(() => {}, 60000);
"@
    $volume = "${linuxApplication}:/opt/socketscope:ro"
    $run = Invoke-Docker @(
        'run',
        '--detach',
        '--rm',
        '--name', $containerName,
        '--cgroupns', 'host',
        '--publish', "127.0.0.1::$listenerPort",
        '--volume', $volume,
        '--env', 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1',
        $image,
        'node',
        '-e', $nodeScript)
    if ($run.Output.Count -ne 1 -or $run.Output[0] -notmatch '^[0-9a-f]{64}$') {
        throw 'Docker did not return one reviewed container identifier.'
    }
    $containerCreated = $true

    $running = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $state = Invoke-Docker @('inspect', '--format', '{{.State.Running}}', $containerName)
        if ($state.Output.Count -eq 1 -and $state.Output[0] -eq 'true') {
            $running = $true
            break
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $running) {
        throw 'The disposable Docker listener did not reach running state.'
    }

    $insideResult = Invoke-Docker @(
        'exec',
        '--env', 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1',
        $containerName,
        '/opt/socketscope',
        'cli',
        '--language', 'en',
        'list',
        '--json',
        '--no-firewall')
    $inside = ($insideResult.Output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 32
    $insideRows = @($inside.observations | Where-Object {
        $_.localPort -eq $listenerPort -and $_.processName -eq 'node'
    })
    if ($insideRows.Count -lt 1) {
        throw 'SocketScope did not find the controlled listener inside the Docker namespace.'
    }
    if (-not ($insideRows | Where-Object {
            $_.evidenceSource -eq 'linux.sock-diag' -and
            $_.containerAvailability -eq 'Available' -and
            $_.container -like 'docker:*'
        })) {
        throw 'The controlled Docker listener lacks sock_diag and cgroup-backed attribution evidence.'
    }

    $portResult = Invoke-Docker @('port', $containerName, "$listenerPort/tcp")
    if ($portResult.Output.Count -ne 1 -or $portResult.Output[0] -notmatch '^127\.0\.0\.1:(?<port>\d{1,5})$') {
        throw 'Docker returned an unexpected loopback port-forwarding description.'
    }
    $hostPort = [int] $Matches.port
    if ($hostPort -isnot [int] -or $hostPort -lt 1 -or $hostPort -gt 65535) {
        throw 'Docker returned an invalid host port.'
    }

    $hostReport = Convert-ApplicationJson `
        -Application $windowsApplication `
        -Arguments @('cli', '--language', 'en', 'list', '--json', '--no-firewall')
    $hostRows = @($hostReport.observations | Where-Object { $_.localPort -eq $hostPort })
    if ($hostRows.Count -lt 1) {
        throw 'SocketScope did not find the Docker Desktop host forwarding listener.'
    }
    if ($hostRows | Where-Object { $null -ne $_.container }) {
        throw 'SocketScope made an unsupported concrete container claim for a Docker Desktop forwarding proxy.'
    }
    if (-not ($hostRows | Where-Object { $_.containerAvailability -eq 'Partial' })) {
        throw 'SocketScope did not retain the Docker Desktop forwarding proxy as partial evidence.'
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        product = 'OeXYZ SocketScope'
        status = 'pass'
        scope = 'Disposable Docker Desktop Linux x64 namespace and Windows host-forwarding check'
        finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        image = $image
        applications = [ordered]@{
            linuxX64Sha256 = (Get-FileHash -LiteralPath $linuxApplication -Algorithm SHA256).Hash.ToLowerInvariant()
            windowsX64Sha256 = (Get-FileHash -LiteralPath $windowsApplication -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        assertions = [ordered]@{
            controlledTcpListener = $listenerPort
            insideProvider = 'linux.sock-diag'
            insideContainerAttribution = 'available (Docker cgroup evidence)'
            hostForwardingRows = $hostRows.Count
            hostContainerAttribution = 'partial proxy evidence; no concrete container claimed'
        }
        limitations = @(
            'This validates one disposable Docker Desktop Linux container on the local x64 host.',
            'It does not prove attribution for every engine, namespace mode, or rootless configuration.',
            'The randomly assigned host port and transient container identifier are intentionally omitted.'
        )
    }
    $json = $evidence | ConvertTo-Json -Depth 8
    $temporary = "$output.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $output, $true)
    } finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }

    [pscustomobject]@{
        Result = 'PASS'
        InsideRows = $insideRows.Count
        HostForwardingRows = $hostRows.Count
        Evidence = [IO.Path]::GetRelativePath($repositoryRoot, $output)
    }
} finally {
    if ($containerCreated) {
        if ($containerName -notmatch '^socketscope-evidence-[0-9a-f]{32}$') {
            throw 'Refusing to remove a Docker container with an unexpected name.'
        }
        $null = Invoke-Docker -Arguments @('rm', '--force', $containerName) -AllowFailure
    }
}
