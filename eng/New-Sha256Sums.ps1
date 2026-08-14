#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ReleaseDirectory,

    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$directory = [IO.Path]::GetFullPath($ReleaseDirectory)
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    throw "Release directory does not exist: $directory"
}

$target = if ($OutputPath) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    Join-Path $directory 'SHA256SUMS'
}
if ([IO.Path]::GetDirectoryName($target) -ne $directory) {
    throw 'SHA256SUMS must be written directly inside the release directory.'
}

$files = Get-ChildItem -LiteralPath $directory -File | Where-Object {
    $_.Name -match '^OeXYZ-SocketScope-v\d+\.\d+\.\d+-(win|linux)-(x64|arm64)\.(exe|tar\.gz|spdx\.json)$'
} | Sort-Object Name
if (-not $files) {
    throw 'No release application assets or SBOMs were found.'
}

$lines = foreach ($file in $files) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$($file.Name)"
}
[IO.File]::WriteAllText($target, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $target
