# SocketScope implementation and repository plan

> Status: implementation sequence approved and executed as a 1.0 release candidate on
> 2026-08-13. The milestone definitions below remain the acceptance contract. Actual
> results and unresolved native-host gates are recorded in
> [PHASES-2-7.md](PHASES-2-7.md); a passing build alone does not make a user-facing
> claim complete.

## Delivery rules

- Reliability and truthful degradation take priority over feature count.
- Work remains inside the SocketScope repository.
- Each phase ends with automated tests, applicable manual checks, updated documents,
  and an explicit list of known defects and unsupported states.
- Production views contain no placeholder/sample observations. Fixtures live only in
  tests or an explicitly labeled development harness that is excluded from releases.
- A partially implemented feature is absent or labeled experimental in development;
  it is not documented as a 1.0 capability.
- No later privileged or modifying feature enters 1.0 merely because an interface seam
  exists for it.

## Planned repository structure

```text
SocketScope/
|-- .github/
|   |-- ISSUE_TEMPLATE/
|   |   |-- bug_report.yml
|   |   |-- feature_request.yml
|   |   |-- security-question.yml
|   |   `-- config.yml
|   |-- workflows/
|   |   |-- ci.yml
|   |   |-- codeql.yml
|   |   |-- release.yml
|   |   |-- scorecards.yml
|   |   `-- dependency-review.yml
|   |-- CODEOWNERS
|   |-- dependabot.yml
|   `-- pull_request_template.md
|-- assets/
|   |-- fonts/
|   |   |-- Inter-VariableFont_opsz,wght.ttf
|   |   |-- Inter-Italic-VariableFont_opsz,wght.ttf
|   |   `-- OFL.txt
|   |-- icons/
|   `-- brand/
|-- docs/
|   |-- ARCHITECTURE.md
|   |-- SECURITY-AND-PRIVACY.md
|   |-- PLATFORM-SUPPORT.md
|   |-- EXPORT-FORMAT.md
|   |-- THREAT-MODEL.md
|   |-- TESTING.md
|   |-- DESIGN-SYSTEM.md
|   |-- RELEASES.md
|   `-- decisions/
|       |-- 0001-runtime-and-ui.md
|       |-- 0002-exposure-evidence-model.md
|       |-- 0003-platform-data-sources.md
|       |-- 0004-secure-local-storage.md
|       `-- 0005-update-boundary.md
|-- eng/
|   |-- verify-assets.ps1
|   |-- verify-assets.sh
|   |-- verify-release.ps1
|   `-- verify-release.sh
|-- schemas/
|   |-- socketscope-snapshot-v1.schema.json
|   `-- socketscope-report-v1.schema.json
|-- src/
|   |-- OeXYZ.SocketScope.Core/
|   |   |-- Abstractions/
|   |   |-- Classification/
|   |   |-- Diagnostics/
|   |   |-- Domain/
|   |   |-- Export/
|   |   |-- Inventory/
|   |   |-- Export/
|   |   `-- Snapshots/
|   |-- OeXYZ.SocketScope.Platform.Windows/
|   |   |-- Firewall/
|   |   |-- Interop/
|   |   |-- Processes/
|   |   |-- Services/
|   |   |-- Sockets/
|   |   `-- Storage/
|   |-- OeXYZ.SocketScope.Platform.Linux/
|   |   |-- Containers/
|   |   |-- Firewall/
|   |   |-- Interop/
|   |   |-- Procfs/
|   |   |-- Services/
|   |   |-- Sockets/
|   |   `-- Storage/
|   |-- OeXYZ.SocketScope.Cli/
|   |   |-- Commands/
|   |   |-- Localization/
|   |   `-- Rendering/
|   `-- OeXYZ.SocketScope.Desktop/
|       |-- Accessibility/
|       |-- Assets/
|       |-- Controls/
|       |-- Localization/
|       |-- Services/
|       |-- Styles/
|       |-- ViewModels/
|       `-- Views/
|-- tests/
|   |-- OeXYZ.SocketScope.Core.Tests/
|   |-- OeXYZ.SocketScope.Platform.Windows.Tests/
|   |-- OeXYZ.SocketScope.Platform.Linux.Tests/
|   |-- OeXYZ.SocketScope.IntegrationTests/
|   |-- OeXYZ.SocketScope.ArchitectureTests/
|   `-- OeXYZ.SocketScope.UiSmokeTests/
|-- testdata/
|   |-- firewall/
|   |-- native/
|   |-- procfs/
|   |-- reports/
|   `-- snapshots/
|-- .editorconfig
|-- .gitattributes
|-- .gitignore
|-- CHANGELOG.md
|-- CODE_OF_CONDUCT.md
|-- CONTRIBUTING.md
|-- Directory.Build.props
|-- Directory.Build.targets
|-- Directory.Packages.props
|-- global.json
|-- LICENSE
|-- NuGet.config
|-- OeXYZ.SocketScope.slnx
|-- README.md
|-- SECURITY.md
`-- THIRD_PARTY_NOTICES.md
```

`testdata` contains synthetic or explicitly non-sensitive fixtures and a provenance
README in each directory. Captured data from a developer's real machine is never
committed. Fonts and every packaged asset are mapped to license/provenance entries in
`THIRD_PARTY_NOTICES.md`; CI checks the mapping.

The proposed project license is MIT, consistent with the three reviewed active OeXYZ
projects and suitable for a broadly reusable local diagnostic tool. The repository
owner must supply the exact copyright line before the license is committed. Third-party
assets and libraries retain their own licenses and do not become MIT merely by being
packaged with SocketScope.

## Milestones

### Phase 1: research and written design

**Status:** complete as a documentation-only package on 2026-08-13, subject to owner
approval.

**Outputs**

- Versioned evidence inventory for the profile, three accessible active repositories,
  disabled-repository limitation, and website.
- Design DNA and explicit transfer/do-not-transfer decisions.
- Published-scope competitor comparison and non-goals.
- MVP product contract, architecture, threat model, repository tree, wireframes, plan,
  and risk register.

**Gate**

- All ten requested outputs are linked from `docs/PHASE-1.md`.
- Local Markdown links resolve; source URLs and reviewed commit IDs are recorded.
- No source project, workflow, binary, product screenshot, or placeholder feature was
  created.
- Known evidence gaps are explicit.

There are no SocketScope product tests in this phase because there is no product code.
The relevant test is consistency/link validation of the planning artifacts.

### Phase 2: architecture, threat controls, and platform spikes

**Deliverables**

- Repository foundation, solution/projects, central package versions, lock files, and
  license/notice inventory.
- Accepted ADRs for runtime/UI, evidence model, platform sources, storage, and update
  boundary.
- Immutable domain records, typed availability/evidence states, provider contracts,
  classification truth table, snapshot/report schemas, and test doubles.
- Disposable spikes for Windows x64/ARM64 structures and timestamps; Linux sock_diag,
  proc visibility, systemd/firewall access; Avalonia table/accessibility; secure writes.
- Updated platform support matrix and threat model based on measured results.

**Verification gate**

- Architecture tests prove dependency direction and absence of platform/UI types in
  Core.
- Classifier tests cover IPv4/IPv6 loopback, specific, wildcard, missing-interface, and
  dual-stack evidence without an Internet-reachability state.
- Spikes run on real Windows x64, Windows ARM64, Linux x64, and Linux ARM64 or the
  corresponding target remains explicitly unsupported until tested.
- Dependency/license review and initial CodeQL/CI pass.
- No spike code enters production without tests and review.

### Phase 3: Core engine and CLI

**Deliverables**

- Capture coordinator, normalization, enrichment joins, stable identity, live diff,
  snapshot persistence/comparison, Full/Share-safe reports, JSON/HTML exporters, structured logging,
  and CLI commands, initially against deterministic fake providers.
- Versioned schemas and deterministic synthetic report fixtures.
- CLI human output in German/English and invariant machine JSON.

**Verification gate**

- Unit/property tests for classification, PID reuse, process disappearance, ambiguous
  identity, snapshot diff, cancellation, atomic/faulted writes, schemas, report profiles,
  HTML escaping/CSP, and localization parity.
- 100,000-row cap, 50,000-row performance test, 30-minute churn soak, and bounded
  export tests meet documented budgets.
- Network-deny test records zero connections for all ordinary CLI commands.
- `dotnet test` passes on Windows and Linux with no production provider required.

### Phase 4: Windows and Linux providers

**Windows deliverables**

- IPv4/IPv6 TCP and UDP IP Helper capture, process metadata, SCM service candidates,
  interface inventory, Windows Firewall candidate evidence, and secure file storage.

**Linux deliverables**

- IPv4/IPv6 TCP and UDP sock_diag capture, proc process correlation, systemd/cgroup
  attribution, best-effort container/forwarding evidence, nftables/firewalld partial
  evidence, and secure file storage. `/proc/net` is fallback; `ss`/`lsof` are optional
  diagnostics only.

**Verification gate**

- Fixture/fuzz tests plus ordinary-user live integration tests for all four protocol/
  family combinations.
- Processes disappearing during every enrichment stage, access denial, unknown owner,
  shared Windows service host, missing systemd, missing/varied/denied `nft`, namespace
  mismatch, and cancellation all produce typed results without a crash.
- Docker and Podman matrices cover rootful/rootless bridge, host networking, published
  port/NAT or proxy evidence, absent daemon socket, and Docker Desktop/WSL limitation.
- Results are compared diagnostically with TCPView/PowerShell and `ss`/`lsof` on test
  hosts, but those tools are not test or runtime dependencies.
- Native leak/handle, ARM64 layout, sanitizer/fuzzer, and 30-minute churn tests pass.

### Phase 5: Avalonia desktop

**Deliverables**

- Local fonts/icons and licensed OeXYZ SocketScope theme.
- Responsive shell, Overview, Connections, Listeners, Changes, Reports, Settings,
  evidence detail, provider warnings, and empty/error/permission states.
- Virtualized keyed table updates, stable selection/scroll/focus, filters/sort/search,
  pause/resume, tombstones, keyboard shortcuts, and density/scaling support.

**Verification gate**

- UI smoke tests launch and navigate every production view on Windows and Linux.
- 50,000-row harness meets interaction budgets with no placeholder data in release.
- Automated focus/name/role checks plus manual Narrator, NVDA, and Orca scripts pass.
- Scaling at 100/150/200/300%, 760 x 520 minimum, narrow reflow, high contrast, reduced
  motion, and German text expansion produce no inaccessible content.
- A refresh never changes selection, sort order, scroll anchor, or focus unless the
  selected row genuinely expires and the documented fallback is applied.

### Phase 6: exports, localization, and accessibility hardening

**Deliverables**

- Explicit Full/Share-safe export actions, reduction counts, standalone HTML, and
  schema-valid JSON.
- Complete English/German resources and first-run language behavior.
- Accessibility help/shortcut documentation and final assistive-technology fixes.

**Verification gate**

- Canary/property tests prove private IP, hostname, user-path, command/environment,
  custom literal, and optional process-name handling.
- Injection corpus proves JSON validity, HTML encoding, CSP, no active content, and
  zero network requests when opening reports.
- No language setting file/key is written before an explicit choice; English is the
  in-memory first-run default.
- All screenshots/report fixtures are synthetic and privacy-reviewed.

### Phase 7: repository, CI, release, and security review

**Deliverables**

- Final English README with optional short German introduction; architecture,
  platform, privacy/security, export, testing, release, contribution, disclosure,
  limitations, portable-use, CLI, roadmap, and verification documentation.
- Required community files/templates, Dependabot, CodeQL, dependency review, SBOM,
  checksums, artifact attestations, changelog, and four portable application assets.
- Authenticode/package-authenticity status documented per platform. Optional MSI/DEB/RPM/AppImage remain
  later roadmap items.

**Verification gate**

- Clean-room locked restore/build/test/publish for all RIDs from the tagged source.
- Two independent clean builds are compared; deterministic matches and unavoidable
  differences are recorded.
- Release assets contain only the expected single application plus embedded or
  adjacent local assets, licenses, notices, schemas, and documentation; no test data,
  user files, secrets, or debug logs.
- Every final application asset has a matching SHA-256 entry, per-artifact SBOM, and verifiable
  GitHub provenance attestation. Verification commands in the README are executed from
  downloaded assets.
- CodeQL, dependency/license, secret, architecture, unit, integration, UI smoke,
  localization, accessibility, and security test gates pass.
- Threat-model review has no unaccepted High finding; known limitations and authenticity
  gaps are in release notes and README.

## Required test matrix

| Requirement | Minimum automated evidence | Minimum live/manual evidence |
|---|---|---|
| IPv4 / IPv6 | Parser/classifier fixtures and dual-stack invariant tests | Loopback, specific, and wildcard listeners on each OS |
| TCP / UDP | Native fixture and normalized-model tests | TCP listener/active connection and UDP endpoint on each OS |
| Vanishing process / PID reuse | Deterministic race provider at every stage | High-churn child-process test |
| Missing permission / unknown owner | Typed-result and UI/export golden tests | Protected/other-user processes without elevation |
| Very large results | 50k performance, 100k cap, over-limit rejection | Soak on documented reference machine |
| Interface scopes | RFC range and interface-kind truth tables | Loopback, Ethernet/Wi-Fi, VPN/tunnel, removed address |
| Docker/Podman forwarding | cgroup/NAT/proxy fixtures | Rootless/rootful/host/bridge/published-port matrix where supported |
| No systemd | Provider `Unsupported` integration container/VM | At least one non-systemd environment |
| Linux distributions | Locked container/VM fixtures for parser/storage | Final documented supported distro/session matrix |
| Windows services | SCM shared/exclusive/omitted fixtures | Own-process and shared-host services |
| Firewall evidence | Windows/nft/firewalld rule matrices and unknown predicates | Profile/zone/default-policy/permission scenarios |
| Localization | Key parity, placeholder, accelerator, hard-coded-string tests | German/English content and expansion review |
| Report export | Full/Share-safe boundary, canary, injection, schema, and CSP tests | Content review and browser open test |
| Snapshot comparison | New/removed/changed/ambiguous symmetry/golden tests | Cross-capture and old-schema workflows |
| Cancellation | Every provider/pipeline/write/export stage | Ctrl+C, pause, close-window, slow-provider exercise |
| Accessibility | Automation name/role/focus tests | Narrator, NVDA, Orca, keyboard-only, scaling/high contrast |

Every manual matrix records OS/kernel/build, architecture, desktop session, effective
permissions, exact scenario, expected result, actual result, and evidence date. A
single developer screenshot is not a regression test.

## Release artifacts

| RID | 1.0 package | Required runtime evidence |
|---|---|---|
| `win-x64` | `OeXYZ-SocketScope-v<version>-win-x64.exe` | Windows UI/CLI/integration/smoke and one-file self-update tests on x64 |
| `win-arm64` | `OeXYZ-SocketScope-v<version>-win-arm64.exe` | Native ARM64 interop and UI smoke; otherwise clearly unvalidated |
| `linux-x64` | `OeXYZ-SocketScope-v<version>-linux-x64.tar.gz` | Supported glibc distro/session matrix on x64 |
| `linux-arm64` | `OeXYZ-SocketScope-v<version>-linux-arm64.tar.gz` | Native ARM64 or explicitly documented equivalent runtime test |

`SHA256SUMS`, SBOMs, changelog excerpt, source tag/commit, and verification instructions
are release assets. Attest the final executable/archive and its SBOM relationship after
packaging. Do not attest an intermediate directory and imply it covers a later asset.

## Open technical risks

| Risk | Likelihood / impact | Current mitigation and decision gate |
|---|---|---|
| Windows owner-module tables or creation timestamps require rights or behave differently across architectures | Medium / Medium | Spike owner-module and owner-PID fallbacks as ordinary user on x64/ARM64. UI always has First observed; creation time remains optional. |
| Windows ARM64 structure layout or third-party/Avalonia support differs from x64 | Medium / High | Source-generated interop, size/offset tests, native device run. Withhold ARM64 artifact if runtime evidence is absent. |
| Linux `/proc` ownership is incomplete under `hidepid`, other UIDs, hardened kernels, or short-lived processes | High / Medium | Per-field typed availability, cookie/inode/start identity, ordinary-user matrices. Never recommend root as the default fix. |
| `sock_diag` output/permissions vary by kernel and namespace | Medium / High | Minimum-kernel spike, strict parser/fuzzing, `/proc/net` fallback, namespace identity in generations. Narrow support rather than parse unknown data optimistically. |
| Container ownership and published-port mapping are not one-to-one, especially Docker Desktop/WSL and rootless forwarders | High / Medium | Separate owner vs forwarding evidence, cgroup/NAT best effort, no engine socket by default, `Unknown` as normal outcome. Document platform-specific limits. |
| Local firewall semantics cannot be fully simulated and unprivileged Linux rule reads often fail | High / High | Candidate-only vocabulary, unsupported-predicate retention, provider completeness, Windows/nft/firewalld matrices. A firewall panel may often be partial, never falsely green. |
| IPv6 wildcard/dual-stack behavior is easy to misstate | Medium / High | Family-specific observations, no implied dual stack, `IPV6_V6ONLY` spike where observable, exhaustive classifier tests and copy assertions. |
| Linux has no portable reliable socket creation timestamp | High / Low | First-observed is the guaranteed field; OS-reported creation is optional and provenance-labeled. |
| Avalonia open-source DataGrid may not meet 50k-row stability or assistive-technology needs | Medium / High | Phase 2 prototype before broad UI, virtualization benchmark, Narrator/NVDA/Orca test. Build a focused virtualized presenter or reduce supported live capacity if needed; do not silently add a commercial dependency. |
| Avalonia native Wayland remains experimental relative to X11 baseline | Medium / Medium | Support X11/XWayland for 1.0, test scaling/clipboard/AT-SPI, list native Wayland as experimental or unsupported based on evidence. |
| Secure atomic no-symlink writes are difficult across Windows filesystems and older Linux kernels | Medium / High | Dedicated `ISecureFileStore`, handle-relative platform code, adversarial race tests. Block unsafe destinations or portable mode instead of weakening silently. |
| Self-contained cross-platform applications may not be bit-for-bit reproducible due to SDK/runtime/package metadata | Medium / Medium | Normalize Linux archive order/times, pin inputs, compare clean builds, publish documented reproducibility level rather than claim perfect reproducibility. |
| Font/icon provenance or redistribution terms are incomplete | Low / High | Use Inter only after bundling OFL/source/hash; create original minimal icons or use a clearly licensed set; asset-to-notice CI gate. Omit uncertain assets. |
| Windows Authenticode and Linux package identities are intentionally absent for 1.0 | High / Medium | State `not-present` plainly; publish checksums/SBOM/attestations and verification steps; never describe them as a substitute for platform identity or reputation. |
| GitHub provenance verification normally requires tooling/network knowledge | Medium / Medium | Provide exact `gh attestation verify` and checksum commands, downloadable bundles/offline guidance, and a simple release verification script whose source is reviewable. |
| Optional update metadata creates the product's only unsolicited-risk network path | Medium / Medium | Default off, explicit Check now/opt-in, exact host, bounded response, test denied origins. Drop startup checking if consent or provenance UX is not clear enough. |
| Cross-platform service attribution is inherently asymmetric | High / Low | Show Windows service vs systemd unit terminology, shared/partial states, and unsupported init systems rather than inventing a common guarantee. |
| Test environments can accidentally commit real endpoint/user data | Medium / High | Synthetic fixtures, fixture provenance READMEs, canary/secret/privacy CI, review binary screenshots and test artifacts before upload. |

## Decision checkpoints before implementation expands

1. **After platform spikes:** freeze minimum Windows/Linux support and decide whether
   all four RIDs can honestly be labeled supported.
2. **After firewall spikes:** define exactly which normalized rule constructs are 1.0
   supported and which always make an assessment partial.
3. **After table/accessibility spike:** accept Avalonia DataGrid, build a small focused
   presenter, or revise the performance target with documented evidence.
4. **After secure-storage spike:** freeze minimum filesystem/kernel requirements and
   portable-mode failure/warning behavior.
5. **Before release workflow:** choose and audit SBOM tooling, pin all actions, define
   attestation subjects, and publish the actual Authenticode/package status.

Any checkpoint that reduces scope updates README, platform support, UI copy, tests, and
the threat model together. It must not be hidden in a changelog footnote.
