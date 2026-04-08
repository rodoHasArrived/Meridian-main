# Meridian - Target End Product

**Last Updated:** 2026-04-07
**Status:** Current end-state product summary aligned to the April 7 roadmap refresh

---

## Snapshot

Meridian's target end state is a workflow-centric, self-hosted trading workstation and fund-operations platform. It is not just a collector, not just a backtester, and not just a governance add-on. It is one connected product where evidence-backed data trust, research, paper trading, portfolio visibility, ledger auditability, Security Master, reconciliation, and governed reporting all live inside the same operator workflow. In current roadmap terms, the authoritative Security Master seam is already in place; the remaining gap is turning that seam and the existing workstation flows into deeper governance and operator-ready workflows.

---

## Operator Workflow

1. **Data Operations** establishes trusted provider coverage, backfill health, symbol readiness, and export confidence.
2. **Research** uses that trusted data to run, compare, and review strategy runs across engines and modes.
3. **Trading** promotes approved runs into paper operation, manages orders and positions, and keeps replay, risk, and session history visible.
4. **Governance** turns the same runs and positions into portfolio, ledger, Security Master, reconciliation, cash-flow, and reporting workflows.

The finished product should feel like one lifecycle, not four isolated tools.

---

## Product Surfaces

### Research

Research is where operators validate datasets, run experiments, compare results, inspect fills and attribution, and review promotion readiness.

### Trading

Trading is where operators run paper workflows, manage sessions, review orders and fills, monitor positions and exposure, and apply explicit promotion controls.

### Data Operations

Data Operations is where operators manage providers, symbols, backfills, data quality, storage health, and operational exports.

### Governance

Governance is where operators review Security Master coverage, portfolio and ledger outcomes, reconciliation breaks, cash-flow questions, multi-ledger views, and governed reporting outputs.

---

## First-Class Capabilities

- Evidence-backed provider trust and data quality
- Shared run history across backtest, paper, and live-aware modes
- Explicit `Backtest -> Paper -> Live` promotion workflow with auditability
- Portfolio, fills, attribution, and ledger visibility from the same run-centered model
- Security Master as the authoritative instrument-definition layer
- Reconciliation, cash-flow, and reporting workflows built on shared contracts
- Governance and fund-operations workflows treated as core product surfaces rather than optional add-ons
- Web and WPF surfaces that reinforce the same operator model instead of diverging from it

---

## What Remains Optional

- L3 inference and queue-aware simulation
- multi-instance scale-out
- deeper QuantScript libraries and advanced research tooling
- Phase 1.5 preferred and convertible equity domain extension
- assembly-level optimization beyond the core product need

These improve Meridian's ceiling, but they are not required for the core end-state product to feel complete.

---

## One-Paragraph Narrative

When Meridian is finished, an operator can trust their data, run research, promote strategies into paper trading, manage orders and positions, inspect portfolio and ledger outcomes, resolve Security Master and reconciliation issues, and generate governed outputs from one self-hosted workstation product. Research, trading, data operations, and governance are separate workspaces, but they share one model of runs, instruments, portfolio state, and audit evidence.
