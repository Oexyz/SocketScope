# Contributing to SocketScope

Thank you for helping improve SocketScope. Reliability, evidence quality, privacy, and
clear limitations take priority over feature count.

## Before opening an issue

- Search existing issues and the platform limitations.
- Use synthetic addresses/processes or a sanitized report.
- Never post credentials, private keys, access tokens, customer data, raw host reports,
  or screenshots containing unrelated endpoints.
- Report vulnerabilities privately according to [SECURITY.md](SECURITY.md).

## Development setup

Install the SDK pinned in `global.json`, then run:

```bash
dotnet restore OeXYZ.SocketScope.slnx --locked-mode
dotnet build OeXYZ.SocketScope.slnx --configuration Release --no-restore
dotnet test OeXYZ.SocketScope.slnx --configuration Release --no-restore
```

Use an ordinary user account. Tests must not require administrator/root privileges,
modify firewall rules, stop services, terminate processes, or contact remote hosts
unless a narrowly scoped test explicitly documents and isolates that behavior.

## Change expectations

- Keep Core independent from UI, platform interop, shell execution, and network access.
- Return typed `Unknown`, `Partial`, `PermissionDenied`, or `NotTested` outcomes rather
  than guessing.
- Bound all OS/file/network input and honor cancellation.
- Treat process names, paths, service/container names, imported JSON, and output paths
  as untrusted.
- Do not add telemetry, accounts, remote assets, plugins, or a background service.
- Do not add a dependency without a license, maintenance, security, size, and removal
  assessment plus an update to `THIRD_PARTY_NOTICES.md` when distributed.
- Keep English technical documentation complete; update German UI text at the same
  time as English.
- Add tests for both the intended result and relevant denial/race/malformed cases.

Platform behavior must not be hidden behind a false common denominator. A change to
native APIs, network destinations, write paths, update verification, export fields, or
privilege boundaries requires an architecture/threat-model update.

## Pull requests

Keep pull requests focused and explain:

1. the user-visible problem;
2. observed versus inferred behavior;
3. security/privacy impact;
4. Windows/Linux differences;
5. tests and manual checks performed; and
6. known limitations or follow-up work.

The pull-request template contains the release gates. CI must be green, warnings are
errors, lock files must match, and generated artifacts must not be committed unless
they are intentional licensed assets or documentation evidence.

By contributing, you agree that your contribution is provided under the repository's
MIT license and that you have the right to submit it.
