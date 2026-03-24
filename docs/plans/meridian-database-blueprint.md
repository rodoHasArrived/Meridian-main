# Meridian Database Blueprint

**Owner:** Core Team
**Audience:** Platform, storage, workstation, strategy, governance, and API contributors
**Last Updated:** 2026-03-24
**Status:** Proposed blueprint grounded in current repository architecture

## Scope

**In Scope:** A database blueprint that supports Meridian's intended end-state across `Research`, `Trading`, `Data Operations`, and `Governance`, while fitting the current archival-first storage architecture and existing workstation/security-master code.

**Out of Scope:** Replacing the existing WAL + JSONL + Parquet event archive, introducing cloud-only data services, or designing a pure timeseries database as the sole storage model for market events.

**Assumptions:**

- Meridian remains a hybrid platform:
  - market-event truth stays in the existing storage stack
  - relational database storage is added for operational state, read models, orchestration, and audit workflows
- The roadmap target state remains the active product direction:
  - unified `StrategyRun` across backtest, paper, and live
  - first-class portfolio and ledger workflows
  - Security Master as the authoritative instrument-definition layer
  - future governance, reconciliation, QuantScript, and L3 simulation capabilities
- PostgreSQL is the preferred operational database because Meridian already has a checked-in PostgreSQL Security Master store under `src/Meridian.Storage/SecurityMaster/`.

**Depth Mode:** full

## Architectural Overview

### Context Diagram

```mermaid
flowchart LR
    Providers["Providers / Backfill / Replay"] --> Pipeline["Event Pipeline"]
    Pipeline --> Archive["WAL + JSONL + Parquet Archive"]
    Pipeline --> Projections["Projection / Aggregation Services"]
    Projections --> Db["PostgreSQL Operational Database"]
    Archive --> ReadJobs["Replay / ETL / Derived Metric Jobs"]
    ReadJobs --> Db
    Db --> Api["HTTP + SSE + Workstation APIs"]
    Db --> Wpf["WPF Workstation"]
    Db --> Web["Web Dashboard"]
    Db --> Reports["Governance / Export / Report Packs"]
```

### Design Decisions

This revision makes the storage topology explicit and decision-driven. It keeps PostgreSQL as the operational system of record, while narrowing scope, enforcing boundaries, and reserving specialized stores for specialized workloads.

#### Decision 1: Topology and boundaries

**Recommended option:** One PostgreSQL database initially (`meridian`) with module-per-schema ownership and API-only cross-domain reads.

**What this means:**
- keep a single operational database for migration/backups/ops simplicity
- assign write roles per domain schema (for example: `ref_rw`, `strategy_rw`, `ops_rw`)
- deny cross-schema direct `SELECT` by default
- expose only curated cross-domain read APIs (versioned `read.*` views/functions or application query services)

**Why this is the default now:** it preserves single-DB operational simplicity while giving enforceable boundaries instead of convention-only boundaries.

**Why not the alternatives (now):**

| Alternative | Why not chosen as default |
|---|---|
| One DB with convention-only schema boundaries | Too easy for accidental cross-schema coupling; weak long-term governance. |
| Two DBs (`meridian_ref` + `meridian_ops`) from day one | Strong model, but adds immediate operational and query complexity before required by current scale. |

**Trigger to revisit:** promote to two-DB split when Security Master backup/restore SLAs and governance controls require independent RPO/RTO and change cadence.

#### Decision 2: Operational store composition

**Recommended option:** PostgreSQL as source of truth + Redis-style cache for browse UX (optional but first-class).

**What this means:**
- Postgres remains authoritative for correctness, transactional workflows, and queryability
- projection workers can publish browse-oriented rows (run list, recent activity, break queues) into cache keys/sets
- workstation screens use cache-first + Postgres fallback for low-latency list/detail navigation

**Why this is the default now:** it avoids forcing `read.*` to absorb all latency-sensitive UI pressure while keeping correctness in Postgres.

**Why not the alternatives (now):**

| Alternative | Why not chosen as default |
|---|---|
| Postgres-only for all browse/query UX | Risks over-expanding `read.*` and hurts workstation responsiveness under load. |
| Postgres + OLAP immediately | Valuable for heavy analytics, but premature until cross-run/time-range analytics become dominant. |

**Trigger to revisit:** add OLAP when run comparisons/attribution/performance series exceed acceptable latency or produce sustained wide-scan pressure on Postgres.

#### Decision 3: Read model construction

**Recommended option:** CQRS projection tables as the primary read-model mechanism; materialized views allowed only for safe aggregates.

**What this means:**
- projection workers own explicit `read.*` tables and incremental updates
- each projection records watermark/checkpoint, projection version, and updated timestamp
- matviews are limited to non-critical, bounded-cost aggregates with predictable refresh behavior

**Why this is the default now:** projection tables provide deterministic, incremental, resumable behavior aligned with checkpointing.

**Why not the alternatives (now):**

| Alternative | Why not chosen as default |
|---|---|
| Materialized-view-first read architecture | Can drift into full-refresh operational pain (locks, long refresh windows, planner surprises). |
| Fully normalized OLTP-only query model | Misses workstation/API browse performance needs and increases app query complexity. |

#### Decision 4: Event history and audit strategy

**Recommended option:** Keep full-fidelity domain event history in archival storage; keep Postgres journals narrowly focused on operational/audit needs.

**What this means:**
- Postgres stores current state + minimal append audit trails needed for operator trust and traceability
- WAL/JSONL/Parquet remains the long-horizon replay and portability source of truth for full event detail
- outbox pattern remains available per domain when downstream integration/replay requirements justify it

**Why this is the default now:** prevents Postgres from becoming a second event lake while preserving auditability for workflows.

**Why not the alternatives (now):**

| Alternative | Why not chosen as default |
|---|---|
| Full immutable event store in Postgres for every domain immediately | Strong replay model but higher up-front complexity and storage growth before all domains need it. |
| Mutable status rows with minimal history | Insufficient for governance, reconciliation, and operator forensic workflows. |

#### Decision 5: Partitioning and time-series extensions

**Recommended option:** Partition only high-churn append/event tables; keep entity tables unpartitioned by default.

**What this means:**
- candidate partitioned tables: `trading.order_events`, `trading.executions`, `ops.health_snapshots`, `audit.action_log`
- keep entity/state tables (`strategy.strategy_runs`, `strategy.strategies`, `accounting.ledger_accounts`) unpartitioned unless measured pressure demands it
- use TimescaleDB only for operational telemetry snapshots if adopted, not for core financial correctness tables

**Why this is the default now:** minimizes schema/migration complexity early while preserving clear scaling levers.

**Why not the alternatives (now):**

| Alternative | Why not chosen as default |
|---|---|
| Broad monthly partitioning across many tables from day one | Adds significant DDL/test/maintenance overhead without proven need. |
| Timescale as a core dependency across domains | Increases extension coupling for core accounting/trading workflows. |

#### Decision 6: Delivery scope for initial implementation waves

**Recommended option:** First implementation wave focuses on three schemas: `ref`, `strategy`, and `ops` (+ `read` projections).

**What this means:**
- implement immediately: security master/reference metadata, strategy run lifecycle metadata, operations/backfill/subscriptions/checkpoints, workstation read models
- delay full `accounting`, `trading`, and `research` schemas until domain invariants and lifecycle semantics stabilize

**Why this is the default now:** reduces speculative schema churn and makes execution feasible within 1–2 quarters.

**Why not the alternatives (now):**

| Alternative | Why not chosen as default |
|---|---|
| Full 9-schema rollout in first phase | High risk of rework and slow delivery due to unresolved domain details. |
| Single monolithic schema | Fast short-term start but weak boundary enforcement and high long-term coupling. |

#### Decision-driven schema checklist (required for each new schema)

Every schema proposal must document the following before implementation:

1. **Fast-path queries:** which queries must remain low latency and at what target percentile.
2. **Write profile:** expected write rate and peak burst shape.
3. **Retention profile:** retention duration, archive/export policy, and purge rules.
4. **Source of truth:** whether truth lives in archive, Postgres, or dual with reconciliation.
5. **Recomputability:** what can be rebuilt from archive/events vs what must be preserved.

This checklist is the gate that determines projection-table-vs-matview, partition-now-vs-later, and Postgres-only-vs-Postgres+specialized-store decisions.

## Verified Constraints From The Repository

- `docs/adr/002-tiered-storage-architecture.md` rejects a single database as the primary ingestion store for market data.
- `docs/architecture/storage-design.md` defines Meridian as a collection and archival system first.
- `src/Meridian.Storage/SecurityMaster/` already contains PostgreSQL-backed stores and migrations.
- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs` already defines shared run, portfolio, and ledger read models for the workstation.
- `src/Meridian.Strategies/Storage/StrategyRunStore.cs` is intentionally in-memory today and explicitly calls out future persistence.
- `docs/plans/trading-workstation-migration-blueprint.md` defines the target workflow around `Strategy`, `Run`, `Portfolio`, `Ledger`, `Dataset/Feed`, and `Workspace`.
- `docs/plans/governance-fund-ops-blueprint.md` requires Security Master, multi-ledger accounting, reconciliation, trial balance, cash-flow modeling, and report packs.

## Proposed Database Role

The database should support six categories of responsibility:

1. **Reference data**
   - Security Master
   - canonical symbol and identifier resolution
   - venue, provider, dataset, and account metadata

2. **Operational orchestration**
   - provider sessions
   - subscriptions
   - backfill jobs and schedules
   - maintenance jobs
   - promotion workflow state

3. **Strategy and trading lifecycle**
   - strategies
   - strategy versions
   - runs across backtest, paper, and live
   - orders, fills, positions, executions, and risk events

4. **Portfolio and accounting**
   - portfolio snapshots
   - holdings
   - cash balances
   - ledger books, journal entries, trial balance projections
   - reconciliation runs and breaks

5. **Analytics and workstation read models**
   - run comparisons
   - performance metrics
   - research artifacts
   - QuantScript metadata
   - governance report-pack manifests

6. **Audit and lineage**
   - user/operator actions
   - workflow promotion approvals
   - provenance links to archive files, exports, and external statements

## Database Topology

### Logical Database

- Database: `meridian`
- Engine: PostgreSQL 16+
- Access pattern:
  - application writes through repository/services
  - projection workers build workstation-facing read models
  - no direct hot-path tick ingestion into PostgreSQL

### Schema Layout

| Schema | Purpose |
|---|---|
| `ref` | Security Master, symbol identity, provider and venue reference data |
| `ops` | ingestion jobs, backfill jobs, schedules, maintenance, subscriptions, provider health snapshots |
| `strategy` | strategy definitions, versions, parameter sets, run manifests, run metrics, promotion workflow |
| `trading` | orders, fills, execution sessions, positions, risk alerts, paper/live session state |
| `portfolio` | portfolio snapshots, holdings, cash ledgers, exposures, attribution series |
| `accounting` | ledger books, journal entries, account balances, trial balance, reconciliation |
| `research` | datasets, experiments, backtest artifacts, QuantScript runs, simulation outputs |
| `audit` | action log, workflow approvals, lineage edges, report-pack provenance |
| `read` | denormalized workstation query models and materialized views |

## Interface & API Contracts

### New Storage Contracts

```csharp
namespace Meridian.Storage.Operational;

public interface IOperationalDatabase
{
    Task<T> WithConnectionAsync<T>(
        Func<NpgsqlConnection, Task<T>> callback,
        CancellationToken ct = default);
}

public interface IStrategyRunRepository
{
    Task UpsertRunAsync(StrategyRunRecord record, CancellationToken ct = default);
    Task<StrategyRunRecord?> GetRunAsync(string runId, CancellationToken ct = default);
    Task<IReadOnlyList<StrategyRunRecord>> SearchRunsAsync(
        StrategyRunQuery query,
        CancellationToken ct = default);
}

public interface IProjectionCheckpointStore
{
    Task<long?> GetCheckpointAsync(string projectionName, CancellationToken ct = default);
    Task SaveCheckpointAsync(string projectionName, long position, CancellationToken ct = default);
}
```

### Modified Interfaces

- `Meridian.Strategies.Storage.StrategyRunStore`
  - keep current in-memory implementation for tests/dev
  - add a production PostgreSQL implementation behind the same repository abstraction
- `PortfolioReadService` and `LedgerReadService`
  - continue supporting in-memory derivation from `StrategyRunEntry`
  - add database-backed query paths for cross-run, cross-session, and governance views

### Configuration Schema

```csharp
public sealed class OperationalDatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = "meridian";
    public string SecurityMasterSchema { get; set; } = "ref";
    public string StrategySchema { get; set; } = "strategy";
    public string TradingSchema { get; set; } = "trading";
    public string AccountingSchema { get; set; } = "accounting";
    public string ReadSchema { get; set; } = "read";
    public bool EnableMaterializedViews { get; set; } = true;
    public bool EnableProjectionWorkers { get; set; } = true;
}
```

## Component Design

### `PostgresStrategyRunRepository`

**Namespace:** `Meridian.Strategies.Storage`
**Type:** `sealed class`
**Lifetime:** Singleton
**Responsibilities:**

- persist `StrategyRunEntry` into normalized strategy/run tables
- maintain immutable run manifest and mutable status/summary fields
- support workstation run browser queries
- expose run metadata independently of archive files

**Dependencies:** `NpgsqlDataSource`, serialization helpers, projection checkpoint store
**Concurrency Model:** optimistic upsert by `run_id` and `row_version`
**Error Handling:** database exceptions mapped to strategy persistence errors; duplicate version writes rejected

### `OperationalProjectionWorker`

**Namespace:** `Meridian.Application.Projections`
**Type:** `BackgroundService`
**Lifetime:** Singleton
**Responsibilities:**

- consume completed runs, ledger updates, and maintenance events
- build `read.*` materialized read models
- refresh portfolio, ledger, and governance projections
- save projection checkpoints

**Dependencies:** domain repositories, `IProjectionCheckpointStore`, metrics
**Concurrency Model:** serialized per projection stream; parallel across independent projection families
**Error Handling:** retry with checkpoint safety; projection idempotency required

### `PostgresBackfillJobStore`

**Namespace:** `Meridian.Application.Backfill`
**Type:** `sealed class`
**Lifetime:** Singleton
**Responsibilities:**

- persist scheduled and ad hoc backfill jobs
- record provider attempts, quotas, and completion states
- support workstation schedule management and operational reporting

### `GovernanceReadRepository`

**Namespace:** `Meridian.Application.Governance`
**Type:** `sealed class`
**Lifetime:** Singleton
**Responsibilities:**

- query reconciliations, breaks, cash ladders, and report packs
- join Security Master, portfolio, and accounting domains
- provide workstation-ready drill-downs without scanning raw archive files

## Data Model

### 1. `ref` schema

This extends the already-existing Security Master PostgreSQL design.

#### Core tables

- `ref.security_events`
- `ref.securities`
- `ref.security_identifiers`
- `ref.security_aliases`
- `ref.security_snapshots`
- `ref.projection_checkpoint`

#### Additional tables

- `ref.providers`
- `ref.provider_capabilities`
- `ref.venues`
- `ref.datasets`
- `ref.dataset_members`
- `ref.financial_accounts`
- `ref.counterparties`

#### Notes

- Keep the current event-sourced Security Master model.
- Add `datasets` because workstation flows repeatedly refer to `DatasetReference` and `FeedReference`.
- Add explicit `financial_accounts` and `counterparties` to support governance, reconciliation, and multi-ledger views.

### 2. `ops` schema

#### Tables

- `ops.provider_sessions`
- `ops.provider_health_snapshots`
- `ops.subscription_assignments`
- `ops.backfill_jobs`
- `ops.backfill_job_attempts`
- `ops.backfill_schedules`
- `ops.maintenance_jobs`
- `ops.maintenance_job_runs`
- `ops.export_jobs`
- `ops.import_jobs`

#### Key fields

`ops.backfill_jobs`

| Column | Type | Notes |
|---|---|---|
| `job_id` | `uuid` | primary key |
| `job_type` | `text` | gap repair, manual, scheduled |
| `provider` | `text` | provider id |
| `symbol` | `text` | canonical symbol |
| `requested_range` | `tstzrange` | requested time range |
| `priority` | `smallint` | aligns with priority queue |
| `status` | `text` | pending/running/completed/failed/cancelled |
| `requested_by` | `text` | operator or system |
| `created_at` | `timestamptz` | |
| `started_at` | `timestamptz` | |
| `completed_at` | `timestamptz` | |
| `output_manifest_id` | `uuid` | link to archive/export manifest |

### 3. `strategy` schema

#### Tables

- `strategy.strategies`
- `strategy.strategy_versions`
- `strategy.strategy_parameter_sets`
- `strategy.strategy_runs`
- `strategy.run_tags`
- `strategy.run_metrics`
- `strategy.run_artifacts`
- `strategy.run_comparisons`
- `strategy.run_promotions`

#### Core `strategy_runs` table

| Column | Type | Notes |
|---|---|---|
| `run_id` | `text` | matches current `StrategyRunEntry.RunId` |
| `strategy_id` | `text` | matches current contracts |
| `strategy_name` | `text` | |
| `mode` | `text` | Backtest, Paper, Live |
| `engine` | `text` | MeridianNative, Lean, BrokerPaper, BrokerLive |
| `status` | `text` | normalized workstation status |
| `dataset_reference` | `text` | nullable |
| `feed_reference` | `text` | nullable |
| `portfolio_id` | `text` | nullable |
| `ledger_reference` | `text` | nullable |
| `audit_reference` | `text` | nullable |
| `parameter_set_id` | `uuid` | nullable normalized reference |
| `started_at` | `timestamptz` | |
| `completed_at` | `timestamptz` | nullable |
| `net_pnl` | `numeric(20,8)` | cached summary |
| `total_return` | `numeric(20,8)` | cached summary |
| `final_equity` | `numeric(20,8)` | cached summary |
| `fill_count` | `integer` | cached summary |
| `max_drawdown` | `numeric(20,8)` | cached summary |
| `sharpe_ratio` | `double precision` | cached summary |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

#### Notes

- This directly supports the existing `StrategyRunSummary` and `StrategyRunComparison` contracts.
- `run_artifacts` stores pointers to archive files, exports, charts, report packs, simulation outputs, and notebooks rather than the large blobs themselves.

### 4. `trading` schema

#### Tables

- `trading.execution_sessions`
- `trading.orders`
- `trading.order_events`
- `trading.executions`
- `trading.position_lots`
- `trading.position_snapshots`
- `trading.risk_alerts`
- `trading.watchlists`
- `trading.watchlist_members`

#### Why this schema exists

- The roadmap calls for a paper-trading cockpit and future live workflows.
- The workstation needs trading-history joins that are too operational for archive-only JSONL scans.
- Execution realism and L3 inference outputs can attach here as execution-session artifacts and diagnostics.

### 5. `portfolio` schema

#### Tables

- `portfolio.portfolios`
- `portfolio.portfolio_snapshots`
- `portfolio.portfolio_positions`
- `portfolio.cash_balances`
- `portfolio.exposure_snapshots`
- `portfolio.pnl_attribution`
- `portfolio.performance_series`

#### Modeling rule

- Snapshots are append-only by `as_of`.
- Latest state is queried through views.
- Position and attribution rows are keyed by `portfolio_id`, `run_id`, `symbol`, and `as_of`.

### 6. `accounting` schema

#### Tables

- `accounting.ledger_books`
- `accounting.ledger_book_groups`
- `accounting.ledger_accounts`
- `accounting.journal_entries`
- `accounting.journal_lines`
- `accounting.account_balance_snapshots`
- `accounting.trial_balance_snapshots`
- `accounting.cash_flow_projections`
- `accounting.cash_flow_projection_lines`
- `accounting.reconciliation_runs`
- `accounting.reconciliation_matches`
- `accounting.reconciliation_breaks`
- `accounting.external_statement_imports`

#### Core accounting principles

- Journal entries are immutable after posting.
- Corrections happen through reversing or adjustment entries.
- Trial balance and account-balance snapshots are derived projections.
- Multi-ledger support is modeled through `ledger_books` plus `ledger_book_groups`.

### 7. `research` schema

#### Tables

- `research.experiments`
- `research.experiment_runs`
- `research.quant_scripts`
- `research.quant_script_runs`
- `research.simulation_runs`
- `research.simulation_diagnostics`
- `research.run_annotations`
- `research.export_profiles`

#### Purpose

- support QuantScript, L3 simulation, sensitivity analysis, run annotations, and research artifact tracking
- unify backtest-first and research-first workflows under the same strategy/run identity model

### 8. `audit` schema

#### Tables

- `audit.action_log`
- `audit.workflow_approvals`
- `audit.lineage_edges`
- `audit.report_packs`
- `audit.report_pack_sections`
- `audit.report_pack_artifacts`
- `audit.operator_notes`

#### Notes

- `lineage_edges` ties together raw archive files, derived database records, exports, simulations, and governance reports.
- This is essential for the governance blueprint and for promotion workflow trust.

### 9. `read` schema

#### Materialized views / projection tables

- `read.run_browser_rows`
- `read.run_detail_rows`
- `read.portfolio_overview_rows`
- `read.ledger_overview_rows`
- `read.governance_break_queue_rows`
- `read.provider_operations_rows`
- `read.dataset_coverage_rows`

#### Purpose

- Keep WPF and HTTP browse paths simple and fast.
- Avoid overloading domain tables with workstation-specific query shapes.

## Data Flow

### Operation: Record a strategy run

1. A backtest, paper, or live session begins.
2. Application creates a `StrategyRunEntry`.
3. `strategy.strategy_runs` stores the run manifest and current status.
4. Parameter sets and tags are normalized into `strategy.strategy_parameter_sets` and `strategy.run_tags`.
5. On completion, summary metrics are persisted to `strategy.run_metrics`.
6. Portfolio and ledger projections are written to `portfolio.*` and `accounting.*`.
7. Projection worker refreshes `read.run_browser_rows`, `read.run_detail_rows`, and workstation views.

### Operation: Paper/live trading session

1. Trading cockpit opens or resumes an execution session.
2. Orders are inserted into `trading.orders`.
3. State changes append to `trading.order_events`.
4. Fills append to `trading.executions`.
5. Position, portfolio, and ledger projections update.
6. Risk alerts append to `trading.risk_alerts`.
7. Audit lineage links executions to run, portfolio, ledger, and raw data artifacts.

### Operation: Governance reconciliation

1. Operator imports an external statement or selects internal sources.
2. `accounting.reconciliation_runs` records the requested reconciliation.
3. Matching logic writes `reconciliation_matches` and `reconciliation_breaks`.
4. Break queue projections refresh `read.governance_break_queue_rows`.
5. Report pack generation stores artifacts and provenance in `audit.report_packs` tables.

### Operation: Backfill and data operations

1. Operator creates a backfill request from WPF or API.
2. `ops.backfill_jobs` stores request intent and range.
3. Attempts are recorded in `ops.backfill_job_attempts`.
4. Archive data lands in existing JSONL/Parquet/WAL paths.
5. `audit.lineage_edges` connect the backfill job to produced manifests and derived dataset records.

## Partitioning, Indexing, and Retention

### Partitioning Rules

- Partition by time for append-heavy operational tables:
  - `strategy.strategy_runs` by month on `started_at`
  - `trading.order_events` and `trading.executions` by month on `event_timestamp`
  - `portfolio.portfolio_snapshots` by month on `as_of`
  - `accounting.journal_entries` by month on `posted_at`
  - `ops.provider_health_snapshots` by day or month depending on volume

### High-Value Indexes

- `strategy.strategy_runs (strategy_id, started_at desc)`
- `strategy.strategy_runs (mode, status, started_at desc)`
- `trading.orders (run_id, created_at desc)`
- `trading.executions (order_id, execution_timestamp)`
- `portfolio.portfolio_positions (portfolio_id, as_of desc, symbol)`
- `accounting.journal_lines (ledger_book_id, account_id, posted_at desc)`
- `accounting.reconciliation_breaks (status, severity, detected_at desc)`
- `ops.backfill_jobs (status, provider, created_at desc)`

### Retention Rules

- Keep operational summaries and audit journals in PostgreSQL.
- Keep raw market events and bulky analytical datasets in archive storage.
- Large artifacts stay in the file archive or package store; database stores metadata, manifest ids, checksums, and pointers.
- Apply aggressive retention or downsampling to ephemeral health snapshots and transient diagnostics.

## Migration Strategy

### Phase 1: Foundation

- Reuse existing Security Master PostgreSQL infrastructure.
- Introduce shared `OperationalDatabaseOptions`.
- Add schema runner for `strategy`, `portfolio`, `accounting`, `ops`, `audit`, `read`, `research`, and `trading`.

### Phase 2: Strategy run persistence

- Implement PostgreSQL-backed strategy run repository.
- Persist `StrategyRunEntry` instead of keeping workstation history in memory only.
- Backfill current run artifacts from existing test/dev fixtures where helpful.

### Phase 3: Portfolio and ledger projections

- Write run-completion projections into `portfolio.*` and `accounting.*`.
- Build `read.run_browser_rows`, `read.portfolio_overview_rows`, and `read.ledger_overview_rows`.

### Phase 4: Trading and governance

- Add paper/live order, fill, risk, and reconciliation persistence.
- Introduce multi-ledger grouping, cash-flow projection, and report-pack metadata.

### Phase 5: Research and advanced workflows

- Persist QuantScript run metadata, simulation runs, and sensitivity-analysis artifacts.
- Add dataset lineage and promotion workflow approvals.

## Test Plan

### Unit Tests

| Test Name | What It Verifies |
|---|---|
| `StrategyRunRepository_UpsertsAndReadsSummary` | run manifest and summary persistence |
| `PortfolioProjection_WritesLatestSnapshot` | portfolio read model generation |
| `LedgerProjection_PreservesTrialBalance` | ledger projection correctness |
| `ReconciliationStore_RecordsBreakLifecycle` | reconciliation and break workflow persistence |
| `BackfillJobStore_TracksAttemptsAndStatus` | operational job lifecycle |

### Integration Tests

| Test Name | What It Verifies |
|---|---|
| `OperationalDatabase_MigrationsCreateExpectedSchemas` | migration shape |
| `RunCompletion_ProjectsToReadModels` | end-to-end run persistence and workstation projection |
| `PaperTradingSession_WritesOrdersExecutionsAndPortfolio` | trading lifecycle persistence |
| `SecurityMasterAndGovernanceJoin_ReturnsWorkstationDto` | reference-to-governance joins |
| `LineageEdges_LinkArchiveArtifactsToReportPack` | provenance chain integrity |

### Test Infrastructure Needed

- PostgreSQL testcontainer fixture
- migration runner harness
- deterministic seed builders for strategy runs, ledger books, reconciliation inputs, and report packs

## Implementation Checklist

**Estimated effort:** High
**Suggested branch:** `codex/meridian-database-blueprint`

### Phase 1: Foundation

- [ ] Add `OperationalDatabaseOptions`
- [ ] Add PostgreSQL connection factory / data source registration
- [ ] Add migration runner for all non-Security-Master schemas
- [ ] Document backup, restore, and local-dev bootstrap flow

### Phase 2: Strategy and workstation persistence

- [ ] Add `IStrategyRunRepository` production implementation
- [ ] Persist current workstation run model in PostgreSQL
- [ ] Add `read.run_browser_rows`
- [ ] Switch WPF/browser queries to repository-backed history

### Phase 3: Portfolio and accounting

- [ ] Persist portfolio snapshots and positions
- [ ] Persist ledger books, journal entries, and trial balance projections
- [ ] Add multi-ledger grouping support
- [ ] Add reconciliation tables and workflow statuses

### Phase 4: Trading and operations

- [ ] Add order, execution, and risk-event persistence
- [ ] Add backfill/maintenance/export job stores
- [ ] Add provider-session and subscription-assignment persistence

### Phase 5: Research and governance

- [ ] Add QuantScript and simulation metadata persistence
- [ ] Add report-pack metadata and lineage
- [ ] Add promotion approval workflow tables
- [ ] Add workstation and API query endpoints over `read.*`

### Final Phase: Wrap-up

- [ ] Add config defaults and schema docs
- [ ] Add migration smoke tests
- [ ] Add retention and vacuum guidance
- [ ] ADR compliance review against archival-first storage decisions

## Open Questions

| # | Question | Owner | Impact |
|---|---|---|---|
| 1 | Should paper/live order events be stored only in PostgreSQL, or dual-written to JSONL for external audit portability? | Core Team | Medium |
| 2 | Should `read.*` use PostgreSQL materialized views, projection tables, or both? | Platform | Medium |
| 3 | Do we want native PostgreSQL partitioning only, or TimescaleDB as an optional extension for operational series? | Platform | Medium |
| 4 | Which governance artifacts must be immutable versus replaceable drafts? | Governance | High |
| 5 | How much historical portfolio/ledger recomputation should be supported from archive replay alone? | Strategy + Storage | High |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Database scope grows until it competes with the archive store | Medium | High | Enforce rule that raw market-event storage remains file-based |
| Projection drift creates inconsistent workstation views | Medium | High | Use idempotent projection workers and checkpoints |
| Governance joins become too slow on normalized tables | Medium | Medium | Precompute `read.*` projections and targeted indexes |
| Paper/live workflows diverge from backtest run model | Medium | High | Keep `strategy.strategy_runs` as the shared root aggregate |
| Too many JSON blobs reduce queryability | Medium | Medium | Keep JSON for flexible terms/provenance only; normalize workflow-critical fields |

## Recommended Outcome

Meridian should adopt a **hybrid storage architecture**:

- **Archive system of record:** WAL + JSONL + Parquet for market events and large analytical artifacts
- **Operational system of record:** PostgreSQL for Security Master, strategy/run lifecycle, workstation read models, portfolio/accounting state, governance workflows, and audit lineage

That design is the smallest database expansion that still supports the full proposed Meridian product surface without violating the repository's current storage decisions.
