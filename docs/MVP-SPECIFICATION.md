# SocketScope 1.0 MVP specification

> Status: proposed and testable product contract. Nothing in this document is an
> implemented feature yet.

## Product outcome

SocketScope 1.0 enables a local user to answer three questions without needing to
combine several platform-specific tools:

1. Which local process, service, or identifiable container owns this TCP connection,
   TCP listener, or UDP endpoint?
2. What exactly is the endpoint bound to, and what local firewall evidence may be
   relevant?
3. What changed, and should the exported evidence be complete or reduced for a
   support request?

Primary users are developers diagnosing an unexpected port, support engineers who
need a reproducible local report, and administrators who need a readable first look
before using lower-level tools. The application remains useful without an account,
network connection, elevated token, Docker socket, systemd, or supported firewall.

## Truth and terminology contract

Every displayed conclusion has a type and source. The UI must visually distinguish:

- **Observed:** returned directly by an operating-system source during this capture;
- **Derived:** calculated from observed addresses, interfaces, or rule properties;
- **Candidate:** a possible association or firewall match whose effective behavior
  was not proven;
- **Unavailable:** the provider is missing, unsupported, or denied; and
- **Not tested:** no measurement was attempted.

“Unknown” is a valid value, not an error to hide. Tooltips and the detail view expose
the source, capture time, completeness, and reason for unavailable data.

### Four separate exposure dimensions

| Dimension | Required values | Meaning |
|---|---|---|
| Direct bind scope | Loopback only; Specific local interface; Wildcard for address family; Unknown | Where the local socket is bound in the observed network namespace |
| Interface context | Loopback; LAN/private; Link-local; Global-addressed; Tunnel/VPN; Container/virtual; Other; Unknown | Classification of the matching local interface, not a reachability result |
| Local firewall evidence | Allow rule may match; Block rule may match; Conflicting candidates; No matching rule found; Not evaluated; Permission denied; Unsupported | Best-effort local policy evidence with profile/zone and match reasons |
| External reachability | **Not tested** | Version 1 performs no remote probe and makes no Internet-reachability claim |

Required explanatory copy for a wildcard listener is equivalent to:

> Bound to all local IPv4 interfaces. A local firewall rule may allow this traffic.
> Reachability from the LAN or Internet was not tested.

`0.0.0.0` and `::` are evaluated independently. An IPv6 wildcard does not prove an
IPv4 listener: `IPV6_V6ONLY`, platform defaults, and provider evidence matter. The UI
may say “IPv4 and IPv6” only when two corresponding observations exist or the provider
can prove dual-stack behavior. Loopback describes the direct socket binding; a
separate proxy, NAT, container forward, or tunnel may still change the end-to-end
path, so external reachability remains untested.

### Time semantics

The product exposes two distinct timestamps:

- **First observed:** when this SocketScope session first saw the stable observation;
- **OS-reported creation:** only when a platform API returns a documented and
  successfully parsed creation time.

The UI never labels first observation as the actual connection start. If the operating
system does not expose a reliable age, it says “Creation time unavailable.” Snapshot
files store both values and their provenance.

## Supported platform contract

The release targets self-contained `win-x64`, `win-arm64`, `linux-x64`, and
`linux-arm64` artifacts. Exact supported Windows editions, Linux distributions,
desktop sessions and kernel evidence are recorded per release; cross-build alone does
not establish support.
The initial Linux contract is glibc-based desktop distributions. Avalonia's X11
backend, including XWayland where applicable, is the release baseline; experimental
native Wayland support is not a 1.0 dependency.

The full GUI and CLI run as the signed-in user. Lack of permission reduces metadata
completeness and is shown per field. It never triggers an automatic elevation prompt.

## Inventory and enrichment requirements

### Socket observations

One inventory generation contains:

| Field | Contract |
|---|---|
| Observation ID | Stable for the lifetime of an observed socket when evidence permits; otherwise explicitly fallback-derived |
| Protocol | TCP or UDP |
| Address family | IPv4 or IPv6 |
| Role | TCP listener, active TCP connection, or UDP endpoint |
| Local endpoint | Numeric address, scope ID where relevant, and port |
| Remote endpoint | Numeric address and port for active TCP; not applicable for listeners and ordinary UDP endpoints |
| TCP state | Normalized state plus original platform value; not applicable for UDP |
| Process | PID, display name, executable path when accessible, process-start identity, and per-field availability reason |
| Service | Windows service or systemd unit candidates, with exclusive/shared/uncertain attribution |
| Container | Best-effort local container/cgroup/network-namespace identity and evidence; otherwise unknown/unavailable |
| Bind classification | Direct bind scope, interface context, and a human explanation |
| Firewall evidence | Zero or more candidate rules and a non-definitive assessment |
| Time | Capture interval, first observed, optional OS-reported creation time |
| Provenance | Provider and evidence type for each enrichment group |

Process command lines and environments are not required to answer the MVP questions
and are **not collected by default**. The executable path is shown only when available.
Observed names and paths are untrusted display data: control characters are visibly
escaped, bidi controls are identified, and display/export lengths are bounded without
changing the underlying evidence hash.

### Stable identity and races

Identity is strongest when the OS supplies a socket cookie or creation timestamp.
Fallback identity combines protocol, family, namespace, endpoints, PID, and process
start identity. PID alone is never sufficient because it can be reused.

Enrichment validates process start identity before associating metadata. A process
that exits during capture produces `Process disappeared during capture`; it does not
cause the row to disappear or inherit metadata from a reused PID. Snapshot comparison
supports `New`, `Removed`, `Changed`, `Unchanged`, and `Ambiguous` rather than forcing
uncertain matches.

### Service and container attribution

- Windows maps active services and PIDs through the Service Control Manager. Multiple
  services in a shared host process are shown as candidates, not an exclusive owner.
- Linux prefers read-only systemd D-Bus process-to-unit lookup and uses cgroup evidence
  as a bounded fallback. A missing systemd bus is an ordinary `Unsupported` result.
- Container attribution uses read-only process, cgroup, namespace, and local NAT/proxy
  evidence accessible to the user. SocketScope does not join other namespaces and
  does not open a root-equivalent Docker or Podman API socket by default.
- Docker Desktop/WSL, rootless port forwarders, shared namespaces, and NAT can prevent
  reliable attribution. Those cases remain visible as host forwarding evidence or
  `Unknown`; they are not guessed from process names.

## Desktop behavior

### Overview

The overview shows counts for active TCP connections, loopback-only listeners,
listeners bound to a LAN/private interface, wildcard listeners, and new entries since
the selected snapshot baseline. Counts are links that open an equivalent filter.

Each count includes an info label defining it. “LAN/private interface” means a local
interface classification, not confirmed reachability from another LAN device.
Capture warnings and incomplete providers appear above the counts and cannot be
suppressed by a healthy-looking aggregate number.

### Connections and listeners

The virtualized table supports:

- full-text search across process, service/container label, numeric endpoint, PID,
  and port without DNS lookups;
- filters for protocol, address family, role/state, bind scope, interface context,
  firewall evidence, and change state;
- stable multi-column sort with a visible priority order;
- keyboard row navigation, range selection, column resizing, and copy actions;
- configurable comfortable and compact density;
- a resizable detail pane that remains attached to the selected observation; and
- accessible names for status, sort direction, filter state, and live-update state.

Default refresh is every two seconds, configurable to 1, 2, 5, 10, or 30 seconds.
Captures never overlap; a slow provider coalesces the next scheduled refresh. Pause
stops scheduled capture, freezes the displayed generation, and records “Paused at.”
Resume performs a fresh capture and labels the intervening period as an observation
gap.

New rows receive a non-color `NEW` marker and a subtle highlight. Removed rows remain
as tombstones for five seconds, labeled `REMOVED`, unless a filter excludes them.
Neither marker alone changes the current sort order. Selection is reconciled by stable
ID, scroll position is retained, and a row update never steals keyboard focus.

The synthetic performance gate is 50,000 rows on a documented CI/reference machine:
no UI-thread task over 100 ms during a normal refresh, search/filter feedback within
200 ms at the 95th percentile, and no unbounded growth across a 30-minute refresh
run. A capture hard limit of 100,000 observations returns an incomplete result and a
prominent warning rather than allocating without limit. Final budgets are recorded
with the reference hardware before implementation is declared complete.

### Detail explanation

The selected row answers in this order:

1. **Observed endpoint:** protocol, family, local/remote address, state, timestamp.
2. **Owner:** process, PID/start identity, executable, service, container, and missing
   permission details.
3. **Binding:** literal bind, matching interface(s), derived scope, and derivation.
4. **Firewall:** candidate rules, active profile/zone evidence, why each may match, and
   reasons evaluation is incomplete.
5. **Reachability:** always “External reachability not tested in SocketScope 1.0.”
6. **Reports:** an explicit Full report or a reduced Share-safe support copy.

Observed process names, paths, hostnames, and arguments are rendered only as text.
They cannot become links, markup, commands, or tooltips containing hidden raw values.

### Changes and snapshots

Snapshots are local, schema-versioned documents containing the immutable observation
generation, evidence/provenance, capture warnings, application/schema version, and OS
family/version. They do not contain logs, environment variables,
or hidden UI state.

Users can save a named snapshot, choose any two compatible snapshots, and see new,
removed, changed, unchanged, and ambiguous observations. A binding, owner, executable,
service/container, or firewall-evidence change displays field-level before/after data.
Comparison rejects unsupported future schemas safely and reports partial older-schema
comparisons rather than silently dropping fields.

Files are written atomically with user-only permissions where the platform supports
them. Portable mode is explicit and warns when the destination cannot provide the
same permission guarantees.

## Full and Share-safe reports

The Reports view presents two explicit choices. Full preserves every available
diagnostic value and carries a warning to inspect the file before publishing it.
Share-safe creates a reduced support copy and explains both what changes and what can
remain identifying. The user can save a default profile, reviewed category switches,
and up to 32 custom sensitive literal values. Literals are bounded, case-insensitive,
non-regex matches stored only in the protected local settings file. Reports record the
configured count and match count, never the plaintext values.

### Share-safe profile

| Data | Default treatment |
|---|---|
| User directory in Windows/Linux paths | Replace with `<USER_HOME>` while retaining a useful trailing basename |
| Non-loopback IP addresses | Deterministic tokens within one export, such as `<PRIVATE-IP-1>` or `<PUBLIC-IP-1>`; retain address family and relationship, not the value |
| Wildcard and loopback literals | Preserve because they explain binding and are not host-specific |
| Hostnames | Not collected or resolved by the MVP |
| Process command-line arguments | Omit |
| Environment data | Omit; not collected by default |
| Process/executable name | Preserve for diagnostic attribution |
| Service/container identifiers | Preserve for diagnostic attribution |
| PID and volatile identifiers | Replace with per-report tokens |
| Up to 32 user-defined sensitive literals | Replace retained attribution/evidence-text matches with per-report `<SENSITIVE-…>` tokens; never serialize the configured plaintext |

Tokenization uses a cryptographically random in-memory export salt that is not written
into the report. Repeated values remain comparable inside one report but not across
reports. The report records reduction counts by category and never writes an
intermediate Full copy while producing Share-safe output.

The CLI does not accept sensitive literals as command-line arguments because process
command lines can be visible to other local tools. Custom literals currently apply to
desktop-generated Share-safe reports; the CLI uses the reviewed category defaults.

JSON is produced only by `System.Text.Json` against a published schema. The standalone
HTML report contains no scripts, remote URLs, active forms, or untrusted markup. All
values are HTML-encoded, generated links are allowlisted, an inline restrictive CSP
is included, and CSS is a trusted embedded template. CSV is not a 1.0 export format;
future CSV support must neutralize spreadsheet formulas.

## CLI contract

The CLI uses the same capture, classification, report-profile, snapshot, and export code as
the desktop application. The implemented commands are available from the same
single-file executable:

```text
OeXYZ.SocketScope list [filters] [--json] [--no-firewall]
OeXYZ.SocketScope watch [filters] [--interval 2000] [--iterations N]
OeXYZ.SocketScope snapshot save <file> [--no-firewall]
OeXYZ.SocketScope snapshot compare <left> <right>
OeXYZ.SocketScope report <file> [--format json|html] [--share-safe]
OeXYZ.SocketScope cli version
OeXYZ.SocketScope shell install
OeXYZ.SocketScope shell status
```

Filters support protocol, role, state, scope, search, sort, and descending order.
Reports are Full by default. `--share-safe` explicitly selects the reduced support
copy. Human output defaults to English; machine JSON is locale-independent and versioned.
`--language de|en`, redirected output, and cancellation via Ctrl+C are supported. Exit
codes distinguish success, partial capture, invalid input/schema, permission-limited
operation, cancellation, and internal failure. No inventory command modifies a
process, service, firewall, or container.

`shell install` is a separate, explicit local installation action. It creates the
stable ordinary-user command `%LOCALAPPDATA%\OeXYZ\SocketScope\bin\socketscope.exe` on
Windows or `~/.local/bin/socketscope` on Linux from the published single-file
application. With no arguments, `socketscope` opens the GUI; `socketscope gui` is the
explicit equivalent. Windows user-PATH changes require a new terminal. Linux shell
startup files are not modified.

## Language and settings

German and English ship together. On first start, SocketScope uses English in memory
and writes **no language preference**. Only an explicit user change to German or back
to English writes a language key.

Settings cover refresh interval, language, and update-check behavior. Update checks
default to off. `Check now` is an explicit network action
against a documented exact endpoint. A user may opt into a startup version check; that
consent is stored and reversible. Finding an update does not install it. The user sees
version, origin, size, and SHA-256 and explicitly confirms installation; a short-lived
ordinary-user helper then applies, health-checks, rolls back on failure, and restarts.

No setting, snapshot, log, or update check synchronizes to a cloud service.

## Accessibility acceptance criteria

- All functions are reachable with keyboard only; expected shortcuts are documented.
- Focus order follows visual order and remains stable during refresh.
- Standard controls expose names, roles, values, validation, sort, and selection via
  Windows UI Automation and Linux AT-SPI where Avalonia supports them.
- Narrator plus NVDA on Windows and Orca on the supported Linux matrix receive a
  manual release test; a framework claim is not accepted as test evidence.
- Text and meaningful icons meet WCAG 2.2 AA contrast; focus indication is at least
  3:1 against adjacent colors; status never relies on color alone.
- 100%, 150%, 200%, and 300% scaling, narrow-window reflow, high-contrast themes, and
  reduced motion are tested. Essential content does not clip at 200%.
- Live updates do not repeatedly announce the entire table. A polite, summarized
  status region reports only meaningful changes and can be muted.

## Wireframes

The wireframes show information hierarchy, not final artwork.

### Application shell and overview

```text
+----------------------------------------------------------------------------------+
| SocketScope       Local host | Updated 10:42:18 | [Paused?] [Refresh] [Help]    |
+------------------+---------------------------------------------------------------+
| Overview         | Capture warning / incomplete-provider banner                  |
| Connections      |                                                               |
| Listeners        | [214 Active TCP] [18 Loopback] [3 LAN-bound] [2 Wildcard]    |
| Changes       4  | [4 New since baseline]                                        |
| Reports          |                                                               |
| Settings         | What needs attention                                          |
|                  | 8080  TCP  all IPv4 interfaces  process: devserver            |
|                  | Local firewall: allow candidate | External: not tested        |
|                  |---------------------------------------------------------------|
|                  | Capture completeness and recent changes                       |
+------------------+---------------------------------------------------------------+
```

The rail is 208-232 logical px on a wide window. Below 1,100 px it collapses to icons
with accessible labels; below 980 logical pixels it becomes a horizontally scrollable
compact navigation row, preserving content width and keyboard order.
Summary cards are buttons with full textual labels, not decorative gauges.

### Connections and listeners

```text
+----------------------------------------------------------------------------------+
| Connections | Search...  Protocol v  State v  Scope v  More filters  [Clear]    |
| 1,248 rows   | Live / Paused                 Columns v   Density v               |
+----------------------------------------------------------------------------------+
| +/- | Proto | Local              | Remote             | State | Process    | PID |
| NEW | TCP 4 | 127.0.0.1:5432     | -                  | Listen| postgres   | 912 |
|     | TCP 6 | [::]:443            | -                  | Listen| webhost    | 433 |
|     | TCP 4 | 10.0.0.8:51822      | 203.0.113.x:443    | Estab.| browser    | ... |
| ... virtualized rows ...                                                    ^    |
+------------------------------------------------------+---------------------------+
| Selected row remains visible                        | Evidence details          |
|                                                      | Observed / Owner          |
|                                                      | Binding / Firewall        |
|                                                      | Reachability: Not tested  |
|                                                      | [Copy safe summary]       |
+------------------------------------------------------+---------------------------+
```

The detail pane docks right above 1,300 px, below the table at medium widths, and opens
as a full content page on narrow windows. Listeners uses the same component with a
listener-focused default column set and binding explanation; this avoids divergent
behavior between two tables.

### Changes

```text
+----------------------------------------------------------------------------------+
| Compare [Baseline 2026-08-12 18:00 v] with [Current capture v]  [Swap]          |
| [4 New] [2 Removed] [1 Changed] [1 Ambiguous]                                   |
+----------------------------------------------------------------------------------+
| Change  Endpoint         Before owner/scope       After owner/scope              |
| NEW     TCP4 *:8080      -                        devserver / wildcard            |
| CHANGED TCP6 ::1:3000    node / loopback          node / wildcard                |
+----------------------------------------------------------------------------------+
| Field evidence: bind changed ::1 -> :: | Firewall candidates changed 0 -> 1     |
+----------------------------------------------------------------------------------+
```

Ambiguous matches are a first-class filter and never folded into new/removed counts.

### Reports

```text
+----------------------------------------------------------------------------------+
| Full report                              | Share-safe report                     |
| All available diagnostic values          | Reduces common personal identifiers   |
| May contain IPs, PIDs and user paths      | Keeps process/service attribution     |
| [Full HTML] [Full JSON]                   | [Share-safe HTML] [Share-safe JSON]    |
+----------------------------------------------------------------------------------+
| External reachability: Not tested | Result and reduction counts                 |
+----------------------------------------------------------------------------------+
```

Both paths use the same typed `ReportDocument` builder with an explicit profile. The
HTML exporter receives no raw platform objects and encodes every untrusted value.

### Settings

Settings are a single readable form grouped into Language, Live view, and Updates.
Every setting shows its default and consequence. Updates show `Off` until the user
makes an explicit choice.

## Out of scope for 1.0

Process termination, service restart, firewall modification, blocking, external port
testing, remote hosts, SSH, container control, notifications, a persistent background
service, automatic security scoring, packet capture, automatic package installation,
and plugin execution are excluded. Interfaces may leave safe extension seams, but the
UI must not contain disabled teaser buttons or simulated results for these functions.

## MVP release acceptance

The MVP is complete only when all required fields degrade honestly on both platforms;
the four evidence dimensions cannot be conflated by UI or export; all listed tests in
the implementation plan pass; German and English have parity; snapshots and both
exports are schema-validated and injection-tested; the four RID artifacts have SBOMs,
checksums, and attestations; and the README states the exact Authenticode/package status and
known platform gaps. A feature with only fixture or placeholder data is not complete.
