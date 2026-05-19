# Meridian Current Direction And Status

**Last Reviewed:** 2026-05-18
**Role:** Single planning entry point for current direction, project status, and document roles.

Use this file before reading individual roadmap or plan documents. It consolidates the active interpretation of Meridian's many planning files without replacing their detailed implementation notes.

## Snapshot

Meridian is being productized as an evidence-backed investment operations platform: a local-first system for proving what happened across trusted data, research, paper validation, portfolio review, ledger/accounting, reconciliation, approvals, and governed reports.

The current product path is browser-first for operator workflows:

- Active operator UI lane: `src/Meridian.Ui/dashboard/` and built workstation assets in `src/Meridian.Ui/wwwroot/workstation/`.
- Retained desktop lane: `src/Meridian.Wpf/` remains support for compatibility, shared-contract regression coverage, and retained desktop workflows.
- Shared UI/API support: `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` should continue to carry read models and workstation endpoints used by both browser and retained desktop surfaces.
- Visible operator navigation: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
- No mobile product lane is active. Do not add native mobile clients, mobile-first workflows, MAUI, React Native, Flutter, iOS, or Android product surfaces. Responsive browser validation is allowed for the web workstation only.

The release-level story remains the Meridian Assurance Loop:

```text
Data Trust Passport
-> Run Evidence Graph
-> Promotion Passport
-> Accounting-Grade Paper Trading
-> Governed Report Pack
```

## Current Status

Wave status labels and target dates are canonical in [`../status/PROGRAM_STATE.md`](../status/PROGRAM_STATE.md). The current interpretation is:

| Wave | Status | Current meaning |
| --- | --- | --- |
| W1 Provider confidence and checkpoint evidence | Done | Repo-closed trust gate. Keep DK1 packet, provider matrix, checkpoint, and evidence docs synchronized when provider evidence changes. |
| W2 Paper-trading cockpit | In Progress | Critical path. The readiness contract, replay evidence, operator inbox, and browser Trading surfaces are support evidence; operator-accepted cockpit reliability remains open. |
| W3 Shared run / portfolio / ledger continuity | In Progress | Critical path. Shared run, portfolio, ledger, brokerage-sync, evidence, and reconciliation seams exist; cross-workspace continuity still needs acceptance evidence. |
| W4 Governance and fund operations | In Progress | Critical path. Security Master baseline and reconciliation/report-pack support exist; durable casework, close/report workflows, approvals, and provenance remain open. |
| W5 Backtest Studio unification | Planned | Later wave. Do not pull forward ahead of W2-W4 unless the roadmap explicitly changes. |
| W6 Live integration readiness | Planned | Later wave. Keep read-only/paper-first defaults until trust, paper, reconciliation, and promotion gates are materially closed. |
| Optional advanced tracks | Optional | L3, performance, scale-out, advanced research, and similar work should not compete with the core operator-ready path. |

Current acceleration evidence: `PilotAcceptanceHarnessTests` is the canonical executable harness
for the golden path from trusted data through governed report pack. A passing run writes
`artifacts/pilot-acceptance/latest/pilot-readiness.json` and
`artifacts/pilot-acceptance/latest/pilot-readiness.md` with eight stage gates, blockers, evidence
IDs, ledger references, and validation text. `.github/workflows/golden-path-validation.yml` now
turns that harness into a repeatable acceptance lane and uploads the generated readiness dashboard
as `pilot-acceptance-evidence`. This advances support evidence for W2-W4; it does not by itself
close operator acceptance, live readiness, broad WPF scope, mobile, W5, or W6.

W2/W3/W4 claim discipline: every new roadmap or status claim for these waves must either map to a
`pilot-readiness.*` stage gate that turns green in the latest harness output or name the blocker in
that stage gate's `blockers` list. The current stage mapping is:

| Wave | Pilot-readiness stage gate(s) that can support a claim | If not green, record blocker there |
| --- | --- | --- |
| W2 paper-trading cockpit | `TrustedData`, `PaperPromotion`, `PaperSession` | Trading cockpit, promotion, replay, stale-session, or DK1 trust blockers belong on the affected stage. |
| W3 shared run / portfolio / ledger continuity | `TrustedData`, `ResearchRun`, `RunComparison`, `PaperPromotion`, `PortfolioLedgerReview`, `Reconciliation` | Run continuity, portfolio, ledger, brokerage/account, reconciliation, or evidence-packet gaps belong on the affected stage. |
| W4 governance and fund operations | `TrustedData`, `PortfolioLedgerReview`, `Reconciliation`, `GovernedReportPack` | Casework, approval/sign-off, provenance, report-pack lifecycle, or governed-output gaps belong on the affected stage. |

## Active Planning Set

These documents define current direction and should be read in this order:

| Document | Current role |
| --- | --- |
| [`../status/ROADMAP.md`](../status/ROADMAP.md) | Authoritative roadmap narrative, wave order, and conservative completion claims. |
| [`../status/PROGRAM_STATE.md`](../status/PROGRAM_STATE.md) | Canonical wave status table and target dates. |
| [`README.md`](README.md) | Active plan index and role classification for every plan file in `docs/plans/`. |
| [`evidence-backed-investment-operations-plan.md`](evidence-backed-investment-operations-plan.md) | Product-category filter and archive rule for the evidence-backed investment-operations direction. |
| [`meridian-6-week-roadmap.md`](meridian-6-week-roadmap.md) | Short-horizon execution slice for W2-W4 plus trust-gate maintenance. |
| [`waves-2-4-operator-readiness-addendum.md`](waves-2-4-operator-readiness-addendum.md) | Owner lanes, dependencies, and exit criteria for the active W2-W4 path. |
| [`web-ui-development-pivot.md`](web-ui-development-pivot.md) | Browser-first operator UI policy and retained WPF support boundary. |
| [`meridian-pilot-workflow.md`](meridian-pilot-workflow.md) | Golden-path productization filter: trusted data to governed report pack. |
| [`paper-trading-cockpit-reliability-sprint.md`](paper-trading-cockpit-reliability-sprint.md) | Wave 2 reliability contract for readiness, replay, controls, and promotion. |
| [`brokerage-portfolio-sync-blueprint.md`](brokerage-portfolio-sync-blueprint.md) | Brokerage and custodian account-sync design for Wave 3-4 continuity. |
| [`governance-fund-ops-blueprint.md`](governance-fund-ops-blueprint.md) | Governance, reconciliation, and report-pack target state for Wave 4. |
| [`ledger.md`](ledger.md) | Ledger execution roadmap for period management, persistence, accruals, multi-ledger support, and reporting. |
| [`fund-management-pr-sequenced-roadmap.md`](fund-management-pr-sequenced-roadmap.md), [`fund-management-module-implementation-backlog.md`](fund-management-module-implementation-backlog.md), [`fund-management-product-vision-and-capability-matrix.md`](fund-management-product-vision-and-capability-matrix.md) | Fund-management detail backlog. Use these after the core W2-W4 path, not as competing scope. |

## Deferred Or Reference Planning

These plans remain useful, but they are not current readiness drivers unless the roadmap explicitly pulls them forward:

- Backtest Studio and research acceleration plans are Wave 5 material.
- UFL target-state packages are active reference designs for asset coverage and Security Master depth, not readiness-closure documents.
- Options, portfolio-level backtesting, database, runbook registry, kernel parity, L3 inference, QuantScript multi-instance, and assembly-performance plans are technical or later-wave references.
- Commercial package names such as Meridian Evidence OS, Report Factory, Controls, PaperOps, and FundOps are positioning language until their shared contracts, browser workflows, evidence retention, and governed outputs are accepted.

## Consolidation Rules

- Start from this file, then use [`README.md`](README.md) to locate the detailed plan for the slice being changed.
- Do not treat support evidence as a wave exit. A route, DTO, fixture, WPF page, or endpoint proves support only when the corresponding acceptance evidence is present.
- Keep WPF work narrow unless it protects shared contracts, fixes regressions, or maintains retained desktop support.
- Prefer browser-workstation implementation for new operator-visible workflows.
- Archive completed, superseded, or historical plans under `archive/docs/` and link to them only as historical context.
- Keep active docs aligned to the seven visible workspaces. Legacy `Research`, `Data Operations`, and `Governance` names may appear as compatibility groupings, not as visible root navigation.

## Before Claiming Completion

Before a planning doc marks a slice as complete, verify that it has the evidence expected for its lane:

- W1/DK1: fresh provider packet, matrix alignment, packet-bound sign-off when evidence changes, and provider-validation docs updated.
- W2: readiness endpoint behavior, replay freshness, operator-inbox routing, promotion/checkpoint gates, and browser cockpit acceptance evidence.
- W3: shared run, portfolio, ledger, brokerage/account, reconciliation, and evidence-packet continuity across the seven-workspace path.
- W4: reconciliation casework, approval/sign-off, report-pack lifecycle, provenance, and governed output evidence.
- W5/W6: only after W2-W4 gates stop being speculative blockers.
