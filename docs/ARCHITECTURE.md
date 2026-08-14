# SocketScope architecture

> Status: implemented 1.0 release-candidate architecture, reviewed 2026-08-13.
> Planned improvements are labeled explicitly and are not presented as current APIs.

## Architectural invariants

1. The desktop application and CLI are unprivileged, read-only, local processes.
2. Core contains no Avalonia types, native interop, shell invocation, or platform
   branching.
3. Platform projects turn incomplete OS evidence into typed results; they do not hide
   access denial, unsupported facilities, truncation, or races.
4. Observed facts, derived classifications, rule candidates, and untested assertions
   remain separate through UI and export.
5. The same core pipeline serves Desktop and CLI. Neither reimplements classification,
   diff, report-profile, or export policy.
6. Input originating from the OS, a snapshot, settings, or a report is untrusted and
   bounded before display or persistence.
7. No runtime resource, update, analytics endpoint, plugin, font, image, or icon is
   fetched merely by opening the application.

## Baseline decisions

### Runtime and UI

Use .NET 10 LTS. At the research date, .NET 10 was active through November 2028 and
the latest servicing release was 10.0.10. Use the latest fully tested .NET 10 patch in
`global.json`, with roll-forward disabled in CI. Reference:
[.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

Use Avalonia 12, pinned with lock files. The latest stable NuGet package observed on
2026-08-13 was 12.1.1. Avalonia provides the required Windows/Linux UI path and current
documentation describes Windows UI Automation and Linux AT-SPI support. Both claims
still require real assistive-technology tests. References:
[Avalonia package](https://www.nuget.org/packages/Avalonia/),
[platform support](https://docs.avaloniaui.net/docs/supported-platforms), and
[accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility).

Use the open-source Avalonia `DataGrid` or a small custom virtualized presenter after a
performance/accessibility spike. Do not make the commercial TreeDataGrid dependency a
silent architectural requirement. Avoid an MVVM framework initially; Avalonia,
`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, and a
small internal observable/view-model base are sufficient until measured complexity
justifies another dependency.

Use `System.Text.Json`, `TimeProvider`, `System.Net` primitives, immutable records, and
explicit discriminated-result records. Add dependencies only with a documented owner,
license, security/update rationale, and removal cost.

### Dependency direction

```text
             +--------------------+       +--------------------+
             | Desktop (Avalonia) |       | CLI                |
             +----------+---------+       +----------+---------+
                        |                            |
                        +-------------+--------------+
                                      v
                     +------------------------------------+
                     | Core                               |
                     | domain, capture orchestration,     |
                     | classification, diff, reports,     |
                     | snapshots, reports, contracts      |
                     +-----------------+------------------+
                                       ^
                         implements    |    implements
                    +------------------+-------------------+
                    |                                      |
          +---------+-----------+                +---------+----------+
          | Platform.Windows    |                | Platform.Linux    |
          | IP Helper, SCM,     |                | sock_diag, /proc, |
          | process, firewall   |                | cgroup, firewall  |
          +---------------------+                +--------------------+

                OS APIs and local files are the outer trust boundary.
                No platform project references Desktop or CLI.
```

The composition root selects exactly one OS socket provider and the available optional
enrichers using `OperatingSystem.IsWindows()` / `IsLinux()`. Unsupported operating
systems fail with a clear diagnostic before a window is created.

## Core domain

### Capture envelope

The fundamental return type is an immutable `InventoryGeneration`, not a mutable list:

```text
InventoryGeneration
  GenerationId
  CaptureStartedAt / CaptureCompletedAt
  NetworkNamespaceIdentity
  Observations: immutable array<SocketObservation>
  ProviderResults: immutable array<ProviderResult>
  IsComplete / TruncationReason
```

Each `ProviderResult` has `Available`, `Unsupported`, `PermissionDenied`, `Partial`,
`TimedOut`, `Cancelled`, or `Failed` state and a stable diagnostic code. Exception
messages and raw command output are not persisted as user-facing facts.

`SocketObservation` stores literal endpoint data, normalized enums, process identity,
optional ownership/enrichment records, timestamps, and evidence references. It does
not store display strings such as “LAN reachable.” Those are localized renderings of
typed evidence.

### Implemented interfaces

```csharp
public interface ISocketInventoryProvider
{
    ValueTask<RawInventoryGeneration> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken);
}

public interface IProcessMetadataProvider;
public interface IServiceAttributionProvider;
public interface IContainerAttributionProvider;
public interface INetworkInterfaceProvider;
public interface IFirewallEvidenceProvider;
public interface ISecureFileStore;
public interface ISnapshotStore;
public interface IReportExporter;
```

Provider signatures favor batch operations. Calling a provider once per row creates
avoidable races and `O(n)` system-call explosions. Interfaces return bounded batches
with individual field outcomes and accept cancellation. Test doubles are used by Core,
integration, updater, architecture, and UI tests.

### Pipeline

```text
Capture sockets
    -> normalize address/state and validate bounds
    -> take process/service/interface batch snapshots
    -> enrich with bounded concurrency
    -> revalidate process start identity
    -> derive bind/interface classifications
    -> collect firewall candidates independently
    -> build immutable generation with completeness report
    -> diff against previous generation or explicit snapshot
    -> publish one generation to Desktop/CLI
```

Scheduled captures never overlap. Cancellation stops enrichment promptly and returns
no half-published UI generation. A completed but partial generation is publishable
only with its warning state. The UI reconciles rows by identity after receiving the
whole generation, which preserves selection and prevents visible intermediate states.

### Identity and diff

Use a provider-stable socket cookie or OS-reported creation identity when available.
Fallback identity contains address family, protocol, network namespace, endpoint tuple,
PID, and process start identity. Store an `IdentityStrength` and never serialize a
fallback key as if it were kernel identity.

Snapshot diff uses two levels:

- endpoint identity matches the logical local socket/connection; and
- ownership identity detects a process, service, container, or binding change.

Conflicting fallback matches become `Ambiguous`. Active TCP state changes are field
changes when socket identity is strong; with a weak tuple they may remain ambiguous.
The diff algorithm is deterministic, symmetric where applicable, and independent of
display order.

## Windows provider

### Socket source

Use documented IP Helper APIs:

- [`GetExtendedTcpTable`](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable)
  for IPv4 and IPv6 TCP listener/connection tables with owner PID data;
- owner-module table variants when available for the documented creation timestamp,
  with a fallback to owner-PID tables if permission or platform behavior prevents it;
- [`GetExtendedUdpTable`](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedudptable)
  for IPv4 and IPv6 UDP endpoints; and
- documented row layouts for each address family, with size/alignment tests on x64 and
  ARM64.

The standard two-call buffer-size pattern must handle `ERROR_INSUFFICIENT_BUFFER`,
integer overflow, inconsistent row counts, and a table changing between calls. Retry a
bounded number of times, then return partial/failed evidence rather than looping.

P/Invoke uses source-generated `LibraryImport` where practical and
`DefaultDllImportSearchPaths(System32)` for Windows system DLLs. Raw ports, addresses,
FILETIME values, state values, and structure padding receive fixture and live tests.

### Processes and services

Open each process with the minimum query right and use documented image-path APIs such
as [`QueryFullProcessImageNameW`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew).
Capture a process creation identity and recheck it before joining metadata to sockets.
Protected processes, access denial, and exit races are per-field outcomes.

Use
[`EnumServicesStatusExW`](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-enumservicesstatusexw)
with process information to batch-map running services to PIDs. The API may silently
omit services the caller cannot query; this must mark service attribution partial.
Several services can legitimately share a process, so all matching units are retained
with a `SharedProcess` qualifier.

### Firewall evidence

Read active Windows Firewall profiles and rules through the documented
[`INetFwPolicy2`](https://learn.microsoft.com/en-us/windows/win32/api/netfw/nn-netfw-inetfwpolicy2)
family. Normalize direction, action, enabled state, protocol, local ports/addresses,
profiles, application path, service, interfaces, and remote scope. Matching produces
candidate rules plus a reason trace.

The evaluator is intentionally not an effective Windows Filtering Platform simulator.
Rule precedence, Group Policy, IPsec, application packages, edge traversal, third-party
filters, and runtime network category can change the result. Default actions and
candidate rules are shown separately. “No matching rule found” is not “blocked.”

## Linux provider

### Socket source

The 1.0 primary provider opens `AF_NETLINK` with `NETLINK_SOCK_DIAG` and requests
bounded IPv4/IPv6 TCP and UDP dumps through the documented version-two
`SOCK_DIAG_BY_FAMILY` ABI. It validates every message/header length, sequence number,
multipart termination, `NLMSG_ERROR`, dump interruption, family, address, state,
inode, row count, cancellation poll, and ten-second deadline before accepting data.

If the kernel path is explicitly unavailable or returns invalid/incomplete native
data, the facade records a diagnostic and falls back to strictly bounded parsing of
`/proc/net/tcp*` and `/proc/net/udp*`. Both paths correlate socket inodes through
accessible `/proc/<pid>/fd` links. The presence or output format of `ss` and `lsof` is
never required. Reference: [`sock_diag(7)`](https://man7.org/linux/man-pages/man7/sock_diag.7.html).

### Process and service correlation

Correlate diag inode/cookie data with `socket:[inode]` targets in accessible
`/proc/<pid>/fd` entries. Read `/proc/<pid>/stat` carefully around parenthesized names,
capture process start ticks plus boot identity, and revalidate. Read the `exe` symlink
as data; never follow it for execution. `hidepid`, Yama, short-lived processes, other
users, and container namespace boundaries commonly make attribution partial.

The 1.0 service provider performs bounded parsing of `/proc/<pid>/cgroup` when the
systemd runtime marker exists. It never spawns `systemctl`. On a non-systemd
distribution it returns `Unsupported`, while socket/process inventory still works.
A read-only systemd D-Bus provider may be added later after separate compatibility and
privacy review.

### Namespaces and containers

The default provider observes only its current network namespace. It does not use
`setns`, inspect namespace file descriptors with elevated rights, or enumerate every
container namespace. Record the namespace identity in every generation so snapshots
from different namespaces cannot be compared silently.

Best-effort attribution parses accessible cgroup and namespace metadata with bounded,
versioned recognizers. It may label evidence for Docker, Podman, containerd, or a
systemd scope only when an ID/name is actually present. Opening the Docker daemon
socket is excluded by default because control of that socket can be equivalent to host
root access; Podman's API similarly grants broad control as its service user. References:
[Docker daemon socket security](https://docs.docker.com/engine/security/protect-access/)
and [Podman service security](https://docs.podman.io/en/latest/markdown/podman-system-service.1.html).

Host proxies, nftables DNAT, rootlesskit, and slirp/pasta forwards may not have a
one-to-one owning process. Represent the forwarding evidence separately from socket
ownership.

### Firewall evidence

There is no assumption that every Linux system uses nftables or firewalld. The provider
chain may use:

1. an accessible read-only firewalld D-Bus view for its managed runtime zones;
2. `libnftables` after an ABI/support spike; or
3. the installed `nft` binary with fixed argument tokens and JSON output as an optional
   fallback.

If `nft` is invoked, resolve a trusted absolute executable, set
`UseShellExecute=false`, pass an argument list rather than a command string, provide no
untrusted stdin, use a minimal environment, bound stdout/stderr, enforce a timeout,
check the exit code, and validate the documented JSON schema. Text output is never
parsed as rules. Missing binaries and `Operation not permitted` are expected states.

The nftables evaluator understands only explicitly supported match/action constructs.
Unknown expressions, custom chains, sets/maps, hooks/priorities, policy routing,
iptables compatibility layers, eBPF, namespaces, or another firewall cause a partial
assessment. Reference: [nftables manual and JSON mode](https://netfilter.org/projects/nftables/manpage.html).

## Classification engine

`BindingClassifier` is pure Core code driven by literal socket addresses and a captured
interface inventory. Rules are table-tested for all IPv4 loopback addresses, `::1`,
unspecified/wildcard addresses, private/ULA/link-local/global ranges, scoped IPv6,
removed addresses, tunnels, virtual adapters, and unknown interface types.

The classifier returns facts, not a risk score:

```text
BindingClassification
  DirectScope
  AddressFamilyCoverage
  MatchingInterfaceIds
  InterfaceContexts
  DerivationCode
  EvidenceCompleteness
```

`FirewallCandidateMatcher` is a separate pure component with a platform-normalized
rule model. It retains every unsupported predicate and explains why a rule is only a
candidate. No automatic “security grade” or severity is derived from port number,
process reputation, or an allow rule.

## Storage and local data

### Default locations

- Windows configuration/state: `%LOCALAPPDATA%\OeXYZ\SocketScope`.
- Linux configuration: `$XDG_CONFIG_HOME/oexyz/socketscope`, falling back to the XDG
  specification's user configuration location.
- Linux snapshots/logs: appropriate `$XDG_STATE_HOME/oexyz/socketscope` subdirectories.
- Portable mode: an explicit `--portable` flag or documented marker, never silently
  inferred from a removable drive.

Paths remain complete in the live UI, snapshots, and Full reports. The explicit
Share-safe report profile reduces user names in recognized home-directory paths.

### Secure atomic writes

`ISecureFileStore` owns settings, snapshots, and logs. It creates private directories,
creates a random sibling temporary file with create-new semantics, writes and flushes
content, verifies size/schema, atomically replaces the destination, and flushes the
directory where supported. It never follows a user-controlled link during a write.

The Windows implementation uses handles that inspect/reject reparse points, a DACL for
the current user, link-count checks, and documented atomic replace/move APIs. The Linux
implementation uses `0700` directories, `0600` files, a held directory descriptor,
`openat` + `O_NOFOLLOW`, link-count checks, random same-directory temporary files,
`fsync`, and `renameat`. An inability to guarantee the policy blocks the write; it does
not silently downgrade.

Settings have a schema and unknown-field preservation policy. The language field is
absent until explicitly chosen. Snapshots use a published JSON Schema and a maximum
depth, file size, string length, and observation count. Importing a snapshot performs
no OS action.

### Logging

`Microsoft.Extensions.Logging` feeds an internal bounded JSON-array provider with
stable event IDs. It deliberately discards formatted messages, state values, exception
messages, and stack traces. Persisted entries contain only UTC time, level, sanitized
category, numeric/name event identity, and whether an exception existed. Up to 2,048
entries are atomically rewritten to the private `current.json` log. Endpoint addresses,
ports, hostnames, process names, paths, user names, command lines, environment data,
snapshot content, and report previews never enter the log model. A canary test passes
a sensitive value through message state and exception text and asserts it is absent.

## Reports and schemas

`ReportBuilder` converts an inventory generation into a `ReportDocument` using an
explicit `Full` or `ShareSafe` profile. Exporters accept only `ReportDocument`, not raw
platform objects. Full is the CLI default; the desktop exposes Full and Share-safe as
separate actions. Share-safe applies the user's allowlisted field-category choices and
records the effective options plus per-category reduction counts in the document.
Observation IDs are always tokenized for Share-safe output. The default also reduces
non-loopback IPs, PIDs, user-name path segments, container identifiers, and
firewall-rule names; process and service names stay diagnostic by default but can be
selected explicitly.

The desktop may additionally store up to 32 bounded custom sensitive literals in the
private settings store. `ReportBuilder` matches them case-insensitively only against
retained attribution/evidence text, replaces matches with per-report HMAC tokens, and
records only configured/match counts. Plain configured values never enter a report or
log. Full reports ignore them. The CLI deliberately exposes no secret-bearing literal
argument.

JSON schemas live under `schemas/` with semantic version fields and golden fixtures.
HTML uses `HtmlEncoder.Default`, an immutable local template, no JavaScript, no remote
resources, and a restrictive CSP. Export paths go through `ISecureFileStore` and are
not opened automatically after writing unless the user explicitly requests it.

The MVP has no custom regex, XPath, JSONPath, template, or script configuration. Custom
sensitive values are bounded literal strings, not executable matching rules. The HTML
exporter encodes every untrusted value regardless of profile.

## Desktop state and dependency injection

The Desktop composition root registers platform providers, Core services, settings,
localization, and view models. View models receive interfaces and `TimeProvider`; they
never call static platform APIs. The capture coordinator is application-scoped and
owns one cancellation tree. Window close cancels capture and bounded file writes.

The live table holds one immutable generation plus a keyed observable projection.
Diffing happens off the UI thread; only the minimal key-based change set is dispatched.
Selected key, focus owner, scroll anchor, column state, and sort/filter state are
reconciled independently. No timer callback mutates controls directly.

Localization uses one strongly typed presentation string service for the Avalonia UI
and an explicit language edge in the CLI. Diagnostic codes remain invariant and
localized messages are resolved at the presentation boundary. The language setting is
absent until a user explicitly selects German or English. English is used in memory on
first launch and for CLI calls without `--language`.

The desktop window starts maximized inside the operating system work area. Avalonia
measures layout in device-independent pixels, so operating-system DPI scaling feeds the
same breakpoints as window resizing. Below 980 DIPs the fixed sidebar is replaced by a
horizontally scrollable navigation row. Summary cards, report choices, filter controls,
snapshot actions, settings fields, and detail columns reflow at view-specific
breakpoints. Large tables retain bounded minimum column widths and expose horizontal
scrolling rather than clipping content.

## Update and network architecture

No service starts a network request during ordinary launch. The optional version check
is behind `IDesktopUpdateService`, disabled by default, and targets only the fixed
`Oexyz/SocketScope` GitHub Releases API. It runs after an explicit button press or
a previously saved opt-in.

The independently tested updater component is bundled into the single application
executable. It accepts only strict newer `vMAJOR.MINOR.PATCH` releases that are stable
and marked immutable by GitHub. It selects one exact RID asset, requires the GitHub
API's SHA-256 digest and declared length, follows a bounded allowlisted HTTPS redirect
chain, and verifies the complete download before a protected staging rename.

Installation is a second, explicit operation after the desktop displays version,
origin, size, and digest. The new Windows executable (or the executable safely
extracted from the verified Linux archive) runs as a short-lived, non-elevated helper.
It rechecks helper, incoming, and current hashes; waits for the original PID and start
identity to exit; replaces only same-directory fixed-name files while retaining a
rollback copy; runs a bounded version/resource health check; restores the old binary
byte-for-byte on failure; and restarts only after success. Hidden helper arguments do
not confer privilege, and updates are refused while the application is elevated.
GitHub artifact/release attestations remain an additional consumer verification layer;
the local updater does not claim that a digest proves source safety and does not yet
verify attestations itself.

## Terminal integration architecture

The same single-file application dispatches desktop, inventory CLI, updater, and
terminal-integration modes before Avalonia starts. With no arguments, or with `gui`, it
starts the desktop. `shell install` copies the running published binary to the fixed
ordinary-user command path; no shim, service, package manager, or second permanent
executable is introduced.

The installer refuses elevated and non-single-file developer runs, rejects conflicting
filesystem commands already discoverable through process/user/machine PATH values,
and never invokes a shell. It creates and hardens a fixed user-owned bin directory,
rejects links/reparse points, performs a same-directory atomic copy or replacement,
and verifies the complete SHA-256. The Windows user PATH is appended only after the
copy passes verification. Failure triggers restoration of the old PATH and executable;
rollback failure is a distinct error, and a verified target is retained if PATH itself
cannot be restored. Linux uses `~/.local/bin` but does not make assumptions about Bash,
Zsh, Fish, or desktop session startup files. The stable path also becomes the exact
target of a later confirmed self-update.

The Settings card calls this same installer through
`IDesktopTerminalIntegrationService`; it does not duplicate PATH logic. The service
returns typed status and install results to the view model. The UI displays only a
sanitized fixed path (`%LOCALAPPDATA%` or `~`) and never forwards user-entered command
text to a shell.

## Build and release architecture

- Pin SDK and NuGet dependencies; commit lock files; enable warnings as errors, NuGet
  audit, deterministic/CI build properties, repository source metadata, and embedded
  or portable deterministic PDBs as appropriate.
- Build/test on Windows and Linux. Publish self-contained single-file applications for
  `win-x64`, `win-arm64`, `linux-x64`, and `linux-arm64`.
- Exercise Windows ARM64 and Linux ARM64 on native or transparently documented
  hardware/emulation before labeling them supported. Cross-publish alone is not test
  evidence.
- Publish Windows as a direct `.exe`. Wrap the Linux single binary and documentation
  in a deterministically ordered `tar.gz` with normalized timestamps and executable
  modes. Perform two clean-build comparisons and document unavoidable differences.
- Generate an SPDX or CycloneDX SBOM for each artifact, SHA-256 checksums after final
  packaging, and GitHub artifact attestations for application assets, checksum manifest, and
  SBOMs. Attestations identify source/workflow; they do not prove the software is safe.
- Pin every GitHub Action to a full commit SHA, minimize job permissions, separate
  untrusted pull-request CI from release permissions, use a protected release
  environment, and enable Dependabot and CodeQL.
- Release only from a version tag whose source commit passed the full matrix. The
  release notes link the source tree and changelog and state Windows Authenticode and
  Linux package-authenticity status plainly.

References:
[GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations),
[GitHub SBOM export/generation](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/establish-provenance-and-integrity/export-dependencies-as-sbom), and
[full-SHA Action policy](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository).

## Architecture tests

Automated dependency tests enforce:

- Core references no Desktop, Avalonia, platform project, native interop, or process
  execution namespace;
- platform projects reference Core but not each other, Desktop, or CLI;
- Desktop/CLI contain no binding, firewall, or report-transformation algorithms;
- exporters cannot reference raw platform inventory models;
- OS command execution exists only in an allowlisted Linux adapter;
- production views cannot reference fixture/sample namespaces; and
- no project marked cross-platform targets a `-windows` framework.

## Remaining validation risks

Release gates still require native Windows ARM64 and Linux ARM64 smoke tests,
non-root `/proc` visibility across representative distributions, nftables permission
matrices, DataGrid/accessibility testing at very large row counts, AT-SPI behavior under
X11 and Wayland, and secure-write behavior on the documented minimum filesystems.
The current sock_diag implementation has native WSL/Docker x64 evidence but still needs
representative kernels and native ARM64 coverage. Failed validation narrows documented
support; it never weakens the truth contract.
