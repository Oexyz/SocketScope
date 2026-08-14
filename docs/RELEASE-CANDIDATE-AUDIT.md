# SocketScope 1.0 release-candidate requirement audit

> Scope: implementation and local release evidence as of 2026-08-14. This is not a
> publication approval. No commit, tag, push, GitHub release, attestation, or signing
> action is performed by this audit.

This matrix maps the original product request to shipped code, automated tests, and
honestly stated release limits. `Implemented` means a production path exists; it does
not mean that every supported CPU, distribution, desktop environment, firewall, or
container runtime has been exercised on native hardware.

## Product truth contract

| Requirement | Status | Evidence and boundary |
|---|---|---|
| Show TCP connections, TCP listeners, and UDP endpoints | Implemented | Native Windows IP Helper tables; Linux `NETLINK_SOCK_DIAG` with bounded `/proc` fallback; parser and controlled live tests. |
| Show local/remote endpoint, state, process, PID, executable | Implemented | Typed immutable observations and detail view; unavailable or raced metadata is displayed as unknown with a reason. UDP/listener remote endpoints remain not applicable. |
| Distinguish IPv4/IPv6 and loopback, specific-interface, LAN-context, and wildcard bindings | Implemented | Classification is tested independently from interface context. A LAN-context address and a wildcard binding remain different facts. |
| Explain local firewall evidence without claiming Internet reachability | Implemented | Windows Firewall and optional bounded `nft --json` adapters return candidate evidence and unsupported conditions. External reachability is always `NotTested` in 1.0. |
| Attribute Windows services, systemd units, and identifiable containers | Implemented, best effort | SCM and cgroup evidence are implemented. Shared service hosts, hidden namespaces, absent systemd, inaccessible cgroups, and host forwarding proxies remain explicit candidates/unknowns. No engine control socket is required. |
| Explain time | Implemented with platform distinction | Windows creation time is shown where the native table provides it; otherwise SocketScope shows first observed time, never inventing an OS creation time. |

## Desktop and command line

| Requirement | Status | Evidence and boundary |
|---|---|---|
| Overview, Connections, Listeners, Changes, Reports, Settings, Help/Diagnostics | Implemented | Production Avalonia views use live provider data only; no sample observations ship in a production view. Help/Diagnostics contains terminology and metadata-only runtime/provider status, not observed endpoint values. |
| Search, protocol/status/scope filters, stable sorting | Implemented | Core query tests and UI bindings; table columns are resizable, reorderable, and resettable. Localized `Ctrl+C` copying includes visible headings. |
| Automatic refresh, pause, new/removed highlighting, stable selection | Implemented | Captures are cancellable and non-overlapping; keyed reconciliation and bounded removed tombstones avoid full-table replacement and selection loss. |
| Detail view with owner, binding, firewall, service/container, and time evidence | Implemented | Local and remote endpoints, evidence source, reason codes, and `External: not tested` remain visible. Explicit copy actions provide raw details or a separately labeled configured Share-safe summary. |
| Display-aware, scalable, keyboard-operable UI | Implemented and automated locally | Responsive sidebar/compact navigation, card/filter/detail/glossary reflow, top-aligned forms, scrollable tables, `F1`, `F5`, `Ctrl+F`, `Ctrl+C`, keyboard focus, and 100/150/200/300 percent headless render checks. Manual screen-reader and native Linux desktop review remains a release-host gate. |
| English and German; English first-run default | Implemented | Localization parity tests; no language key is written until the user explicitly changes language. |
| `socketscope` in terminals while retaining GUI launch | Implemented | One executable dispatches no arguments/`gui` to Avalonia and commands to the CLI. Settings installs a verified user-local copy and appends the Windows user PATH without elevation. |
| CLI list/watch/snapshot/report output | Implemented | Human and versioned JSON output, filters, sort, cancellation, bounded watch iterations, and English/German help are integration-tested. |

## Snapshots and reports

| Requirement | Status | Evidence and boundary |
|---|---|---|
| Save local snapshots and compare any compatible pair | Implemented | Atomic private storage, closed schema, size/count bounds, namespace check, and field-level new/removed/changed/ambiguous results. |
| Full JSON and readable inert HTML | Implemented | One typed report model, published JSON Schema, strict serializer, HTML encoding, restrictive CSP, no scripts/forms/remote resources. |
| Reduced support export with preview | Implemented | Share-safe preview and counters cover IPs, home-path names, PIDs, process/service/container/firewall identifiers, and observation IDs. |
| Freely configurable sensitive values | Implemented with a safe bounded interpretation | The desktop accepts up to 32 literal values, one per line, applies case-insensitive non-regex replacement, and reports only counts/tokens. Plaintext is stored only in local settings and is never embedded in generated reports. It is deliberately not accepted in CLI arguments, which are visible to other local processes. |
| Avoid command line/environment collection and export injection | Implemented | Neither is collected. Hostile text/control-direction, JSON, HTML, path, and size-limit tests cover export boundaries. CSV is not an export format in 1.0. |

## Platform, security, and repository quality

| Requirement | Status | Evidence and boundary |
|---|---|---|
| Read-only, ordinary-user application | Implemented | Inventory never terminates a process, restarts a service, changes firewall policy, controls containers, or probes an external host. Linux `nft` is optional read-only evidence. |
| Local resources, no telemetry/account/cloud/runtime assets | Implemented | Inter, icon, licenses, and notices are embedded locally; architecture tests reject telemetry packages, unreviewed network endpoints, and Google Fonts endpoints. |
| Safe storage and logging | Implemented | Atomic bounded writes, private ACL/modes, link/reparse checks, no event payloads/state/exceptions in local logs, and storage race/permission tests. |
| Core/Windows/Linux/CLI/Desktop/Test separation and DI | Implemented | Architecture tests enforce dependency direction and constrain external process/network/native-library boundaries. |
| README, governance, security, architecture, threat model, platform/export/testing docs | Implemented | Root policies, issue/PR templates, all requested documents, English technical README, and German introduction are present. |
| CI, CodeQL, dependency review, Dependabot, locked dependencies | Implemented as repository configuration | Actions are full-commit pinned, permissions are scoped, builds treat warnings as errors, and restore is locked/audited. They become remotely observable only after an owner-approved push. |
| Four self-contained targets | Built locally | Windows x64/ARM64 are single EXEs; Linux x64/ARM64 are deterministic tarballs containing one self-contained app plus local documentation/licenses. Windows x64 is runtime-tested here; ARM64 requires matching hardware. |
| SHA-256 and SPDX SBOM | Generated locally | Final assets have one checksum manifest and one SPDX JSON SBOM per runtime. |
| GitHub artifact attestation | Workflow ready, not generated locally | The protected workflow attests final packages, SBOMs, and checksum manifest before creating a draft. An attestation requires an approved tag and GitHub run. |
| Authenticode | Not present by owner decision | Packaging and evidence report `not-present`; neither documentation nor the UI claims the EXE is signed. |
| Updater | Implemented, opt-in network access | Optional startup check/manual check, strict immutable GitHub release metadata, fixed repository/origins, exact RID asset, size/SHA-256 verification, explicit install, non-elevated atomic replacement, health check, rollback, and restart. Attestation verification remains an independent manual release check. |

## Explicitly not implemented in 1.0

The following requested future ideas remain architectural extension points, not hidden
or simulated features: process termination, service restart, firewall modification,
port blocking, external port testing, remote hosts, SSH sessions, Docker/Podman
control, notifications, a persistent background service, and automatic security
scoring. Adding any modifying or privileged action requires a new threat-model review,
preview, plain-language explanation, and explicit confirmation.

## Validation interpretation

The current counters, package hash, skipped-test reason, WSL result, Docker result, and
full-resolution screenshot review live in [`release-evidence`](release-evidence/).
The checked-in test image is a human-readable rendering of sanitized TRX counters; it
does not replace the machine-readable CI logs. WSL and Docker evidence validates Linux
CLI/provider behavior, not a native Linux desktop or accessibility stack. Cross-build
success is not labeled as native ARM64 validation.
