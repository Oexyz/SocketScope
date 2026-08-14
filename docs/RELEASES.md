# Releases

SocketScope release candidates are produced by the reviewed GitHub Actions workflow
from a version tag. The workflow creates a **draft** GitHub release; it does not make
the release public. Local and draft packages are not called official until the
repository owner approves the exact commit, screenshots, changelog, and artifacts and
explicitly performs or authorizes publication.

## Release inputs

- Tag: strict `vMAJOR.MINOR.PATCH`, matching the application version.
- Source: clean tagged commit on the protected release branch.
- SDK and NuGet inputs: pinned by `global.json`, central versions, and lock files.
- Actions: pinned to full commit SHAs and reviewed by Dependabot.
- Permission: ordinary CI is read-only; only the release job receives minimal
  `contents`, `id-token`, `attestations`, and artifact-metadata write scopes.

## Package layout

Windows is distributed as exactly one self-contained executable. The desktop, CLI,
updater, optional user-local terminal installer, .NET runtime, locally bundled
font/icon, MIT license, third-party notices, and Inter OFL text are embedded;
`--notices` prints the legal documents. Linux contains one
self-contained application binary plus the same human-readable documentation,
licenses, schemas, and build metadata in `tar.gz` so Unix executable modes survive the
download.

Expected names:

```text
OeXYZ-SocketScope-v<VERSION>-win-x64.exe
OeXYZ-SocketScope-v<VERSION>-win-arm64.exe
OeXYZ-SocketScope-v<VERSION>-linux-x64.tar.gz
OeXYZ-SocketScope-v<VERSION>-linux-arm64.tar.gz
```

Each application asset receives a matching SPDX JSON SBOM. `SHA256SUMS` covers final
executables/archives and SBOM files, not intermediate directories. GitHub provenance
attestations independently cover the application assets, SBOM files, and checksum
manifest produced by the protected release job.

## Required gates

1. Locked restore and warning-free Release build on Windows and Linux.
2. Unit, platform, integration, architecture, updater, and headless UI suites.
3. Ordinary-user live provider checks on the corresponding operating system.
4. Package-content allowlist, local-font hash/origin check, secret/string scan, and
   launch/CLI/export/notices/terminal-status smoke tests from the final application
   asset.
5. Native runtime evidence for each architecture advertised as validated.
6. Sensitive-data and readability review of screenshots and generated reports.
7. CodeQL, dependency review, license/notice diff, SBOM generation, checksum, and
   attestation completion.
8. Changelog/release notes stating unsupported platforms, external reachability as not
   tested, and the current Authenticode/package-authenticity status.

An untested ARM artifact may be retained only as clearly labeled experimental build
output; it must not be presented as validated. A failed integrity, origin, native
smoke, or high-severity security gate blocks the release.

## Windows Authenticode status

Version 1.0 records Windows Authenticode as **not present**. The release workflow has
no certificate step or certificate secret. SHA-256 and GitHub attestations provide
separate integrity and workflow-provenance evidence, but they do not create Windows
SmartScreen reputation.

## Verification

Consumers should download the application asset, `SHA256SUMS`, and SBOM from the same immutable
release, verify the checksum, then verify GitHub provenance against this repository and
`.github/workflows/release.yml`. Exact commands are maintained in the README.

The built-in checker remains opt-in. It stages only a complete application asset whose
exact name, size, origin, immutable release metadata, and SHA-256 digest match. After a
visible evidence preview and explicit confirmation, the non-elevated installer rechecks
hashes, keeps a rollback copy, health-checks the new executable, restores the previous
file on failure, and restarts on success. It does not silently install a background
update.

The optional `shell install` action does not change the release layout: it copies the
verified running single-file application to a stable, protected user-owned command
path. Native release VMs verify installation in a disposable user profile; package
smokes in ordinary development environments run only read-only `shell --help` and
`shell status` so they never alter the maintainer's PATH.

## Maintainer checklist

- Confirm `git status` is clean and review the full diff.
- Update `CHANGELOG.md` and remove “Unreleased” only for the actual release date.
- Run `eng/Test-Release.ps1` against the locally produced candidate.
- Generate the public-safe TRX summary and test image with
  `eng/New-TestEvidence.ps1`.
- Capture controlled fixture screenshots, inspect every pixel-visible field, and add
  only approved images under `docs/images`.
- Obtain the repository owner's explicit approval before any push or release action.
- After approval, push source/tag and let the protected workflow build fresh artifacts
  into a draft release.
- Download the draft artifacts, repeat verification, enable GitHub release
  immutability for the repository, then obtain explicit publication approval. GitHub
  applies immutability only when the release is published.
