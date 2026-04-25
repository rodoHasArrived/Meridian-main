# Meridian - Production Status

**Version:** 1.7.2
**Last Updated:** 2026-04-25
**Status:** Development / Pilot Ready - Wave 1 trust gate is closed and Waves 2-4 productization work remain active

This document summarizes Meridian's current readiness posture and active delivery gaps from the current repository state. It is subordinate to [`ROADMAP.md`](ROADMAP.md): use this file for readiness language and current posture, and use the roadmap for full wave sequencing.

---

## Canonical Program State

Program wave status is canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md). Any wave status wording in this file is explanatory context only.

<!-- program-state:begin -->
| Wave | Owner | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Done | 2026-04-17 | [`production-status.md#provider-evidence-summary`](production-status.md#provider-evidence-summary) |
| W2 | Trading Workstation | In Progress | 2026-05-29 | [`ROADMAP.md#wave-2-web-paper-trading-cockpit-completion`](ROADMAP.md#wave-2-web-paper-trading-cockpit-completion) |
| W3 | Shared Platform Interop | In Progress | 2026-06-26 | [`ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity`](ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity) |
| W4 | Governance + Fund Ops | In Progress | 2026-07-24 | [`ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline`](ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline) |
| W5 | Research Platform | Planned | 2026-08-21 | [`ROADMAP.md#wave-5-backtest-studio-unification`](ROADMAP.md#wave-5-backtest-studio-unification) |
| W6 | Execution + Brokerage Integrations | Planned | 2026-09-18 | [`ROADMAP.md#wave-6-live-integration-readiness`](ROADMAP.md#wave-6-live-integration-readiness) |
<!-- program-state:end -->

---

## Executive Summary

Meridian already has working ingestion, storage, replay, backtesting, provider orchestration, export tooling, shared workstation endpoints, web and WPF workstation shells, and a delivered Security Master baseline. The main product gap is no longer missing foundations. It is the remaining work required to turn those foundations into a coherent operator-facing trading workstation and fund-operations product with trustworthy provider evidence, a dependable paper-trading lane, one shared run-centered model, and deeper governance workflows.

The current working tree reinforces that direction rather than changing it. The WPF workspace-shell baseline is present through `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel` orchestration, deep-page hosting, shell-context strips, shell smoke tests, and focused tests for Batch Backtest, Position Blotter, Notification Center, Welcome, workspace queue tone styles, and workspace shell context-strip behavior. Workflow-level acceptance still belongs to Waves 2-4 rather than a separate desktop migration milestone.

### Overall Assessment: **DEVELOPMENT / PILOT READY**

| Capability | Maturity | Notes |
| --- | --- | --- |
| Core event pipeline | Complete | Channel-based processing with backpressure, metrics, validation, and storage fan-out |
| Storage layer | Complete | JSONL/Parquet composite sink with WAL, catalog, packaging, and export support |
| Backfill providers | Partial | Broad provider baseline with fallback chain; some paths still need credentials or runtime proof |
| Backtesting engine | Complete | Tick-by-tick replay with fill models, portfolio metrics, and Lean integration |
| Paper-trading gateway baseline | Complete | Risk rules, position and fill tracking, session endpoints, promotion seam, canonical promotion approval checklist, and the shared trading-readiness contract are in code |
| Brokerage gateway framework | Partial | Alpaca, IB, Robinhood, and StockSharp paths exist; broader runtime proof remains open |
| Shared run / portfolio / ledger baseline | Partial | Shared run, portfolio, ledger, and reconciliation seams are in code; broader paper/live, cash-flow, and multi-ledger depth remains |
| Security Master platform seam | Complete | WPF, Research, Trading, Portfolio, Ledger, Reconciliation, and Governance share one authoritative coverage/provenance contract |
| Governance product surfaces | Partial | Security coverage, reconciliation drill-ins, direct lending, and reporting-adjacent seams are live; broader multi-ledger, cash-flow, and governed reporting workflows remain incomplete |
| Web and WPF workstation shells | Partial | Both surfaces expose meaningful workspace flows; workflow hardening and deeper workflow-first consolidation remain |
| Monitoring and observability | Complete | Prometheus, OpenTelemetry, SLO registry, and alert/runbook linkage are in place |
| Provider confidence | Complete | The active Wave 1 gate is Alpaca, Robinhood, and Yahoo; Alpaca and Yahoo are repo-closed, Robinhood remains explicitly runtime-bounded by committed artifacts, and deferred providers stay outside the active closure target |
| Test baseline | Partial | Cross-project coverage is strong, but operator-grade acceptance coverage is still catching up in active Wave 1-4 areas |

---

## Current Strengths

- mature ingestion, replay, storage, export, and data-quality foundations
- shared composition and host startup patterns
- shared workstation shell in web plus materially aligned WPF workstation surfaces
- paper trading, execution, lifecycle, and promotion seams already exposed through stable REST surfaces
- Security Master as the shared instrument-definition baseline across Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF
- direct lending, reconciliation, and governance-facing export/report-adjacent seams already present in the repo
- existing shared run, portfolio, and ledger read services that give Meridian a real cross-workspace integration seam
- workflow guide and screenshot-refresh tooling for ongoing operator-facing validation

---

## Active Gaps By Wave

Wave status labels and dates are canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md).

### Wave 1: Closed provider confidence and checkpoint gate

- Alpaca, Robinhood, and Yahoo define the closed active Wave 1 provider gate; Alpaca and Yahoo are closed by repo-backed evidence, while Robinhood remains explicitly bounded by committed runtime broker-session scenarios
- backfill checkpoint reliability and Parquet L2 flush behavior are closed Wave 1 sub-gates backed by repo tests, including retry-safe L2 flush retention on failed or cancelled writes
- Polygon, Interactive Brokers, NYSE, and StockSharp remain deferred or non-blocking inventory for the current wave and should not be described as active Wave 1 blockers
- provider-confidence language must stay tied to [`provider-validation-matrix.md`](provider-validation-matrix.md), `artifacts/provider-validation/`, `run-wave1-provider-validation.ps1`, the generated DK1 parity packet, and the latest generated validation summary instead of architecture intent; the current DK1 packet is ready for operator review, but sign-off remains open

### Wave 2: Paper-trading cockpit hardening

- the web trading cockpit already has real surfaces for positions, orders, fills, replay, sessions, and promotion, but it still needs clearer daily-use acceptance criteria
- `/api/workstation/trading/readiness` now gives Wave 2 a single DTO surface for active paper-session state, replay consistency, execution controls, promotion approval checklist state, brokerage sync posture, and operator work items
- session persistence, replay behavior, audit visibility, and execution-control flows need more explicit operator validation
- live-readiness claims must remain downstream of a trustworthy paper workflow

#### Cockpit hardened acceptance gate (objective pass/fail)

“Cockpit hardened” is **Pass** only when all three scenario acceptance criteria below are green in CI and locally reproducible. It is **Fail** if any criterion is red.

| Scenario acceptance criterion | Pass evidence | Fail condition |
| --- | --- | --- |
| `/api/execution/*` to `/api/promotion/*` continuity | `Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable` proves one operator flow can create/close/replay a paper session, evaluate promotion eligibility, approve promotion, and see both execution and promotion evidence in returned contracts. | Any break in endpoint contract continuity, missing promotion-history visibility, or missing audit linkage (`PromotionId`/actor/correlation) in the same scenario run. |
| Session persistence + replay verification | The same continuity scenario asserts `/api/execution/sessions/{sessionId}/replay` returns `ReplaySource=DurableFillLog`, `IsConsistent=true`, empty mismatches, and deterministic replayed cash after persisted fills. | Replay endpoint unavailable, inconsistent replay state, mismatch reasons present for the deterministic baseline, or missing durable-fill-log provenance fields. |
| Promotion decision visibility + audit rationale | `Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale` verifies blocked promotion evaluations expose policy reasons (`BlockingReasons`) and rejected decisions keep explicit operator rationale in the decision payload. | Evaluation omits blocking rationale for an ineligible run, or rejection response does not carry an explicit reason suitable for operator audit/review. |

Operator-readiness language for Wave 2 should stay “in progress” until the full cockpit-hardened gate above is continuously passing.

### Wave 3: Shared run / portfolio / ledger continuity

- the shared run seam exists, but paper/live-adjacent history, cash-flow, and reconciliation continuity are not equally deep in every surface yet
- portfolio, ledger, fills, attribution, and reconciliation need to feel like one run-centered system rather than adjacent slices
- WPF workflow work must keep reinforcing the same read-model seam instead of reintroducing page-local orchestration

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

- Security Master is a delivered baseline, not an open foundation item
- governance still needs deeper account/entity, multi-ledger, cash-flow, reconciliation, and governed reporting workflows; the first reconciliation break-queue slice is now file-backed with review, resolve/dismiss, and audit-history routes, but tolerance calibration and generalized governance casework remain open
- the next governance slices should extend shared DTOs, read models, and export seams instead of creating a second governance stack


#### Wave 4 objective pass/fail gate (cockpit-style)

"Wave 4 objective" is **Pass** only when every governance/fund-ops criterion below is green in CI and locally reproducible. It is **Fail** if any criterion is red.

| Criterion | Required endpoint(s) + response fields | Required workstation surface behavior | Fail condition |
| --- | --- | --- | --- |
| Security Master conflict lifecycle is traceable end-to-end | `/api/security-master/conflicts`, `/api/security-master/conflicts/{conflictId}`, and `/api/security-master/conflicts/{conflictId}/resolve` must expose `ConflictReasonCode`, source-provenance identifiers, and resolution payload rationale (`ResolutionDecision`, `ResolutionRationale`, `Actor`, `TimestampUtc`, `CorrelationId`). | Operator can **search -> drill-in -> history -> resolution** for one conflicted instrument and see conflict reasons, source provenance, prior resolution history, and final resolution decision in one continuous flow. | Any missing linkage between conflict list/detail/resolution views, missing conflict reason code, missing source provenance, or missing explicit resolution rationale/audit chain in the same scenario run. |
| Corporate action provenance and parameter versioning remain explainable | `/api/security-master/corporate-actions` and `/api/security-master/trading-parameters` must return event provenance (`CorporateActionSource`, `IngestedAtUtc`) plus effective version fields (`EffectiveVersion`, `EffectiveFromUtc`, `SupersedesVersion`). | Operator can **search -> drill-in -> history -> resolution** from instrument view into corporate-action timeline and trading-parameter history, then resolve a flagged discrepancy with the effective-version trail visible. | Corporate-action timeline lacks provenance, trading-parameter change lacks effective-version traceability, or discrepancy resolution is recorded without explainable source/version linkage. |
| Governance audit trail is complete across fund-ops decisions | Governance workflow endpoints (`/api/fund-structure/workspace-view`, `/api/fund-structure/report-pack-preview`, and reconciliation decision endpoints) must emit audit metadata (`AuditActor`, `AuditTimestampUtc`, `CorrelationId`) and decision rationale fields for approvals/rejections. | Operator can **search -> drill-in -> history -> resolution** for an account/entity decision, inspect prior decision history, and complete or reject resolution with rationale that remains visible in history and governed output previews. | Any governance decision path that omits actor/timestamp/correlation, fails to retain decision rationale, or breaks history-to-resolution linkage between workspace and governed-output views. |

Wave 4 readiness language should stay "in progress" until the full objective gate above is continuously passing.

Use this file for readiness evidence and operator-facing risk notes; use [`ROADMAP.md`](ROADMAP.md) for full wave sequencing.

---

## Provider Evidence Summary

Use [`provider-validation-matrix.md`](provider-validation-matrix.md) as the primary per-scenario evidence source. Current high-signal summary:

| Provider | Posture | Notes |
| --- | --- | --- |
| Alpaca | Complete | Checked-in provider suites plus the stable `/api/execution/*` seam close the active core-provider row in repo evidence |
| Robinhood | Partial | Brokerage, polling, historical, and symbol-search evidence exist; remaining runtime scenarios stay bounded under `artifacts/provider-validation/robinhood/2026-04-09/` |
| Yahoo | Complete | Deterministic historical-provider and intraday contract evidence close the historical-only core-provider row |

Deferred from the active Wave 1 gate: Polygon, Interactive Brokers, NYSE, and StockSharp remain part of broader provider inventory, but they are not current closure blockers.

---

## Core Operator-Readiness Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** the matrix in `provider-validation-matrix.md` points to executable suites or committed artifact folders for every row, and `run-wave1-provider-validation.ps1` can reproduce the offline gate.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

---

<a id="pre-production-checklist"></a>

## Immediate Readiness Checklist

- [x] Keep provider claims synchronized with executable evidence in [`provider-validation-matrix.md`](provider-validation-matrix.md)
- [x] Keep Robinhood runtime-bounded evidence and deferred-provider labels consistent with the closed Wave 1 gate
- [x] Keep `artifacts/provider-validation/` and `run-wave1-provider-validation.ps1` current as the Wave 1 gate evolves
- [ ] Harden the paper-trading cockpit against realistic operator scenarios before widening live-readiness language
- [ ] Keep `Backtest -> Paper` explicit, auditable, and operator-visible through the workstation
- [ ] Extend shared run / portfolio / ledger / reconciliation continuity across `Research`, `Trading`, and `Governance`
- [ ] Extend governance beyond the delivered Security Master baseline into account/entity, multi-ledger, cash-flow, reconciliation, and reporting workflows
- [ ] Validate operator-facing observability and diagnostics against the active workstation surfaces

---

## Reference Documents

- [ROADMAP.md](ROADMAP.md)
- [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md)
- [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md)
- [IMPROVEMENTS.md](IMPROVEMENTS.md)
- [Provider Validation Matrix](provider-validation-matrix.md)
- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)
- [Meridian 6-Week Roadmap](../plans/meridian-6-week-roadmap.md)
