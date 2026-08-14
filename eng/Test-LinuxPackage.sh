#!/usr/bin/env bash

set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  printf 'Usage: %s <linux-x64 release tar.gz> [evidence-json]\n' "$0" >&2
  exit 64
fi

package="$(realpath -- "$1")"
name="$(basename -- "$package")"
if [[ ! -f "$package" || ! "$name" =~ ^OeXYZ-SocketScope-v([0-9]+\.[0-9]+\.[0-9]+)-linux-x64\.tar\.gz$ ]]; then
  printf 'The input is not a named Linux x64 SocketScope package: %s\n' "$package" >&2
  exit 64
fi

version="${BASH_REMATCH[1]}"
root_name="OeXYZ-SocketScope-v${version}-linux-x64"
repository="$(realpath -- "$(dirname -- "$0")/..")"
evidence=""
evidence_temporary=""
if [[ $# -eq 2 ]]; then
  evidence="$(realpath -m -- "$2")"
  case "$evidence" in
    "$repository"/*) ;;
    *)
      printf 'Evidence output must remain inside the repository.\n' >&2
      exit 64
      ;;
  esac
  evidence_parent="$(dirname -- "$evidence")"
  if [[ ! -d "$evidence_parent" || -L "$evidence_parent" || -L "$evidence" ]]; then
    printf 'Evidence output must use an existing, non-linked repository directory.\n' >&2
    exit 64
  fi
fi
temporary="$(mktemp -d /tmp/socketscope-release.XXXXXX)"
cleanup() {
  if [[ -n "$evidence_temporary" && -f "$evidence_temporary" ]]; then
    rm -f -- "$evidence_temporary"
  fi
  rm -rf -- "$temporary"
}
trap cleanup EXIT

tar -xzf "$package" -C "$temporary"
application="$temporary/$root_name/OeXYZ.SocketScope"
if [[ ! -x "$application" ]]; then
  printf 'The extracted application is missing or is not executable.\n' >&2
  exit 1
fi

# The WSL evidence host is deliberately minimal and has no ICU package. This switch
# affects only this disposable smoke; normal desktop distributions use their ICU.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

"$application" --version
"$application" --help | grep -Fq 'Running without a command'
"$application" cli --language en list --json --no-firewall > "$temporary/report.json"
grep -Fq '"schema": "urn:oexyz:socketscope:report:2"' "$temporary/report.json"
grep -Fq '"evidenceSource": "linux.sock-diag"' "$temporary/report.json"

"$application" cli snapshot save "$temporary/snapshot.json" --no-firewall
grep -Fq '"schema": "urn:oexyz:socketscope:snapshot:1"' "$temporary/snapshot.json"

sock_diag_rows="$(grep -Fc '"evidenceSource": "linux.sock-diag"' "$temporary/report.json")"
if [[ "$sock_diag_rows" -lt 1 ]]; then
  printf 'The final package returned no NETLINK_SOCK_DIAG rows.\n' >&2
  exit 1
fi

if [[ -n "$evidence" ]]; then
  package_sha256="$(sha256sum -- "$package" | cut -d ' ' -f 1)"
  finished_utc="$(date -u +'%Y-%m-%dT%H:%M:%SZ')"
  evidence_temporary="$(mktemp --tmpdir="$(dirname -- "$evidence")" .linux-x64-wsl-local.XXXXXX)"
  printf '{\n  "schemaVersion": 1,\n  "product": "OeXYZ SocketScope",\n  "productVersion": "%s",\n  "status": "pass",\n  "scope": "WSL 2 Ubuntu x64 final-package CLI/provider smoke",\n  "finishedUtc": "%s",\n  "package": "%s",\n  "packageSha256": "%s",\n  "host": {\n    "environment": "WSL 2 Ubuntu",\n    "architecture": "%s"\n  },\n  "assertions": {\n    "version": "pass",\n    "cliHelp": "pass",\n    "reportSchemaMarker": "urn:oexyz:socketscope:report:2",\n    "snapshotSchemaMarker": "urn:oexyz:socketscope:snapshot:1",\n    "provider": "linux.sock-diag",\n    "sockDiagRows": %s\n  },\n  "limitations": [\n    "WSL is provider/CLI evidence, not native Linux desktop validation.",\n    "The smoke uses invariant globalization because this minimal WSL image has no ICU package.",\n    "Native Linux ARM64 and desktop accessibility remain separate release-host gates."\n  ]\n}\n' \
    "$version" \
    "$finished_utc" \
    "$name" \
    "$package_sha256" \
    "$(uname -m)" \
    "$sock_diag_rows" > "$evidence_temporary"
  mv -f -- "$evidence_temporary" "$evidence"
  evidence_temporary=""
fi

printf 'LINUX_X64_PACKAGE_RUNTIME=PASS\n'
printf 'SOCK_DIAG_ROWS=%s\n' "$sock_diag_rows"
if [[ -n "$evidence" ]]; then
  printf 'EVIDENCE=%s\n' "$evidence"
fi
