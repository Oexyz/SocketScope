# Published data schemas

SocketScope writes plain, uncompressed JSON. Schema identifiers are stable and a
reader must reject a future major schema version it does not understand.

- `snapshot-v1.schema.json` describes local, potentially sensitive snapshots.
- `report-v2.schema.json` is the current Full/Share-safe report schema, including
  firewall evidence, reduction counters, and only the count—not the plaintext—of
  configured custom sensitive values. `report-v1.schema.json` remains an identification
  contract for the earlier pre-release shape.
- `settings-v2.schema.json` describes current local preferences, configurable
  Share-safe categories, and bounded custom sensitive literals. Version 1 is retained
  for migration. The `language` member is absent until the user explicitly chooses a
  language.

Runtime import additionally enforces a 64 MiB document limit, JSON depth 32, bounded
strings and collections, unique observation identifiers, valid endpoint ports, and no
duplicate object member names. Importing a snapshot never performs an operating-system
action or network request.
