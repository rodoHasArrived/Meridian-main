# Workflow Diagrams

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-20

This folder groups committed operational and lifecycle workflow diagrams. It is a routing index for
diagram source and rendered assets; active operator procedures belong in `docs/operators/`, and
durable roadmap or source-module status belongs in the roadmap and source registries.

## Subfolders

| Folder | Purpose |
| --- | --- |
| `operations/` | Data flow, onboarding, backfill, execution, security-master, strategy, and fund-operations workflow diagrams. |

## Operations Diagrams

| Diagram | Source | Rendered assets | Purpose |
| --- | --- | --- | --- |
| Backfill workflow | `operations/backfill-workflow.dot` | `operations/backfill-workflow.svg`, `operations/backfill-workflow.png` | CLI, REST, and scheduled backfill through providers, rate limits, gap checks, WAL, and storage. |
| Data flow | `operations/data-flow.dot` | `operations/data-flow.svg`, `operations/data-flow.png` | Source ingestion, processing, storage, export, and optional downstream paths. |
| Event pipeline sequence | `operations/event-pipeline-sequence.dot` | `operations/event-pipeline-sequence.svg`, `operations/event-pipeline-sequence.png` | Provider events through domain processing, validation, pipeline fanout, storage, and observability. |
| Execution layer | `operations/execution-layer.dot` | `operations/execution-layer.svg`, `operations/execution-layer.png` | Paper-first order lifecycle, gateway behavior, and position tracking. |
| Fund ops and reconciliation | `operations/fund-ops-reconciliation.dot` | `operations/fund-ops-reconciliation.svg`, `operations/fund-ops-reconciliation.png` | Accounting workbench, reconciliation review, F# rule checks, and persisted evidence loop. |
| Onboarding flow | `operations/onboarding-flow.dot` | `operations/onboarding-flow.svg`, `operations/onboarding-flow.png` | First-run setup, provider detection, configuration, diagnostics, and dry-run checks. |
| Security master lifecycle | `operations/security-master-lifecycle.dot` | `operations/security-master-lifecycle.svg`, `operations/security-master-lifecycle.png` | Import, conflict triage, projection, cache, and workstation/query consumption path. |
| Strategy lifecycle | `operations/strategy-lifecycle.dot` | `operations/strategy-lifecycle.svg`, `operations/strategy-lifecycle.png` | Strategy registration, state transitions, paper promotion evidence, and live-readiness gates. |

## Maintenance

- Keep each DOT, SVG, and PNG triplet together unless a diagram is explicitly retired.
- Prefer source DOT edits plus deterministic rerendering over manual image edits.
- Route obsolete or superseded workflow material through `archive/docs/` with replacement links.
