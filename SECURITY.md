# Security policy

Security and privacy are release criteria for SocketScope, not optional features.

## Supported versions

| Version | Security updates |
|---|---|
| Latest stable 1.x release | Yes |
| Older stable releases | Best effort until superseded in the release notes |
| `main`, local builds, and prereleases | Not production-supported |

No stable release exists until it appears in this repository's GitHub Releases page.

## Reporting a vulnerability

Send a private report to `security@oexyz.de`. Confidential reports may be encrypted
with the OeXYZ Security OpenPGP public key published by the OeXYZ GitHub profile and
website. Verify the primary fingerprint through an independent OeXYZ channel:

```text
160C 83EF ABF2 97F8 EDF8 F6B5 34D7 4FDC 82EF FA7A
```

Please include only the minimum evidence needed to reproduce the issue:

- affected SocketScope version, operating system, and architecture;
- the security boundary or expected behavior that failed;
- deterministic reproduction steps using synthetic data where possible;
- impact and whether exploitation requires the same user, elevation, or network access;
- a small sanitized proof of concept; and
- a safe contact method for follow-up.

Do not send credentials, tokens, private keys, customer data, raw socket tables,
unsanitized reports, full home-directory paths, or screenshots containing unrelated
processes and endpoints. Do not open a public issue containing an active exploit.

You should receive an acknowledgement when the report has been read. Investigation and
fix timing depends on reproducibility, severity, affected platforms, and maintainer
availability; this policy does not promise a fixed remediation deadline. Coordinated
disclosure is preferred.

## In-scope examples

- code execution, privilege escalation, DLL/library hijacking, or unsafe process start;
- update origin/integrity bypass, downgrade, unsafe redirect, or staging-path escape;
- symlink/reparse/hardlink attacks against settings, snapshots, logs, or reports;
- HTML/JSON injection that becomes active content;
- report redaction failures or sensitive values written to privacy-safe logs;
- parser memory corruption or practical denial of service with bounded input; and
- a UI/export conclusion that incorrectly claims verified external reachability.

Expected platform access denial, best-effort attribution, incomplete firewall evidence,
and the documented lack of external reachability testing are normally limitations, not
vulnerabilities, unless they are misrepresented or create another security impact.

## Release integrity

Official releases publish SHA-256 checksums, SPDX SBOMs, and GitHub artifact
attestations. The repository's release workflow is the expected provenance source.
Windows code has Authenticode status **not present**, and that limitation must remain
visible in release notes.
See the verification commands in [README.md](README.md).

The complete design analysis is maintained in
[docs/THREAT-MODEL.md](docs/THREAT-MODEL.md) and
[docs/SECURITY-AND-PRIVACY.md](docs/SECURITY-AND-PRIVACY.md).
