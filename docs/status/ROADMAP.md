# Meridian - Project Roadmap

**Last Updated:** 2026-04-21
**Status:** Active productization — the narrow Wave 1 trust gate is repo-closed, Waves 2-4 remain the core operator-readiness path, and the current working tree shows active WPF workspace-shell consolidation on top of the delivered platform baseline
**Repository Snapshot (2026-04-13 working tree):** solution projects: 39 | `src/` project files: 27 | test projects: 9 | workflow files: 42

Meridian is no longer primarily blocked on missing platform primitives. The repo already contains strong market-data, storage, replay, backtesting, execution, ledger, workstation, and Security Master foundations. The remaining delivery problem is now narrower and more product-shaped: prove operator trust, close workflow gaps, and deepen governance without letting the product split into parallel subsystems.

The active roadmap therefore centers on four outcomes:

- prove operator trust with evidence-backed provider, checkpoint, and replay validation
- harden the paper-trading cockpit already visible in the workstation
- make shared run / portfolio / ledger continuity the default integration path across `Research`, `Trading`, `Data Operations`, and `Governance`
- productize governance and fund-operations on top of the delivered Security Master baseline

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) - current capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) - normalized non-assembly backlog
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) - completed improvement history
- [`production-status.md`](production-status.md) - current readiness posture and provider-confidence gates
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) - prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) - concise end-state product summary
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) - shortest combined roadmap, opportunity, and target-state entry point
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state
- [`../plans/brokerage-portfolio-sync-blueprint.md`](../plans/brokerage-portfolio-sync-blueprint.md) - external brokerage and custodian account-sync design
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) - current short-horizon execution plan
- [`../plans/waves-2-4-operator-readiness-addendum.md`](../plans/waves-2-4-operator-readiness-addendum.md) - concrete owner-based workstreams, dependencies, and exit criteria for the active Waves 2-4 path

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

## Summary

Meridian's platform foundations are already broad enough that roadmap priority should now come from operator value and readiness evidence, not from generalized platform sprawl. The repo already includes:

- a strong ingestion and storage baseline with bounded channels, WAL durability, JSONL and Parquet sinks, replay, backfill scheduling, gap analysis, packaging, lineage, and export
- shared workstation endpoints and a workstation model organized around `Research`, `Trading`, `Data Operations`, and `Governance`
- shared `StrategyRun`, portfolio, ledger, and reconciliation read paths in `src/Meridian.Strategies/Services/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- execution, paper-trading, strategy lifecycle, and promotion seams, including wired `/api/execution/*`, `/api/promotion/*`, and `/api/strategies/*` surfaces
- a WPF workstation baseline with run-centered pages, Security Master drill-ins, and desktop shell modernization already landed
- a delivered Security Master platform seam with shared coverage and provenance flowing across research, trading, portfolio, ledger, reconciliation, governance, and WPF drill-ins

The meaningful repo delta since the April 8 planning refresh is not a new product direction. It is stronger evidence that WPF workflow-first consolidation is actively moving. The current working tree now includes `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel` shell orchestration, and new shell smoke coverage in `tests/Meridian.Wpf.Tests/Views/`. That is meaningful K1 progress, but it should still be treated as in-flight rather than a closed migration milestone.

---

## Current State

Wave-by-wave status labels are tracked in the canonical table in [`PROGRAM_STATE.md`](PROGRAM_STATE.md).

For implementation detail and evidence, use:

- [`production-status.md`](production-status.md) for readiness posture and provider evidence
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) for active execution tracks
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) for the shortest stakeholder summary

---

## What Is Complete

### Platform baseline

- Meridian's ingestion, storage, replay, export, and data-quality stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and materially stronger operational-readiness foundations than earlier roadmap snapshots.
- The historical improvement backlog is effectively closed for the current platform baseline, which is a real milestone.

### Execution and workflow foundations

- The four-workspace model is present in both planning and implementation.
- The web workstation contains material workflows for `Research`, `Trading`, `Data Operations`, and `Governance` rather than only navigation and summary surfaces.
- WPF already has meaningful run-centered workstation pages on top of the broader desktop page inventory.

### Shared-model baseline

- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` give Meridian a stable seam for unifying backtest, paper, live-aware, portfolio, and ledger views.
- Workstation endpoints already expose run comparison, diff, fills, attribution, ledger summaries, reconciliation, and Security Master read paths.

### Security Master baseline

- Security Master is no longer a blueprint-only seam. The WPF browser, workstation endpoints, shared security references, conflict handling, corporate actions, and trading-parameter flows are materially in code.
- Meridian now has one authoritative instrument-definition seam that already propagates into Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF drill-ins.

### Governance baseline

- Governance is no longer hypothetical. Security Master, reconciliation, direct lending, export profiles, and governance-facing UI and API seams are real and discoverable in the repo.
- The product gap has shifted from "build governance foundations" to "finish governance productization and workflow continuity."

---

## What Remains

- **Wave 1 maintenance:** keep the closed provider-confidence, checkpoint, and Parquet evidence gate aligned around Alpaca, Robinhood, and Yahoo
- **Wave 2:** turn the current paper-trading cockpit from "visible" into "dependable"
- **Wave 3:** make run history, portfolio, ledger, cash-flow, and reconciliation behave like one cross-workspace model
- **Wave 4:** deepen governance and fund-operations workflows on top of the delivered Security Master baseline
- **Wave 5:** unify native and Lean workflows into one Backtest Studio once the shared model is stable enough to support it cleanly
- **Wave 6:** expand into controlled live integration readiness only after trust and paper-workflow gates are materially closed
- **Optional:** pursue advanced research, simulation, scale-out, and performance tracks only after the core workstation product is coherent and trustworthy

---

## Target End Product

Meridian's target end state is a self-hosted trading workstation and fund-operations platform with four connected workspaces: `Research`, `Trading`, `Data Operations`, and `Governance`.

Data Operations establishes evidence-backed provider trust, Research turns that data into reviewed runs, Trading promotes approved runs into paper workflows, and Governance operates on the same instruments and records through the delivered Security Master baseline, portfolio, ledger, reconciliation, cash-flow, and reporting workflows.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context.

---

## Recommended Next Waves

Across Waves 2-4, keep WPF workflow-first consolidation, validation coverage, and architecture simplification reinforcing the same read-model and orchestration seams rather than becoming a parallel delivery program.

### Wave 1: Closed provider confidence and checkpoint evidence gate

**Why now:** This gate is now closed in repo evidence and should be preserved as the trust boundary for every downstream readiness claim.

**Focus:**

- keep Alpaca provider and stable execution seam evidence explicit as the repo-closed core provider baseline
- keep Robinhood supported-surface evidence aligned with its bounded runtime artifact set without overstating live readiness
- formalize Yahoo as a historical-only core provider row backed by deterministic repo tests
- keep checkpoint reliability and Parquet L2 flush behavior on the passing suite list inside `run-wave1-provider-validation.ps1`
- keep provider-confidence docs, deferred-provider language, runtime artifact folders, the validation matrix, and the latest automation summary synchronized with executable evidence

**Exit signal:** The Wave 1 matrix, roadmap, status docs, and automation summary all describe the same active provider set, Alpaca and Yahoo remain repo-closed, Robinhood remains explicitly bounded, checkpoint and L2 rows stay closed in repo tests, and deferred providers are not implied to be current blockers.

### Wave 2: Web paper-trading cockpit completion

**Why now:** Meridian already has the execution, session, and promotion APIs. Product value now depends on finishing the operator cockpit.

**Focus:**

- tighten positions, orders, fills, replay, sessions, and risk workflows into a dependable operator lane
- keep promotion evaluation and approval explicitly tied to operator review
- verify session persistence and replay behavior under realistic scenarios
- align cockpit behavior with brokerage-adapter and provider-confidence evidence

**Exit signal:** A strategy can move from backtest into a visible, auditable paper-trading workflow in the web workstation.

### Wave 3: Shared run / portfolio / ledger continuity

**Why now:** The contracts exist, but the product experience around them is not yet fully realized.

**Focus:**

- deepen run history and comparison depth across backtest, paper, and live-aware modes
- strengthen portfolio, attribution, fills, ledger, cash-flow, and reconciliation continuity
- land brokerage and custodian account-sync ingestion that feeds the same shared portfolio, ledger, and reconciliation seams
- keep Security Master enrichment and WPF workflow work tied to the same shared read-model seam

**Exit signal:** Strategy runs become Meridian's primary cross-workspace product object rather than one of several overlapping representations.

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

**Why now:** Governance is already visible in code, and Security Master is already the delivered authoritative instrument seam. This is now a workflow-deepening problem rather than a missing-foundation problem.

**Focus:**

- add account/entity and strategy-structure workflows on top of the existing governance baseline
- add multi-ledger, cash-flow, reconciliation, and reporting slices on top of shared DTOs, read services, and export seams
- connect external brokerage account state to fund-account review, cash movement, and reconciliation workflows through shared projections
- deepen governance workflows without creating separate reporting or accounting stacks


#### Wave 4 objective pass/fail gate (cockpit-style)

"Wave 4 objective" is **Pass** only when every governance/fund-ops criterion below is green in CI and locally reproducible. It is **Fail** if any criterion is red.

| Criterion | Required endpoint(s) + response fields | Required workstation surface behavior | Fail condition |
| --- | --- | --- | --- |
| Security Master conflict lifecycle is traceable end-to-end | `/api/security-master/conflicts`, `/api/security-master/conflicts/{conflictId}`, and `/api/security-master/conflicts/{conflictId}/resolve` must expose `ConflictReasonCode`, source-provenance identifiers, and resolution payload rationale (`ResolutionDecision`, `ResolutionRationale`, `Actor`, `TimestampUtc`, `CorrelationId`). | Operator can **search -> drill-in -> history -> resolution** for one conflicted instrument and see conflict reasons, source provenance, prior resolution history, and final resolution decision in one continuous flow. | Any missing linkage between conflict list/detail/resolution views, missing conflict reason code, missing source provenance, or missing explicit resolution rationale/audit chain in the same scenario run. |
| Corporate action provenance and parameter versioning remain explainable | `/api/security-master/corporate-actions` and `/api/security-master/trading-parameters` must return event provenance (`CorporateActionSource`, `IngestedAtUtc`) plus effective version fields (`EffectiveVersion`, `EffectiveFromUtc`, `SupersedesVersion`). | Operator can **search -> drill-in -> history -> resolution** from instrument view into corporate-action timeline and trading-parameter history, then resolve a flagged discrepancy with the effective-version trail visible. | Corporate-action timeline lacks provenance, trading-parameter change lacks effective-version traceability, or discrepancy resolution is recorded without explainable source/version linkage. |
| Governance audit trail is complete across fund-ops decisions | Governance workflow endpoints (`/api/fund-structure/workspace-view`, `/api/fund-structure/report-pack-preview`, and reconciliation decision endpoints) must emit audit metadata (`AuditActor`, `AuditTimestampUtc`, `CorrelationId`) and decision rationale fields for approvals/rejections. | Operator can **search -> drill-in -> history -> resolution** for an account/entity decision, inspect prior decision history, and complete or reject resolution with rationale that remains visible in history and governed output previews. | Any governance decision path that omits actor/timestamp/correlation, fails to retain decision rationale, or breaks history-to-resolution linkage between workspace and governed-output views. |

**Exit signal:** Governance becomes a real operator workflow with concrete review, drill-in, and governed-output seams built on the same contracts already used elsewhere in the workstation.

### Wave 5: Backtest Studio unification

**Why now:** Research becomes much stronger once Waves 1-4 have made the shared run model stable enough to unify native and Lean experiences cleanly.

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling
- broaden fill-model realism
- improve performance for larger windows where it materially changes operator experience

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 6: Live integration readiness

**Why now:** Live-adjacent credibility should follow, not precede, a finished paper workflow and validated provider trust.

**Focus:**

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` promotion gates
- formalize operator controls such as manual overrides, circuit breakers, and intervention flows

**Exit signal:** Meridian can support a controlled live-readiness story without overclaiming broad live-trading completion.

<a id="phase-16-assembly-level-performance-optimizations"></a>

### Optional advanced research / scale tracks

**Focus:**

- QuantScript deeper integration
- L3 inference and queue-aware execution simulation
- multi-instance coordination
- Phase 16 performance work
- broader advanced research extensions after the core workstation product is trustworthy and coherent

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is operator-ready.

---

<a id="desktop-improvements"></a>
<a id="phase-8-repository-organization--optimization"></a>

## Wave DK Program (Focused Migration Wrapper for Waves 2-4)

To avoid piecemeal adoption, Meridian now treats the active workstation migration as a two-wave **Delivery Kernel (DK)** program that wraps and strengthens Waves 2-4 rather than running in parallel.

### Program intent

- keep one dependency-ordered path from provider trust to operator-ready cockpit, then into shared-model continuity and governance productization
- require the same quality gates in each wave: **parity pass**, **explainability pass**, **calibration pass**, and **operator sign-off**
- enforce shared interop contracts through one cross-wave owner so subsystem delivery does not drift into incompatible seams

### Wave DK1 - Data quality and provider trust hardening

**Scope alignment:** operationally reinforces Wave 2 and the trust-dependent portions of Wave 3.

**Primary outcomes:**

- maintain and extend the closed Wave 1 evidence gate into daily operator workflows
- make provider behavior, replay outcomes, and cockpit data surfaces explainable to operators
- calibrate trust metrics and promotion thresholds before expanding promotion scope

**Entry criteria (must all be true):**

1. **Parity entry:** Wave 1 matrix remains repo-closed for Alpaca, Robinhood (bounded), Yahoo, checkpoint reliability, and Parquet L2 proof.
2. **Explainability entry:** provider-confidence evidence is visible in operator-facing docs and workstation drill-ins, not only in scripts.
3. **Calibration entry:** baseline trust thresholds are declared for freshness, completeness, and replay consistency.
4. **Operator entry:** Data Operations and Trading operator reps agree on the DK1 pilot symbol/account set.

**Exit criteria (must all be true):**

1. **Parity pass:** paper-cockpit data views match validated provider and replay outputs for the agreed pilot set.
2. **Explainability pass:** every trust alert in scope has attributable source, reason code, and operator action guidance.
3. **Calibration pass:** trust thresholds are tuned against replay + paper session evidence with documented false-positive and false-negative review.
4. **Operator sign-off:** named Data Operations and Trading owners approve DK1 completion and unblock DK2 promotion scope.

### Wave DK2 - Promotion, export, and reconciliation continuity

**Scope alignment:** delivers the integration-critical path of Waves 3-4 (promotion workflow, export reliability, and governance reconciliation).

**Primary outcomes:**

- make `Backtest -> Paper -> Governance` promotion a single audited path
- ensure exports and governed outputs are consistent with shared run/portfolio/ledger contracts
- establish reconciliation as an always-on control rather than end-of-process cleanup

**Entry criteria (must all be true):**

1. **Parity entry:** DK1 exit is signed and shared run/portfolio/ledger DTO seams are the active path for pilot workflows.
2. **Explainability entry:** promotion and export decisions emit audit-grade rationale with linked run, portfolio, and ledger context.
3. **Calibration entry:** reconciliation tolerance bands and exception severities are defined per subsystem.
4. **Operator entry:** Governance and Trading operators accept the DK2 pilot operating playbook.

**Exit criteria (must all be true):**

1. **Parity pass:** promoted runs, exported artifacts, and reconciliation outputs agree across workstation, API, and governance views for pilot scenarios.
2. **Explainability pass:** operators can trace each promoted run to source data trust signals, approval chain, exported package, and reconciliation state.
3. **Calibration pass:** reconciliation thresholds and promotion controls are tuned with documented exception burn-down and zero unresolved critical mismatches.
4. **Operator sign-off:** Trading and Governance owners sign production-readiness for the DK2 scope.

### Subsystem ownership and interop governance

| Subsystem | Primary owner | Responsibilities |
| --- | --- | --- |
| Data quality + provider trust | Data Operations & Provider Reliability owner | Provider evidence gate maintenance, trust metrics, provider incident review |
| Promotion + paper-trading cockpit | Trading Workstation owner | Promotion controls, paper workflow reliability, operator controls |
| Export + packaging | Data Operations Export owner | Export contract parity, package lineage, operator-facing export diagnostics |
| Reconciliation + governance | Governance/Fund Ops owner | Reconciliation policy, exception workflow, governed outputs |
| Shared run/portfolio/ledger contracts | Shared Platform Interop owner (Architecture + Contracts) | Cross-subsystem DTO/version governance, compatibility policy, contract change review |

**Interop contract governance rule:** no DK subsystem can ship a contract-breaking change without Shared Platform Interop owner approval and a documented compatibility/rollback note.

### Risk register and rollback plans by subsystem

| Subsystem | Key risk | Leading indicator | Rollback plan |
| --- | --- | --- | --- |
| Data quality + provider trust | trust drift between validation scripts and cockpit surfaces | rising unresolved trust alert delta between scripts and UI | freeze promotion expansion, pin to last verified provider matrix + replay baseline, rerun DK1 calibration |
| Promotion + paper cockpit | promotion path divergence across UI/API | mismatched promotion state or approval chain in audits | revert promotion workflow to last signed contract version, disable new promotion lanes behind feature flags |
| Export + packaging | exported artifact schema drift or lineage gaps | increase in export validation failures or missing lineage links | roll back exporter contract version, regenerate artifacts from last good run snapshots |
| Reconciliation + governance | tolerance miscalibration causing exception floods or misses | sustained spike in unresolved critical exceptions | restore prior tolerance profile, reprocess affected window, require manual governance approval for new promotions |
| Shared interop contracts | uncoordinated DTO/version change cascades | cross-workspace contract test failures | revert to previous shared contract package/API shape and block downstream deploy until compatibility suite passes |

### Kernel readiness dashboard (single status surface)

Use [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md) as the single hand-authored status dashboard for DK wave and subsystem readiness.

Dashboard requirements:

- one row per subsystem with current DK wave state, gate status, owner, and next milestone
- explicit tracking of parity/explainability/calibration/operator-sign-off per subsystem
- linked evidence and rollback status so release decisions are auditable

### Alignment guardrail with Waves 2-4

DK1 and DK2 are **execution wrappers** for existing Waves 2-4, not new parallel scope:

- Wave 2 cockpit hardening work is planned and reported through DK1
- Wave 3 shared-model continuity is split: trust-dependent scope in DK1, promotion/export/reconciliation continuity in DK2
- Wave 4 governance productization readiness gates are tracked through DK2 exit criteria

Any proposed work item that cannot map to Wave 2, 3, or 4 plus DK1/DK2 gates should be treated as optional or deferred work, not core operator-readiness path.

### Immediate implementation commitments (2026-04-20 to 2026-05-29)

To move from planning into execution, the DK program now carries date-bounded commitments tracked in the dashboard:

1. **2026-04-20 -> 2026-05-01:** close DK1 pilot parity runbook and replay/sample standardization for Alpaca/Robinhood/Yahoo.
2. **2026-04-20 -> 2026-05-01:** publish shared interop compatibility matrix template and contract-review cadence.
3. **2026-05-02 -> 2026-05-15:** lock promotion rationale fields and operator approval checklist coverage for DK1 -> DK2 handoff.
4. **2026-05-09 -> 2026-05-22:** freeze governed export schema/version contract and validate pilot scenarios.
5. **2026-05-16 -> 2026-05-29:** calibrate reconciliation tolerance profiles and exception routing for governance sign-off readiness.

The implementation source of truth remains [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md), which must be updated weekly.

---

## Risks and Dependencies

- **Provider trust is still the first dependency.** The narrow Wave 1 gate is now closed, but downstream workflow polish still depends on preserving that evidence boundary instead of reopening provider scope by prose drift.
- **Stronger tests are not the same as broad live-vendor proof.** Replay, contract, and pipeline evidence materially improve confidence but do not close every vendor-runtime gap by themselves.
- **Cockpit hardening should precede live-readiness claims.** Meridian now has meaningful trading surfaces, but operator trust still matters more than feature count.
- **The shared run model must remain the center of gravity.** If Research, Trading, Portfolio, Ledger, and Governance drift apart again, the workstation migration loses its product logic.
- **Security Master must remain the authoritative seam.** It should enrich portfolio, ledger, reconciliation, and reporting flows rather than being reimplemented inside parallel governance workflows.
- **Governance should extend shared DTOs, not invent a new stack.** Cash-flow, reconciliation, and reporting should reuse the same read-model and export seams already in place.
- **WPF migration should avoid page-level re-fragmentation.** The right move is more orchestration and view-model or service extraction, not more page-local logic.
- **Documentation drift is now a real delivery risk.** The planning set is large enough that roadmap, status, blueprint, and short-horizon docs need deliberate synchronization.

---

## Release Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** the active provider gate for Alpaca, Robinhood, and Yahoo is documented in executable evidence, checkpoint reliability plus Parquet L2 flush behavior are closed in repo tests, and `run-wave1-provider-validation.ps1` reproduces the offline gate.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

Until then, Meridian is best described as feature-rich, structurally strong, and actively being productized into its intended workstation and fund-operations end state.

---

## Reference Documents

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md)
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
