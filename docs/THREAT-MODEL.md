# SocketScope threat model

> Status: reviewed against the implemented 1.0 release-candidate data flows on
> 2026-08-13. It must be updated when data flows or privilege boundaries change.

## Security objectives

SocketScope must preserve:

1. **System safety:** observing sockets must not execute attacker-controlled content,
   grant new privileges, or change processes, services, containers, or firewalls.
2. **Conclusion integrity:** the product must not turn partial local evidence into an
   unsupported reachability or safety claim.
3. **Local privacy:** endpoints, user paths, process identity, host details, snapshots,
   logs, and reports must remain local unless the user explicitly exports them.
4. **Artifact integrity and origin:** published binaries must be traceable to source
   and workflow, with limitations of each verification mechanism documented.
5. **Availability:** a malicious local process or imported file must not make the UI
   unusable through unbounded data, pathological text, or parser behavior.

Read-only does not mean risk-free. Socket metadata is attacker-controlled display and
export input, and a misleading exposure explanation can cause a user to make a harmful
decision elsewhere.

## Scope and assumptions

### In scope

- Desktop and CLI processes running as the signed-in user.
- Windows IP Helper, process, Service Control Manager, and firewall-policy adapters.
- Linux netlink, `/proc`, cgroup/systemd, and optional firewall adapters.
- Settings, logs, snapshots, JSON and HTML reports, portable mode, localization, and
  optional release-metadata checks.
- Build workflows, dependencies, packaged runtimes/assets, SBOMs, checksums, and
  attestations.

### Trust assumptions

- The OS kernel and currently loaded security policy are trusted to enforce the user's
  privileges. Kernel/root or administrator compromise can falsify observations and is
  outside the ability of this user-space tool to defeat.
- Malware already running as the same user may read memory, automate the UI, or read
  user-owned files. SocketScope reduces stored data and file permissions but cannot
  isolate itself from an equally privileged attacker.
- GitHub and certificate authorities are part of the optional online version-check and
  release-distribution trust path. Attestation verification narrows provenance; it is
  not a claim that source or builds are vulnerability-free.
- External NAT, routers, cloud security groups, carrier filtering, VPN peers, reverse
  proxies, and another host are not observed in 1.0.

### Adversaries and adverse inputs

- An unprivileged local process deliberately chooses a hostile process name, path,
  socket pattern, command line, or rapid lifetime.
- Another local user creates inaccessible processes or files.
- A user opens a malicious or unexpectedly large snapshot or chooses a hostile export
  destination.
- A compromised or unusual `nft`/firewall tool emits malformed, huge, or deceptive
  output.
- A dependency, GitHub Action, package feed, release account, website, or update
  endpoint is compromised.
- A well-intentioned user misreads a technically correct but poorly worded status.

## Data classification

| Class | Examples | Policy |
|---|---|---|
| Sensitive local evidence | IP addresses, hostnames, executable paths, user path segments, service/container names, process names, connection times, user-configured sensitive literals | Memory during live view; snapshots and Full reports only on explicit save/export; omitted from logs; allowlisted category reductions and bounded literal replacement in explicitly selected Share-safe reports; configured literal plaintext remains only in the private settings store |
| Highly sensitive and unnecessary | Environment variables, tokens, passwords, full command-line arguments | Do not collect or map into reports in the MVP |
| Operational metadata | Counts, provider status, duration, schema/app version, bounded error code | Allowed in bounded local logs |
| Public product data | Localized UI strings, schemas, license notices, release checksums/SBOMs | Shipped in repository/release |

## Trust boundaries and flows

```text
Untrusted local processes / OS tables / firewall state
                         |
                         v
       [bounded platform parsers and typed availability results]
                         |
                         v
       [Core normalization + classification + immutable generation]
                 |                         |
                 v                         v
          Desktop / CLI             explicit local snapshot
                 |                         |
                 +------------+------------+
                              v
               [explicit report-profile boundary]
                              |
                              v
                    JSON / inert HTML report

Optional explicit version check -> exact HTTPS endpoint -> bounded metadata only
Release workflow -> executable assets + SBOM + checksums + provenance attestations
```

There is no UI-to-root helper, plugin loader, arbitrary script/template engine, remote
host connection, packet payload capture, or package installer in 1.0.

## Risk scale

`High` can plausibly lead to code execution, privilege change, serious privacy loss, or
a materially dangerous false conclusion. `Medium` can cause local disclosure,
persistent corruption, or denial of service. `Low` is contained degradation with an
obvious recovery path. Ratings assume the proposed controls; residual risk is stated
separately.

## Threats and controls

### Hostile observed metadata

| ID | Threat and consequence | Controls | Verification | Residual risk |
|---|---|---|---|---|
| T01 High | **Manipulated process names, service/container names, or command lines** contain control characters, bidi overrides, fake extensions, markup, terminal escapes, or shell syntax. They could spoof the UI, inject reports, alter terminal output, or become commands. | Do not collect command lines by default. Treat every name as text. Bound UTF-8/UTF-16 length; replace invalid sequences; visibly annotate bidi/control characters; never interpolate into markup, a shell, a URI, or an executable argument. CLI escapes terminal controls and supports machine JSON separately. | Corpus/property tests for C0/C1, ANSI, bidi, invalid surrogates, long Unicode, quotes, and newlines in UI, CLI, JSON, and HTML. | A screenshot can still confuse a viewer if hostile text resembles an ordinary name; explicit field labels and control markers reduce but cannot eliminate this. |
| T02 High | **Malicious executable paths** use device paths, UNC syntax, trailing characters, alternate data streams, embedded newlines, or links to trick display or cause access to another resource. | Paths are evidence strings, never commands. No automatic open, icon extraction, signature check, or file traversal in 1.0. Query by already-open process handle where possible. Normalize only for comparison with preserved literal/source; cap lengths; escape display/export. | Windows and Linux path corpora; verify no file/network access occurs when selecting a row; architecture test forbids shell APIs in presentation. | The OS can return a path that changes after capture; the UI labels it captured evidence rather than a current guarantee. |
| T03 High | **Race conditions during process analysis and PID reuse** attach an innocent process's path/service to an attacker's old socket or crash capture. | Capture process start identity, join in batches, revalidate before publish, and use socket cookie/creation identity where available. Return `Disappeared` or `Ambiguous`; never retry indefinitely or join on PID alone. Immutable generations prevent partial UI joins. | Deterministic fake provider that exits/reuses thousands of PIDs at each pipeline stage; live churn integration test; ThreadSanitizer-equivalent review of native lifetime handling. | Some OS rows lack strong socket identity, so a small ambiguity remains and is displayed as such. |
| T04 Medium | **Unexpectedly large or rapidly changing connection tables** exhaust memory/CPU, starve the UI, or create perpetual capture overlap. | 100,000-row hard cap, bounded strings, batch calls, bounded concurrency, cancellation, no overlapping capture, immutable generation size checks, virtualized UI, bounded tombstones, and bounded export. Mark truncated captures prominently. | 50,000/100,000/over-limit synthetic tests, 30-minute churn soak, cancellation latency, memory allocation budget, fuzzed counts/length arithmetic. | Legitimate very large hosts may receive partial results; correctness is favored over completeness. |
| T05 Medium | **Malformed native data** uses invalid row counts, lengths, alignments, netlink attributes, enum values, or truncated buffers to cause memory errors or wrong endpoints. | Managed parsing where possible; checked arithmetic; exact structure tests per architecture; validate all native lengths before access; bounded retry; unknown enum preservation; safe handles and source-generated interop. | Fuzz netlink/proc/native-fixture parsers, x64/ARM64 layout tests, truncated corpus, static interop analysis, live comparisons with platform tools as non-authoritative diagnostics. | A kernel/driver bug can still return internally plausible false data. |

### Storage, snapshots, and exports

| ID | Threat and consequence | Controls | Verification | Residual risk |
|---|---|---|---|---|
| T06 High | **Manipulated export data** injects HTML/script, closes tags, creates dangerous links, breaks JSON, or later triggers spreadsheet formulas. | `ReportBuilder` produces a typed document with an explicit Full or Share-safe profile. `System.Text.Json` only; HTML encode every value; trusted static template; no script/forms/remote resources; restrictive CSP; no CSV in 1.0. | OWASP-style payload corpus in every field, parse exported JSON against schema, open HTML with network interception and CSP console checks, assert zero external requests and no executable DOM. | Browser extensions or a compromised local browser remain outside the report's control. |
| T07 High | **Symlink/reparse-point attacks** redirect settings, snapshot, log, or export writes into another file, especially during atomic replacement. | Private base directory; handle-relative traversal; reject reparse/symlink components; create-new random sibling temp; restrictive mode/DACL; flush and atomic replace; recheck destination; do not use path-only check-then-open. Portable mode warns/blocks when guarantees fail. | Race harness repeatedly swaps directory/file links; Windows junction/reparse tests; Linux symlink/hardlink/openat tests on minimum kernels; verify target outside test root never changes. | Filesystems without required semantics may not support safe writes; the operation must fail rather than claim atomic safety. |
| T08 Medium | **Malicious or huge snapshots/settings** exploit parser depth, duplicate fields, future schema confusion, decompression, or values that later reach OS operations. | Plain uncompressed JSON only; maximum bytes/depth/string/row count; schema and version validation; reject duplicate security-critical properties; no polymorphic type metadata; snapshot import performs no OS action; settings use allowlisted values and ranges. | Fuzz and property-test imports; billion-laughs equivalent/deep nesting/duplicate/canonicalization corpus; future-version and partial-old-version tests. | A valid maximum-size file can still consume noticeable resources, so parsing remains cancellable and off the UI thread. |
| T09 Medium | **Partial or failed atomic write** corrupts the only snapshot/settings file or leaks raw report data through temporary files. | Same-directory temp, restrictive permissions before content, flush, validate, atomic replace, optional last-known-good settings, cleanup by safe handle. Report preview stays in memory; no raw preview temp. | Power-loss/fault injection at every write stage; disk-full and permission tests; inspect temp permissions and recovery behavior. | Filesystem/controller behavior can violate durability guarantees; document “atomic replacement” separately from guaranteed power-loss durability. |
| T10 High | **Sensitive data in screenshots, logs, settings, and reports** exposes addresses, user names, hostnames, paths, configured literals, or business services through support channels. | Logs have an allowlist and exclude observed/settings values. Desktop Full and Share-safe actions are visually separate; Full warns about included identifiers, while Share-safe lists reductions and residual identifiers. Custom literals are count/length bounded, stored only by the private atomic settings store, matched without regex, and never serialized in plaintext into reports. The CLI requires `--share-safe` for the reduced copy and has no literal-on-command-line option. Support docs require review rather than assuming anonymity. | Canary values across capture, settings, exception, log, crash, both report profiles, preview, HTML, and JSON; bounds/overlap/case tests; manual screenshot/support review; repository secret scanning. | Process memory, the visible settings field, OS crash dump, clipboard/recording tools, retained context, or a compromised same-user settings file can still expose or alter values. SocketScope cannot revoke a shared file or screenshot. |

### Privilege and execution boundaries

| ID | Threat and consequence | Controls | Verification | Residual risk |
|---|---|---|---|---|
| T11 High | **Local privilege escalation** through an elevated GUI, setuid/capability binary, unsafe helper IPC, writable install path, or attacker-influenced privileged environment. | No elevation, service, privileged helper, setuid bit, Linux capability, process termination, or service/firewall/container modification in 1.0. The update helper is short-lived, uses the same ordinary-user token, accepts fixed internal arguments, and refuses elevation. Terminal installation writes only to a protected user-owned path. Future privileged work requires a separate minimal helper, authenticated local IPC, fixed verbs, preview, consent, and threat-model revision. | Packaging permission/capability checks; architecture test finds privileged APIs/actions; ordinary-user install/update tests; elevated update and terminal-install refusal; verify capture access denial degrades fields. | A user can still start the inventory UI as root/admin manually; the read-only providers then see more data. The application does not treat elevation as authority for mutating actions. |
| T12 High | **DLL or shared-library hijacking** loads attacker code from the current directory, PATH, environment, plugin folder, or writable extraction path. | No plugin system. Windows system imports constrained to System32 search and explicit library names. No PATH-based native load. Self-contained files are integrity-covered; installer/package guidance requires user-owned or protected extraction. App is never privileged, limiting escalation value. Release startup does not load website/user assets as code. | Static scan of `DllImport`/`LibraryImport`/`NativeLibrary.Load`; planted-DLL tests in current/data directories; dependency inventory/SBOM; packaging permissions. | A same-user attacker who can replace application files can normally run code as that user; code signing and artifact verification help before installation, not after same-user compromise. |
| T13 High | **Shell/argument injection from external tools**, especially `nft`, executes a process name/path/rule fragment or hangs on output. | External tools are optional and never the socket source. One allowlisted adapter resolves a trusted absolute binary, uses no shell, fixed argument tokens, minimal environment, closed stdin, bounded stdout/stderr, timeout/cancellation, exit-code check, and strict JSON schema. No observed string becomes an argument. The only other process launches are the updater's already verified absolute application paths with fixed tokenized arguments. | Architecture test permits process start only in the reviewed firewall adapter and self-updater; fake malicious executables; huge/slow/binary output; missing/version-varied/permission-denied tests; updater argument and hash tests. | A compromised trusted binary executes as the same user; provider status then cannot be trusted. Show source and allow disabling it. |
| T14 High | **Container engine socket access** grants broad or root-equivalent control merely to obtain labels/port mappings. | Do not open Docker/Podman APIs by default, add users to groups, change socket permissions, or request credentials. Prefer cgroup/namespace/NAT evidence. Any future opt-in engine integration needs a separate warning and least-capability design; read-only HTTP verbs do not make a control socket read-only. | Test with absent/inaccessible engine sockets; static network/socket destination audit; documentation review against Docker/Podman security guidance. | Container attribution remains incomplete, especially for Docker Desktop and rootful engines. This is preferable to expanding privilege. |

### Inference, firewall, and networking

| ID | Threat and consequence | Controls | Verification | Residual risk |
|---|---|---|---|---|
| T15 High | **False exposure conclusion** equates wildcard/private/global binding or a firewall allow candidate with LAN/Internet reachability. A user may expose or ignore a service based on it. | Four separate evidence dimensions; external is always `Not tested`; exact derivation/source in details and exports; no risk score; unknown predicates make firewall evaluation partial; copy review bans unsupported wording. | Exhaustive classifier table; localization snapshot tests; semantic UI tests assert forbidden state combinations/copy; expert review of every status string. | Users can still infer more than stated. Repetition near high-salience counts and reports reduces the risk. |
| T16 High | **Incorrect firewall rule matching** ignores profile/zone, precedence, default policy, sets/maps, third-party filters, namespaces, or IPsec and says allow/block incorrectly. | Results are candidate evidence, not effective verdicts. Retain unsupported conditions, active context, default action, conflicts, and provider completeness. “No matching rule” is never “blocked.” | Rule fixture matrices, differential diagnostic comparisons on isolated VMs, unknown-expression tests, Group Policy/firewalld/nftables variants, manual review. | Complete firewall simulation is intentionally outside MVP; many systems will show partial/not evaluated. |
| T17 Medium | **Unexpected network access or DNS leakage** resolves remote addresses, loads assets, checks updates, or follows report content without consent. | Numeric addresses only by default; no reverse DNS; all UI assets local; no telemetry/ads; update check off; exact documented endpoint only after action/opt-in; HTML has no remote URLs; tests deny undeclared origins. | Network namespace/proxy tests during launch, capture, filter, snapshot, and report; fail build if unexpected DNS/HTTP occurs; inspect packaged assets. | The OS/framework may perform platform services outside app control; test and document any discovered behavior before release. |
| T18 Medium | **Namespace/container ambiguity** compares different Linux namespaces or attributes host NAT/proxy evidence to the wrong container. | Record namespace identity, refuse silent cross-namespace comparison, represent socket owner and forwarding evidence separately, never join namespaces, and use `Unknown/Ambiguous` labels. | Network-namespace fixtures; Docker/Podman rootful/rootless host-network/bridge/published-port tests; WSL/Docker Desktop limitation tests. | Full cross-namespace attribution is impossible without more privilege or engine cooperation in many configurations. |

### Supply chain and updates

| ID | Threat and consequence | Controls | Verification | Residual risk |
|---|---|---|---|---|
| T19 High | **Unsafe update metadata or executable** is substituted, downgraded, swapped after download, or accepted because its checksum shares the publisher's trust domain. A failed replacement could leave the program unusable. | Checks are off until action/opt-in. The integrated updater fixes repository/API paths; requires a strict newer stable immutable release, exact RID name, HTTPS/allowlisted redirects, declared size, and GitHub-provided SHA-256; bounds metadata, download, archive entries, and expanded executable; and uses constant-time digest comparison. Installation separately requires a visible version/origin/size/digest preview and explicit click. A non-elevated helper rechecks itself, incoming, and current hashes; waits for PID plus start identity; rejects linked paths; replaces only same-directory random fixed-suffix files with a rollback copy; executes a bounded version/resource health check; rolls back byte-for-byte on failure; and restarts only on success. | Host/redirect/size/schema/downgrade/mutable-release/digest/partial-file tests; tampered-incoming tests; simulated health failure; real 1.0.0 -> 1.0.1 single-file replacement; real version-mismatch rollback; package/runbook review. Final workflow evidence verifies asset/SBOM/checksum attestations independently. | GitHub/source/action/account compromise or malicious reviewed source can still produce matching metadata and provenance. A same-user attacker who can race application files already has equivalent execution authority. The updater does not yet verify GitHub attestations or publisher signing locally; neither checksum nor attestation alone proves safety. |
| T20 High | **Compromised dependencies or GitHub Actions** exfiltrate source/secrets, alter artifacts, or add vulnerable code. | Minimal dependencies; lock files; NuGet audit; Dependabot; CodeQL; SBOM; SHA-pinned Actions; minimal permissions; no release secrets in pull-request jobs; protected release environment; review generated diffs and Action updates; artifact attestations. | Clean CI, dependency/license scan, CodeQL, secret scan, reproducibility comparison, attestation verification from a separate job/machine, branch-protection audit. | Maintainer and platform compromise cannot be eliminated. Attestations establish origin, not safety. |
| T21 Medium | **Unlicensed or remotely mutable fonts/icons/images** create legal risk, tracking, or altered UI content. | All runtime assets committed locally with source URL, exact version/hash, license file, redistribution review, and `THIRD_PARTY_NOTICES.md`; deterministic asset inventory; no URL fonts/icons/images. | CI compares packaged assets to notice inventory, checks licenses, and runs app with network blocked. | License interpretation may require legal review; omit uncertain assets rather than ship them. |
| T22 High | **Terminal installation or PATH hijacking** shadows another command, copies a substituted executable, follows a link, writes a privileged directory, or leaves PATH and files inconsistent. | Installation is explicit, single-file-only, and refused while elevated. Targets are fixed under `%LOCALAPPDATA%` or `~/.local/bin`; process/user/machine PATH entries are checked for filesystem conflicts; the Windows entry is appended, never prepended; shell commands and startup files are not used. The installer rejects links/reparse components, applies a current-user DACL or `0700`, uses random same-directory staging, verifies SHA-256, and attempts file/PATH rollback on failure. A rollback failure is surfaced, and a verified target is retained if PATH restoration cannot be confirmed. | Pure PATH-policy/conflict tests; real atomic-copy and path-failure rollback tests with an injected environment; packaged `shell --help`/`shell status` smoke; ordinary-user install test in clean Windows/Linux VMs before release. | Same-user malware can change user files or PATH with equivalent authority. Shell functions, aliases, command hashing, and a terminal's stale environment cannot be fully inspected cross-shell; status and new-terminal guidance make this limit visible. |

## Privacy-specific abuse cases

- A support agent asks for a full report or raw screenshot when a reduced copy would
  suffice. Documentation lists what each artifact can expose and requires review.
- A process uses a private hostname as its executable/service label. Share-safe keeps
  attribution names for diagnostic value by default. The user can add the hostname as
  a custom literal, but the UI still avoids an anonymity guarantee and requires output
  review.
- Correlatable tokens across reports become identifiers. The export salt is therefore
  random, memory-only, and unique per report.
- A saved snapshot outlives its diagnostic need. No hidden history database or cloud
  retention exists, but 1.0 also has no automatic retention/deletion control. The
  documented local directory can be removed by the user; a later in-app delete action
  requires its own safe-path review.
- Portable mode writes beside the application on a shared/removable drive. The README
  and privacy documentation explain the weaker trust boundary, and unsafe writes fail
  closed.

## Security verification plan

### Automated

- Parser fuzzing for netlink, `/proc`, nftables JSON, Windows native fixtures, settings,
  snapshots, report schemas, and localization placeholders.
- Property tests for classifier invariants, Full/Share-safe transformation boundaries,
  deterministic same-report tokenization, HTML encoding, JSON round-trip, and diff
  symmetry/determinism.
- Race, cancellation, PID reuse, oversized table/file/string, disk-full, permission,
  symlink/reparse, and atomic-write fault injection.
- Architecture rules for dependency direction, process execution, raw exporter access,
  native library loading, network endpoints, and fixture leakage.
- Network-deny integration test that permits zero requests during ordinary launch and
  local analysis.
- CodeQL, dependency audit, secret scanning, binary/SBOM inventory, and action-pin
  verification in CI.

### Manual release gates

- Ordinary-user runs on every supported OS/architecture, including permission-limited
  processes and firewalls.
- Narrator/NVDA/Orca review of evidence status and both report choices.
- Screenshot and support-report privacy review in German and English.
- Windows Defender/SmartScreen and Linux desktop/package behavior documented without
  suppressing warnings caused by absent Authenticode/package signatures.
- Independent checksum, SBOM, and artifact-attestation verification from downloaded
  release assets.
- Threat-model diff: every new data source, network destination, native dependency,
  write path, or privileged capability blocks release until modeled.

## Security response and disclosure

[`SECURITY.md`](../SECURITY.md) uses the established OeXYZ private-report path and
verified OpenPGP fingerprint, defines supported versions, requests minimal reproducible
evidence, and warns reporters not to include real endpoints, credentials, or customer
reports. It deliberately avoids guaranteed response/remediation times. Public issues
must not receive active exploit details before a coordinated fix.

## Accepted residual risks for 1.0

- A root/admin or kernel-level adversary can falsify or hide socket and process data.
- A same-user adversary can read visible data or replace a user-owned portable install.
- Local firewall evaluation is incomplete on complex and third-party policy stacks.
- Container attribution is best effort within the current namespace and user rights.
- External reachability, upstream router/NAT/cloud policy, and packet contents are not
  measured.
- A raw screenshot, snapshot, Full report, or residual identifier in a Share-safe report
  can disclose sensitive data.
- Authenticode/package signatures are absent in 1.0 and may trigger platform warnings;
  checksums and attestations are separate evidence and do not create reputation.

These risks are disclosed in-product and in the README. None may be converted into a
green “safe” state.
