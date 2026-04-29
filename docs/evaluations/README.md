# Technology Evaluations

**Owner:** Core Team
**Scope:** Governance — Architecture Reviews
**Review Cadence:** As evaluations are completed or superseded
**Last Audited:** 2026-04-16

This directory contains the source evaluations, proposals, brainstorms, and comparative research that explain why Meridian's architecture and improvement decisions were made.

> **Governance Zone:** Evaluations belong to the governance zone alongside `audits/`. The distinction:
>
> - **`evaluations/`** — Technology and architecture evaluations, improvement brainstorms, capability proposals
> - **`audits/`** — Targeted code-quality audits, cleanup analyses, and hygiene assessments
>
> For a consolidated cross-folder view, start with [Evaluations & Audits Summary](../status/EVALUATIONS_AND_AUDITS.md).

Use this folder for deep context and implementation rationale. For live prioritization and execution status, prefer the normalized status documents in `docs/status/`.

---

## How To Use This Folder

1. Start with [Evaluations & Audits Summary](../status/EVALUATIONS_AND_AUDITS.md) for the repo-wide consolidation.
2. Use [Roadmap](../status/ROADMAP.md), [Improvements](../status/IMPROVEMENTS.md), and [Full Implementation TODO](../status/FULL_IMPLEMENTATION_TODO_2026_03_20.md) for active delivery sequencing.
3. Read the individual evaluation or proposal in this folder only when you need subsystem-specific rationale, design tradeoffs, or historical context.

---

## Canonical Architecture Evaluations

| Document | Current Role | Status / Notes |
| --- | --- | --- |
| [Realtime Streaming Architecture](realtime-streaming-architecture-evaluation.md) | Canonical streaming and resilience assessment | Re-evaluated 2026-03-11; current production-grade architecture review |
| [Storage Architecture](storage-architecture-evaluation.md) | Canonical storage and persistence assessment | Revised 2026-03-15; current reference for storage design |
| [Data Quality Monitoring](data-quality-monitoring-evaluation.md) | Canonical quality-monitoring assessment | Evaluation complete with 2026-03-19 implementation follow-up |
| [Historical Data Providers](historical-data-providers-evaluation.md) | Canonical provider comparison and fallback-chain guidance | Updated provider assessment for backfill operations |
| [Ingestion Orchestration](ingestion-orchestration-evaluation.md) | Canonical scheduler, checkpointing, and job-model evaluation | Updated 2026-03-11; P0/P1 recommendations marked implemented |
| [Operational Readiness](operational-readiness-evaluation.md) | Canonical deployment, observability, and release-governance evaluation | Recommendations substantially adopted; follow-up retained in-file |

## Desktop And Workflow References

| Document | Current Role | Status / Notes |
| --- | --- | --- |
| [Windows Desktop Provider Configurability](windows-desktop-provider-configurability-assessment.md) | Provider-settings assessment and phased implementation rationale | Last reviewed 2026-03-19; infrastructure complete, UI workflow still in progress |
| [Desktop Platform Improvements Guide](desktop-platform-improvements-implementation-guide.md) | Detailed implementation reference for the desktop improvement program | Original six priorities complete; extended follow-up retained in-file |
| [QuantScriptEnvironment Blueprint Brainstorm](quant-script-blueprint-brainstorm.md) | Product and UX evaluation for QuantScript design decisions | Updated 2026-04-16; companion to `docs/plans/quant-script-environment-blueprint.md` |

## Strategy, Proposal, And Market Context Documents

| Document | Current Role | Status / Notes |
| --- | --- | --- |
| [Competitive Analysis (Mar/Apr 2026)](competitive-analysis-2026-03.md) | Current market and competitor landscape analysis | Updated 2026-04-02; broadest competitive reference in this folder |
| [High-Value Low-Cost Improvements](high-value-low-cost-improvements-brainstorm.md) | Active high-ROI quick-win idea set | Updated 2026-03-15 with implementation progress |
| [Next Frontier Brainstorm](2026-03-brainstorm-next-frontier.md) | Living document for future capabilities beyond core platform hardening | Updated 2026-03-12 with implementation-status tracking |
| [Nautilus-Inspired Restructuring](nautilus-inspired-restructuring-proposal.md) | Structural proposal set for provider and repository organization | Last reviewed 2026-03-11; partially implemented |
| [Assembly-Level Performance Opportunities](assembly-performance-opportunities.md) | Hot-path performance and SIMD/intrinsics viability assessment | Updated 2026-03-17; roadmapped for later delivery |

---

## Archived Or Historical Companions

- [Desktop End-User Improvements](https://github.com/rodoHasArrived/Meridian-main/blob/main/archive/docs/assessments/desktop-end-user-improvements.md) — comprehensive desktop UX assessment retained as historical reference
- [Desktop Improvements Executive Summary](https://github.com/rodoHasArrived/Meridian-main/blob/main/archive/docs/summaries/desktop-improvements-executive-summary.md) — archived executive snapshot superseded by the implementation guide and current status docs
- [High-Impact Improvements (Mar 2026)](https://github.com/rodoHasArrived/Meridian-main/blob/main/archive/docs/assessments/high-impact-improvement-brainstorm-2026-03.md) — archived March 2026 brainstorm retained as historical rationale after promotion into normalized status docs
- [High-Impact Improvements Brainstorm](https://github.com/rodoHasArrived/Meridian-main/blob/main/archive/docs/assessments/high-impact-improvements-brainstorm.md) — older brainstorm superseded by the March 2026 refresh and normalized status docs

## Related

- [Evaluations & Audits Summary](../status/EVALUATIONS_AND_AUDITS.md) — consolidated overview across `docs/evaluations/`, `docs/audits/`, and related historical references
- [Code Audits](../audits/README.md) — Code quality audits (see audits/ for targeted hygiene analyses)
- [Roadmap](../status/ROADMAP.md) — current delivery sequencing informed by these evaluations
- [Improvements](../status/IMPROVEMENTS.md) — normalized improvement tracker
- [Full Implementation TODO](../status/FULL_IMPLEMENTATION_TODO_2026_03_20.md) — active execution backlog
