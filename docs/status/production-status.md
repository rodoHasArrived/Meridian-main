# Meridian - Production Status

**Version:** 1.7.2
**Last Updated:** 2026-03-24
**Status:** Development / Pilot Ready (comprehensive fund-management planning active)

This document summarizes the current production-readiness posture and the next product-delivery gaps from the current repository state.

## Executive Summary

Meridian already has working ingestion, storage, replay, backtesting, provider orchestration, export tooling, and a web dashboard. The current product gap is not the absence of core building blocks; it is the remaining work required to unify those blocks into a coherent operator-facing fund-management product spanning front, middle, and back office workflows.

The active plan now has two connected delivery tracks:

1. **Front-office workstation delivery** across Research, Trading, Data Operations, and Governance workspaces using shared run, portfolio, and ledger models.
2. **Middle- and back-office fund-operations delivery** adding Security Master productization, account/entity support, multi-ledger views, trial balance, cash-flow modeling, reconciliation, trade-management support, and investor/report-pack generation.

### Overall Assessment: **DEVELOPMENT / PILOT READY**

| Category | Status | Notes |
|----------|--------|-------|
| Core event pipeline | Implemented | Channel-based processing with backpressure, metrics, validation, and storage fan-out |
| Storage layer | Implemented | JSONL/Parquet composite sink with WAL, catalog, packaging, and export support |
| Backfill providers | Implemented | 10+ providers with fallback chain; some require credentials |
| Backtesting engine | Implemented | Tick-by-tick replay with fill models, portfolio metrics, and Lean integration |
| Paper trading gateway | Implemented | Risk rules (position limits, drawdown stops, order rate throttle), position and fill tracking |
| **Brokerage gateway framework** | **Implemented** | `BaseBrokerageGateway` + Alpaca, IB, StockSharp adapters; live-validated runtime paths pending |
| Direct lending module | Implemented | PostgreSQL-backed services, workflows, and `/api/loans/*` endpoints |
| CppTrader integration | Implemented | Host management, order gateway, ITCH ingestion, replay service |
| WPF desktop shell | Active | Included in solution build; builds full WPF app on Windows, CI stub on Linux/macOS |
| Shared run / portfolio / ledger model | In progress | First workstation browser/detail/portfolio/ledger flow is in code; broader paper/live coverage remains |
| Security Master baseline | Implemented in code, not yet productized | Contracts, application, storage, and F# domain anchors exist |
| Governance product surfaces | Planned | Trial balance, multi-ledger, cash-flow, reconciliation, investor reporting, and governed reporting are blueprint-backed but not fully implemented |
| Monitoring and observability | Implemented | Prometheus and OpenTelemetry foundations are in place |
| Provider confidence | Mixed | Alpaca is solid; Polygon, StockSharp, IB, and NYSE still need setup or hardening work |
| Improvement tracking | Core baseline complete | 35/35 core items are complete; current focus has moved to workstation and governance expansion |

## Current Strengths

- Mature ingestion, replay, storage, and export foundations
- Shared composition and host startup patterns
- **Brokerage gateway framework** with Alpaca, IB, and StockSharp adapters ready for cockpit integration
- Backtesting engine with tick replay, fill models, and QuantConnect Lean integration
- Direct lending module with PostgreSQL persistence, workflows, and API endpoints
- Portfolio and ledger concepts already present in the codebase (double-entry accounting, F# ledger, trading state machines)
- Security Master foundations already present in contracts, storage, application, and F# domain modules
- Existing export infrastructure that can support future report-pack generation
- WPF desktop application included in solution build (`src/Meridian.Wpf/`; builds full WPF app on Windows, CI stub on Linux/macOS)
- Comprehensive test coverage (~4,756 tests across 7 test projects)

## Current Gaps

### Workstation productization

Meridian still exposes too much capability through page-first flows instead of operator workflows. The Research, Trading, Data Operations, and Governance taxonomy is now established, but richer cockpit shells and broader shared-run coverage remain to be implemented.

### Governance and fund operations

The target governance capability set is now defined, but still mostly pending implementation:

- Security Master product surfaces
- account, entity, and strategy-structure management
- multi-ledger tracking
- consolidated and per-ledger trial balance
- cash-flow modeling
- reconciliation engine
- report generation tools and report packs
- investor reporting and stakeholder-ready outputs

### Provider readiness

Some providers remain conditionally operator-ready:

- **Polygon**: broader replay/live validation still needed
- **Interactive Brokers**: requires `IBAPI` plus the official vendor surface for real runtime use
- **StockSharp**: depends on connector-specific setup and validated adapter coverage
- **NYSE**: requires external credentials and setup

## Blueprint References

The current planning set is synchronized around these documents:

- [ROADMAP.md](ROADMAP.md)
- [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md)
- [IMPROVEMENTS.md](IMPROVEMENTS.md)
- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)

## Pre-Production Checklist

- [ ] Configure real provider credentials and validate operator startup paths
- [ ] Complete remaining provider-confidence hardening for Polygon, StockSharp, IB, and optional NYSE
- [ ] Validate brokerage gateway adapters (Alpaca, IB, StockSharp) against live vendor surfaces
- [ ] Build paper-trading cockpit in web dashboard wired to brokerage gateways
- [ ] Finish workspace-first trading workstation flows beyond the first shared run baseline
- [ ] Productize Security Master for workstation use
- [ ] Implement multi-ledger, trial-balance, and cash-flow governance views
- [ ] Implement reconciliation workflows and break-review UX
- [ ] Implement report generation and governed export/report-pack flows
- [ ] Validate end-to-end observability and operator diagnostics against the final product surfaces
