# Meridian - Production Status

**Version:** 1.7.2
**Last Updated:** 2026-04-13
**Status:** Development / Pilot Ready - structurally strong baseline with Wave 1-4 productization work still active

This document summarizes Meridian's current readiness posture and active delivery gaps from the current repository state. It is subordinate to [`ROADMAP.md`](ROADMAP.md): use this file for readiness language and current posture, and use the roadmap for full wave sequencing.

---

## Executive Summary

Meridian already has working ingestion, storage, replay, backtesting, provider orchestration, export tooling, shared workstation endpoints, web and WPF workstation shells, and a delivered Security Master baseline. The main product gap is no longer missing foundations. It is the remaining work required to turn those foundations into a coherent operator-facing trading workstation and fund-operations product with trustworthy provider evidence, a dependable paper-trading lane, one shared run-centered model, and deeper governance workflows.

The current working tree reinforces that direction rather than changing it. WPF workspace-shell consolidation is actively moving through `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel` orchestration, and new shell smoke tests, but that work should still be treated as in-flight support for Waves 2-4 rather than as a closed migration milestone.

### Overall Assessment: **DEVELOPMENT / PILOT READY**

| Capability | Maturity | Notes |
|---|---|---|
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
| Provider confidence | Partial | Evidence-backed matrix, committed bounded-runtime artifacts, checkpoint proof, and Parquet L2 proof are now in repo; remaining gaps are vendor-runtime and entitlement bounded |
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

### Wave 1: Provider confidence and checkpoint evidence

- Polygon, Robinhood, Interactive Brokers, StockSharp, and NYSE still have at least one runtime-bounded scenario that depends on credentials, entitlements, packages, or vendor sessions
- backfill checkpoint reliability and Parquet L2 flush behavior now have repo-backed proof, including retry-safe L2 flush retention on failed/cancelled writes, and should be treated as closed Wave 1 sub-gates unless those tests regress
- provider-confidence language must stay tied to [`provider-validation-matrix.md`](provider-validation-matrix.md), `artifacts/provider-validation/`, and `run-wave1-provider-validation.ps1` instead of architecture intent

### Wave 2: Paper-trading cockpit hardening

- the web trading cockpit already has real surfaces for positions, orders, fills, replay, sessions, and promotion, but it still needs clearer daily-use acceptance criteria
- session persistence, replay behavior, audit visibility, and execution-control flows need more explicit operator validation
- live-readiness claims must remain downstream of a trustworthy paper workflow

### Wave 3: Shared run / portfolio / ledger continuity

- the shared run seam exists, but paper/live-adjacent history, cash-flow, and reconciliation continuity are not equally deep in every surface yet
- portfolio, ledger, fills, attribution, and reconciliation need to feel like one run-centered system rather than adjacent slices
- WPF workflow work must keep reinforcing the same read-model seam instead of reintroducing page-local orchestration

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

- Security Master is a delivered baseline, not an open foundation item
- governance still needs deeper account/entity, multi-ledger, cash-flow, reconciliation, and governed reporting workflows
- the next governance slices should extend shared DTOs, read models, and export seams instead of creating a second governance stack

Waves 5 and 6 remain valid roadmap steps, but they sit after the Wave 1-4 core operator-readiness path. Optional advanced research / scale tracks do not change the current readiness posture.

---

## Provider Evidence Summary

Use [`provider-validation-matrix.md`](provider-validation-matrix.md) as the primary per-scenario evidence source. Current high-signal summary:

| Provider | Posture | Notes |
|---|---|---|
| Alpaca | Complete | The checked-in execution path is validated end to end through the stable `/api/execution/*` seam |
| Polygon | Partial | Recorded-session replay is strong; remaining gaps are bounded to live reconnect and websocket throttling transcripts |
| Robinhood | Partial | Brokerage and polling-path evidence exist; remaining runtime scenarios are tracked under `artifacts/provider-validation/robinhood/2026-04-09/` |
| Interactive Brokers | Partial | Guidance, smoke-build, and version-bound tests are in repo; vendor runtime remains tracked under `artifacts/provider-validation/interactive-brokers/2026-04-09/` |
| StockSharp | Partial | Wave 1 validated adapters are narrowed to Rithmic, IQFeed, CQG, and Interactive Brokers, with runtime bounds tracked under `artifacts/provider-validation/stocksharp/2026-04-09/` |
| NYSE | Partial | L1/shared-lifecycle evidence is strong; remaining auth/rate-limit/depth bounds are tracked under `artifacts/provider-validation/nyse/2026-04-09/` |

---

## Core Operator-Readiness Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** the matrix in `provider-validation-matrix.md` points to executable suites or committed artifact folders for every row, and `run-wave1-provider-validation.ps1` can reproduce the offline gate.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

---

## Immediate Readiness Checklist

- [ ] Keep provider claims synchronized with executable evidence in [`provider-validation-matrix.md`](provider-validation-matrix.md)
- [ ] Close the remaining vendor-runtime-bounded Wave 1 scenarios for Polygon, Robinhood, IB, StockSharp, and NYSE
- [ ] Keep `artifacts/provider-validation/` and `run-wave1-provider-validation.ps1` current as the Wave 1 gate evolves
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
