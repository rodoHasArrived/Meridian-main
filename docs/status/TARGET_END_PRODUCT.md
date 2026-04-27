# Meridian - Target End Product

**Last Updated:** 2026-04-27
**Status:** Current end-state product summary aligned to the canonical roadmap, DK1/DK2 readiness wrapper, signed DK1 parity-packet evidence with packet-bound sign-off validation, cockpit readiness projection, stable route-aware run review-packet work items, seeded reconciliation exception-route/tolerance/sign-off metadata, initial shared operator-inbox contract with route-aware account-scoped WPF shell queue-button consumption and shell-context attention cues, current WPF shell baseline, Trading/Research/Data Operations desk briefing heroes, Trading Hours session briefing, Provider Health posture briefing, System Health triage with pending-scan versus confirmed-empty guidance, Notification Center filter recovery, Activity Log triage/export/clear support, Watchlist posture, Messaging Hub delivery posture with refresh recency, StrategyRuns filter recovery, BatchBacktest results empty guidance, QuantScript run-history handoffs, Security Master runtime/search recovery, Fund Accounts operator briefing, canonical `ResearchShell` launch routing, single-instance launch-argument forwarding, workflow automation hardening, and demo-data fixture semantics

---

## Snapshot

Meridian's target end state is a self-hosted trading workstation and fund-operations platform with four connected workspaces: `Research`, `Trading`, `Data Operations`, and `Governance`.

Data Operations establishes evidence-backed provider trust through reproducible provider, replay, checkpoint, DK1 pilot sample-set, parity-packet evidence, and operator-visible sign-off posture. Research turns that data into reviewed runs, Trading promotes approved runs into paper workflows, and Governance operates on the same instruments and records through the delivered Security Master baseline, portfolio, ledger, reconciliation, cash-flow, and reporting workflows.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context.

---

## Operator Workflow

1. **Data Operations** establishes trusted provider coverage, backfill health, symbol readiness, checkpoint confidence, and export confidence.
2. **Research** uses that trusted data to run, compare, and review strategy runs across engines and modes.
3. **Trading** promotes approved runs into paper operation, manages orders and positions, and keeps replay, risk, and session history visible.
4. **Governance** turns the same runs and positions into Security Master, portfolio, ledger, reconciliation, cash-flow, and governed reporting workflows.

The finished product should feel like one lifecycle, not four isolated tools.

---

## Product Surfaces

### Research

Research is where operators validate datasets, run experiments, compare results, inspect fills and attribution, and review promotion readiness.

### Trading

Trading is where operators run paper workflows, manage sessions, review orders and fills, monitor positions and exposure through the blotter, and apply explicit promotion controls.

### Data Operations

Data Operations is where operators manage providers, symbols, backfills, data quality, storage health, and operational exports.

### Governance

Governance is where operators review Security Master coverage, fund-account queues and provider-routing posture, portfolio and ledger outcomes, reconciliation breaks, cash-flow questions, multi-ledger views, and governed reporting outputs.

---

## First-Class Capabilities

- evidence-backed provider trust and checkpoint confidence
- shared run history across backtest, paper, and live-aware modes
- explicit `Backtest -> Paper -> Live` promotion workflow with auditability, stable readiness work items, an initial shared operator-inbox route for readiness and reconciliation blockers, route-aware WPF shell queue routing for the primary work item, shell-context attention cues for active reviews, and operator-visible action readiness
- portfolio, fills, attribution, ledger, cash-flow, and reconciliation visibility from the same run-centered model
- the delivered Security Master baseline as the authoritative instrument-definition layer
- governance and fund-operations workflows treated as core product surfaces rather than optional add-ons
- a primary WPF operator shell and retained local API/web support surfaces that reinforce the same operator model instead of diverging from it; the current WPF shell/navigation baseline plus Trading, Research, and Data Operations desk briefing heroes, Trading Hours session briefing, Provider Health posture briefing, System Health triage with pending-scan versus confirmed-empty guidance, Notification Center filter recovery, Activity Log triage/export/clear support, Watchlist posture, Messaging Hub delivery posture with refresh recency, StrategyRuns filter-aware recovery/run-scope presentation, BatchBacktest results empty guidance, stable route-aware run review-packet work items, account-scoped operator-inbox routing with shell-context attention cues, QuantScript local execution-history handoffs to shared Research surfaces for mirrored runs, Security Master runtime/search recovery, Fund Accounts account-queue/provider-routing/shared-data briefing, canonical workspace launch/deep-link routing, single-instance launch-argument forwarding, workflow automation hardening, and clear demo-data fixture cues are present, while workflow-level acceptance remains tied to Waves 2-4

---

## Path To Core Operator-Readiness

### Wave 1: Provider confidence and checkpoint evidence

Prove provider trust and checkpoint reliability with replay, runtime, auth, and validation evidence. The current DK1 wrapper extends this into an Alpaca/Robinhood/Yahoo pilot parity packet with an emitted `pilotReplaySampleSet`, signed 2026-04-27 date-stamped parity-packet artifacts, valid packet-bound sign-off evidence, trust rationale mapping, threshold calibration, and future-review rules for regenerated packets.

### Wave 2: Paper-trading cockpit hardening

Harden the paper-trading cockpit already in code into a dependable operator workflow, using the shared trading-readiness contract and the initial operator-inbox endpoint as acceptance infrastructure rather than proof that cockpit operations are complete.

### Wave 3: Shared run / portfolio / ledger continuity

Make shared run, portfolio, ledger, cash-flow, and reconciliation continuity the backbone of the product across workspaces.

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

Deepen governance and fund-operations workflows on top of the delivered Security Master baseline using shared contracts, read models, and export seams.

Waves 1-4 define **core operator-readiness**.

---

## After Core Operator-Readiness

### Wave 5: Backtest Studio unification

Unify native and Lean backtesting into one Backtest Studio experience.

### Wave 6: Live integration readiness

Validate controlled live integration readiness without overstating broad live-trading completion.

Waves 5-6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

---

## Optional Advanced Research / Scale Tracks

- deeper QuantScript libraries and workflow integration beyond the delivered local execution-history and mirrored-run handoff slice
- L3 inference and queue-aware simulation
- multi-instance coordination
- Phase 16 performance work
- broader advanced research tooling after the core workstation product is trustworthy and coherent

These improve Meridian's ceiling, but they are not required for the core end-state product to feel complete.

---

## One-Paragraph Narrative

When Meridian is finished, an operator can trust their data, run research, promote strategies into paper trading, manage orders and positions, inspect account, portfolio, and ledger outcomes, resolve Security Master and reconciliation issues, and generate governed outputs from one self-hosted workstation product. `Research`, `Trading`, `Data Operations`, and `Governance` are separate workspaces, but they share one model of runs, instruments, account posture, portfolio state, and audit evidence.
