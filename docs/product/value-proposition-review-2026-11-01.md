# Adversarial Value-Proposition Review — Meridian (2026-11-01)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-11-01
**Scope:** adversarial review of a proposed expansion of Meridian's end-user value proposition
("Meridian: Expanding and Refining the End-User Value Proposition"), followed by an adversarial
pass on the first refinement and the second refinement adopted into the
[design charter](meridian-design-document.md) Version 1.7 under `DEC-VALUE-PROPOSITION-001`.
**Method:** compared the proposal with charter Version 1.6, the
[vision summary](../architecture/meridian-vision.md), the
[deferred expansion boundaries](deferred-expansion-boundaries.md), the roadmap registry
(`docs/roadmap/data/*.yml`), ADR-019, and the 2026-10-27 next-work determination. Each capability
the proposal names was checked against source with a code sweep and spot verification. Source
anchors are on `main` at `86a1689f`.

> This review is deliberately critical. It credits the proposal before criticizing it, and it does
> not compete with the roadmap registry for status. It registers no roadmap row, changes no rank,
> and reopens no deferred lane. Findings that imply implementation work are recorded for later
> registry decisions, not fixed here.

## Headline

The proposal diagnoses a real **communication** problem and misdiagnoses it as a **product**
problem. Charter Section 1 reads as a catalogue of invariants rather than a promise to a buyer, and
the strategy and order-management objectives (`DEC-TRADING-STRATEGY-DIRECTION-001`) are appended
to Section 1.2 rather than integrated into it. The proposal's remedy repositions Meridian as a broad
"integrated investment operations and decision platform" with eight product domains, twelve
prioritized capabilities (four at P0), seven role workspaces, and an individual-investor persona.
That trades Meridian's one checkable differentiator — proof travels with the number — for the most
crowded positioning in investment technology, the front-to-back suite, at the point where the
program is closest to its first release candidate.

> **The proposal's own MVP proves the activation doctrine.** Nearly every bond calculation it
> asks for already exists in source. The coupon-accrual posting it needs is written, tested, and
> has no production caller. And the "unified investment workbench" it proposes already exists,
> holding a second, browser-local lot record beside the ledger.

## What the Proposal Gets Right

1. **Outcome-first framing.** Section 1 of the charter is written for governance, not for buyers.
2. **The bridge.** "From investment idea to verified financial outcome" joins the strategy/OMS
   half and the financial-operations half far better than the charter's appended paragraph.
3. **Pre-trade financial impact preview** is the strongest idea. It is genuinely connective, and it
   can reuse the `Expected`/`Projected` lifecycle stages of the asset accounting event spine
   (`W9-ASSET-010`).
4. **Proportional controls and modular adoption** are already charter law; they deserve to be part
   of the buyer-facing promise.
5. **A vertical bond lifecycle slice** has the right shape (a proven slice, Section 2.2) and the
   right instrument.
6. **A decision record** linking a thesis to a previewed consequence and an order is a real gap in
   the object model.
7. **Honest labeling.** The proposal states that its domains and priorities are not a verified gap
   analysis.

## Findings

Most severe first.

| # | Severity | Finding | Evidence | Disposition |
| --- | --- | --- | --- | --- |
| F1 | Critical | **It demotes the only differentiator.** The core statement never names proof; "evidence-driven" is an adjective. Its differentiator ("each function understands the others") is the incumbent suite pitch, where a solo-developed product cannot reach breadth parity. Its six outcomes are not measurable. | Charter §1.2, §1.5 | Keep proof as the noun; widen what is proven to include decisions. |
| F2 | Critical | **"Better investment decisions" is unfalsifiable and risky.** Meridian cannot verify decision quality. Combined with retail users, AI that discovers opportunities, and a strategy marketplace, it drifts toward an investment-advice posture. | Proposal §1–3 | Promise known consequences and retained rationale, not better decisions. |
| F3 | High | **Scope expansion against the binding constraint.** About 26 rows are open, production readiness is `blocked`, and the `v0.1.0-rc.2` tag is waiting on signing secrets. The Activation Ratio (§1.5) penalizes breadth, and every review since 2026-07 found built-but-unwired seams. The proposal names this risk and then adds four P0 items. | `program-state.yml`; charter §2.1, §5.3; [next-work 2026-10-27](plans/next-work-determination-2026-10-27.md) | No new rows, ranks, or reopened lanes; candidates only. |
| F4 | High | **Most "new" capabilities already exist or are registered.** Relabelling them invites the parallel engines the charter forbids. See the overlap table below. | Registry; source paths below | Map each to its owner before adopting anything. |
| F5 | High | **Unacknowledged conflicts with recorded exclusions.** Configurable workflow builder (§1.3 "not"; deferred lane). Planning and scenario forecasting engines (deferred; distinct from the existing contractual cash ladder). Portfolio VaR, stress testing, and optimization (enterprise risk deferred; VaR/stress exist only inside `AdvancedCarryDecisionEngine`, mean-variance only in QuantScript `PortfolioBuilder`). "Workflow Automation, Governance & AI" as a product domain (§7.2 removed AI as a domain). Execution algorithms (EMS scope, beyond W13's declared order types). A third-party strategy marketplace (distributing executable code runs into `PRD-012` isolation; the charter's marketplace is policy and report templates, phase three). Insurance and statutory accounting (an unregistered segment). External API access (the v1 envelope is local-only; remote `ProductionApi` fails closed, §12.3). Setting P0–P3 outside the registry also contradicts §25.1. | §1.3, §7.2, §12.3, §24; deferred-expansion-boundaries.md | Each stays out unless reopened by its own registry decision. |
| F6 | High | **Modular adoption contradicts the integration differentiator.** A module adopted alone competes alone with best-of-breed point tools; integration value appears only when two or more modules connect. | Proposal §1, §6 | "Start anywhere; every record joins the same chain": each entry point must emit records already wired to the proof chain. |
| F7 | High | **The governed-approval promise is not honestly deliverable in the v1 envelope.** This is a charter-level tension surfaced by the retail stress test. ADR-019 defines v1 as a single-operator, per-user, local workstation and excludes a remote browser-hosted `ProductionApi`. Source enforces two principals in governed flows: `MakerCheckerRequired` (`src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionOperationsService.cs:854`), `SelfApprovalNotAllowed` (`src/Meridian.Strategies/Services/IReconciliationBreakQueueRepository.cs:383`), and `OperatorOverrideSelfApprovalException` (`src/Meridian.Storage/SecurityMaster/IOperatorOverridesStore.cs:77`). An independent approver has no supported path to the host, and a second login on the same workstation would make "who approved it?" untrue. | `docs/adr/019-production-support-matrix-and-deployment-posture.md:48`; source paths | Open decision recorded in charter §25.3.12: labeled single-principal attestation in v1; real maker-checker in a future small-team envelope. |
| F8 | High | **Bond MVP: the hard part is unwired, not unbuilt.** See the seam list below. The slice also competes with the charter's First Trusted Close (§22.6.1). | Source paths below | Converge the two slices; the seams are the work list. |
| F9 | Medium | **The proposed workbench already exists and hosts a second lot record.** `/portfolio/asset-detail` (Overview, Passport, Details & Lots, Trading Parameters, Cash-flow scenarios) and `/accounting/security-master/detail` exist, and the Security & Instrument explorer (`src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.SecurityRows.cs`) shows identity, conflicts, projected cash flows, accounting projection, reconciliation, and usage. Its `LotsTrackerPanel` computes cost basis and unrealized P/L from browser `localStorage` (`src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx:490,519`), labeled only by a card description ("Lots are stored locally per security", `security-details-tracker.view-model.ts:642`). That is weaker than §2.3's datum-level labeling, and it is an ungoverned lot record beside the ledger lot spine. | Paths cited | Replace or loudly relabel the local tracker before any workbench expansion (later registry work). |
| F10 | Medium | **Research hides a data-licensing problem.** Of 22 adapter families, the only parsed fundamentals source is SEC EDGAR XBRL (`src/Meridian.Contracts/SecurityMaster/EdgarReferenceDtos.cs`). Interactive Brokers fundamentals return as an unparsed string (`ProviderFundamentalReport` in `src/Meridian.ProviderSdk/IProviderDataReadService.cs`). Alpaca news is an in-memory buffer. There is no ratings feed (ratings exist only as `CreditRatingTerms`). Watchlists are data-collection subscriptions (`src/Meridian.Core/Subscriptions/Models/Watchlist.cs`). No research notes, theses, or screening criteria exist. Global fundamentals, ratings, consensus, and news are licensed and redistribution-restricted. | `src/Meridian.Infrastructure/Adapters/` | Research is retained research records over EDGAR and data the user already licenses; not a data product. |
| F11 | Medium | **The impact preview needs guardrails or it becomes a second accounting engine.** Risk rules already compute post-order exposure, but only to reject (`CompositeRiskValidator`, `GrossExposureRule`, `SymbolConcentrationRule` in `src/Meridian.Risk/Rules/`). No route runs the check without submitting (`RiskEndpoints.cs`). The browser `buildOrderPreview` (`src/Meridian.Ui/dashboard/src/screens/trading-screen.view-model.ts`) shows notional and resulting position only. `ProjectedAccountingEffectDto` exists, but nothing feeds a proposed order into it, and the cash forecast accepts recorded flows only. | Paths cited | Server-side composition of the existing engines, labeled `Projected`, retained on the order intent (charter §25.3.8). |
| F12 | Medium | **The AI assistant example is candidate §25.3.3 in disguise.** "Why did this bond's book value change?" needs the deterministic variance bridge first. There is no runtime LLM integration in `src/`; `src/Meridian.Mcp` is a developer co-pilot exposing repository tools. The only product hook is the `OperationsActionOriginDto` origin labels (`AssistantDraft`, `AutomationSuggestion`). A self-hosted product also needs a model-hosting and data-egress policy and injection-safe handling of imported documents. | Charter §21, §25.3.3; `OperationsContinuityDtos.cs` | Bridge, then passport, then explanation assistant (charter §25.3.11). |
| F13 | Medium | **Individual investors** — see the stress test below. The default first-run kit is already "Personal Portfolio Desk" (`src/Meridian.Ui.Shared/Services/FirstRunExperienceService.cs:21`), while §3 lists no individual buyer. | — | Self-directed principal posture; no retail segment. |
| F14 | Medium | **Seven role workspaces would fork navigation.** They do not map to the seven canonical roots, `W8-UX-CONSOL-001` is consolidating screens, and `W8-WPF-PARITY-001` doubles the cost of every surface. Role landing presets already exist as first-run starter kits that route into the roots. | `FirstRunExperienceService.cs:19-26` | Extend starter kits; no new roots. |
| F15 | Low–Medium | **Statutory and insurance accounting by implication.** A `Statutory` basis kind and a generic `stat-default-v1` policy exist (`src/Meridian.Contracts/Ledger/LedgerBookDtos.cs:14`, `AccountingPolicyService.cs:186`), but live GL reconciliation refuses statutory books (`AccountingSystemIntegrationService.Reconciliation.cs:160`) and there is no SSAP, NAIC designation, AVR/IMR, BACV, or Schedule D reporting. | Paths cited | Drop "insurance investment accounting"; keep "statutory" only as a declared basis where implemented. |
| F16 | Low | **The object list mostly duplicates §10.2.** Renames such as security/instrument risk vocabulary drift. Only "decision" is new, and its owner is ambiguous (Planning & Forecasting owns "decision records" but is a later context). | §10.2, §7.1 | Becomes the Decision Trace (charter §25.3.9); a `docs/domain/` page precedes code. |

### F4 — Proposed capability to existing owner

| Proposed capability (priority) | Existing owner or source | Posture |
| --- | --- | --- |
| Unified Investment Workbench (P0) | `W5X-FREX-001` Security & Instrument explorer; `/portfolio/asset-detail`; Security Master passport | Exists; see F9 |
| Portfolio What-If Simulator (P0) | Risk rules, `ProjectedAccountingEffectDto`, `AccountCashForecastEngine` | Components exist; composition is new (§25.3.8) |
| End-to-End Lifecycle Explorer (P0) | `W5X-OEG-001`, `W10-PROV-001`, `W11-PASSPORT-001` | Registered |
| Intelligent Reconciliation Workbench (P0) | `W9-INGEST-009` (accepted), `W10-RECON-001`–`004` | Accepted and registered |
| Strategy-to-Portfolio Deployment (P1) | `W13-STRAT-001`, `W13-OMS-001` | Registered |
| Cash Flow & Liquidity Forecasting (P1) | `AccountCashForecastEngine`, `PortfolioCashLadderEngine` (`src/Meridian.Instruments/AssetOperations/`), F# `CashLadder` (`CashFlowProjection.fs`), with tests | Exists in source; activation, not a build |
| Fixed-Income & Structured-Asset Studio (P1) | `W10-BOND-001` (in progress); `ContractualCashFlowEngine`; `FactorPaydownProjectionService` | Registered; no deal waterfall |
| Portfolio Performance Attribution (P1) | `W10-PERF-001` (planned; no code yet); `BacktestMetricsEngine` for runs only | Registered; §6.4 excludes causal attribution |
| Corporate Action Lifecycle Manager (P1) | `W9-CORPACT-011` (accepted with reservations) | Accepted |
| AI Investigation Assistant (P1) | Charter §21 permitted uses; no runtime integration | New infrastructure |
| Configurable Workflow Builder (P2) | Deferred lane | Excluded |
| Portfolio & Strategy Marketplace (P3) | Charter ecosystem phase (templates only) | Excluded as proposed |

### F8 — Bond lifecycle seams

The analytics the proposal asks for largely exist: `BondPricingMath.TryPrice` and `TrySolveYield`
(`src/Meridian.Contracts/FixedIncome/BondPricingMath.cs`); `BondAnalyticsService` v2 for clean,
dirty, accrued, yield to maturity, yield to worst, duration, convexity, and DV01
(`src/Meridian.Instruments/FixedIncome/BondAnalyticsService.cs`); nine day-count conventions;
the `ContractualCashFlowEngine` with floating resets, caps/floors, and CPR/SMM/PSA prepayment;
`BondExerciseEconomics` for calls and puts; and straight-line and constant-yield lot amortization
(`OpenLotAmortization.cs`). Much of it is registered as `W10-BOND-001` and `W10-LOT-002`.

The proposal's steps fail at these seams:

1. **Analytics scope.** `BondAnalyticsService` accepts fixed and zero-coupon bullets only, and it
   declares `Amortization=false` and `Posting=false` (`BondAnalyticsService.cs:119-129, 206-208`).
2. **Paper settlement.** Paper fills book price per 100 against face (`PaperTradingGateway.cs:118`)
   but carry no accrued interest in fill or settlement cash. Accrued interest is handled separately
   in `BondSettlementConsideration.cs` on the ledger side.
3. **Coupon-accrual posting is dormant.** `SecurityMasterAmortizationLedgerBridge.PostProjectedCashFlowsAsync`
   and `SecurityMasterCashFlowService.BuildLedgerPostingsAsync` are called only from tests. No
   coupon-income posting path was found.
4. **Backtesting refuses bonds.** `BacktestEngine.ResolveLotConventions` and the `SimulatedPortfolio`
   constructor throw `NotSupportedException` for non-share units, so a bond "strategy" is
   operator-directed only.
5. **No bond reconciliation fixture.** Statement fixtures under `tests/Meridian.Tests/Reconciliation/Fixtures/`
   are equity-only; no custodian fixture carries bond accrued interest.

In addition, the Bond asset class declares `SupportsBasicCreateWorkflow: false`, so "import or
define a bond" depends on the governed Security Master draft path.

## Retail Stress Test

**For individual investors.** The v1 envelope (single operator, single company, local Windows
workstation) technically fits one sophisticated individual. File and synthetic adoption work
without live credentials. The family-office hierarchy already models individuals as entities. The
default first-run kit is already personal.

**Against a retail segment.**

1. Pricing the governed estate (§2.5) has no retail analogue.
2. Maker-checker is enforced in source (F7), so one person cannot finish governed flows. Using a
   second identity to satisfy it would falsify the proof.
3. Market-data terms distinguish professional and non-professional use and restrict redistribution.
4. Research suggestions, AI, and a strategy marketplace aimed at individuals approach advice and
   publishing exposure.
5. Support burden and free broker tooling.

**Position.** Support a **self-directed principal** posture inside the Single Family Office profile,
where one person is both preparer and approver, only through an explicit, labeled single-principal
attestation (charter §25.3.12). Do not build retail-specific surfaces, advice features, or retail
marketing claims.

## Second Pass: Weaknesses in the First Refinement

The first refinement kept proof as the noun but was itself reviewed and revised:

1. **It overclaimed.** "Proves every step" is false today. Verified Coverage is uncomputed
   (`W11-COVER-001` planned), and amount-level proof covers posted ledger amounts only, still in
   progress (`W10-PROV-001`). Truth discipline must apply to the proposition itself.
2. **It was still a module list.** Decide, execute, and close is a sitemap, not what a user is
   trying to learn.
3. **It named no buyer.**
4. **Its integration claim was untestable.** "Connects decisions to outcomes" names no property a
   buyer can check.
5. **It led with "self-hosted"**, a deployment fact rather than a value.
6. **It proposed two candidate metrics** where the charter already carries four unregistered ones.
7. **It was silent on F7**, selling "who approved it?" to a single-operator envelope.

## Resulting Proposition

Adopted into the charter (Sections 1.2–1.5, 3.1, 25.3.7–25.3.12) and the vision summary:

> **Meridian shows you what a decision will do before you make it, records what actually happened,
> and proves every number that results — and says plainly which numbers it cannot prove yet.**
>
> Ask any question. Verify every answer.

| Question | Proof object | Buyer-checkable test |
| --- | --- | --- |
| What happens if I do this? | Retained pre-trade preview, labeled `Projected` | Preview Fidelity (candidate) |
| What actually happened? | Decision Trace (candidate) | Every executed order resolves a complete trace, or shows its gap |
| Can I prove it? | Number Passport | Verified Coverage (unchanged primary metric) |

Every number has a passport; every decision has a trace. The first buyer is recorded as a discovery
hypothesis only: small investment teams that run the front office and the books themselves.

## What This Review Does Not Do

- It registers no roadmap row, changes no rank or status, and reopens no deferred lane.
- It changes no ADR, deployment envelope, or release gate; F7 is recorded as an open decision.
- It fixes none of F8–F9; those require their own registry decisions and source changes.
