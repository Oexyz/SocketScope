# OeXYZ SocketScope: Phase 1 decision record

> Status: completed on 2026-08-13. This is the historical research/design gate that
> preceded implementation; current implementation evidence is recorded in
> [PHASES-2-7.md](PHASES-2-7.md).

**Project:** OeXYZ SocketScope<br>
**Guiding line:** *Know what listens. Know what is exposed.*

## Decision

SocketScope was approved as a local, read-only Windows/Linux diagnostic tool with a
conservative evidence model:

1. literal local socket binding;
2. derived local interface context;
3. specificity or wildcard scope;
4. candidate local firewall evidence; and
5. external reachability, which version 1.0 never tests.

Those facts must not collapse into a single exposure verdict. A wildcard bind such as
`0.0.0.0` or `::` means all applicable local interfaces in that address family and
network namespace. It does not prove Internet reachability.

The approved baseline was current stable .NET with Avalonia, dependency-light immutable
Core contracts, documented Windows APIs, Linux `NETLINK_SOCK_DIAG` plus `/proc`,
explicitly partial enrichment, ordinary-user execution, no telemetry, and no runtime
web assets.

## Ten requested outputs

| # | Output | Record |
|---:|---|---|
| 1 | Current OeXYZ GitHub profile analysis | [Research and design DNA](RESEARCH-AND-DESIGN-DNA.md#1-current-oexyz-profile) |
| 2 | Design DNA of existing projects | [Research and design DNA](RESEARCH-AND-DESIGN-DNA.md#2-design-dna) |
| 3 | Competition and product boundary | [Research and design DNA](RESEARCH-AND-DESIGN-DNA.md#3-competition-and-boundary) |
| 4 | Concrete MVP specification | [MVP specification](MVP-SPECIFICATION.md) |
| 5 | Architecture proposal | [Architecture](ARCHITECTURE.md) |
| 6 | Threat model | [Threat model](THREAT-MODEL.md) |
| 7 | Planned repository structure | [Implementation plan](IMPLEMENTATION-PLAN.md#planned-repository-structure) |
| 8 | Principal-view wireframes | [MVP specification](MVP-SPECIFICATION.md#wireframes) |
| 9 | Verifiable milestones | [Implementation plan](IMPLEMENTATION-PLAN.md#milestones) |
| 10 | Open technical risks | [Implementation plan](IMPLEMENTATION-PLAN.md#open-technical-risks) |

## Research result

The review found recurring OeXYZ principles: dark restrained surfaces, direct language,
local/self-contained delivery, visible limitations, portable operation, checksums, and
small dependency sets. Project-specific Minecraft-console, payment-terminal, and
SIGNAL//LOST game aesthetics were rejected. SocketScope instead uses a table-first
diagnostic layout, subdued blue accent, status text in addition to color, local Inter,
and its own original endpoint/radar icon.

The profile, named repositories, releases/workflows, public site, competitors, and
primary platform documentation were recorded with source links and retrieval dates in
[RESEARCH-AND-DESIGN-DNA.md](RESEARCH-AND-DESIGN-DNA.md). No website or third-party
asset is loaded by the application.

## Gate outcome

Phase 1 was accepted before implementation began. The planning documents became the
acceptance contract; implementation was then authorized in phases 2–7. Claims about
working code must be taken from tests, package evidence, and the current status record,
not inferred from this historical decision.
