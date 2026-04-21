# Meridian - Production Status

**Version:** 1.7.2
**Last Updated:** 2026-04-21
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

The current working tree reinforces that direction rather than changing it. WPF workspace-shell consolidation is actively moving through `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel` orchestration, and new shell smoke tests, but that work should still be treated as in-flight support for Waves 2-4 rather than as a closed migration milestone.

### Overall Assessment: **DEVELOPMENT / PILOT READY**

| Capability | Maturity | Notes |
| --- | --- | --- |
| Core event pipeline | Complete | Channel-based processing with backpressure, metrics, validation, and storage fan-out |
| Storage layer | Complete | JSONL/Parquet composite sink with WAL, catalog, packaging, and export support |
| Backfill providers | Partial | Broad provider baseline with fallback chain; some paths still need credentials or runtime proof |
| Backtesting engine | Complete | Tick-by-tick replay with fill models, portfolio metrics, and Lean integration |
| Paper-trading gateway baseline | Complete | Risk rules, position and fill tracking, session endpoints, and promotion seam are in code |
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
