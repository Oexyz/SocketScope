#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [switch] $OfficialTaggedSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceDescription = if ($OfficialTaggedSource) {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $exactTag = (& git -C $repositoryRoot describe --tags --exact-match HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $exactTag -ne "v$Version") {
        throw "Official release notes require HEAD to be the exact tag v$Version."
    }
    "Source: verified version tag v$Version."
} else {
    'Source: local uncommitted/untagged release candidate; not an official published release.'
}

$content = @"
# OeXYZ SocketScope $Version

SocketScope is a local, read-only Windows/Linux network-endpoint inspector.

Important boundaries:

- External reachability is not tested. A wildcard binding or local allow-rule
  candidate is not proof of Internet reachability.
- Update checks are disabled by default. After a version/origin/size/digest preview,
  installation requires explicit confirmation; the unprivileged helper rechecks the
  executable, applies it with a rollback copy, runs a health check, and restarts.
- Release verification: SHA-256 manifest provided; the protected GitHub release
  workflow generates the artifact attestation; Windows Authenticode status: not
  present. No certificate step or certificate secret is configured.
- ARM64 artifacts must be treated as unvalidated unless native test evidence is
  explicitly attached to this release.

Download the single Windows executable or Linux archive for your runtime together with
`SHA256SUMS` and its SPDX JSON SBOM. Verification commands and the complete limitations
are in the README. Changes are documented in `CHANGELOG.md`.

$sourceDescription
"@

$fullPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath)) | Out-Null
[IO.File]::WriteAllText($fullPath, ($content.TrimEnd() + "`n"), [Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $fullPath
