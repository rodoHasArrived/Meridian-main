# Product Documentation

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-07-31

This is the canonical stakeholder-facing entrypoint for Meridian product direction, capability posture, and roadmap interpretation.
It routes non-technical audiences to verified evidence and prevents duplicate claims that compete with roadmap/source registries.

## What a Stakeholder Should Read Here

- If you need the current product framing, start with:
  - [Meridian Design Document (Version 1.0)](meridian-design-document.md) — ground-up rewrite of
    the design charter: the proven-numbers value proposition, activation-over-expansion and
    proven-slice delivery doctrines, truth discipline, current wave posture through the ranked W9
    slate, and all retained normative invariants (customer-neutral core model, ledger truth and
    treasury-ledger principles, fail-closed authority, governed-autonomy AI boundary, seven-root
    navigation, no mobile lane). The superseded 0.15–0.25 lineage is summarized in its Section 25
    and archived in full at `archive/docs/design/meridian-design-document-v0.25.md`
  - [Implementation and Readiness Tracker](implementation-todo-list.md) — current execution, evidence, and readiness follow-up
  - [Roadmap Registry](../roadmap/README.md)
  - [Roadmap Generated Summary](../roadmap/generated/ROADMAP_SUMMARY.md)
  - [2026-07 First-Order Improvement Slate](product-roadmap-priorities-2026-07.md) — ranked W9
    priority rationale (`W9-TRUTH-001` through `W9-INGEST-009`, decision `DEC-PRIORITY-SLATE-001`);
    live status stays in the roadmap registry
  - [2026-07 Depth Slate](w10-depth-slate-2026-07.md) — W10 rationale for deepening existing
    functionality (`W10-MARK-001` through `W10-CONSOL-001`, decision `DEC-DEPTH-SLATE-001`);
    live status stays in the roadmap registry
- Treat the following as dated working design inputs, not canonical status sources:
  - [High-Value Code Brainstorm (2026-07)](high-value-code-brainstorm-2026-07.md) — market-researched
    prioritization snapshot; use the roadmap registry, not this dated sequencing, for live status
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
  - [Excel Onboarding Workbook & Provider Connection UX Brainstorm (2026-07)](excel-onboarding-workbook-brainstorm-2026-07.md) —
    five grounded ideas for populating security-master, entity, ledger, and account data via a
    prepared Excel workbook (download → fill → upload → review → governed commit) and tying
    provider connection setup to the imported instrument universe
- If you need evidence-backed examples of current operations, check:
  - [generated roadmap outputs](../roadmap/generated/)
  - Current project source-of-truth references listed below.

## Current Project Snapshot

The registry snapshot dated 2026-07-31 records Evidence Vault productization, statement
reconciliation onboarding, and WPF parity as the active productization targets, with the ranked W9
first-order slate and the W10 depth slate accepted as planned priority order behind them. The
accepted W1-W5, FREX, FINOPS, connector-library, and bounded W7 milestones remain bounded completion
claims, not blanket production certification, and every W9 and W10 row is planned with
planned-evidence posture rather than a completion claim.

Production readiness is currently **blocked**. The release posture changes only when the
[Implementation and Readiness Tracker](implementation-todo-list.md), roadmap evidence, packaging,
operator preflight, and required GitHub Actions evidence agree on the same release commit.

## Canonical Product Design Charter

The stakeholder-facing baseline for product scope and posture is `docs/product/meridian-design-document.md`.
This file is the current authoritative source for:

- target customer model,
- primary operator lifecycle framing,
- and current capability posture.

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

As of 2026-07-06, active product UI work runs across two co-equal operator UI lanes: the browser
workstation (`src/Meridian.Ui/dashboard/`) and the reactivated WPF desktop workstation
(`src/Meridian.Wpf/`). Both consume the shared `Meridian.Ui.Services`, `Meridian.Ui.Shared`, and
`Meridian.Contracts` seams rather than forking product state. The WPF lane's immediate focus is
closing web-UI parity gaps for screens that shipped browser-first while it was deferred, tracked as
`W8-WPF-PARITY-001` and detailed in
[`docs/development/wpf-web-ui-alignment-plan.md`](../development/wpf-web-ui-alignment-plan.md).

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

- The [Meridian Design Document (Version 1.0)](meridian-design-document.md) is the active stakeholder-facing product framing source.
- It is treated as the canonical product design charter and should be updated as the first step before changing stakeholder capability narrative.
- Current roadmap-facing status claims must point to registry-backed outputs (`docs/roadmap/data/*.yml`, `docs/roadmap/generated/*`) and reference this design charter for scope.
- Design Baseline: `Meridian Design Document (Version 1.0)` is the canonical product thesis. It centers the proven-numbers value proposition and Verified Coverage north star, encodes activation-over-expansion and truth-discipline doctrines from the 2026-07 adversarial program review, and preserves the charter invariants (customer-neutral operational-finance core, treasury-ledger controls, shared explorer UX direction, bounded W7 live-readiness governance, the active WPF co-equal lane and web-UI parity policy, and the operational proof layer thesis).

## Stakeholder Narrative

- Evidence-backed stance: Meridian is an operations-first platform centered on:
  - trusted data intake and provider validation,
  - reconciliation and exception workflows,
  - approval and promotion controls,
  - governed reporting and evidence retention,
  - capital-account, fund-event, and treasury-ledger records.
- Product wedge: Meridian should make the operational proof chain the product, linking source evidence through validation, reconciliation, ledger impact, capital-account impact, close state, report line, delivery evidence, and audit history.
- Operator UX direction: Accounting, Portfolio, Data, and Reporting share completed Financial Record Explorer patterns, while Accounting/Reporting expose the completed W5X-FINOPS control-center boundary for close and reconciliation state, priority-ranked exception queues, report-package release safety, and proof drill-through. Evidence Vault, statement onboarding, and WPF workstation parity are the active productization lanes.
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

Roadmap acceptance is deliberately bounded. A completed roadmap row proves the named capability and
its linked evidence; it does not by itself certify every deployment profile, operational runbook, or
production release. Release and support claims require current preflight, packaging, deployment,
and required GitHub Actions evidence.

## Current Supportability Claims

- Current roadmap and source evidence centers the supported baseline on accounting configuration, manual journal entry workbenches, private-capital fund-event ledger records, capital-account subledgers, retained evidence, governed report outputs, report-pack delivery/readiness, report-writer grids, statement connectors, and scoped access metadata.
- `src/Meridian.Contracts/`, `src/Meridian.Ledger/`, `src/Meridian.Reporting/`, `src/Meridian.Ui.Services/`, and `src/Meridian.Ui.Shared/` carry the shared source seams for those workflows. The browser workstation and active WPF desktop workstation consume those seams as co-equal product lanes instead of forking product state.
- External GL and provider systems remain source evidence or integration context unless a route explicitly posts Meridian-owned records. Meridian-owned ledger entries, retained source evidence, approvals, documents, and report outputs are the operational record.
- Mobile remains closed. Responsive browser validation is allowed for the browser workstation, but there is no native mobile, MAUI, React Native, Flutter, or mobile-first workflow lane.

## Current Wave Posture

- W1-W5 are closed baselines in the registry and form the coherent accepted operational-record baseline unless a later registry change says otherwise.
- W5 is the v0.15 accounting records, operational evidence, and multi-asset coverage package.
- W5X-FREX-001 is complete. Ledger, Portfolio, Security & Instrument, and Report-Line Provenance explorers share contracts, saved views, proof state, evidence links, and audit routing across the accepted browser and WPF scope.
- W5X-FINOPS-001 is a completed evidence-backed productization milestone that turns Financial Operations into the shared Accounting/Reporting control center for current close/reconciliation state, exception queues, approval/workflow controls, close-readiness blockers, retained evidence, governed reopen posture, and direct-lending operating controls. The accepted boundary is the shared Operations Continuity and Fund Ledger read-model surface consumed by browser Operations Continuity; WPF surfaces this through Fund Ledger today, with remaining Operations Continuity parity tracked as `W8-WPF-PARITY-001`. Evidence Vault and statement onboarding proceed through their own active rows. The bounded Asset Accounting Event Spine completed under `W9-ASSET-010`; additional fund-event command-center specializations remain separate roadmap decisions.
- W5X-CONNECT-001 is complete. Declarative CSV/OFX profiles, IB Flex XML, OFX bank/investment, and Alpaca statement connectors normalize into the shared reconciliation workflow with preview, drift, confidence, and retained-source evidence.
- W5X-EVIDENCE-001 and W5X-STMT-ONBOARD-001 are in progress. The current acceptance path is browser-first statement reconciliation onboarding into retained Evidence Vault proof; broader document-portal and collaboration scope remains separate.
- W6-BTSTUDIO-001 remains planned rather than active.
- W7-LIVE-001 is complete as a bounded governance milestone. Broader live execution productization and live portfolio operations are not part of that completion claim.
- W8-WPF-PARITY-001 is in progress, closing browser-first screen gaps while preserving one shared contract/read-model seam.
- `W9-ASSET-010` is complete. It established the evidence-backed Acquisition, Capitalization, Valuation, Income, Corporate Action, Impairment, Depreciation/Amortization, and Disposal spine, preserves Expected/Projected/Drafted/Approved/Posted/Reconciled/Reported as distinct states, and joins acquisition or selected-lot disposal consequences to the immutable journal transaction, with focused contract, spine, storage, endpoint, shared-read-model, and readiness suites as evidence.
- The remaining proof-layer targets, including the broader Operational Evidence Graph, fund-event command-center specializations, Capital Account Workbench, and Private-Capital Close Cockpit, remain design priorities until roadmap rows and acceptance evidence move them into delivery status.
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
| Product design and assumptions | [Meridian Design Document (Version 1.0)](meridian-design-document.md) | Core design source for stakeholder framing |
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

- [Meridian Design Document (Version 1.0)](meridian-design-document.md)
- [Roadmap README](../roadmap/README.md)
- [Roadmap item register](../roadmap/generated/roadmap-register.md)
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
