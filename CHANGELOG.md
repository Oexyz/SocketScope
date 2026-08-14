# Changelog

All notable changes are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow Semantic
Versioning.

## [Unreleased]

### Added

- Cross-platform Core inventory pipeline with immutable evidence models.
- Windows TCP/UDP, process, service, interface, and firewall-candidate providers.
- Linux `NETLINK_SOCK_DIAG` TCP/UDP inventory with a bounded `/proc/net` fallback,
  `/proc` process/inode correlation, cgroup/systemd attribution, interfaces, and
  optional nftables evidence.
- Desktop live view, overview, listeners, changes, reports, settings, German/English
  localization, and accessible keyboard navigation.
- CLI list/watch/snapshot/report commands.
- Secure atomic settings/snapshot/report storage with reparse/symlink protections.
- Explicit Full and configurable Share-safe JSON/HTML reports, firewall-candidate
  details, in-memory reduction preview, and published versioned JSON schemas.
- Bounded custom sensitive literals for desktop Share-safe exports, with local-only
  plaintext storage, non-regex matching, per-report tokens, and non-disclosure tests.
- English first-run and CLI defaults without persisting a language until the user
  changes it.
- Responsive desktop layouts for DPI-scaled, compact, standard, and wide windows,
  including compact navigation, reflowing controls, and scrollable large tables.
- Localized DataGrid headers that remain readable and support mouse-based column
  reordering and resizing in Overview, Connections, Listeners, and Changes.
- Localized `Ctrl+C` table copying, resettable column layouts, and selected-endpoint
  copy actions for endpoints, executable paths, full details, and configured
  Share-safe summaries.
- An offline `F1` help and diagnostics view with terminology, keyboard shortcuts,
  responsive glossary cards, and a copyable metadata-only provider/runtime summary.
- Opt-in GitHub release checks and an integrated immutable-release/SHA-256 updater with
  explicit evidence preview, confirmation, atomic replacement, health check, rollback,
  and restart.
- Self-contained single-file Windows application combining desktop, CLI, update
  verification, local Inter font, icon, and embedded legal notices.
- Optional ordinary-user `socketscope` terminal installation with a fixed private
  path, Windows user-PATH integration, Linux `~/.local/bin` support, conflict checks,
  atomic verified copying, and `shell status` diagnostics; the GUI remains available
  through `socketscope` or `socketscope gui`.
- A responsive Terminal command card in Settings with a sanitized fixed path, current
  installation/PATH status, centered actions, refresh, and explicit install or update
  action.
- Metadata-only structured local logs with bounded entries and no observed values.
- Core, platform, integration, updater, architecture, and headless UI tests.
- Automated 100/150/200/300 percent DPI changes, keyboard traversal, and WCAG palette
  contrast gates.

### Security

- External reachability remains explicitly untested.
- Update checks never imply installation. Applying an update requires explicit
  confirmation, ordinary-user execution, repeated hash checks, and a successful
  post-replacement health check; failures restore the prior executable.
- No telemetry, advertising, account, cloud sync, privileged helper, or runtime web
  assets.

### Changed

- Linux release archives use deterministic USTAR headers instead of generated PAX
  header names, allowing repeated builds of unchanged input to match byte-for-byte.
- Generated release-evidence JSON is kept beside release assets instead of being
  recursively embedded into Linux archives that the evidence itself describes.

## [1.0.0] - Unreleased

The first stable release will be cut only after all release gates in
`docs/IMPLEMENTATION-PLAN.md` and `docs/TESTING.md` have evidence. Until then, the
repository is pre-release software.

[Unreleased]: https://github.com/Oexyz/SocketScope/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Oexyz/SocketScope/releases/tag/v1.0.0
