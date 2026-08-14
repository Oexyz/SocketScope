# SocketScope implementation phases 2–7

> Status: 1.0 release-candidate implementation record, updated 2026-08-14.
> Publication remains blocked until the repository owner approves the exact source,
> screenshots, and final artifacts. No commit, tag, push, release, or signing action is
> part of this record.

This file records what was actually implemented after the Phase 1 decision gate.
Detailed test counters and package hashes are generated from the final candidate under
[`docs/release-evidence`](release-evidence/); native-host limitations remain explicit.

## Phase 2 — architecture, threat model, and contracts

Completed:

- immutable socket/evidence models and availability states;
- strict separation of observed binding, derived interface context, candidate firewall
  evidence, and external reachability `NotTested`;
- cancellable provider interfaces, dependency direction, secure-store boundary, and
  typed diagnostics;
- versioned snapshot, report v2, and settings v2 contracts;
- threat model covering hostile names/paths, races, huge tables, exports, links,
  privilege escalation, native-library hijacking, updates, screenshots/logs, false
  exposure claims, firewall ambiguity, container sockets, and terminal/PATH writes;
- explicit exclusion of post-1.0 modifying, privileged, remote, external-probe,
  notification, and background-service functions.

Verification: Core contract/unit tests, architecture dependency/security rules,
schema parsing, formatting, and warning-as-error builds.

## Phase 3 — Core engine and CLI

Completed:

- coordinated inventory enrichment, stable identities, first-observed timeline, and
  bounded 100,000-row capture behavior;
- search, protocol/role/state/scope filtering, deterministic sorting, and change
  reconciliation;
- snapshot save/compare and mismatched-namespace refusal;
- `list`, `watch`, `snapshot`, and `report` commands with cancellation and stable
  exit behavior;
- a single dispatcher where no arguments or `gui` opens Avalonia, while direct
  commands stay in the terminal;
- ordinary-user `shell install/status` terminal integration without a permanent shim.

Verification: Core, integration, terminal, package CLI, real PATH-isolated command,
and cancellation tests.

## Phase 4 — Windows and Linux providers

Completed:

- Windows IP Helper TCP/UDP owner tables for IPv4/IPv6, process metadata, creation
  timestamps where the API supplies them, SCM service attribution, interface context,
  Windows Firewall candidate rules, secure storage, and Docker Desktop forwarding
  proxy evidence;
- Linux `NETLINK_SOCK_DIAG` v2 multipart capture for IPv4/IPv6 TCP/UDP with strict
  sequence/length/error/deadline/cancellation validation;
- explicit `/proc/net` fallback, socket-inode-to-PID correlation, process identity
  revalidation, systemd cgroup services, Docker/Podman/containerd/CRI-O cgroup
  evidence, optional bounded `nft --json` candidate evaluation, and secure storage;
- honest degradation for access denial, disappearing processes, absent systemd,
  unavailable Netlink, unsupported firewall expressions, and unresolved host proxies.

Evidence collected:

- native Windows x64 controlled TCP/UDP and SCM tests;
- WSL 2 Ubuntu x64 live TCP/UDP capture using provider `linux.sock-diag`;
- disposable Debian Docker namespace attribution and Windows Docker Desktop forwarding
  check;
- parser/layout tests that also run on the cross-platform CI matrix.

Not claimed: native Windows ARM64, native Linux desktop, or native Linux ARM64 runtime
validation. WSL DrvFS cannot enforce the Unix ownership/mode invariants required by the
secure-store tests, so those cases are explicitly skipped there and run on native CI.

## Phase 5 — Avalonia desktop

Completed:

- Overview, Connections, Listeners, Changes, Reports, and Settings views;
- keyed live row reconciliation, stable selection, pause/resume, automatic refresh,
  localized search/filter/sort, new/removed/changed presentation, movable/resizable
  table columns, and detailed process/service/container/binding/firewall/timestamp
  evidence;
- maximized startup and logical-pixel responsive reflow for sidebar, navigation,
  cards, filters, detail panels, reports, settings, and terminal actions;
- top-aligned input content, 760×520 minimum, scrollable wide tables, English first-run
  default, and full German/English presentation;
- local Inter and original local icon, visible focus, text-plus-color states, no
  animation dependency, and a restrained high-contrast OeXYZ palette.

Verification: Avalonia headless creation/interaction tests, real header drag, keyboard
Tab traversal, 100/150/200/300 percent render-scale changes, compact/wide reflow,
German header switching, top alignment, and calculated WCAG text contrast. Final
release screenshots are captured from the packaged EXE and inspected at full
resolution.

## Phase 6 — exports, localization, and accessibility hardening

Completed:

- inert UTF-8 HTML and closed-schema JSON from one typed report model;
- Full and configurable Share-safe profiles, in-memory reduction preview, per-category
  counters, firewall-candidate details, and explicit `NotTested` external state;
- default reductions for non-loopback IPs, PIDs, observation IDs, home-path user
  segments, container identifiers, and firewall-rule names; optional process/service
  name reduction plus bounded, non-regex custom sensitive literals whose plaintext is
  never embedded in reports;
- HMAC per-report tokens, HTML encoding, restrictive CSP, no CSV, no remote assets, and
  no command-line/environment collection;
- settings v1 migration to v2 without persisting a language until the user changes it.

Verification: hostile HTML/control-text corpus, JSON round trips, maximum-row preview,
settings migration, configured category/literal reductions, package-generated report/snapshot
validation against the published schemas, localization, DPI, focus, and contrast tests.

Manual Narrator/NVDA and native Linux Orca/AT-SPI evidence is still a release-host gate;
automation does not substitute for it.

## Phase 7 — repository, release, and security gates

Implemented:

- repository policies/templates, MIT license, disclosure and contribution documents,
  CodeQL, dependency review, Dependabot, pinned Actions, locked NuGet restore, audit,
  deterministic settings, and warnings as errors;
- four self-contained targets: Windows x64/ARM64 single EXE and Linux x64/ARM64
  deterministic `tar.gz`;
- SPDX JSON SBOM generation, final SHA-256 manifest, GitHub artifact attestations, a
  protected draft-release boundary, factual release notes, and explicit Authenticode
  status `not-present`;
- fixed-origin opt-in GitHub updater with immutable-release/name/size/redirect/digest
  checks, preview, explicit install confirmation, non-elevated replacement,
  post-replacement health check, rollback, and restart;
- package allowlist, embedded notices/font-license verification, Google Fonts endpoint
  rejection, terminal smoke, updater end-to-end rollback, controlled screenshots, and
  machine-readable plus rendered test evidence.

Latest local Windows x64 evidence: 7/7 suites, 216 passed, 0 failed, 1 skipped out
of 217 tests. The final one-file candidate hash and all screenshot hashes are recorded
under [`docs/release-evidence`](release-evidence/).

The workflow can create only a draft from an approved version tag. It does not publish
automatically. The local updater validates fixed GitHub origin and integrity but does
not locally verify the GitHub attestation or Authenticode; consumers can verify the
release attestation independently. Authenticode signing is deliberately not configured.

## Remaining technical release risks

- ARM64 artifacts are cross-built but cannot be called runtime-validated without
  matching hardware/emulation evidence.
- WSL and Docker prove Linux CLI/provider behavior, not native X11/XWayland,
  Fontconfig/FreeType, Orca/AT-SPI, packaging integration, or every distribution.
- Firewall evaluation remains candidate evidence; third-party filters, complex
  nftables constructs, policy order, IPsec, namespaces, and upstream devices can
  change effective reachability.
- Container attribution depends on visible cgroup/proxy evidence and intentionally
  avoids privileged engine control sockets.
- Very short-lived sockets/processes and restricted metadata can remain unknown.
- External reachability is not tested. No 1.0 screen or report may imply otherwise.
