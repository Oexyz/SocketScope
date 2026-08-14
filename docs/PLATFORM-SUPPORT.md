# Platform support

SocketScope 1.0 targets Windows and Linux on x64 and ARM64. A build target is not the
same as a runtime-validated platform; release notes must name the evidence actually
collected for each artifact.

## Release targets

| RID | Package | Intended status | Release evidence required |
|---|---|---|---|
| `win-x64` | Single `.exe` | Supported | Native Windows x64 provider, CLI, desktop, export, and package smoke tests |
| `win-arm64` | Single `.exe` | Build target | Native Windows ARM64 launch and IP Helper layout tests before “validated” is claimed |
| `linux-x64` | `tar.gz` | Supported after native desktop validation | Native Linux provider/CLI tests plus X11/XWayland desktop smoke |
| `linux-arm64` | `tar.gz` | Build target | Native ARM64 provider and desktop smoke before “validated” is claimed |

Cross-publishing proves that the SDK can produce an artifact; it does not prove native
interop, graphics, accessibility, kernel, or distribution behavior. An artifact whose
native gate was not run is labeled unvalidated in the corresponding release notes.

## Windows

- Intended OS baseline: supported Windows 10 and Windows 11 editions on x64; Windows
  11 on ARM64 after native validation.
- Socket source: documented `GetExtendedTcpTable` and `GetExtendedUdpTable` IP Helper
  APIs for IPv4/IPv6 TCP and UDP, including owner PID tables.
- Process metadata: ordinary-user process query APIs. Protected or exited processes
  remain unknown.
- Services: `OpenSCManagerW` and `EnumServicesStatusExW`; several services may share a
  PID and are retained as separate candidates.
- Firewall evidence: read-only `INetFwPolicy2` COM policy/rule view. It is candidate
  evidence, not a Windows Filtering Platform verdict.
- Storage: private per-user directories, reparse-point checks, atomic replacement, and
  explicit failure when the destination cannot be protected.
- Optional terminal command: a protected ordinary-user copy at
  `%LOCALAPPDATA%\OeXYZ\SocketScope\bin\socketscope.exe`; only the user PATH is appended,
  and a fresh terminal is required.

The GUI never requests elevation. Running as administrator can expose more metadata
but is not a supported permanent operating mode.

## Linux

- Runtime baseline: a current glibc distribution with a usable X11/XWayland desktop
  session for Avalonia. Exact distributions are recorded per release.
- Self-contained archives include the .NET runtime but rely on distribution-provided
  glibc, ICU, X11/XWayland client libraries, Fontconfig, and FreeType. A minimal image
  or WSL installation may not have these desktop dependencies.
- Socket source: bounded `AF_NETLINK` / `NETLINK_SOCK_DIAG` multipart dumps for TCP
  and UDP in IPv4 and IPv6. Every native length, sequence, termination/error message,
  family, row count, and deadline is checked. Strictly parsed `/proc/net/tcp`, `tcp6`,
  `udp`, and `udp6` tables are the explicit fallback. Both paths correlate accessible
  `/proc/<pid>/fd` socket inodes. `ss` and `lsof` are not runtime requirements.
- Process metadata: bounded reads from `/proc/<pid>` with process-start revalidation.
- Services: systemd unit attribution from accessible cgroup paths. No `systemctl` is
  spawned; non-systemd systems return an explicit unsupported state.
- Containers: best-effort cgroup evidence only. No Docker or Podman API/control socket
  is opened, and port-forward ownership can remain unknown.
- Firewall evidence: optional fixed-path `nft --json list ruleset` with no shell,
  bounded output, timeout, minimal environment, strict JSON validation, and partial
  semantics for unsupported expressions. Missing `nft` or permission denial is normal.
- Storage: `0700` directories, `0600` files, no-follow/openat-style checks, same-directory
  atomic replacement, and failure on unsafe links.
- Optional terminal command: a user-only executable at `~/.local/bin/socketscope`.
  SocketScope reports a missing PATH entry but never edits shell startup files.

The data directory must reside on a filesystem that can enforce Unix ownership and
permission modes. SocketScope deliberately refuses protected writes on filesystems
that cannot demonstrate those guarantees. In particular, a repository or portable
directory on a Windows-mounted WSL DrvFS volume can be rejected; this is not silently
downgraded. WSL can provide useful CLI/provider evidence but is not the release's
native Linux desktop validation environment.

SocketScope observes its current network namespace only. It does not elevate or enter
other namespaces. `/proc` options such as `hidepid`, containers, sandboxing, Yama, and
short process lifetimes can reduce attribution without preventing socket enumeration.

## Desktop sessions and accessibility

The minimum window is 760 by 520 logical pixels. The sidebar, summary cards, filters,
report choices, settings editors, terminal actions, and detail grids reflow at their
implemented responsive thresholds. Keyboard navigation, visible focus, named controls,
and 100/150/200/300 percent scaling are release gates. Automated Avalonia headless
tests complement but do not replace manual Narrator/NVDA testing on Windows and
AT-SPI/Orca testing on Linux.

Native Wayland behavior depends on the Avalonia version and distribution stack. The
1.0 release should use an X11/XWayland baseline unless the release evidence explicitly
records successful native Wayland testing.

## Not supported in 1.0

macOS, BSD, Android, remote hosts, SSH collection, external port probes, packet
capture, firewall mutation, service/process control, container control, a privileged
background helper, and MSI/DEB/RPM/AppImage installation are outside the 1.0 scope.
