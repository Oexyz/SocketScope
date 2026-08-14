# Research, OeXYZ design DNA, and product boundary

> Research snapshot: 2026-08-13. This document records observable public
> evidence and the design conclusions drawn from it. It is not an endorsement of
> every upstream implementation choice.

## Evidence and limits

The profile, repositories, documentation, source structure, build files, tests,
workflows, release pages, screenshots, SVGs, and other committed assets were reviewed
at these revisions:

| Source | Reviewed revision or state |
|---|---|
| [OeXYZ profile](https://github.com/Oexyz) and [profile repository](https://github.com/Oexyz/Oexyz) | `a58c0efda78b7ecc3ba733c907b893aa1e5fe0bb` |
| [OeXYZ Minecraft Console Client](https://github.com/Oexyz/OeXYZ-Minecraft-Console-Client) | `7666f10f8fff36b38b67a9141bd0d5e092cb1f0a` |
| [zvt2sumup](https://github.com/Oexyz/zvt2sumup) | `1667782b8fe199b26724065c5c031ae5edd77cb4` |
| [signal-lost](https://github.com/Oexyz/signal-lost) | `9364994cdb296388266a8aff367c4022338be00e` |
| [oexyz.de](https://oexyz.de/) | Public response and local assets as served on 2026-08-13 |

GitHub listed five public repositories. The fifth,
[`Oexyz/MagiskOnWSA`](https://github.com/Oexyz/MagiskOnWSA), was marked disabled by
the GitHub API and its repository page and clone endpoint returned a GitHub staff
restriction. Its contents were therefore not available for inspection and it is not
used as design or technical evidence.

The three active product repositories are very young, with two to six commits in the
reviewed history and little public adoption evidence. Their consistency is useful as
maintainer intent, but it is not evidence that the patterns have been validated by a
large user base. Upstream test suites were inspected, not treated as SocketScope test
results.

## 1. Current OeXYZ profile

### Profile README and identity

The profile positions OeXYZ around infrastructure, networking, security,
observability, automation, and open source. Its central message is that systems should
remain understandable. The operating sequence is `observe -> understand -> simplify
-> automate -> document`, supported by five recurring preferences:

- secure defaults over hidden assumptions;
- root-cause analysis over temporary workarounds;
- infrastructure as code over undocumented changes;
- useful monitoring over noisy dashboards; and
- privacy-conscious software over background telemetry.

This is unusually compatible with SocketScope. The product should make uncertainty
visible, show the source of a conclusion, and document boundaries as prominently as
features. The profile also establishes a security contact and OpenPGP fingerprint,
which should be carried into the future project security policy after independent
verification against the website.

The profile currently says the website is “launching soon,” although the website was
online during this review. That small inconsistency is a useful maintenance lesson:
facts copied between surfaces need an owner or an automated freshness check.

The profile banner uses a near-black navy gradient (`#080A0E` to `#101A2B`), white and
blue type, a restrained grid, a blue-to-cyan-to-green accent, and a small amount of
soft glow. The composition is modern but quiet. The glow and large hero treatment are
appropriate to a profile banner, not to a dense diagnostic application.

### Public website

The website reinforces the same identity through plain, outcome-oriented German copy,
wide spacing, Inter typography, and a mostly monochrome surface system with blue and
green accents. Relevant observable properties were:

- Inter variable normal and italic fonts are served locally from `oexyz.de`;
- scripts, styles, images, and fonts are first-party paths rather than runtime
  third-party embeds;
- the published privacy page says there is no tracking, advertising, analytics,
  external font loading, or account requirement;
- GitHub metadata is described as server-side fetched and cached, so the browser does
  not contact GitHub until a user follows an external link;
- the site implements a skip link, visible keyboard focus, responsive breakpoints,
  and reduced-motion handling;
- its response included a restrictive Content Security Policy, HSTS, frame denial,
  a no-referrer policy, and a restrictive Permissions Policy; and
- an explicitly selected language is stored locally, while no tracking identifier is
  associated with it.

The website has light and dark palettes. Its dark foundation is approximately
`#09090B` for the page and `#111216` for surfaces, with `#F5F5F7` text,
`#9DA0A8` muted text, and `#087BEA` blue. Large 30 px radii, deep marketing shadows,
and hero sizes up to roughly 104 px are portfolio-specific. SocketScope should borrow
the calm contrast and whitespace, not the marketing scale.

The website is identity reference only. SocketScope must not hotlink its fonts, logo,
CSS, images, API responses, or other resources. Any reused asset must be copied into
the repository only after its license and provenance are documented.

### OeXYZ Minecraft Console Client

This is a small .NET 10 WinForms application split into protocol, updater, desktop,
and integration-client projects, with two dependency-light executable test projects.
The architecture uses asynchronous operations, cancellation, immutable queued UI
updates, bounded batches, a 5,000-line display cap, and deliberate preservation of
selection and scroll position. Those are directly relevant to a non-flickering live
socket table.

The README is English and unusually explicit about scope. It describes the app as a
self-contained Windows client, identifies runtime and build-time dependencies,
documents every expected network destination, distinguishes checksums and GitHub
attestations from Authenticode signing, and labels public-server tests and limitations
carefully. Its competitor table is framed as a scope comparison rather than a claim of
universal superiority.

Security and privacy documentation identifies local data, DPAPI boundaries, redaction
expectations, and responsible-use constraints. Network access is associated with a
user action, and background update polling is absent. The updater accepts only a
bounded archive from a fixed GitHub location, validates its checksum, and does not
execute the archive. The checksum and payload still share a trust domain, so this
mechanism is integrity evidence, not independent publisher authentication.

Builds pin .NET 10 through `global.json`, use locked dependencies, deterministic build
properties, warnings as errors, and SHA-pinned GitHub Actions. The CI and release
workflows build only `win-x64`. The reviewed release workflow creates a portable ZIP,
`SHA256SUMS`, and a GitHub artifact attestation. It does not yet generate a full SBOM
or run CodeQL. The latest observed release was `v1.0.3`, and the README clearly states
that the binaries are not Authenticode-signed.

The screenshots show a very dark, compact, English desktop UI: a fixed left rail,
top tabs, thin borders, blue selection, green success, and a large terminal surface.
The welcome view uses a restrained centered card. It is strong evidence for stable
selection and dense dark surfaces, but its game-console density, fixed Windows layout,
system fonts, and English-only UI should not be copied.

### zvt2sumup

This is the strongest source for trust-boundary design. The .NET 10 solution separates
Core, protocol, SumUp integration, infrastructure, Windows service, desktop UI, tools,
and tests. It uses asynchronous and cancellable operations, integer monetary values,
atomic local writes, explicit confirmation for consequential actions, and no blind
retry of non-idempotent payments.

The German README, architecture document, security and privacy document, testing
guide, support guide, and installation guide clearly distinguish defaults from
deployment choices. Loopback binding is the default; external binding is accompanied
by warnings. Credentials are protected with DPAPI and filesystem ACLs, with the limits
of machine-local protection documented. Support guidance calls out redaction and the
risk of screenshots.

The updater is more defensive than the Console Client updater. It constrains hosts,
repository, names, versions, archive size and contents; rejects reparse points; checks
PE files and versions; stages into a protected location; requires elevation only for
installation; and has rollback behavior. Its documentation still correctly notes
that a checksum from the same release location is not independent provenance.

The reviewed xUnit suite reports 42 deterministic tests and includes service/tool/GUI
smoke coverage, layout checks at 960 x 640, protocol behavior, update-host and archive
validation, staging, and rollback cases. Site-specific hardware tests remain manual
and are not presented as automated evidence.

CI uses locked restore, NuGet audit, deterministic builds, warnings as errors,
secret-file rejection, a deterministic icon check, tests, publish, and smoke checks.
Actions are SHA-pinned. The release is currently Windows-only and does not add SBOM,
CodeQL, or artifact attestation. The latest observed release was `v1.0.1`, with an
unsigned portable archive and checksums.

Its German screenshots use the same dark palette as the Console Client, a wide left
rail, tabbed content, thin outlined cards, uppercase monospace labels, and blue/green/
amber/red status text. Cards reflow from three to two to one column. The consequence-
aware language, responsive card layout, and explicit diagnostics are transferable;
payment-terminal phrasing, oversized numeric status blocks, and a fixed rail are not.

### signal-lost

`signal-lost` is a small pnpm monorepo with web, server, protocol, game-engine, and
scenario packages. Its architectural boundary is valuable: clients send intentions,
the server owns canonical state, and projections expose only information appropriate
to each role. Repository checks enforce legal/privacy boundaries, local assets, and
the absence of arbitrary HTML. Playwright tests check network-origin and browser
persistence behavior. CI is SHA-pinned, and its container build is digest-pinned and
non-root.

The README honestly labels the project as Phase 0 with no public server or release.
Privacy and security documents distinguish implemented behavior from planned work.
That discipline should be copied exactly in spirit.

Its visual language is intentionally unsuitable for SocketScope: `#070908` black,
acid lime, cyan, scanlines, grid overlays, radar/orbit motion, glow, condensed uppercase
and monospaced type, and a full science-fiction terminal composition. The requested
SocketScope direction explicitly excludes this aesthetic. Only its focus visibility,
reduced-motion handling, local-resource discipline, and explicit state boundaries
transfer. Its third-party notice also says further review is needed, so it is not a
template for a release-ready notice.

## 2. Design DNA

### Recurring principles to retain

**Visual system**

- Near-black blue/gray foundations with separation created by small luminance changes
  and thin borders rather than large shadows.
- Blue for primary focus and selection; green for success; amber for caution; red for
  failure; muted gray for unavailable or unknown information.
- High-contrast off-white body text and visibly quieter secondary text.
- A simple OeXYZ mark, compact controls, short labels, and clear information groups.
- Responsive layouts that preserve reading order instead of merely shrinking a fixed
  desktop composition.

**Language and interaction**

- Short, literal product lines and factual interface copy.
- Limitations beside the affected feature, not hidden in legal text.
- Explicit state, source, last-updated time, and failure reason.
- Confirmation proportional to consequence. For SocketScope 1.0, read-only behavior
  removes the need for most confirmations; Full report export remains consequential.
- No claim that checksums, local firewall rules, or a bind address prove more than
  they actually prove.

**Engineering and trust**

- Small components with named boundaries and dependency direction.
- Async and cancellable work, bounded queues/collections, atomic writes, and stable UI
  state under live updates.
- No telemetry, advertising, mandatory account, remotely loaded UI assets, or hidden
  background network traffic.
- Locked dependencies, deterministic build settings, warnings as errors, SHA-pinned
  Actions, tests tied to documented claims, and self-contained releases.
- Security, privacy, testing, architecture, responsible disclosure, and third-party
  notices treated as product documentation rather than repository decoration.

### Observed UI-token evidence

The two WinForms projects share an almost exact desktop palette: background
`#070A0F`, sidebar `#0B0F16`, surface `#101620`, raised surface `#171F2C`, tint
`#1D2736`, border `#283649`, ink `#F0F6FC`, muted `#8F9FB4`, blue `#087BEA`, bright
blue `#33A3FF`, green `#36D399`, amber `#FFBD2E`, and red `#FF5F56`. They use Segoe UI
for ordinary controls, Bahnschrift for the wordmark, and Consolas for terminal or
technical values. Controls are compact, borders are generally one pixel, and spacing
is based on small repeated increments rather than isolated values. This is the clearest
existing OeXYZ desktop family signal, but the system-font choice is not portable.

The profile banner is more expressive: `#080A0E` to `#101A2B`, `#F7F9FC` ink,
`#A8B4C5` muted ink, and a `#1688F8` -> `#46B7FF` -> `#43D7A0` accent. Its Inter/
Segoe UI/Arial stack and light grid support a brand image, not a component library.

SIGNAL//LOST intentionally diverges with `#070908` void, `#0D100E` panels,
`#E5E8DC` paper, `#899287` dim text, `#C7FF2E` acid green, `#FFB525` amber,
`#FF5B46` danger, and `#68D5D0` cyan. Combined with scanlines, radar motion, uppercase
condensed type, and glow, this confirms which visual choices are game-specific and
must stay out of SocketScope.

### Patterns not to transfer unchanged

| Existing pattern | Why it is project-specific | SocketScope decision |
|---|---|---|
| WinForms and Windows-only release jobs | Both desktop utilities target a Windows-only problem | Avalonia and explicit Windows/Linux provider boundaries |
| Fixed wide navigation rail | Works in the reviewed screenshots but consumes table width | Collapsible rail; compact mode below 1,100 logical px |
| System Segoe UI/Bahnschrift/Consolas | Convenient on Windows but inconsistent on Linux and conflicts with the local-font requirement | Repository-local Inter variable font plus its OFL license; use tabular numerals instead of adding a mono font unless testing proves one necessary |
| Website hero typography, 30 px radii, and deep shadows | Marketing-page scale, not diagnostic density | 4/8/12 px radii, restrained elevations, table-first layout |
| Console/game terminal density | Optimized for chat and game output | Adjustable comfortable/compact table density and persistent column headers |
| SIGNAL//LOST lime, scanlines, glow, and animation | Narrative game identity | Explicitly prohibited; none enters the product theme |
| Existing updater mechanisms | Useful integrity checks but incomplete independent authenticity | Reuse no implementation blindly: version 1 applies an update only after fixed-origin metadata checks, a GitHub-provided digest, explicit evidence preview/confirmation, ordinary-user replacement, health check, and rollback; provenance limitations remain documented |
| English-only or German-only UI | No shared localization framework exists yet | English and German from the first production UI |
| Current release coverage | Primarily `win-x64`; supply-chain controls vary by repository | Four required RIDs, SBOM, checksums, attestations, and explicit signing status |

### Proposed SocketScope visual foundation

These are new product tokens inspired by the shared OeXYZ family, not a copy of a
specific screen:

| Role | Proposed value | Intended use |
|---|---:|---|
| App background | `#080B10` | Window background |
| Navigation/panel | `#0D121A` | Persistent regions |
| Surface | `#121A25` | Cards, table, detail panes |
| Raised surface | `#172231` | Menus and selected details |
| Border | `#29384B` | One-pixel separators |
| Primary text | `#F2F6FA` | Body and headings |
| Muted text | `#A8B4C3` | Secondary evidence |
| OeXYZ blue | `#268BE3` | Focus, links, selection, primary controls |
| Success | `#55C98B` | Verified success only |
| Caution | `#E7B64E` | Partial or potentially exposed evidence |
| Error | `#EE746C` | Failed capture or definite blocking/error evidence |
| Unknown | `#AAB3BF` | Unknown, unavailable, or not tested |

On the proposed panel/surface backgrounds, all listed text and status colors have at
least a 4.9:1 contrast ratio. Primary filled blue buttons should use near-black text,
not white, to maintain normal-text contrast. Status is always paired with text and an
icon or shape; color alone never communicates meaning.

Use local Inter variable normal/italic files with the full SIL Open Font License in
the repository and notices. Typography should use a 14-16 px base range, 1.4-1.55 line
height, tabular numerals for ports/PIDs/times, and no all-uppercase paragraphs. The
spacing scale is 4, 8, 12, 16, 24, and 32 logical pixels. Controls target at least
40 logical pixels, with 44 preferred for primary touch targets. A 3 px visible focus
ring, reduced-motion behavior, 200% scaling checks, and keyboard parity are release
requirements.

### How SocketScope remains its own product

SocketScope belongs to OeXYZ through restrained dark surfaces, blue/green accents,
plain wording, local-first behavior, auditable boundaries, and unusually honest
limitations. It remains distinct through a network-topology vocabulary, a table-first
information architecture, a four-layer evidence model, a lens/scope-oriented product
mark to be designed later, and no game or payment metaphors. Its characteristic UI
element should be an **evidence explanation**: “what was observed,” “what was derived,”
“what could not be checked,” and “what would be required to verify more.”

## 3. Competition and boundary

This comparison uses the projects' published scope as of the research date. Feature
sets can change, and the table is not a claim that one tool is universally better.

| Tool | Published strength | Difference from SocketScope 1.0 |
|---|---|---|
| [Microsoft TCPView/Tcpvcon](https://learn.microsoft.com/en-us/sysinternals/downloads/tcpview) | Windows TCP/UDP endpoints, owning process and service, one-second refresh, change colors, connection closing, CLI/CSV companion | Excellent Windows live inventory; not a cross-platform evidence-led snapshot/report-profile workflow. SocketScope does not close connections in 1.0. |
| [NirSoft CurrPorts](https://www.nirsoft.net/utils/cports.html) | Portable Windows endpoint/process details, filters, change marking, multiple exports, close/kill actions | Broader Windows utility actions and export formats; SocketScope is open source, cross-platform, read-only, and offers explicit Full and Share-safe reports. |
| [`ss`](https://manpages.debian.org/testing/iproute2/ss.8.en.html) and [`lsof`](https://github.com/lsof-org/lsof) | Scriptable Linux socket/file inspection and process association | Essential expert tools, not a guided cross-platform evidence model or stable snapshot-comparison desktop experience. SocketScope may use neither as its sole socket source. |
| [OpenSnitch](https://github.com/evilsocket/opensnitch) | Linux interactive application firewall, outbound policy, nftables integration, multi-node/SIEM capabilities | A policy-enforcement product with a privileged daemon. SocketScope 1.0 observes and explains local evidence; it does not enforce policy. |
| [Portmaster](https://safing.io/) | Windows/Linux application firewall, live monitoring, per-app rules, DNS/tracker blocking | A broader firewall/privacy suite. SocketScope does not intercept, block, score, or reroute traffic. |
| [GlassWire](https://www.glasswire.com/features/) | Historical network graph, app/host activity, alerts, bandwidth and firewall functions | A monitoring/firewall suite with broader account/commercial features. SocketScope is local-only, no-account, diagnostics-focused, and explicit about report contents. |
| [Wireshark](https://www.wireshark.org/docs/wsug_html_chunked/ChapterIntroduction.html) | Deep packet capture and protocol analysis across platforms | SocketScope does not capture packet payloads. It answers ownership and local exposure questions with much less sensitive data. |

### Positioning

SocketScope's defensible niche is:

> A local, open-source Windows and Linux diagnostic tool that connects sockets to
> processes, services, and best-effort container identity; explains binding and local
> firewall evidence without claiming untested Internet reachability; preserves changes
> through snapshots; and creates privacy-reduced support reports.

Published competitor scopes overlap with individual parts of this description. The
combination, and especially the explicit separation of exposure evidence, is the
product distinction. This is a proposed positioning based on the reviewed public
documentation, not a proof that no other product offers similar capabilities.

### Explicit non-positioning

Version 1.0 is not a port scanner, packet analyzer, firewall, intrusion-detection
system, vulnerability scanner, bandwidth monitor, remote fleet manager, threat score,
external port checker, or guarantee of Internet reachability. It must not use copy
such as “safe,” “exposed to the Internet,” or “blocked” unless the exact supporting
observation and its limits are shown beside the statement.

## Primary external references

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [Avalonia accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility)
- [Windows `GetExtendedUdpTable`](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedudptable)
- [Windows `EnumServicesStatusExW`](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-enumservicesstatusexw)
- [Windows `INetFwPolicy2`](https://learn.microsoft.com/en-us/windows/win32/api/netfw/nn-netfw-inetfwpolicy2)
- [Linux `sock_diag(7)`](https://man7.org/linux/man-pages/man7/sock_diag.7.html)
- [Linux `/proc/net/tcp` documentation](https://docs.kernel.org/networking/proc_net_tcp.html)
- [Linux `/proc/<pid>/fd` documentation](https://man7.org/linux/man-pages/man5/proc_pid_fd.5.html)
- [systemd D-Bus API](https://www.freedesktop.org/software/systemd/man/latest/org.freedesktop.systemd1.html)
- [nftables manual](https://netfilter.org/projects/nftables/manpage.html)
- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
