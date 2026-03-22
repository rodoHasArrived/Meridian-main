# Technology Evaluations

**Owner:** Core Team
**Scope:** Governance — Architecture Reviews
**Review Cadence:** As evaluations are completed or superseded
**Last Audited:** 2026-03-19

This directory contains in-depth evaluations of architecture components, providers, and improvement proposals.

> **Governance Zone:** Evaluations belong to the governance zone alongside `audits/`. The distinction:
> - **`evaluations/`** — Technology and architecture evaluations, improvement brainstorms, capability proposals
> - **`audits/`** — Targeted code-quality audits, cleanup analyses, and hygiene assessments
>
> For a consolidated view of all reviews, see [Evaluations & Audits Summary](../status/EVALUATIONS_AND_AUDITS.md).

---

## Architecture Evaluations

| Document | Description |
|----------|-------------|
| [Storage Architecture](storage-architecture-evaluation.md) | Tiered storage design: JSONL + Parquet dual-format, WAL durability, tiered storage |
| [Realtime Streaming Architecture](realtime-streaming-architecture-evaluation.md) | Event pipeline, provider connectivity, and WebSocket resilience evaluation |
| [Data Quality Monitoring](data-quality-monitoring-evaluation.md) | 12+ quality services: gap detection, anomaly detection, SLA monitoring |
| [Ingestion Orchestration](ingestion-orchestration-evaluation.md) | Scheduler and backfill control: job state machine, policy-driven scheduling |
| [Operational Readiness](operational-readiness-evaluation.md) | Production readiness: SLOs, alerting, incident response |
| [Historical Data Providers](historical-data-providers-evaluation.md) | Provider comparison across 10 backfill sources with recommended fallback chain |

## Desktop Evaluations

| Document | Description |
|----------|-------------|
| [Windows Desktop Provider Configurability](windows-desktop-provider-configurability-assessment.md) | Provider configuration assessment and phased improvement roadmap |
| [Desktop Platform Improvements Guide](desktop-platform-improvements-implementation-guide.md) | Implementation guide: test infrastructure, fixture mode, DI modernization |
| [Desktop Improvements Executive Summary](desktop-improvements-executive-summary.md) | Impact analysis, phase status, quick reference, and success criteria |

**Archived:** [Desktop End-User Improvements](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/assessments/desktop-end-user-improvements.md) — Comprehensive desktop UX assessment retained for historical reference

## Improvement Proposals & Brainstorms

| Document | Description |
|----------|-------------|
| [High-Impact Improvements (Mar 2026)](high-impact-improvement-brainstorm-2026-03.md) | Critical defects and high-impact improvements: data integrity, stability, architecture (31 items with status) |
| [High-Value Low-Cost Improvements](high-value-low-cost-improvements-brainstorm.md) | Quick-win improvements across startup, observability, developer experience, and data integrity (47 items) |
| [Next Frontier Brainstorm](2026-03-brainstorm-next-frontier.md) | Future capabilities: correlation engine, ML anomaly detection, cloud sinks, governance (Mar 2026) |
| [Nautilus-Inspired Restructuring](nautilus-inspired-restructuring-proposal.md) | Structural improvements inspired by nautilus_trader: co-located configs, parsing layer, FSM lifecycle |
| [Assembly-Level Performance Opportunities](assembly-performance-opportunities.md) | SIMD/.NET hardware intrinsics optimization candidates for hot paths |

**Archived:** [High-Impact Improvements Brainstorm](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/assessments/high-impact-improvements-brainstorm.md) — Older two-part brainstorm retained for historical reference after promotion of the newer March 2026 high-impact set and status-tracker summaries

## Related

- [Evaluations & Audits Summary](../status/EVALUATIONS_AND_AUDITS.md) — Consolidated overview of all evaluations
- [Code Audits](../audits/README.md) — Code quality audits (see audits/ for targeted hygiene analyses)
- [Roadmap](../status/ROADMAP.md) — How evaluations feed into planning
