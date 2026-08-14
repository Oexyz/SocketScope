# Testing

SocketScope uses layered tests because parser correctness, UI startup, and a
cross-compiled binary answer different questions.

## Automated suites

| Project | Evidence |
|---|---|
| `Core.Tests` | binding classification, identity/diff, cancellation, settings/snapshot bounds, Full/Share-safe reports, bounded custom-literal replacement/non-disclosure, JSON, HTML escaping/CSP |
| `Platform.Windows.Tests` | native table parsing, firewall candidate logic, SCM service attribution, Docker Desktop proxy evidence, secure storage, ordinary-user live capture |
| `Platform.Linux.Tests` | `NETLINK_SOCK_DIAG` and `/proc` fallback parsing/ownership, process-disappearance and no-systemd behavior, cgroup attribution, nft JSON handling, secure storage, Linux live capture |
| `IntegrationTests` | real CLI list/watch/snapshot/report behavior and process-boundary exit codes |
| `Updater.Tests` | strict versions, immutable metadata, exact names/origins, redirects, bounds, digest tampering, atomic replacement, health failure, rollback, safe cleanup, terminal PATH policy and install rollback |
| `ArchitectureTests` | dependency direction, command/network boundaries, native DLL search policy, telemetry absence, local-font provenance, original icon hashes, and WCAG palette contrast |
| `UiSmokeTests` | production view creation, focus and Tab traversal, English/language defaults, compact/wide responsive breakpoints, 100/150/200/300 percent DPI changes, localized movable/resizable/resettable table headers, real headless `Ctrl+C` clipboard transfer, Full/Share-safe copy commands, metadata-only diagnostic copying, `F1`/`Ctrl+F`, glossary/report/settings/terminal reflow, terminal-service dispatch without real PATH mutation, bounded local logging, portable mode |

Run the complete local suite:

```powershell
dotnet restore OeXYZ.SocketScope.slnx --locked-mode
dotnet test OeXYZ.SocketScope.slnx --configuration Release --no-restore
```

On Linux:

```bash
dotnet restore OeXYZ.SocketScope.slnx --locked-mode
dotnet test OeXYZ.SocketScope.slnx --configuration Release --no-restore
```

A skip is acceptable only when the test reports a specific unavailable operating
system facility, architecture, or filesystem capability. Release evidence records
passes, skips, host OS/kernel, architecture, privilege level, and command.

For the reviewed Windows x64 candidate, generate a public-safe image and JSON summary
directly from fresh TRX counters:

```powershell
.\eng\New-TestEvidence.ps1 `
  -Package .\artifacts\final\OeXYZ-SocketScope-v1.0.0-win-x64.exe
```

The script refuses elevation, validates the package name and Authenticode status,
runs every test project with the pinned SDK, discards the raw TRX files because they
contain host/user metadata, and writes only the reduced summary plus rendered image.

## Manual platform matrix

For each supported native runtime, record:

- IPv4 and IPv6 loopback, specific-interface, and wildcard listeners;
- TCP listener plus established TCP client/server connection and UDP endpoint;
- an owning process that exits during capture;
- protected/other-user metadata with expected permission denial;
- a Windows service or systemd cgroup unit where available;
- Docker/Podman/rootless forwarding when available, including an honest unknown case;
- missing/non-readable firewall tooling and candidate-rule cases;
- snapshot save/compare and standard JSON/HTML export;
- cancellation, pause/resume, refresh churn, and stable selection; and
- ordinary-user launch from the extracted release package.

No test should require the full GUI to run as administrator or root.

## UI and accessibility matrix

Review all seven main sections in English and German at 100, 150, 200, and 300 percent scaling,
including the 760 by 520 minimum and a larger desktop window. Verify keyboard-only
navigation, visible focus, `F1`/`F5`/`Ctrl+F`/`Ctrl+C`, resettable columns, copy actions,
filter dropdowns, empty/partial/error states, high contrast, and screen-reader names
with Narrator/NVDA on Windows and Orca/AT-SPI on Linux.

Screenshots use controlled local fixture sockets. Before committing an image, inspect
the full-resolution file for user names, hostnames, private/public addresses, unrelated
processes, notification contents, taskbar data, and window chrome. Test-console
screenshots supplement machine-readable logs; they are not the primary proof of a pass.

## Package tests

`eng/New-ReleasePackage.ps1` creates a self-contained candidate.
`eng/Test-Release.ps1` opens or extracts it into a fresh workspace-local directory and checks:

- exact package name, one-file Windows invariant, and no path traversal or link entries;
- embedded Windows legal notices or required Linux license/notices/schema files;
- no `.pdb`, source, test fixture, local data, log, or settings file;
- the reviewed Inter license/provenance and absence of Google Fonts endpoints;
- launcher, embedded notices, CLI help/list/report/snapshot, report and snapshot JSON
  Schema validation, updater help, terminal help/status, and health-check behavior;
- desktop process startup as an ordinary user, then clean controlled termination; and
- SHA-256 agreement with `SHA256SUMS` when provided.

On a headless Linux host, the package test starts the real desktop executable under
`xvfb-run`; it fails explicitly if neither `DISPLAY` nor `xvfb-run` is available.

The automatic replacement layer additionally has unit tests for a tampered incoming
file, successful replacement, and forced health-check rollback. The release candidate
is exercised at the installed stable `socketscope.exe` path with two real single-file
executables: update 1.0.0 to 1.0.1, then an
intentional version mismatch that must return verification exit 77 and restore 1.0.0
byte-for-byte. These tests run as an ordinary user; elevated execution is expected to
be refused.

Terminal integration tests never change the developer machine's real PATH. Unit tests
inject a temporary environment and prove the atomic copy plus file/PATH rollback. The
package smoke calls only read-only `shell --help` and `shell status`. A final native VM
release check explicitly runs `shell install`, opens a fresh terminal, verifies both
`socketscope --version` and GUI startup, then removes the disposable test profile.
`eng/Test-TerminalCommand.ps1` provides a non-persistent maintainer smoke: it copies the
one release EXE to a workspace-local `socketscope.exe`, changes PATH only inside its
child PowerShell process, exercises version, real inventory, and `socketscope gui`, and
then removes the test directory. It never writes the developer's user PATH.

Native ARM64 launch tests cannot be replaced by extraction or static inspection on an
x64 host.

The disposable Docker Desktop evidence test uses a digest-pinned Debian Node image,
mounts the final Linux x64 binary read-only, opens one controlled listener, asserts
`linux.sock-diag` plus cgroup-backed Docker attribution inside the container, and then
asserts that the Windows host reports only partial forwarding-proxy evidence without
inventing a concrete container. It removes only its GUID-named disposable container:

```powershell
.\eng\Test-DockerEvidence.ps1 `
  -LinuxApplication .\artifacts\package-root\linux-x64\OeXYZ-SocketScope-v1.0.0-linux-x64\OeXYZ.SocketScope `
  -WindowsApplication .\artifacts\final\OeXYZ-SocketScope-v1.0.0-win-x64.exe
```

## Latest local evidence

The reviewed Windows x64 release-candidate run on 2026-08-14 produced:

- 7/7 automated suites passed: 216 tests passed, 0 failed, 1 skipped out of
  217 total;
- the single skipped Windows storage test requires a symlink capability unavailable
  to this ordinary-user host;
- the final package smoke passed with exactly one executable, embedded notices,
  CLI/list/report/updater/terminal checks, GUI startup, SHA-256 verification, and
  Authenticode status `not-present`;
- all four required RIDs built successfully and passed static package validation;
  only Windows x64 received a native runtime smoke on this host;
- terminal resolution, version, live inventory, and GUI launch passed without changing
  the real user PATH;
- the real single-file update moved 1.0.0 to 1.0.1, while a forced health-version
  mismatch restored 1.0.0 byte-for-byte with exactly one executable left;
- the final Linux x64 package passed CLI, report, snapshot-schema, and live
  `linux.sock-diag` checks in WSL 2; the disposable Docker Desktop test passed its
  namespace/cgroup and honest Windows forwarding-proxy assertions;
- eight application screenshots and the test-evidence image passed full-resolution
  readability and sensitive-content review, including an automated scan of visible
  UI Automation names and values;
- locked restore/audit and `dotnet list package --vulnerable --include-transitive`
  reported no known vulnerable NuGet packages from the configured current source.

The machine-readable sanitized counters are in
[`release-evidence/windows-x64-local.json`](release-evidence/windows-x64-local.json),
and the matching visual is
[`images/socketscope-test-evidence.png`](images/socketscope-test-evidence.png).
The final Linux x64 package smoke is recorded in
[`release-evidence/linux-x64-wsl-local.json`](release-evidence/linux-x64-wsl-local.json),
and the disposable container/host-forwarding check is recorded in
[`release-evidence/docker-x64-local.json`](release-evidence/docker-x64-local.json).
Image dimensions, hashes, provenance, and the completed review scope are recorded in
[`release-evidence/screenshots.json`](release-evidence/screenshots.json).
This is local Windows x64 evidence, not a claim of native Linux or ARM64 validation.
