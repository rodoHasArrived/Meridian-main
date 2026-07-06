# Product Documentation

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-06-10

This is the canonical stakeholder-facing entrypoint for Meridian product direction, capability posture, and roadmap interpretation.
It routes non-technical audiences to verified evidence and prevents duplicate claims that compete with roadmap/source registries.

## What a Stakeholder Should Read Here

- If you need the current product framing, start with:
  - [Meridian Design Document (Version 0.24)](meridian-design-document.md) — design baseline extended with current implementation posture, the v0.15 accounting records package, the v0.16 private-capital operations and treasury-ledger addendum, the v0.17 shared Financial Record Explorer productization target, the W5X Financial Operations control target, and the v0.18 operational proof layer market-gap update, the v0.19 no-code provider integration manifest, the v0.20 customer-neutral operational-finance architecture clarification, the v0.21 LedgerGraph OS / Close, Data and Evidence Control Tower positioning addendum, the v0.23 bounded W7 live-readiness gate, and the v0.24 WPF deferral policy
  - [Meridian Implementation TODO List](implementation-todo-list.md) — single planning-tooling tracker for implemented design-document items and remaining TODOs
  - [Roadmap Registry](../roadmap/README.md)
  - [Roadmap Generated Summary](../roadmap/generated/ROADMAP_SUMMARY.md)
  - [High-Value Code Brainstorm (2026-07)](high-value-code-brainstorm-2026-07.md) — market-researched
    prioritization of the next highest-value implementable features, sequenced against the W6/W7
    roadmap lanes
  - [Data Provider & Accounting Code Brainstorm (2026-07)](data-provider-accounting-brainstorm-2026-07.md) —
    code-grounded improvement lanes for the provider and accounting subsystems with a dated status
    table tracking which lanes have since shipped
  - [Portfolio Cash Ladder Blueprint (2026-07)](portfolio-cash-ladder-blueprint-2026-07.md) —
    code-ready design for the wave-8 portfolio cash-flow forecasting and liquidity ladder engine,
    aggregating per-security projection runs into scenario-aware, per-currency cash ladders
  - [Browser Workstation UI Improvements Brainstorm (2026-07)](web-ui-improvements-brainstorm-2026-07.md) —
    nine grounded browser-workstation UX ideas with effort/impact triage, platform-bet analysis,
    and sequencing
  - [Browser Workstation UI Improvements Implementation Plan (2026-07)](web-ui-improvements-implementation-plan-2026-07.md) —
    phased, code-ready implementation plan for the nine brainstorm ideas with per-phase file
    anchors, contracts, test plans, and validation commands
- If you need evidence-backed examples of current operations, check:
  - [generated roadmap outputs](../roadmap/generated/)
  - Current project source-of-truth references listed below.

## Canonical Product Design Charter

The stakeholder-facing baseline for product scope and posture is `docs/product/meridian-design-document.md`.
This file is the current authoritative source for:

- target customer model,
- primary operator lifecycle framing,
- and initial capability posture.

Use this file as the first check before updating any product narrative, claims, or roadmap interpretation.

## Product Positioning

Meridian is a self-hosted, evidence-backed investment operations platform for:

- financial operations professionals,
- registered investment advisors,
- family offices,
- and similar operational teams requiring auditable, governed workflows.

The operating question remains:

> Can Meridian prove, book, reconcile, approve, and report an investment decision?

## Product Scope Guidance

Scope guidance is evidence-led. Use current source, the roadmap registry, and the design charter to determine whether work belongs to a prior baseline, named productization target, or later expansion lane. Prior baselines and named targets are roadmap/status evidence, not development ceilings.

As of 2026-07-05, active product UI work is browser-first. WPF product/UI work is deferred until
explicitly reactivated; existing WPF shell code, compatibility, tests, and release workflows remain
retained support surfaces.

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

## Design Charter Integration

- The [Meridian Design Document (Version 0.24)](meridian-design-document.md) is the active stakeholder-facing product framing source.
- It is treated as the canonical product design charter and should be updated as the first step before changing stakeholder capability narrative.
- Current roadmap-facing status claims must point to registry-backed outputs (`docs/roadmap/data/*.yml`, `docs/roadmap/generated/*`) and reference this design charter for scope.
- Design Baseline: `Meridian Design Document (Version 0.24)` is the canonical product thesis for this rebuild phase, extended from the imported design draft with current roadmap, source-module evidence, private-capital operating patterns, treasury-ledger controls, shared explorer UX direction, bounded W7 live-readiness governance, WPF deferral, and the operational proof layer thesis.

## Stakeholder Narrative

- Evidence-backed stance: Meridian is an operations-first platform centered on:
  - trusted data intake and provider validation,
  - reconciliation and exception workflows,
  - approval and promotion controls,
  - governed reporting and evidence retention,
  - capital-account, fund-event, and treasury-ledger records.
- Product wedge: Meridian should make the operational proof chain the product, linking source evidence through validation, reconciliation, ledger impact, capital-account impact, close state, report line, delivery evidence, and audit history.
- Operator UX direction: Accounting, Portfolio, Portfolio/Data, and Reporting should converge around shared Financial Record Explorers, while Accounting/Reporting should expose a W5X-FINOPS Financial Operations cockpit for today's close and reconciliation state, priority-ranked exception queues, report-package release safety, and drill-through into Ledger Explorer, Evidence Vault, Operational Event Command Spine, fund-event specializations, and Report-Line Provenance Explorer.
- Operating model: configurable tenant-aware system, not separate apps per organization type.
- Extensibility model: stable financial operations core objects stay consistent across tenants; workflows, rules, data mappings, reports, permissions, domain extensions, and tenant templates are governed configuration layers. See the [Core Extensibility Model](../architecture/core-extensibility-model.md).
- Shared operator root model remains: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`.

## Evidence-Backed Investment-Operations Lifecycle

Use this lifecycle when validating any new or changed stakeholder claim:

1. **Import**: data is acquired and preserved from the source.
2. **Validate**: quality and mapping rules are applied with replayable provenance.
3. **Reconcile**: breaks are surfaced, assigned, and resolved.
4. **Approve**: workflow and policy gates are satisfied with auditable records.
5. **Report**: evidence-bound outputs are published and retained.

A claim is only “complete” when it is backed by:

- one or more roadmap rows in `docs/roadmap/data/*.yml` (status and acceptance),
- the matching generated roadmap artifact in `docs/roadmap/generated/`,
- and at least one supporting operational proof surface in `docs/operators` or `docs/reference`.

Use this compact claim status model for stakeholder-facing updates:

- **Complete**: roadmap row + generated proof + operational evidence reference.
- **Supported/Experimental**: explicit mention in design charter and a supporting design/prototype artifact, but no active acceptance gate yet.
- **Blocked**: acceptance gating exists but upstream dependency, data quality, or policy constraint is not yet cleared.

## Current Supportability Claims

- Source review on 2026-06-09 confirms the active W1-W5 support surface is centered on accounting configuration, manual journal entry workbenches, private-capital fund-event ledger records, capital-account subledgers, retained evidence categories, governed report outputs, report-pack delivery/readiness, report-writer grids, and company-scoped access metadata.
- `src/Meridian.Contracts/`, `src/Meridian.Ledger/`, `src/Meridian.Reporting/`, `src/Meridian.Ui.Shared/`, and `src/Meridian.Ui/dashboard/` now carry the active shared source seams for those workflows; retained WPF compatibility should consume those seams instead of forking product state when maintenance is explicitly approved.
- External GL and provider systems remain source evidence or integration context unless a route explicitly posts Meridian-owned records. Meridian-owned ledger entries, retained source evidence, approvals, documents, and report outputs are the operational record.
- Mobile remains closed. Responsive browser validation is allowed for the browser workstation, but there is no native mobile, MAUI, React Native, Flutter, or mobile-first workflow lane.

## Current Wave Posture

- W1-W5 are closed baselines in the registry and treated as the coherent near-term operational record release unless a later registry change says otherwise.
- W5 is the v0.15 accounting records, operational evidence, and multi-asset coverage package.
- W5X-FREX-001 is a planned productization target that uses the closed W1-W5 evidence baseline to build shared Ledger Explorer, Portfolio Explorer, Security & Instrument Explorer, and Report-Line Provenance Explorer surfaces. It is not a claim that those explorer surfaces are already complete.
- W5X-FINOPS-001 is a completed evidence-backed productization milestone that turns Financial Operations into the shared Accounting/Reporting control center for current close/reconciliation state, exception queues, approval/workflow controls, close-readiness blockers, retained evidence, governed reopen posture, and direct-lending operating controls. The accepted boundary is the shared Operations Continuity and Fund Ledger read-model surface consumed by browser Operations Continuity; prior WPF dense workpaper proof remains historical support, and new WPF product/UI work is deferred. Broader proof-layer expansions such as Evidence Vault request lists, Operational Event Command Spine, and additional fund-event command-center specializations remain separate roadmap decisions.
- The remaining proof-layer targets, including Operational Evidence Graph, Operational Event Command Spine, fund-event command-center specializations, Capital Account Workbench, Private-Capital Close Cockpit, and Evidence Vault with Request Lists, are design priorities until roadmap rows and acceptance evidence move them into delivery status.
- W6 is the deferred Backtesting Studio evidence loop.
- W7 remains the controlled live-readiness path.
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
| Product design and assumptions | [Meridian Design Document (Version 0.24)](meridian-design-document.md) | Core design source for stakeholder framing |
| Extensibility engineering boundaries | [Core Extensibility Model](../architecture/core-extensibility-model.md) and `src/Meridian.Contracts/Extensibility/` | Stable-core and governed-configuration rules for implementation |
| Wave sequencing and acceptance | [Roadmap Registry](../roadmap/README.md) | Durable sequence and acceptance control |
| Current capability status | Generated roadmap artifacts + source registries | Verifiable and machine-checkable status posture |
| Detailed planning / historical analysis | `docs/plans/`, `archive/docs/assessments/`, `docs/status/` | Source material only; extract only active facts |

## Product-Critical Migration Index

Stakeholder-facing high-traffic legacy pages map here:

- `docs/plans/current-direction-and-status.md` → [archive copy](../../archive/docs/plans/current-direction-and-status.md)
- `docs/plans/evidence-backed-investment-operations-plan.md` → [archive copy](../../archive/docs/plans/evidence-backed-investment-operations-plan.md)
- `docs/status/ROADMAP.md`, `docs/status/FEATURE_INVENTORY.md`, `docs/status/TARGET_END_PRODUCT.md` → `roadmap/data/*.yml` + generated roadmap outputs

If a legacy page is still actively needed for non-stakeholder operations, keep a short replacement pointer in the same page and move it to `archive/docs/plans/` or `archive/docs/status/` once the replacement is stable.

## Stakeholder Claim Rules

- `Closed`/`Done` only when the registry source and supporting evidence agree.
- `Planned` only when registry state is explicit and unambiguous.
- Do not describe technical or UI details as complete strategy if registry acceptance has not been updated.
- Any old comparison or analysis document is context, not product contract.

## High-Value Input Files for Stakeholder Questions

- [Meridian Design Document (Version 0.21)](meridian-design-document.md)
- [Roadmap README](../roadmap/README.md)
- [Roadmap item list](../roadmap/README.md)
- [Generated roadmap summary](../roadmap/generated/ROADMAP_SUMMARY.md)
- `docs/roadmap/data/` (YAML truth tables)

Legacy links that remain for context but not primary truth:

- [Current Direction and Status (Archived)](../../archive/docs/plans/current-direction-and-status.md)
- [Evidence-Backed Investment Operations Plan (Archived)](../../archive/docs/plans/evidence-backed-investment-operations-plan.md)
- [Feature Inventory (Archived)](../../archive/docs/status/FEATURE_INVENTORY.md)
- [Project Roadmap (Registry Snapshot)](../roadmap/generated/ROADMAP_SUMMARY.md)
- [Target End Product (Archived)](../../archive/docs/status/TARGET_END_PRODUCT.md)

## Legacy Source-Material Index

- If you need historical decision rationale, see:
  - [plans (archive index)](../../archive/docs/plans/README.md)
  - [status migration index](../status/README.md)
  - [archive `archive/docs/`](../../archive/docs/README.md)

## Product-Owner Validation

```powershell
python build/scripts/docs/validate-docs-structure.py --summary
python build/scripts/docs/validate-roadmap-registry.py --summary
python build/scripts/docs/render-roadmap-docs.py --summary
python build/scripts/docs/check-ai-inventory.py --summary
```
