# Evaluation Source-Material Index

**Status:** migration-source
**Owner:** core-team
**Reviewed:** 2026-05-30

This folder is retained as source material for historical evaluations, proposals, brainstorms, and comparative research. It is no longer the canonical product, engineering, or roadmap front door in the documentation rebuild.

Use current guidance first:

- [Documentation Front Door](../README.md)
- [Product Documentation](../product/README.md)
- [Engineering Documentation](../engineering/README.md)
- [Roadmap Registry](../roadmap/README.md)
- [Documentation Ownership Contract](../documentation-ownership.md)
- [Assessment Archive](../../archive/docs/assessments/README.md)

## Current Role In The Rebuild

- Treat these files as evidence and rationale to mine, not as final structure.
- Extract durable product direction into `docs/product/`.
- Extract durable architecture or implementation rules into `docs/engineering/` or `docs/reference/`.
- Keep durable roadmap truth in `docs/roadmap/data/*.yml` and generated roadmap views.
- Archive superseded evaluations under `archive/docs/assessments/` after replacement links exist.

## Source-Material Groups

### Architecture and platform evaluations

| Document | Target lane | Notes |
| --- | --- | --- |
| [Realtime Streaming Architecture](realtime-streaming-architecture-evaluation.md) | archive | Archived as March 2026 streaming architecture source material; current streaming contracts belong in engineering/reference/source evidence. |
| [Storage Architecture](storage-architecture-evaluation.md) | archive | Archived as March 2026 storage architecture source material; current persistence contracts belong in engineering/reference/source evidence. |
| [Data Quality Monitoring](data-quality-monitoring-evaluation.md) | archive | Archived as February 2026 quality-assessment source material; current quality claims require provider validation evidence and generated status artifacts. |
| [Historical Data Providers](historical-data-providers-evaluation.md) | archive | Archived as February 2026 provider-selection source material; current provider lookup belongs in reference matrices and provider validation evidence. |
| [Ingestion Orchestration](ingestion-orchestration-evaluation.md) | archive | Archived as February/March 2026 scheduler/backfill-control source material; current ingestion behavior belongs in engineering/operator docs and source evidence. |
| [Operational Readiness](operational-readiness-evaluation.md) | archive | Archived as February 2026 readiness source material; current operational proof belongs in operator SLO/runbook and validation evidence. |

### Desktop, workflow, and product evaluations

| Document | Target lane | Notes |
| --- | --- | --- |
| [Windows Desktop Provider Configurability](windows-desktop-provider-configurability-assessment.md) | archive | Archived as February/March 2026 provider-configurability source material; current guidance belongs in operator and engineering docs. |
| [Desktop Platform Improvements Guide](desktop-platform-improvements-implementation-guide.md) | archive | Archived as historical desktop improvement program material; current desktop development guidance belongs in engineering/operator docs and desktop source tests. |
| [QuantScriptEnvironment Blueprint Brainstorm](quant-script-blueprint-brainstorm.md) | product/engineering/archive | Extract current QuantScript direction if still active. |

### Strategy, proposal, and market context

| Document | Target lane | Notes |
| --- | --- | --- |
| [Competitive Analysis](competitive-analysis-2026-03.md) | archive | Archived as March/April 2026 market-context source material; current positioning belongs in product docs and roadmap registry. |
| [High-Value Low-Cost Improvements](high-value-low-cost-improvements-brainstorm.md) | archive | Archived as a February/March 2026 improvement brainstorm; current priorities belong in product, engineering, and roadmap docs. |
| [Next Frontier Brainstorm](2026-03-brainstorm-next-frontier.md) | archive | Archived as a March 2026 product/analytics brainstorm; current direction belongs in product and roadmap docs. |
| [Nautilus-Inspired Restructuring](nautilus-inspired-restructuring-proposal.md) | archive | Archived as March 2026 structural proposal source material; current module boundaries belong in engineering/source docs. |
| [Assembly-Level Performance Opportunities](assembly-performance-opportunities.md) | archive | Archived as March 2026 performance-opportunity source material; current performance work requires source and benchmark revalidation. |

### Archived evaluation redirects

| Document | Archive copy | Reason |
| --- | --- | --- |
| [Next Frontier Brainstorm](2026-03-brainstorm-next-frontier.md) | [archive/docs/assessments/2026-03-brainstorm-next-frontier.md](../../archive/docs/assessments/2026-03-brainstorm-next-frontier.md) | Historical March 2026 product/analytics brainstorm; current product direction should be revalidated through product docs and roadmap registry. |
| [Assembly-Level Performance Opportunities](assembly-performance-opportunities.md) | [archive/docs/assessments/assembly-performance-opportunities.md](../../archive/docs/assessments/assembly-performance-opportunities.md) | Historical performance-opportunity assessment; current performance work requires current source and benchmark evidence. |
| [Competitive Analysis](competitive-analysis-2026-03.md) | [archive/docs/assessments/competitive-analysis-2026-03.md](../../archive/docs/assessments/competitive-analysis-2026-03.md) | Historical March/April 2026 market-context snapshot; current positioning should be revalidated through product docs and roadmap registry. |
| [Data Quality Monitoring](data-quality-monitoring-evaluation.md) | [archive/docs/assessments/data-quality-monitoring-evaluation.md](../../archive/docs/assessments/data-quality-monitoring-evaluation.md) | Historical data-quality assessment; current quality claims require provider validation evidence and generated status artifacts. |
| [Desktop Platform Improvements Guide](desktop-platform-improvements-implementation-guide.md) | [archive/docs/assessments/desktop-platform-improvements-implementation-guide.md](../../archive/docs/assessments/desktop-platform-improvements-implementation-guide.md) | Historical desktop improvement program guide; current desktop development must use current engineering, operator, source, and test evidence. |
| [High-Value Low-Cost Improvements](high-value-low-cost-improvements-brainstorm.md) | [archive/docs/assessments/high-value-low-cost-improvements-brainstorm.md](../../archive/docs/assessments/high-value-low-cost-improvements-brainstorm.md) | Historical implementation-priority snapshot; current engineering priorities should not be inferred from old status counts. |
| [Historical Data Providers](historical-data-providers-evaluation.md) | [archive/docs/assessments/historical-data-providers-evaluation.md](../../archive/docs/assessments/historical-data-providers-evaluation.md) | Historical provider-selection assessment; current rate-limit, entitlement, and quality claims require provider docs or validation evidence. |
| [Ingestion Orchestration](ingestion-orchestration-evaluation.md) | [archive/docs/assessments/ingestion-orchestration-evaluation.md](../../archive/docs/assessments/ingestion-orchestration-evaluation.md) | Historical scheduler/backfill-control assessment; current orchestration claims require source and operator evidence. |
| [Nautilus-Inspired Restructuring](nautilus-inspired-restructuring-proposal.md) | [archive/docs/assessments/nautilus-inspired-restructuring-proposal.md](../../archive/docs/assessments/nautilus-inspired-restructuring-proposal.md) | Historical structural proposal; current module-boundary changes must use engineering and source-registry evidence. |
| [Operational Readiness](operational-readiness-evaluation.md) | [archive/docs/assessments/operational-readiness-evaluation.md](../../archive/docs/assessments/operational-readiness-evaluation.md) | Historical readiness assessment; current readiness claims require SLO, runbook, release-gate, and validation evidence. |
| [QuantScriptEnvironment Blueprint Brainstorm](quant-script-blueprint-brainstorm.md) | [archive/docs/assessments/quant-script-blueprint-brainstorm.md](../../archive/docs/assessments/quant-script-blueprint-brainstorm.md) | Historical critique/backlog after QuantScript baseline delivery; current implementation docs and code evidence win on conflicts. |
| [Realtime Streaming Architecture](realtime-streaming-architecture-evaluation.md) | [archive/docs/assessments/realtime-streaming-architecture-evaluation.md](../../archive/docs/assessments/realtime-streaming-architecture-evaluation.md) | Historical streaming architecture assessment; current streaming changes require source, operator, and validation evidence. |
| [Storage Architecture](storage-architecture-evaluation.md) | [archive/docs/assessments/storage-architecture-evaluation.md](../../archive/docs/assessments/storage-architecture-evaluation.md) | Historical storage architecture assessment; current persistence changes require source, reference, and validation evidence. |
| [Windows Desktop Provider Configurability](windows-desktop-provider-configurability-assessment.md) | [archive/docs/assessments/windows-desktop-provider-configurability-assessment.md](../../archive/docs/assessments/windows-desktop-provider-configurability-assessment.md) | Historical desktop provider-config assessment; current workflows should use typed configuration, shared credential resolution, and operator evidence. |

## Migration Rules

1. Do not call an evaluation canonical unless a rebuilt docs entrypoint still links to it as active guidance.
2. Extract verified-current facts into the owning canonical lane.
3. Keep historical rationale in `archive/docs/assessments/` when it explains past decisions.
4. Prefer redirect stubs for files linked from root, AI, roadmap, or command docs.
5. Update [Documentation Inventory](../documentation-inventory.md) and archive indexes in the same batch.
