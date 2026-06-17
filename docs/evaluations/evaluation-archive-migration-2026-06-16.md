# Evaluation Archive Migration — 2026-06-16

**Status:** complete
**Owner:** core-team
**Classification lane:** `archive-doc` / `dated-doc-snapshot`

This record documents the final cleanup of historical evaluation redirect stubs from `docs/evaluations/`. Full historical copies already live in `archive/docs/assessments/`; this pass removes the active-folder stubs and leaves `docs/evaluations/README.md` as the canonical routing surface.

## Archived Documents

| Former active path | Archive path | Classification | Current authority |
| --- | --- | --- | --- |
| `docs/evaluations/2026-03-brainstorm-next-frontier.md` | `archive/docs/assessments/2026-03-brainstorm-next-frontier.md` | Historical product/analytics brainstorm | Product docs and roadmap registry |
| `docs/evaluations/assembly-performance-opportunities.md` | `archive/docs/assessments/assembly-performance-opportunities.md` | Historical performance-opportunity assessment | Engineering docs, source evidence, and current benchmarks |
| `docs/evaluations/competitive-analysis-2026-03.md` | `archive/docs/assessments/competitive-analysis-2026-03.md` | Historical market-context snapshot | Product docs and roadmap registry |
| `docs/evaluations/data-quality-monitoring-evaluation.md` | `archive/docs/assessments/data-quality-monitoring-evaluation.md` | Historical data-quality assessment | Provider validation evidence and generated status artifacts |
| `docs/evaluations/desktop-platform-improvements-implementation-guide.md` | `archive/docs/assessments/desktop-platform-improvements-implementation-guide.md` | Historical desktop improvement program guide | Engineering, operator, source, and desktop test evidence |
| `docs/evaluations/high-value-low-cost-improvements-brainstorm.md` | `archive/docs/assessments/high-value-low-cost-improvements-brainstorm.md` | Historical improvement-priority brainstorm | Product docs, engineering docs, and roadmap registry |
| `docs/evaluations/historical-data-providers-evaluation.md` | `archive/docs/assessments/historical-data-providers-evaluation.md` | Historical backfill-provider assessment | Reference docs, provider matrices, and validation evidence |
| `docs/evaluations/ingestion-orchestration-evaluation.md` | `archive/docs/assessments/ingestion-orchestration-evaluation.md` | Historical scheduler/backfill-control assessment | Engineering docs, operator docs, and source evidence |
| `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | `archive/docs/assessments/nautilus-inspired-restructuring-proposal.md` | Historical structural proposal | Engineering docs and source registry evidence |
| `docs/evaluations/operational-readiness-evaluation.md` | `archive/docs/assessments/operational-readiness-evaluation.md` | Historical readiness assessment | Operator SLOs, runbooks, release gates, and validation evidence |
| `docs/evaluations/quant-script-blueprint-brainstorm.md` | `archive/docs/assessments/quant-script-blueprint-brainstorm.md` | Historical QuantScript critique/backlog | Product docs, engineering docs, and current QuantScript implementation evidence |
| `docs/evaluations/realtime-streaming-architecture-evaluation.md` | `archive/docs/assessments/realtime-streaming-architecture-evaluation.md` | Historical streaming architecture assessment | Engineering docs, operator docs, and source/readiness evidence |
| `docs/evaluations/storage-architecture-evaluation.md` | `archive/docs/assessments/storage-architecture-evaluation.md` | Historical storage architecture assessment | Engineering docs, reference docs, and storage validation evidence |
| `docs/evaluations/windows-desktop-provider-configurability-assessment.md` | `archive/docs/assessments/windows-desktop-provider-configurability-assessment.md` | Historical desktop provider-config assessment | Operator docs, engineering docs, and provider configuration contracts |

## Reference Evidence

The archive trace for this pass found active references that were either migration inventory rows, self-references from the old evaluations README, generated/status snapshots, or legacy development-doc links to the desktop guide. The active links were updated to point at current guidance or the assessment archive before the redirect stubs were removed.

## Follow-Up Rule

Do not recreate per-document redirect stubs in this folder. If a reader needs historical rationale, link directly to `archive/docs/assessments/`; if they need current behavior, link to the owning canonical docs lane.
