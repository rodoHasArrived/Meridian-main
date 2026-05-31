# Product Documentation

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-05-31

This is the canonical stakeholder-facing entrypoint for Meridian product direction, capability posture, and roadmap interpretation.
It routes non-technical audiences to verified evidence and prevents duplicate claims that compete with roadmap/source registries.

## What a Stakeholder Should Read Here

- If you need the current product framing, start with:
  - [Meridian Design Document](meridian-design-document.md)
  - [Roadmap Registry](../roadmap/README.md)
  - [Roadmap Generated Summary](../roadmap/generated/ROADMAP_SUMMARY.md)
- If you need evidence-backed examples of current operations, check:
  - [generated roadmap outputs](../roadmap/generated/)
  - Current project source-of-truth references listed below.

## Product Positioning

Meridian is a self-hosted, evidence-backed investment operations platform for:

- financial operations professionals,
- registered investment advisors,
- family offices,
- and similar operational teams requiring auditable, governed workflows.

The operating question remains:

> Can Meridian prove, book, reconcile, approve, and report an investment decision?

## Canonical Product Truth Order

1. `docs/roadmap/data/*.yml`  
   (durable roadmap state, gates, and sequencing)
2. `docs/roadmap/generated/*.md`  
   (rendered roadmap evidence)
3. `docs/product/meridian-design-document.md`  
   (canonical design charter and domain framing)
4. `docs/product/README.md`  
   (stakeholder summary and interpretation)
5. Legacy status/plan files only as historical context, never as replacement source.

## Stakeholder Narrative

- Evidence-backed stance: Meridian is an operations-first platform centered on:
  - trusted data intake and provider validation,
  - reconciliation and exception workflows,
  - approval and promotion controls,
  - governed reporting and evidence retention.
- Operating model: configurable tenant-aware system, not separate apps per organization type.
- Shared operator root model remains: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`.

## Current Wave Posture

- W1–W4 are closed baselines in the registry and treated as preservation targets unless a later registry change says otherwise.
- W5 is active planning for Backtest Studio expansion.
- W6 remains the controlled live-readiness path.
- `Paper-first`, `read-only where uncertain`, and `governance-first` defaults remain active by policy.

For exact wave rows and acceptance language, use the roadmap registry, then follow links into generated outputs.

## Capability Model

- Domain coverage (design-led):
  - Data integration and ingestion
  - Treasury and payments
  - Portfolio and investment operations
  - Financial operations and reconciliation
  - Reference data and instrument obligations
  - Entities and relationships
  - Alternative and structured assets
  - Financing, capital structure, and planning

- Workstream map:
  - `Data → Reconcile → Investigate → Approve → Report`

## Evidence and Source-Material Boundaries

Use this matrix to avoid source-of-truth drift:

| Topic | Canonical home | Why |
| --- | --- | --- |
| Product design and assumptions | [Meridian Design Document (Draft v1.0)](meridian-design-document.md) | Core design source for stakeholder framing |
| Wave sequencing and acceptance | [Roadmap Registry](../roadmap/README.md) | Durable sequence and acceptance control |
| Current capability status | Generated roadmap artifacts + source registries | Verifiable and machine-checkable status posture |
| Detailed planning / historical analysis | `docs/plans/`, `docs/evaluations/`, `docs/status/` | Source material only; extract only active facts |

## Stakeholder Claim Rules

- `Closed`/`Done` only when the registry source and supporting evidence agree.
- `Planned` only when registry state is explicit and unambiguous.
- Do not describe technical or UI details as complete strategy if registry acceptance has not been updated.
- Any old comparison or analysis document is context, not product contract.

## High-Value Input Files for Stakeholder Questions

- [Meridian Design Document (Draft v1.0)](meridian-design-document.md)
- [Roadmap README](../roadmap/README.md)
- [Roadmap item list](../roadmap/README.md)
- [Generated roadmap summary](../roadmap/generated/ROADMAP_SUMMARY.md)
- `docs/roadmap/data/` (YAML truth tables)

Legacy links that remain for context but not primary truth:

- [Current Direction and Status](../plans/current-direction-and-status.md)
- [Evidence-Backed Investment Operations Plan](../plans/evidence-backed-investment-operations-plan.md)
- [Feature Inventory](../status/FEATURE_INVENTORY.md)
- [Project Roadmap](../status/ROADMAP.md)
- [Target End Product](../status/TARGET_END_PRODUCT.md)

## Legacy Source-Material Index

- If you need historical decision rationale, see:
  - [plans](../plans/README.md)
  - [status migration index](../status/README.md)
  - [archive `archive/docs/`](../../archive/docs/README.md)

## Product-Owner Validation

```powershell
python build/scripts/docs/validate-docs-structure.py --summary
python build/scripts/docs/validate-roadmap-registry.py --summary
python build/scripts/docs/render-roadmap-docs.py --summary
python build/scripts/docs/check-ai-inventory.py --summary
```
