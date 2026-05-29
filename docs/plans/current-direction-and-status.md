# Meridian Current Direction And Status

**Last Reviewed:** 2026-05-29

## Current Planning TODOs

- [ ] Keep desktop acceptance matrix rows as the delivery unit for W2/W3/W4 desktop slices: lane, scenario, shared-contract check, and focused test evidence.
- [ ] Prioritize high-traffic page-body workflow outcomes (run -> portfolio -> ledger -> reconciliation and casework/sign-off/reporting) over additional shell framing work.
- [ ] Keep support evidence and exit claims separate in roadmap/status updates; do not mark exits from support-only proofs.
- [ ] Keep browser and desktop parity checks coupled to shared endpoint/read-model payloads for every desktop workflow change.
- [ ] Preserve W2/W3 as closed evidence baselines while keeping W4 governance as the active release gate; do not widen live-readiness language until W4 close/report criteria are green.
- [ ] Re-run structured roadmap/source stale-doc and hash checks after docs or source ownership changes.

**Role:** Single planning entry point for current direction, project status, and document roles.

Use this file before reading individual roadmap or plan documents. It consolidates the active interpretation of Meridian's many planning files without replacing their detailed implementation notes.

## Snapshot

Meridian is being productized as an evidence-backed investment operations platform: a local-first system for proving what happened across trusted data, research, paper validation, portfolio review, ledger/accounting, reconciliation, approvals, and governed reports.

The current product path keeps browser and desktop operator workflows active in parallel:

- Active browser UI lane: `src/Meridian.Ui/dashboard/` and built workstation assets in `src/Meridian.Ui/wwwroot/workstation/`.
- Active desktop UI lane: `src/Meridian.Wpf/` is again a first-class operator surface for Windows desktop workflows.
- Shared UI/API support: `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` should continue to carry read models and workstation endpoints used by both browser and desktop surfaces.
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
| W2 Paper-trading cockpit | Done | Closed in the 2026-05-27 evidence slice: shared W2 readiness/operator-inbox tests, browser Trading parity tests, focused WPF Lane A tests, and pilot `TrustedData`, `PaperPromotion`, and `PaperSession` gates are green. |
| W3 Shared run / portfolio / ledger continuity | Done | Closed in the 2026-05-27 evidence slice: shared brokerage/continuity/pilot tests, focused WPF portfolio/accounting/cash-flow tests, browser route/API parity tests, and pilot `ResearchRun`, `RunComparison`, `PortfolioLedgerReview`, and `Reconciliation` gates are green. |
| W4 Governance and fund operations | In Progress | Critical path. Security Master baseline and reconciliation/report-pack support exist; durable casework, sign-off, close/report lifecycle, and provenance still need end-to-end acceptance evidence. |
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
close operator acceptance, live readiness, desktop/browser parity, mobile, W5, or W6.

2026-05-27 W2/W3 closure evidence: the current implementation slice repaired the shared
W2/W3 compile and contract drift, reran the build-enabled shared acceptance filter
(`Wave2PaperTradingCockpitAcceptanceTests`, `Wave2OperatorInboxAcceptanceTests`,
`TradingOperatorReadinessServiceTests`, `BrokeragePortfolioSyncServiceTests`, and
`PilotAcceptanceHarnessTests`; 71 passed), the browser parity set
(`trading-screen.view-model.test.ts`, `trading-screen.test.tsx`, `api.trading.test.ts`,
`workstation-endpoints.test.ts`, `workspace.test.ts`, and `command-palette.view-model.test.ts`;
154 passed, 1 existing todo), and the focused WPF Lane A/B shell set
(`TradingWorkspaceShellViewModelTests`, `Wave2OperatorInboxAcceptanceTests`,
`MainShellViewModelTests`, `StrategyRunPortfolioViewModelTests`, `AccountPortfolioViewModelTests`,
`AggregatePortfolioViewModelTests`, `CashFlowViewModelTests`, `FundLedgerViewModelTests`, and
`FundAccountsViewModelTests`; 113 passed). The latest
`artifacts/pilot-acceptance/latest/pilot-readiness.json` generated at
`2026-05-27T23:17:46.8333629+00:00` reports all W2 and W3 mapped stage gates as `Ready` with no
blockers. This closes W2 and W3 implementation and acceptance evidence without widening claims into
W4 governance completion, mobile, W5, or W6 live-readiness.

Release/distribution hygiene evidence as of 2026-05-19 is narrower than roadmap completion: publish
output now belongs under ignored `artifacts/publish/` paths, the publish script has a
`-SizeOptimized` mode for local size and low-disk investigations, and
`build/scripts/publish/measure-size.ps1` reports common generated-output roots. Treat this as
developer/release tooling support, not a Wave 2-4 product-readiness claim.

The newer May 19 governance/accounting evidence is also support evidence, not closure: ledger
`posting_kind` guards, report-pack validation/lifecycle metadata, retained evidence-vault manifest
lookup, reconciliation case storage/audit hardening, account-sync history/readiness DTOs, and the
active Security Master validation-gate/snapshot slice all reinforce W3/W4 continuity. They do not
close durable casework, report publication controls, Evidence Vault, close workflow, or
live-readiness.

The latest May 19 browser-workstation support evidence is likewise support evidence, not a wave
exit. Provider setup now feeds visible provider-routing connection, binding, credential-source,
environment, warning, and trust-snapshot refresh state into Data and Settings. Strategy Designer
backend actions now mark GET endpoints as browser-openable and POST validation/preview/backtest
commands as reference-only, and Reporting export commands carry abort signals so superseded profile
exports cannot publish stale status under the wrong selection. These reduce operator confusion and
late-response races. They preserve the closed W2 cockpit and W3 continuity baselines, but they do
not close W4 report-pack lifecycle, Backtest Studio, or live-readiness gates.

May 20 hardening evidence remains support evidence: brokerage order placement fails closed without
required validation/sign-off artifacts, ledger and promotion write routes have explicit
authorization checks, execution metadata is sanitized before endpoint/audit exposure, CodeQL and
gitleaks hardening are in CI, the web-workstation installer repairs invalid preserved provider
configs while archiving legacy installs, and browser workstation forms now expose expired-session,
forbidden-role, and disabled-field states in accessible recovery text. These strengthen the active
path but do not change canonical wave status.

May 21 strategy, contract, and documentation-control evidence is also support evidence. The shared
Strategy Engine foundation now defines strategy metadata, parameter schemas, data dependencies,
pre-run validation, evidence hashes, and workstation definitions/validate-run endpoints for the
Covered Call and visual designer paths. Additive workstation continuity payload guards now protect
ledger/reconciliation/strategy read models, provider adapter follow-up is anchored by a capability
matrix, and roadmap/source documentation truth now flows through structured registries plus
stale-doc and hash validation. These improve execution control, shared-contract discipline, and
documentation freshness, but they do not change W2-W4 status labels.

May 28 support evidence confirms that active acceptance has shifted to W4 rather than reopening
W2/W3. Shared/browser/source-route checks now cover operations-continuity close workflow routes,
reconciliation casework, fund-operations workspace reads, report-pack workflow/provenance, Security
Master endpoint mapping, fund-structure report-pack and cash-flow endpoints, browser
Accounting/Reporting W4 parity, `FundOpsCloseLaneScenarioTests`, and `PilotAcceptanceHarnessTests`.
Additional support slices add Explain-the-Break parity, Investment Accounting Transaction Lab
previews, report restatement review, richer run comparison and portfolio drill-in support, shared
workflow route parity, and screenshot/evidence wrong-route validation. These preserve W2/W3 and
advance W4 support; they do not close W4 until Lane C close/case/report/evidence acceptance is
green, including the build-backed WPF lane.

May 29 documentation review: Security Master custom-asset profile governance now has shared
promotion-candidate assessment support through `SecurityAssetProfileGovernanceService`,
`SecurityAssetProfilePromotionCandidateDto`, and
`/api/security-master/asset-profiles/promotion-candidates`. Treat this as W4/UFL support evidence
for identifying profile-backed assets that may justify a first-class package; actual package
graduation, operator promotion workflow, indexed profile-field projections, and broader acceptance
remain open.

W2/W3/W4 claim discipline: every new roadmap or status claim for these waves must either map to a
`pilot-readiness.*` stage gate that turns green in the latest harness output or name the blocker in
that stage gate's `blockers` list. The current stage mapping is:

| Wave | Pilot-readiness stage gate(s) that can support a claim | If not green, record blocker there |
| --- | --- | --- |
| W2 paper-trading cockpit | `TrustedData`, `PaperPromotion`, `PaperSession` | Trading cockpit, promotion, replay, stale-session, or DK1 trust blockers belong on the affected stage. |
| W3 shared run / portfolio / ledger continuity | `TrustedData`, `ResearchRun`, `RunComparison`, `PaperPromotion`, `PortfolioLedgerReview`, `Reconciliation` | Run continuity, portfolio, ledger, brokerage/account, reconciliation, or evidence-packet gaps belong on the affected stage. |
| W4 governance and fund operations | `TrustedData`, `PortfolioLedgerReview`, `Reconciliation`, `GovernedReportPack` | Casework, approval/sign-off, provenance, report-pack lifecycle, or governed-output gaps belong on the affected stage. |

Desktop/WPF workflow claims also must map to
[`desktop-ui-workflow-acceptance-matrix.md`](desktop-ui-workflow-acceptance-matrix.md). That matrix
is the desktop acceptance filter for Lane A W2 cockpit reliability, Lane B W3 run -> portfolio ->
ledger continuity, and Lane C W4 reconciliation/governance close flow. It requires a shared
contract/read-model check, focused WPF evidence, browser parity or an explicit mismatch blocker, and
the matching pilot-readiness stage posture before any desktop slice is described as accepted.
Treat support evidence separately from exit claims and keep claims scoped to the exact matrix scenario.

## Active Planning Set

These documents define current direction and should be read in this order:

| Document | Current role |
| --- | --- |
| [`../status/ROADMAP.md`](../status/ROADMAP.md) | Authoritative roadmap narrative, wave order, and conservative completion claims. |
| [`../status/PROGRAM_STATE.md`](../status/PROGRAM_STATE.md) | Canonical wave status table and target dates. |
| [`../status/provider-capability-matrix.md`](../status/provider-capability-matrix.md) | Provider capability/state ownership matrix used in recurring status review; non-`complete` rows require both an owner and a target sprint. |
| [`../roadmap/README.md`](../roadmap/README.md), [`../source/README.md`](../source/README.md) | Structured roadmap and source-documentation registries; edit data/renderers rather than generated outputs. |
| [`README.md`](README.md) | Active plan index and role classification for every plan file in `docs/plans/`. |
| [`evidence-backed-investment-operations-plan.md`](evidence-backed-investment-operations-plan.md) | Product-category filter and archive rule for the evidence-backed investment-operations direction. |
| [`meridian-6-week-roadmap.md`](meridian-6-week-roadmap.md) | Short-horizon execution slice for W2-W4 plus trust-gate maintenance. |
| [`wave-implementation-checklists.md`](wave-implementation-checklists.md) | Current concrete TODO ledger for W1 maintenance, W2/W3 baseline preservation, and W4 blocker closure. |
| [`desktop-ui-workflow-acceptance-matrix.md`](desktop-ui-workflow-acceptance-matrix.md) | Desktop/WPF workflow acceptance matrix for W2 cockpit, W3 continuity, and W4 governance lanes, including shared checks, focused WPF tests, browser parity, and release blockers. |
| [`waves-2-4-operator-readiness-addendum.md`](waves-2-4-operator-readiness-addendum.md) | Owner lanes, dependencies, and exit criteria for the closed W2/W3 baselines and active W4 path. |
| [`web-ui-development-pivot.md`](web-ui-development-pivot.md) | Browser workstation foundation, shared-surface rules, and desktop/browser coexistence history. |
| [`meridian-pilot-workflow.md`](meridian-pilot-workflow.md) | Golden-path productization filter: trusted data to governed report pack. |
| [`paper-trading-cockpit-reliability-sprint.md`](paper-trading-cockpit-reliability-sprint.md) | Wave 2 reliability contract for readiness, replay, controls, and promotion. |
| [`brokerage-portfolio-sync-blueprint.md`](brokerage-portfolio-sync-blueprint.md) | Brokerage and custodian account-sync design for Wave 3-4 continuity. |
| [`governance-fund-ops-blueprint.md`](governance-fund-ops-blueprint.md) | Governance, reconciliation, and report-pack target state for Wave 4. |
| [`ledger.md`](ledger.md) | Ledger execution roadmap for period management, persistence, accruals, multi-ledger support, and reporting. |
| [`fund-management-pr-sequenced-roadmap.md`](fund-management-pr-sequenced-roadmap.md), [`fund-management-module-implementation-backlog.md`](fund-management-module-implementation-backlog.md), [`fund-management-product-vision-and-capability-matrix.md`](fund-management-product-vision-and-capability-matrix.md) | Fund-management detail backlog. Use these after the core W2-W4 path, not as competing scope. |

## Deferred Or Reference Planning

These plans remain useful, but they are not current readiness drivers unless the roadmap explicitly pulls them forward:

- Backtest Studio and research acceleration plans are Wave 5 material.
- UFL asset profiles are active reference designs for asset coverage and Security Master depth, not readiness-closure documents.
- Options, portfolio-level backtesting, database, runbook registry, kernel parity, L3 inference, QuantScript multi-instance, and assembly-performance plans are technical or later-wave references.
- Commercial package names such as Meridian Evidence OS, Report Factory, Controls, PaperOps, and FundOps are positioning language until their shared contracts, browser workflows, evidence retention, and governed outputs are accepted.

## Consolidation Rules

- Start from this file, then use [`README.md`](README.md) to locate the detailed plan for the slice being changed.
- Do not treat support evidence as a wave exit. A route, DTO, fixture, WPF page, or endpoint proves support only when the corresponding acceptance evidence is present.
- Treat browser workstation and desktop/WPF as active operator surfaces; choose the surface that best fits the workflow while keeping shared behavior out of surface-specific business logic.
- Land new business logic, read models, and workstation API seams in shared contracts or services before composing them into browser or desktop workflows.
- Archive completed, superseded, or historical plans under `archive/docs/` and link to them only as historical context.
- Keep active docs aligned to the seven visible workspaces. Legacy `Research`, `Data Operations`, and `Governance` names may appear as compatibility groupings, not as visible root navigation.

## Before Claiming Completion

Before a planning doc marks a slice as complete, verify that it has the evidence expected for its lane:

- W1/DK1: fresh provider packet, matrix alignment, packet-bound sign-off when evidence changes, and provider-validation docs updated.
- W2: readiness endpoint behavior, replay freshness, operator-inbox routing, promotion/checkpoint gates, Lane A desktop acceptance evidence, and browser parity evidence from shared payloads.
- W3: shared run, portfolio, ledger, brokerage/account, reconciliation, and evidence-packet continuity across the seven-workspace path.
- W4: reconciliation casework, approval/sign-off, report-pack lifecycle, provenance, and governed output evidence.
- W5/W6: only after active W4 gates stop being speculative blockers and W2/W3 preservation evidence stays green.
