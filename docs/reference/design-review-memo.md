# Design Review Memo (Institutional Sign-Off)

## Document Control
- **System:** Meridian — Integrated Trading Platform
- **Scope:** Architecture, controls, operational posture, and readiness for controlled production use
- **Audience:** Engineering leadership, risk/compliance stakeholders, and institutional reviewers
- **Version:** 3.0
- **Last Updated:** 2026-03-19

---

## 1. Executive Summary

**Meridian** is a high-performance, event-driven trading platform built on .NET 9.0 / C# 13 / F# 8.0. It provides:

1. **📡 Data Collection** — Real-time streaming (90+ sources via Interactive Brokers, Alpaca, NYSE Direct, Polygon, StockSharp, and others) with historical backfill (10+ providers) and symbol search (5 providers). All data flows through a unified event pipeline with integrity validation, sequence monitoring, and data quality SLA enforcement.

2. **🔬 Backtesting** — Tick-by-tick strategy replay engine with fill models (market, limit, weighted-average), full performance attribution (Sharpe ratio, maximum drawdown, XIRR), and complete audit trail for reproducible analysis.

3. **⚡ Real-Time Execution** — Paper trading gateway for zero-risk strategy validation in live market conditions. Designed for seamless integration with live order execution.

4. **🗂️ Portfolio Tracking** — Multi-run performance analytics, strategy lifecycle management, and comparative performance metrics across backtests and live/paper trading runs.

The architecture emphasizes determinism, auditability, controlled change, and operational safety. It can run in both **live** (provider-connected) and **offline** (replay/backtest) modes with identical orchestration logic.

The system is suitable for controlled production deployment (data collection + backtesting + paper trading) provided the operational controls in this memo are followed, and the remaining hardening items are tracked and implemented.

---

## 2. Objectives and Non-Objectives

### Objectives
- **Data Collection:** Capture trades, BBO quotes, and L2 depth with integrity checks, sequence validation, and data quality monitoring across 90+ market data sources.
- **Backtesting:** Enable tick-by-tick strategy replay with accurate fill models, attribution, and reproducible performance metrics for strategy evaluation.
- **Paper Trading:** Provide zero-risk strategy validation in real-time market conditions to bridge the gap between backtest and live execution.
- **Portfolio Tracking:** Track strategy performance across multiple runs (backtest, paper, live) with unified metrics and comparative analytics.
- **Data Governance:** Persist all data in audit-friendly formats (JSONL + Parquet) with stable, versioned schemas and integrity metadata.
- **Architecture Clarity:** Maintain strict separation between vendor integration (provider-specific code), domain logic, and application orchestration.
- **Operational Safety:** Provide runtime visibility (status dashboards), hot configuration reload, and predictable error handling.

### Non-Objectives (current phase)
- Live order execution / risk management (paper trading only; live integration requires compliance approval).
- Guaranteed zero loss under all circumstances (bounded channels may drop under extreme load by design).
- Exchange-certified market data reconstruction (feed limitations and fallback chains acknowledged).
- Real-time portfolio optimization or algorithmic order routing.

---

## 3. Architectural Summary

### Core Layers

**Data Collection Pipeline**
- **Infrastructure:** Provider connectivity, callback handling, contract creation. Contains all provider-specific code via `IMarketDataClient` implementations (90+ sources: Interactive Brokers, Alpaca, Polygon, NYSE Direct, StockSharp, etc.). Historical backfill via `IHistoricalDataProvider` with automatic fallback chains (10+ providers). Symbol search via `ISymbolSearchProvider` (5 providers).
- **Domain:** Order book state (`MarketDepthCollector`), trade analytics (`TradeDataCollector`), BBO caching (`QuoteCollector`), integrity validation. Pure logic, testable without providers. All events emit to `EventPipeline`.
- **Application:** Orchestration, config hot-reload (`ConfigWatcher`), status monitoring (`StatusWriter`, `Metrics`), data quality SLA enforcement.
- **Storage:** Bounded channel routing (`EventPipeline`) with backpressure, buffered persistence (`JsonlStorageSink`, `ParquetStorageSink`), write-ahead log (WAL) for durability.

**Backtesting & Execution Subsystems**
- **Backtesting Engine** (`Meridian.Backtesting`): Tick-by-tick replay from historical data with multiple fill models (market, limit, weighted-average). Computes performance metrics (Sharpe, drawdown, XIRR, return attribution) and generates audit trails.
- **Paper Trading Gateway** (`Meridian.Execution`): Real-time order routing for paper trading, synchronized with live data feeds. Implements order lifecycle (pending → filled/rejected) and position tracking.
- **Strategy Framework** (`Meridian.Backtesting.Sdk`): SDK for strategy implementation with standardized interfaces for signal generation, order placement, and lifecycle hooks.
- **Portfolio Tracker** (`Meridian.Strategies`): Multi-run performance analytics, strategy registration and lifecycle management, comparative metrics across backtest/paper/live runs.

**User Interfaces**
- **Web Dashboard:** Browser-based UI for strategy management, backtest results, paper trading monitoring, and data export.
- **WPF Desktop App:** Windows-only rich client for advanced operations, data visualization, and configuration (Windows only).
- **Status Endpoints:** JSON API for programmatic integration and monitoring.

### Key Control: Unified Event Stream
All market data outputs normalize to `MarketEvent(Type, Symbol, Timestamp, Payload)` with typed payload records (Trade, L2Snapshot, BboQuote, OrderFlow, IntegrityEvent). This provides:
- Stable contract for storage and replay
- Consistent sequencing metadata across all providers (StreamId, Venue, SequenceNumber)
- Support for deterministic backtesting and paper trading parity
- Clear audit trail for all data transformations

---

## 4. Operational Safety and Controls

### 4.1 Data Collection Integrity
- **Trade Sequence Validation:** `TradeDataCollector` validates sequence continuity per symbol/stream. Emits `IntegrityEvent.OutOfOrder` (trade rejected) or `IntegrityEvent.SequenceGap` (trade accepted, stats marked stale) when anomalies occur.
- **Depth Management:** `MarketDepthCollector` emits `DepthIntegrityEvent` on invalid operations/gaps and freezes the symbol stream until `ResetSymbolStream()` is called.
- **Quote State:** `QuoteCollector` maintains per-symbol BBO state with monotonically increasing sequence numbers. The `IQuoteStateStore` interface enables `TradeDataCollector` to infer aggressor side and validate trade liquidity.
- **Data Quality SLA Enforcement:** Monitors feed uptime, stale data, latency, and feed divergence. Emits alerts when thresholds are exceeded. Fallback chain automatically switches to alternative providers on feed failure.

### 4.2 Backtest & Paper Trading Safety
- **Determinism:** All events are timestamped and sequenced at the market data layer, ensuring identical replay across multiple backtest runs.
- **Fill Models:** Backtesting supports market, limit, and weighted-average fills with configurable slippage assumptions. Paper trading uses simulated fills based on live bid/ask.
- **Position Tracking:** Paper trading gateway maintains real-time position state synchronized with order events. Live and backtest positions reconcile to the same base.
- **Audit Trail:** All orders, fills, and portfolio snapshots are persisted with timestamps and source attribution (backtest run ID, strategy version, paper trading session).

### 4.3 Backpressure and Bounded Queues
The `EventPipeline` uses `System.Threading.Channels` with bounded capacity (50,000 for high-throughput policy, 100,000 for default policy) and `DropOldest` strategy. Under sustained high load:
- events may be dropped to protect process stability and prevent memory exhaustion
- drops are counted via `Metrics.Dropped` and visible in status dashboards
- integrity events are emitted on drop detection

This is an explicit design tradeoff: stability and bounded resource usage over guaranteed zero loss.

### 4.4 Configuration Management (Hot Reload)
Configuration changes are applied via:
- atomic file replace from UI
- debounced, retried parsing by `ConfigWatcher`
- diff application via `SubscriptionManager` (symbol subscriptions, provider settings, SLA thresholds)

This reduces restart risk and prevents partial-write corruption. Changes apply within seconds without stopping data collection.

### 4.5 Strategy Isolation
- **Paper Trading:** Runs in isolated order gateway; cannot affect live provider connections or historical data.
- **Backtesting:** Runs on historical snapshots; does not interact with live feeds.
- **Configuration:** Strategy settings isolated per run (backtest run ID, paper session) to prevent cross-contamination.

---

## 5. Data Governance

### Market Data Storage (Collection Phase)
- Append-only JSONL files partitioned by `<Symbol>.<EventType>.jsonl` (e.g., `AAPL.Trade.jsonl`, `SPY.BboQuote.jsonl`).
- Optional gzip compression via `Compress` config option.
- Parquet export available for analytics workflows and QuantConnect Lean integration.
- Status snapshots written separately under `data/_status/status.json` by `StatusWriter`.
- Write-ahead log (WAL) ensures durability; partial failures do not corrupt stored data.

### Backtest & Execution Data
- **Backtest Runs:** Persisted with run metadata (strategy name, symbol list, date range, fill model), order history, position snapshots, and performance metrics.
- **Paper Trading Sessions:** Tracked separately from live data; linked to strategy version and session timestamp.
- **Portfolio Snapshots:** Point-in-time snapshots of positions, cash, and performance metrics. Support multi-run comparison and performance attribution.

### Schema Management
- `MarketEventType` is the canonical type registry: `Trade`, `L2Snapshot`, `BboQuote`, `OrderFlow`, `Integrity`, `DepthIntegrity`.
- Payloads are typed records (C# records) with versioning support for backward compatibility.
- Quote and trade events include `SequenceNumber`, `StreamId`, and `Venue` fields to support reconciliation, feed divergence detection, and deterministic replay.
- Order and fill events include `OrderId`, `Timestamp`, `Status`, and `FillPrice` for audit trail.

**Versioning Policy:** Breaking changes to payload records trigger a new event type version. Old event types remain readable for historical data. Migration scripts handle schema evolution.

---

## 6. Security & Access Considerations

### Data Storage
- Local file output: ensure OS-level permissions restrict access to `data/` and backtest result directories.
- Ensure secure file deletion (no unencrypted copies left in swap/temp).
- Consider encryption at rest for sensitive data (positions, fill prices, strategy parameters).

### User Interfaces
- **Web Dashboard:** Intended for internal/local use. If network-exposed:
  - Add authentication (bearer token, OAuth, mTLS)
  - Restrict network binding to trusted networks
  - Add CSRF protections
  - Implement rate limiting for API endpoints
  - Log all administrative actions
- **WPF Desktop:** Windows-only; relies on OS-level authentication. Accessible only to logged-in user.

### Credentials Management
- **Provider API keys:** Stored in `appsettings.json` (development only). For production:
  - Use environment variables or secret vault (AWS Secrets Manager, Azure KeyVault, HashiCorp Vault)
  - Rotate credentials on a schedule
  - Audit credential access
- **Strategy Parameters:** Do not hardcode trading logic or thresholds; use configuration. Separate strategy parameters from system configuration.

### Strategy Code
- Backtesting SDK enforces strict boundaries: strategies cannot access provider credentials or bypass order gateway.
- Strategy code is versioned and linked to backtest runs for auditability.
- Consider code signing and tamper detection for production strategy deployments.

---

## 7. Deployment Guidance

### Recommended Topology
- Dedicated host / VM with stable time sync (NTP, <1ms skew)
- Local SSD with sufficient throughput (backtest I/O intensive) and monitoring
- Minimum 50 GB free disk (configurable); automatic archival/rotation policy recommended
- Dedicated network for provider connectivity (latency-sensitive)
- Separate data directories for live/backtest/paper trading data

### Deployment Phases
1. **Phase 1: Data Collection Validation**
   - Deploy in offline mode (no provider connections)
   - Validate schema, storage, and status endpoints
   - Verify network connectivity and DNS resolution

2. **Phase 2: Live Data Collection (Read-Only)**
   - Connect to single low-impact data source (e.g., Alpaca)
   - Monitor data quality SLAs, feed uptime, and storage growth
   - Validate integrity events and fallback behavior
   - Run 2–4 weeks before escalating

3. **Phase 3: Backtesting**
   - Load historical data via backfill providers
   - Run sample strategy backtests; validate fill models and attribution
   - Export results to analytics tools (Parquet)

4. **Phase 4: Paper Trading**
   - Deploy paper trading gateway in parallel with data collection
   - Run paper trading with sample strategies
   - Validate order lifecycle, position tracking, and performance metrics
   - Establish monitoring and alerting baselines

5. **Phase 5: Multi-Provider Live Collection** (if needed)
   - Gradually add additional data providers
   - Monitor feed divergence and fallback activation
   - Adjust SLA thresholds based on observed behavior

### Operational Prerequisites
- Market data entitlements or API credentials (set as environment variables)
- Stable network connectivity and DNS resolution
- NTP for time synchronization (critical for backtest/paper parity)
- Log retention and disk space monitoring
- Preflight checks:
  - Disk space and throughput validation
  - Directory permissions and write access
  - Provider API connectivity and rate limits
  - System clock synchronization (NTP)
  - Process isolation (dedicated user account recommended)

---

## 8. Risks and Mitigations

| Risk | Description | Mitigation |
|------|-------------|------------|
| **Data Collection** | | |
| Data gaps | Feed interruptions or missed updates | Integrity events + SLA enforcement + automatic fallback chains |
| Event drops | Bounded queues may drop under sustained load | `Metrics.Dropped` visible in status + channel capacity tuning + load monitoring |
| Trade sequence anomalies | Gaps or out-of-order sequences | `IntegrityEvent` emission; out-of-order trades rejected + sequence log |
| Feed divergence | Multiple provider quotes may diverge | Preserve `StreamId`/`Venue` + feed divergence alarms + fallback to primary source |
| Preferred contract ambiguity | IB preferred shares can resolve incorrectly | Require `LocalSymbol`/`ConId` in config; validate contracts at startup |
| **Backtesting** | | |
| Fill model inaccuracy | Simulated fills may not match live execution | Use conservative slippage assumptions + validate against paper trading results + document fill model limitations |
| Data replay gaps | Backtesting on incomplete historical data | Validate data completeness before backtest runs + emit warnings on gaps |
| Strategy overfitting | Strategies optimized to historical data | Use out-of-sample validation + walk-forward analysis + sensitivity analysis (documented in backtest SDK) |
| **Paper Trading** | | |
| Order rejection | Paper trading orders rejected (e.g., due to insufficient cash) | Validate cash position before order placement + emit warnings + include in audit trail |
| Slippage mismatch | Paper fill prices differ from backtest assumptions | Monitor paper trading fills vs. simulated fills + adjust fill models based on observed slippage |
| **Portfolio Tracking** | | |
| Multi-run reconciliation | Position and performance metrics diverge across runs | Reconcile via order history + fill history + maintain linkage to data snapshot (date/time) for each run |
| **System-Wide** | | |
| UI exposure | Dashboards exposed without authentication | Keep local-only by default or add auth if network-exposed |
| Credential leakage | API keys/secrets in logs or config files | Enforce environment variables + audit logging + avoid logging sensitive data |
| Time synchronization | Clock skew breaks backtest/paper parity | Enforce NTP with <1ms tolerance + emit warnings on sync failures |

---

## 9. Recommended Next Hardening Items

### High Priority (Pre-Live Execution)
1. **Compliance Review** — Ensure paper trading gateway and data handling comply with regulatory requirements for your deployment context.
2. **Authentication & Access Control** — Add user authentication and role-based access control for dashboard and strategy management (if multi-user).
3. **Order Execution Integration** — Implement live order execution gateway with risk controls (position limits, order size limits, kill switches).
4. **Strategy Code Review Framework** — Establish process for code review and approval before deploying strategies to paper/live trading.

### Medium Priority (Production Readiness)
5. Add comprehensive unit/integration test automation in CI (currently ~4,135 tests; aim for >90% code coverage).
6. Add feed-divergence alarms when provider quotes deviate beyond configured tolerances (SLA-driven).
7. Implement credential rotation policies for provider API keys.
8. Add performance profiling and capacity planning guidance (backtest throughput, peak concurrent collections).
9. Document strategy SDK limitations and best practices (e.g., no external I/O in signal generation).

### Lower Priority (Operations Excellence)
10. Implement persistent audit logging for all trades, orders, and configuration changes.
11. Add observability: Prometheus metrics scraping, distributed tracing (OpenTelemetry), centralized log aggregation.
12. Develop runbooks and playbooks for common incidents (feed outage, paper trading reconciliation failures, backtest performance issues).

### Recently Completed

**Data Collection**
- ✅ Real-time streaming (90+ sources via Interactive Brokers, Alpaca, NYSE, Polygon, StockSharp)
- ✅ Historical data backfill with multiple providers (Alpaca, Yahoo, Stooq, Nasdaq Data Link, and 6+ more)
- ✅ Symbol search across 5 providers (Alpaca, Finnhub, Polygon, OpenFIGI, StockSharp)
- ✅ Data quality monitoring with SLA enforcement
- ✅ Write-ahead log (WAL) for durability
- ✅ Parquet export/storage format for analytics workflows

**Backtesting**
- ✅ Tick-by-tick strategy replay engine
- ✅ Multiple fill models (market, limit, weighted-average)
- ✅ Performance metrics (Sharpe ratio, drawdown, XIRR, return attribution)
- ✅ Backtest SDK for strategy development

**Paper Trading & Execution**
- ✅ Paper trading gateway for zero-risk strategy validation
- ✅ Order lifecycle management (pending → filled/rejected)
- ✅ Position tracking and reconciliation

**Portfolio & Strategy Management**
- ✅ Strategy lifecycle management and registration
- ✅ Multi-run performance analytics
- ✅ Comparative metrics across backtest/paper/live runs

**Infrastructure & Observability**
- ✅ QuantConnect Lean integration for analytics
- ✅ Prometheus metrics endpoint and monitoring services
- ✅ Polly-based WebSocket resilience policies
- ✅ FluentValidation-based configuration validation
- ✅ Web dashboard and WPF desktop app (Windows)
- ✅ Configuration hot-reload without restart
- ✅ Provider fallback chains and health monitoring

---

## 10. Sign-Off Recommendation

### Current Status: **Development / Pilot Ready**

**Meridian is recommended for controlled production use in the following phases:**

#### Phase 1: Data Collection (Low Risk) ✅ **Approved for Production**
- Real-time streaming from trusted providers (Alpaca, Interactive Brokers)
- Historical backfill for backtesting
- Data storage with integrity validation
- Status monitoring and alerting

**Prerequisites:** Entitlement validation, disk setup, credential management via environment variables.

#### Phase 2: Backtesting & Analysis (Low Risk) ✅ **Approved for Production**
- Tick-by-tick strategy replay on historical data
- Performance metrics and attribution
- Results export for analytics

**Prerequisites:** Data validation, fill model documentation, out-of-sample validation procedures.

#### Phase 3: Paper Trading (Medium Risk) ⚠️ **Conditional Approval**
- Paper trading gateway for strategy validation in live conditions
- Order lifecycle management and position tracking
- Parity testing vs. backtest results

**Prerequisites before approval:**
- Compliance review (regulatory approval if applicable)
- Order gateway code review and security audit
- Risk control implementation (position limits, kill switches)
- Monitoring and alerting setup
- Incident response procedures

#### Phase 4: Live Execution (High Risk) ❌ **Not Approved**
- Requires comprehensive compliance review
- Regulatory approval and licensing (if applicable)
- Live order execution gateway with risk controls
- Business continuity and disaster recovery procedures
- Separate sign-off process

**Subject to:**
- Adherence to deployment guidance and runbook procedures
- Entitlement validation and secure credential management
- Disk monitoring and scheduled archival
- Completion of high-priority hardening items (compliance review, authentication, execution integration)
- Regular audits and security reviews
- Post-deployment monitoring and incident tracking
