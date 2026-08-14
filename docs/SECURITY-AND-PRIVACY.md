# Security and privacy

SocketScope is an unprivileged, read-only diagnostic application. Its security model
is based on limiting authority, keeping observed data local, preserving uncertainty,
and making every network or export action visible to the user.

## What SocketScope reads

During a capture, SocketScope asks the local operating system for socket endpoints,
network interfaces, process metadata, service attribution, container/cgroup evidence,
and candidate local firewall rules. Access is limited to information available to the
signed-in user. An inaccessible or short-lived object is reported as unavailable or
partial; the desktop application must not be run permanently as administrator or root.

The MVP does not inspect packet payloads, browser history, files opened by processes,
command-line arguments, environment variables, DNS history, or credentials. It does
not open Docker or Podman control sockets.

## Network behavior

| Action | Network request | Default |
|---|---|---|
| Launch, refresh, filter, snapshot, compare, or export | None initiated by SocketScope | Always local |
| Display Inter or any other UI resource | None; resources are packaged locally | Always local |
| Manual **Check for updates** | HTTPS to the fixed public GitHub Releases API | User action only |
| Automatic update check | Same fixed GitHub API | Disabled until explicitly saved |
| Download with the integrated updater | Fixed GitHub API and allowlisted GitHub release-asset hosts | Explicit UI/CLI action only |
| External port test | Not implemented | Never |

The XAML namespace identifiers used by Avalonia are identifiers, not runtime asset
downloads. In particular, SocketScope does not use `fonts.googleapis.com` or
`fonts.gstatic.com`. The reviewed Inter font and its SIL Open Font License are compiled
into the application resource bundle. An architecture test locks their SHA-256 hashes
and rejects Google Fonts endpoints in production source/resources.

## Local storage

Default paths are:

| Data | Windows | Linux |
|---|---|---|
| Settings | `%LOCALAPPDATA%\OeXYZ\SocketScope\settings\settings.json` | `$XDG_CONFIG_HOME/oexyz/socketscope/settings.json`, or `~/.config/oexyz/socketscope/settings.json` |
| Snapshots | `%LOCALAPPDATA%\OeXYZ\SocketScope\snapshots` | `$XDG_STATE_HOME/oexyz/socketscope/snapshots`, or `~/.local/state/oexyz/socketscope/snapshots` |
| Minimal local event log | `%LOCALAPPDATA%\OeXYZ\SocketScope\logs\current.json` | `$XDG_STATE_HOME/oexyz/socketscope/logs/current.json`, or `~/.local/state/oexyz/socketscope/logs/current.json` |
| Temporary verified updates | `%LOCALAPPDATA%\OeXYZ\SocketScope\updates` | `$XDG_STATE_HOME/oexyz/socketscope/updates`, or `~/.local/state/oexyz/socketscope/updates` |
| Optional terminal command | `%LOCALAPPDATA%\OeXYZ\SocketScope\bin\socketscope.exe` | `~/.local/bin/socketscope` |

`--portable` places these directories under `SocketScopeData` beside the executable.
Portable mode should be used only in a private, writable directory.

Settings and snapshots are written through platform-specific atomic stores. The
stores create private directories/files, use same-directory temporary files, reject
unsafe link/reparse-point cases, validate path boundaries, and replace the target only
after a complete write. A safety check that cannot be enforced fails closed.

No language value is written before the user actively chooses German or English.
Snapshots contain the raw captured values visible in the application and should be
treated as sensitive local files.

## Terminal command boundary

`shell install` is an explicit local write, never a startup side effect. It is accepted
only from a published single-file application running without administrator or root
rights. The destination is fixed beneath the current user's profile. SocketScope
rejects linked/reparse path components and a conflicting `socketscope` file on the
process, user, or machine PATH, copies through a random same-directory file, checks
SHA-256 before and after replacement, hardens permissions, and attempts to restore the
previous file and PATH if the Windows PATH write fails. A rollback failure is surfaced
instead of being reported as success; if PATH restoration itself cannot be confirmed,
the verified executable is retained so the entry does not point at a missing file.

On Windows, only the current user's PATH is appended; the machine PATH is never
changed. The .NET environment API records the value in the current-user environment
and notifies Windows, but an already-open terminal retains its old environment. On
Linux, SocketScope creates `~/.local/bin/socketscope` with user-only mode and reports
when `~/.local/bin` is absent from PATH. It deliberately does not parse or edit shell
startup files. `shell status` is read-only. Shell aliases and functions are outside the
filesystem conflict check and remain a documented residual ambiguity.

The Settings **Terminal command** button invokes exactly the same typed installer. It
shows the fixed sanitized destination and current status before any action; opening
Settings or refreshing status is read-only. Installation still requires an explicit
button click, and no free-form text is executed as a command.

## Minimal local logging

The bounded local log contains only UTC timestamp, severity, sanitized category,
numeric/name event identity, and whether an exception existed. It deliberately drops
formatted messages, structured state values, exception text, stack traces, endpoints,
ports, process names, paths, command lines, and report/snapshot content. A canary test
passes a secret through both log state and an exception and verifies that it is absent
from the stored JSON.

This minimal log is not a promise that operating-system crash dumps or third-party
screen-recording tools cannot capture process memory. Do not attach dumps or raw
screenshots to public issues without reviewing them.

## Snapshots and reports

Snapshots are local diagnostic state and are not reduced. Full reports preserve the
available diagnostic values. Share-safe is a separate, explicit export action whose
reviewed field categories are configured in Settings. Observation IDs are always
tokenized. By default, non-loopback addresses, PIDs, user-name home-path segments,
container identifiers, and firewall-rule names are reduced. Process and service names
stay visible by default but can also be selected. Command-line and environment data
are never collected. Users may configure up to 32 literal sensitive values. Their
plaintext is held only in the protected local settings store and in process memory;
Share-safe reports expose only the configured count, match count, and random per-report
tokens. The matcher is bounded and cannot execute regex, templates, or scripts.

Tokens are consistent only inside one report and intentionally change between reports.
Port numbers and any retained process/service context can still be sensitive.
Share-safe is therefore not a guarantee of anonymity. The in-memory preview lists the
active categories and affected-value counts before export; review the resulting file
before sharing. See
[EXPORT-FORMAT.md](EXPORT-FORMAT.md).

HTML output is UTF-8 encoded, escapes all untrusted values, contains no JavaScript or
remote resource, and sets a restrictive Content Security Policy. JSON output uses a
closed, versioned schema. Neither format is opened or uploaded automatically.

## Update boundary

The update checker is the only product network path. It targets the fixed repository
`Oexyz/SocketScope`, accepts strict stable versions, and treats a missing or
mutable release, unexpected asset name, origin, size, redirect, or SHA-256 digest as a
verification failure. Update checks are disabled until the user acts or saves an
opt-in. An available update is not installed merely because it was found: the desktop
first shows the version, origin, size, and full SHA-256 and requires an explicit
installation click.

After confirmation, a short-lived helper from the already verified new application
runs with the same ordinary-user rights. It rechecks the staged asset and both
replacement files, waits for the original PID/start identity to exit, preserves the
current executable as a rollback copy, performs a bounded version/resource health
check, restores the previous executable byte-for-byte on failure, and restarts only on
success. It refuses to run elevated and does not install a service or persistent
background process. Linux extracts only the expected application entry from the
verified bounded archive; link and traversal entries are rejected.

The GitHub-provided asset digest protects integrity relative to received release
metadata, not the trustworthiness of source code. Release artifacts additionally use
checksums, SBOMs, and GitHub provenance attestations, but the local updater does not yet
validate an attestation. Windows Authenticode status for version 1.0 is **not present**.

## Security conclusions

SocketScope separates five different facts:

1. the local address a socket is bound to;
2. the local interface context derived from that address;
3. whether the binding is specific or wildcard;
4. candidate local firewall evidence; and
5. external reachability, which is **not tested** in version 1.0.

A wildcard binding or an allow-rule candidate is never displayed as proof that the
port is reachable from the Internet. Read the detailed [threat model](THREAT-MODEL.md)
and use [SECURITY.md](../SECURITY.md) for responsible disclosure.

## Removing local data

Close SocketScope first. The user may then remove the settings, snapshot, and log
directories listed above, or the complete `SocketScopeData` directory in portable
mode. This does not uninstall the application and cannot revoke copies of reports that
were already shared.

For a terminal installation, first remove the SocketScope bin directory from the
Windows user PATH through the normal Environment Variables settings (Linux startup
files were never edited), close all SocketScope processes, and then delete the fixed
terminal-command file shown above. An in-app uninstall command is not claimed for 1.0.
