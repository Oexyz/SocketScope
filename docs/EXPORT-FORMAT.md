# Export format

SocketScope exports versioned UTF-8 JSON reports and standalone inert HTML reports.
Both are generated from the same typed `ReportDocument`; the HTML exporter never
receives raw platform objects or executes templates supplied by the user.

## JSON report version 2

- Schema identifier: `urn:oexyz:socketscope:report:2`
- Schema version: `2`
- JSON Schema: [`schemas/report-v2.schema.json`](../schemas/report-v2.schema.json)
- Unknown top-level, observation, firewall-rule, and redaction properties are rejected.
- The maximum report observation count is 100,000.
- Enum values and property names are invariant machine values, independent of the UI
  language.

Version 2 adds the effective redaction options, per-category reduction counters,
custom-sensitive-value count and replacement count, operating-system creation time
when available, and the complete set of candidate firewall-rule details. Version 1
remains published only so existing files can be identified; the 1.0 application writes
version 2.

Each observation contains endpoint/protocol/state values, best-effort ownership,
binding evidence, candidate firewall status, first-observed time, optional
operating-system creation time, and the invariant
`externalReachability: "NotTested"`. Consumers must not reinterpret `Wildcard`,
`AllowRuleMayMatch`, or similar values as proof of Internet reachability.

## Explicit report profiles

The desktop provides a configured-default action plus separate Full and Share-safe
actions. The CLI creates a Full report by default; `--share-safe` must be supplied
explicitly for a reduced copy. The selected profile and effective redaction switches
are stored in the report.

### Full

Full reports preserve every available diagnostic value after bounded display-text
normalization. This includes IP addresses, observation identities, process IDs,
executable paths, process/service/container identifiers, and firewall-rule details.
Every redaction option is recorded as `false`. Full reports are intended for local
analysis or a deliberately controlled recipient.

### Share-safe

Share-safe always gives observation identities fresh per-report tokens. The remaining
allowlisted categories are configurable in Settings:

| Category | Default | Behavior when selected |
|---|---:|---|
| Private, ULA, and link-local addresses | Reduce | Replace with category-specific HMAC tokens |
| Public addresses | Reduce | Replace with `<PUBLIC-IP-…>` tokens |
| Home-path user names | Reduce | Replace the user segment with `<USER_HOME>` and keep the suffix |
| Process IDs | Reduce | Replace with `<PID-…>` tokens |
| Process names | Keep | Replace with `<PROCESS-…>` tokens |
| Service names | Keep | Replace with `<SERVICE-…>` tokens |
| Container identifiers/names | Reduce | Keep the runtime, replace the identifier with a token |
| Firewall rule IDs/names | Reduce | Replace both with `<FIREWALL-RULE-…>` tokens |
| User-defined literals | Empty | Replace matches in retained process/path/service/container and firewall/evidence text with `<SENSITIVE-…>` tokens |

Loopback and wildcard addresses are retained because they communicate the key binding
fact without naming another host. Port numbers, protocol, role, binding category,
availability states, timestamps, evidence sources, and unsupported firewall conditions
remain diagnostic. Command lines, environment variables, reverse-DNS host names, and
machine names are not collected by the MVP and therefore cannot appear in reports.

Tokens use HMAC-SHA-256 with a fresh random 32-byte salt for every report. Equal values
remain correlatable inside one report, while tokens intentionally change between
reports. The salt is zeroed after generation and is not stored.

The Reports view previews category counts in memory before export and names the
selected categories plus the number of custom literal matches. The preview does not
write a file. Up to 32 custom values of 3–128 characters (2,048 characters combined)
are matched case-insensitively as literal strings. They are stored only in the secure
local settings file; neither their plaintext nor a reversible digest is serialized in
the report. Only `customSensitiveValueCount`, `customSensitiveMatches`, and random
per-report tokens appear. No regular expression, script, XPath, JSONPath, or custom
template is executed.

Share-safe is still not a promise of anonymity: retained context or user-selected
categories can identify a person, host purpose, customer, or product. Review the
generated file before sharing.

## HTML report

HTML output:

- encodes every untrusted field with `HtmlEncoder.Default`;
- includes candidate firewall details through the same typed report model;
- has no JavaScript, image, font, frame, object, form, or external connection;
- embeds only a fixed local CSS block;
- declares a restrictive Content Security Policy with `default-src 'none'` and
  `connect-src 'none'`; and
- prominently states that external reachability was not tested.

Spreadsheet formula injection does not apply because version 1.0 has no CSV exporter.
Adding CSV later requires a new threat-model review and explicit formula-neutralization
tests.

## Snapshots and settings

Snapshot schema `urn:oexyz:socketscope:snapshot:1` preserves raw local evidence for
comparison and is not a support-safe export. Its schema is
[`schemas/snapshot-v1.schema.json`](../schemas/snapshot-v1.schema.json). Snapshot
files are limited to 64 MiB, bounded to 100,000 observations, validated before use,
and may contain sensitive endpoint and path data.

Current settings use `urn:oexyz:socketscope:settings:2` and
[`schemas/settings-v2.schema.json`](../schemas/settings-v2.schema.json). Version 1
settings are migrated in memory to version 2 and rewritten only through the secure
atomic settings store. No language property exists until the user explicitly selects
a language.

## Compatibility and validation

Readers should dispatch on both `schema` and `schemaVersion`, reject unsupported
major schemas, and retain the source file unchanged when validation fails. The release
smoke creates a Share-safe JSON report and local snapshot from the packaged application
and validates both with PowerShell `Test-Json` against the published schemas.
