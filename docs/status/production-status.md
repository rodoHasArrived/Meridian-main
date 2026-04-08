# Meridian - Production Status

**Version:** 1.7.2
**Last Updated:** 2026-04-07
**Status:** Development / Pilot Ready (workstation and governance productization active)

This document summarizes the current production-readiness posture and the next product-delivery gaps from the current repository state.

## Executive Summary

Meridian already has working ingestion, storage, replay, backtesting, provider orchestration, export tooling, and a web dashboard. The current product gap is not the absence of core building blocks; it is the remaining work required to unify those blocks into a coherent operator-facing fund-management product spanning front, middle, and back office workflows.

The active plan now has two connected delivery tracks:

1. **Front-office workstation delivery** across Research, Trading, Data Operations, and Governance workspaces using shared run, portfolio, and ledger models.
2. **Middle- and back-office fund-operations delivery** building on delivered Security Master productization with account/entity support, multi-ledger views, trial balance, cash-flow modeling, reconciliation, trade-management support, and investor/report-pack generation.

### Overall Assessment: **DEVELOPMENT / PILOT READY**

| Category | Status | Notes |
|----------|--------|-------|
| Core event pipeline | Implemented | Channel-based processing with backpressure, metrics, validation, and storage fan-out |
| Storage layer | Implemented | JSONL/Parquet composite sink with WAL, catalog, packaging, and export support |
| Backfill providers | Implemented | 10+ price providers with fallback chain plus FRED economic-series support; some require credentials |
| Backtesting engine | Implemented | Tick-by-tick replay with fill models, portfolio metrics, and Lean integration |
| Paper trading gateway | Implemented | Risk rules (position limits, drawdown stops, order rate throttle), position and fill tracking |
| **Brokerage gateway framework** | **Implemented** | `BaseBrokerageGateway` + Alpaca, Robinhood, IB, StockSharp adapters; live-validated runtime paths pending |
| Direct lending module | Implemented | PostgreSQL-backed services, workflows, and `/api/loans/*` endpoints |
| CppTrader integration | Implemented | Host management, order gateway, ITCH ingestion, replay service |
| WPF desktop shell | Active | Fluent theme, SVG icons, candlestick charting, and zero-API-key startup landed (PRs #512, #513, #522, #524); MVVM extraction and high-traffic page redesign ongoing |
| Shared run / portfolio / ledger model | In progress | Shared run, portfolio, ledger, and Security Master coverage baselines are in code; broader paper/live, cash-flow, and multi-ledger coverage remains |
| Security Master platform seam | Implemented and productized | WPF, Research, Trading, Portfolio, Ledger, Reconciliation, and Governance now share one authoritative Security Master coverage/provenance contract and drill-in flow |
| Governance product surfaces | Partial | Security coverage, reconciliation drill-ins, and cash-flow/reporting summaries are live; trial balance, multi-ledger, investor reporting, and governed report-pack flows remain incomplete |
| Monitoring and observability | Implemented | Prometheus and OpenTelemetry foundations are in place |
| Provider confidence | Mixed | Evidence-backed matrix now tracked in `provider-validation-matrix.md` with per-scenario pass/fail status and links |
| Improvement tracking | Core baseline complete | 35/35 core items are complete; current focus has moved to workstation and governance expansion |

## Current Strengths

- Mature ingestion, replay, storage, and export foundations
- Shared composition and host startup patterns
- **Brokerage gateway framework** with Alpaca, Robinhood, IB, and StockSharp adapters ready for cockpit integration
- Broader provider surface already includes Robinhood live quotes/brokerage, FRED economic-series backfill, and EDGAR symbol/security-master support providers beyond the core execution-gated set
- Backtesting engine with tick replay, fill models, and QuantConnect Lean integration
- Direct lending module with PostgreSQL persistence, workflows, and API endpoints
- Portfolio and ledger concepts already present in the codebase (double-entry accounting, F# ledger, trading state machines)
- Security Master now acts as the shared instrument-definition seam across WPF, Research, Trading, Portfolio, Ledger, Reconciliation, and Governance surfaces
- Existing export infrastructure that can support future report-pack generation
- WPF desktop application with modernized shell: native Fluent theme, SVG icon set, LiveCharts2 candlestick charting, and zero-API-key startup via Synthetic provider default
- Workflow guide (`docs/WORKFLOW_GUIDE.md`) with live screenshots; CI screenshot-refresh workflow for ongoing visual validation
- Comprehensive test coverage (~4,756 tests across 8 test projects)

## Current Gaps

### Workstation productization

Meridian still exposes too much capability through page-first flows instead of operator workflows. The Research, Trading, Data Operations, and Governance taxonomy is now established, but the existing cockpit and shared-run surfaces still need hardening, deeper continuity, and clearer operator acceptance criteria.

### Governance and fund operations

The target governance capability set is now defined, with Security Master coverage/reconciliation surfaces now live and the broader governance set still pending:

- account, entity, and strategy-structure management
- multi-ledger tracking
- consolidated and per-ledger trial balance
- cash-flow modeling
- reconciliation engine
- report generation tools and report packs
- investor reporting and stakeholder-ready outputs

### Provider readiness

Some providers remain conditionally operator-ready:

- **Polygon**: replay fixture and parser coverage is passing in-repo, but live reconnect/rate-limit runtime proof is still partial ([pass evidence](../../tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs), [partial gap evidence](provider-validation-matrix.md)).
- **Robinhood**: brokerage submit-path, live quote polling, historical bars, and symbol search are implemented with stable-seam execution evidence, but reconnect/rate-limit runtime proof is still partial and the adapter depends on the unofficial Robinhood API ([pass evidence](../../tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs), [provider tests](../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodBrokerageGatewayTests.cs)).
- **Interactive Brokers**: non-`IBAPI` guidance and smoke-build checks pass, but full live runtime path remains partially validated ([pass evidence](../../tests/Meridian.Tests/Infrastructure/Providers/IBRuntimeGuidanceTests.cs), [prerequisites](../providers/interactive-brokers-setup.md)).
- **StockSharp**: connector capability and subscription-guidance tests pass, with runtime connector validation still partial ([pass evidence](../../tests/Meridian.Tests/Infrastructure/Providers/StockSharpSubscriptionTests.cs), [runbook](../providers/stocksharp-connectors.md)).
- **NYSE**: reconnect and parser lifecycle tests pass; auth/rate-limit explicit evidence still pending ([pass evidence](../../tests/Meridian.Tests/Infrastructure/Providers/NyseMarketDataClientTests.cs), [open checks](provider-validation-matrix.md)).

Lower-risk non-execution provider surfaces such as FRED economic-series backfill and EDGAR symbol/security-master ingestion are implemented and inventoried, but they are not part of the execution-oriented readiness matrix.

## Provider Evidence Links (Pass/Fail)

| Provider | Scenario | Status | Evidence |
|---|---|---|---|
| Polygon | Replay fixtures (trades/quotes/aggregates/status) | ✅ Pass | `PolygonRecordedSessionReplayTests`, `PolygonMessageParsingTests`, fixtures in `tests/.../Fixtures/Polygon` |
| Polygon | Reconnect + live rate-limit runtime proof | ⚠️ Partial | `ProviderResilienceTests` baseline + outstanding live validation in matrix |
| Robinhood | Stable execution seam + brokerage submit path | ✅ Pass | `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam`, `RobinhoodBrokerageGatewayTests` |
| Robinhood | Quote polling / reconnect / rate-limit runtime proof | ⚠️ Partial | `RobinhoodMarketDataClientTests`, `RobinhoodHistoricalDataProviderTests`, outstanding live-session validation in matrix |
| IB | Non-IBAPI runtime guidance + smoke compile path | ✅ Pass | `IBRuntimeGuidanceTests`, `scripts/dev/build-ibapi-smoke.ps1` |
| IB | Real vendor runtime (`IBAPI` + TWS/Gateway) | ⚠️ Partial | prerequisites/checklist in `docs/providers/interactive-brokers-setup.md` |
| StockSharp | Subscription guidance + conversion contracts | ✅ Pass | `StockSharpSubscriptionTests`, `StockSharpMessageConversionTests`, `StockSharpConnectorFactoryTests` |
| StockSharp | End-to-end connector runtime profile | ⚠️ Partial | validated baseline + runbook in `docs/providers/stocksharp-connectors.md` |
| NYSE | Reconnect lifecycle behavior | ✅ Pass | `NyseMarketDataClientTests` |
| NYSE | Auth failure and rate-limit behavior | ❌ Fail (evidence missing) | tracked in `provider-validation-matrix.md` |

## Blueprint References

The current planning set is synchronized around these documents:

- [ROADMAP.md](ROADMAP.md)
- [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md)
- [IMPROVEMENTS.md](IMPROVEMENTS.md)
- [Provider Validation Matrix](provider-validation-matrix.md)
- [Provider Reliability and Data Confidence Wave 1 Blueprint](../plans/provider-reliability-data-confidence-wave-1-blueprint.md)
- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)

## Pre-Production Checklist

- [ ] Configure real provider credentials and validate operator startup paths
- [ ] Complete Wave 1 provider-confidence hardening for Polygon replay, IB runtime/bootstrap, NYSE lifecycle/transport, StockSharp validated adapters, backfill checkpoints/gap detection, and Parquet L2 persistence
- [ ] Validate brokerage gateway adapters (Alpaca, Robinhood, IB, StockSharp) against live vendor surfaces
- [ ] Harden and validate the existing paper-trading cockpit in the web dashboard across positions, orders, fills, replay, sessions, and promotion flows
- [ ] Finish workspace-first trading workstation flows beyond the first shared run baseline
- [ ] Extend governance report-pack, cash-flow, and multi-ledger workflows beyond the delivered Security Master/reconciliation baseline
- [ ] Implement multi-ledger, trial-balance, and cash-flow governance views
- [ ] Implement reconciliation workflows and break-review UX
- [ ] Implement report generation and governed export/report-pack flows
- [ ] Validate end-to-end observability and operator diagnostics against the final product surfaces
- [ ] Implement Phase 1.5 preferred/convertible equity domain types (F# — `EquityClassification`, `PreferredTerms`, `ConvertibleTerms` in `SecurityMaster.fs`)
