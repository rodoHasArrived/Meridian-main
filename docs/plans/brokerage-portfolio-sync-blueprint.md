# Brokerage Portfolio Sync Blueprint

**Last Updated:** 2026-04-26

## Summary

Add Meridian-native brokerage and custodian portfolio syncing that imports external account state such as accessible accounts, positions, balances, open orders, fills, and cash transactions into Meridian's existing `fund account`, `portfolio`, `ledger`, `reconciliation`, and `Governance` workflows without turning market-data providers into portfolio-domain services.

This should extend the existing execution and brokerage seams already present in:

- `src/Meridian.Execution.Sdk/`
- `src/Meridian.Execution/`
- `src/Meridian.Application/FundAccounts/`
- `src/Meridian.Application/Banking/`
- `src/Meridian.Strategies/Services/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `src/Meridian.Contracts/Workstation/`

The key design decision is boundary discipline:

- `Meridian.ProviderSdk` remains focused on market and historical data providers
- `Meridian.Execution.Sdk` and brokerage adapters own external brokerage account-state ingestion
- application and governance services own normalization, reconciliation, persistence, and projection into workstation workflows

## Current Operator-Ready Slice

The first implemented slice keeps WPF as the operator acceptance lane and treats Alpaca plugin
checks as market-data evidence only. Brokerage/account continuity is read-side and Meridian-owned:

- `src/Meridian.Execution.Sdk/IBrokerageAccountSync.cs` defines account catalog, portfolio sync,
  activity sync, balances, positions, orders, fills, and cash activity.
- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs` is the first concrete
  adapter for Alpaca Trading API account, position, open-order, fill, and cash-activity reads.
- `src/Meridian.Ui.Shared/Services/BrokeragePortfolioSyncService.cs` orchestrates local durable
  sync using raw snapshots, normalized projections, cursors, stale-state detection, and
  partial-failure reporting.
- `src/Meridian.Ui.Shared/Services/TradingOperatorReadinessService.cs` aggregates paper session,
  replay verification, execution controls, promotion trace, brokerage sync freshness, Security
  Master coverage, and operator work items for WPF and workstation APIs.
- `src/Meridian.Ui.Shared/Services/StrategyRunReviewPacketService.cs` exposes one run review packet
  for Research, Trading, and Governance so run, portfolio, ledger, reconciliation, attribution, and
  optional brokerage evidence use the same source.

Implemented routes:

- `GET /api/workstation/trading/readiness`
- `GET /api/workstation/runs/{runId}/review-packet`
- `GET /api/fund-accounts/brokerage-sync/accounts`
- `GET /api/fund-accounts/{accountId}/brokerage-sync/status`
- `POST /api/fund-accounts/{accountId}/brokerage-sync/run`
- `GET /api/fund-accounts/{accountId}/brokerage-sync/positions`
- `GET /api/fund-accounts/{accountId}/brokerage-sync/activity`

Durable local storage now uses `%LocalAppData%/Meridian/workstation/brokerage-sync` by default:

- raw provider snapshots: `raw/{provider}/{externalAccount}/{timestamp}.json`
- normalized projections: `projections/{fundAccountId}/current.json`
- sync cursors: `cursors/{fundAccountId}.json`

This slice is intentionally not a live-trading readiness expansion. Sync failures, stale cursors,
and Security Master gaps surface as operator warnings and work items; they do not authorize order
routing.

## Scope

### In scope

- brokerage account discovery and account-link metadata
- external position, balance, order, fill, and cash-transaction synchronization
- local persistence of sync snapshots and sync cursors
- fund-account and governance projections that consume synced brokerage state
- shared workstation DTOs and endpoints for operator review, freshness, and reconciliation
- WPF and workstation read-model integration where it reinforces existing `Trading` and `Governance` workflows

### Out of scope

- replacing `IFundAccountService` statement ingestion flows
- moving portfolio or ledger domain logic into provider adapters
- building a generalized ETL platform for every external accounting source
- live-trading readiness claims beyond read-side synchronization
- OCR or unstructured statement ingestion

### Assumptions

- brokerage sync starts as a local-first, pull-based workflow with explicit operator refresh and bounded background refresh
- account-level sync lands on top of existing `FundAccount`, `Banking`, reconciliation, and workstation seams
- provider credentials and runtime constraints remain broker-specific and are not normalized away
- position and balance sync should complement, not replace, statement-based evidence for governance workflows

## Architecture

The capability should be organized into five layers.

### 1. Brokerage sync contracts in `Meridian.Execution.Sdk`

Current anchors:

- `src/Meridian.Execution.Sdk/IBrokerageGateway.cs`
- `src/Meridian.Execution.Sdk/IBrokeragePositionSync.cs`

Keep `IBrokeragePositionSync` for the narrow current reconciliation path. Add sibling interfaces instead of overloading market-data contracts:

- `IBrokerageAccountCatalog`
- `IBrokeragePortfolioSync`
- `IBrokerageActivitySync`

Suggested responsibilities:

- `IBrokerageAccountCatalog`: enumerate accessible brokerage accounts and account metadata
- `IBrokeragePortfolioSync`: return positions and balance/equity snapshots for one account
- `IBrokerageActivitySync`: return open orders, fills, and cash transactions over a bounded window

Suggested path:

- `src/Meridian.Execution.Sdk/IBrokerageAccountCatalog.cs`
- `src/Meridian.Execution.Sdk/IBrokeragePortfolioSync.cs`
- `src/Meridian.Execution.Sdk/IBrokerageActivitySync.cs`

Rationale:

- execution-side brokerage adapters already own external broker semantics
- `BrokerageServiceRegistration` already gives Meridian one place to register broker-specific execution integrations
- this avoids polluting `Meridian.ProviderSdk` with non-market-data concerns

### 2. Broker-specific adapter implementation in `Meridian.Execution`

Current anchors:

- `src/Meridian.Execution/Adapters/BrokerageGatewayAdapter.cs`
- `src/Meridian.Execution/BrokerageServiceRegistration.cs`
- `src/Meridian.Execution/Services/PositionReconciliationService.cs`

Brokerage adapters that can sync account state should implement the new SDK interfaces directly, alongside `IBrokerageGateway` where appropriate.

Suggested additions:

- `src/Meridian.Execution/Services/BrokerageSyncRegistration.cs`
- `src/Meridian.Execution/Services/BrokerageAccountLinkResolver.cs`

The design should support three adapter shapes:

1. full brokerage gateways that trade and sync
2. read-only brokerage sync adapters for brokers or custodians where Meridian is not routing orders
3. paper/local adapters for tests and desktop demo flows

### 3. Application orchestration and persistence in `Meridian.Application`

Current anchors:

- `src/Meridian.Application/FundAccounts/IFundAccountService.cs`
- `src/Meridian.Application/Banking/IBankingService.cs`

Add orchestration services that convert broker-native snapshots into Meridian-owned records.

Suggested new services:

- `src/Meridian.Application/FundAccounts/IBrokeragePortfolioSyncService.cs`
- `src/Meridian.Application/FundAccounts/BrokeragePortfolioSyncService.cs`
- `src/Meridian.Application/FundAccounts/BrokerageSyncCursorStore.cs`
- `src/Meridian.Application/FundAccounts/BrokerageAccountLinkService.cs`

Responsibilities:

- map external broker account ids to Meridian `FundAccount` records
- run sync passes for positions, balances, orders, fills, and cash activity
- persist cursors or watermarks for incremental sync
- normalize synced state into account snapshots and reconciliation inputs
- emit operator-visible sync status and failure details

Persistence guidance:

- use local-first persisted JSON snapshots under the configured data root, consistent with existing fund-account and fund-structure persistence
- for lifecycle-sensitive writes, use Meridian durability primitives rather than ad hoc file writes
- keep raw sync snapshots and normalized projections separate so operators can audit translation errors

Suggested storage shape:

- raw snapshots under a `brokerage-sync/raw/` area keyed by broker, account, and sync timestamp
- normalized state under a `brokerage-sync/projections/` area keyed by Meridian fund account id
- cursor state under a small durable store owned by `BrokerageSyncCursorStore`

### 4. Contracts, read models, and workstation endpoints

Current anchors:

- `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs`
- `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- `src/Meridian.Strategies/Services/PortfolioReadService.cs`
- `src/Meridian.Strategies/Services/LedgerReadService.cs`

Add shared DTOs for sync posture and imported account state.

Suggested new contract file:

- `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs`

Suggested DTOs:

- `BrokerageAccountLinkDto`
- `BrokerageSyncStatusDto`
- `BrokeragePositionSnapshotDto`
- `BrokerageBalanceSnapshotDto`
- `BrokerageOrderSnapshotDto`
- `BrokerageFillSnapshotDto`
- `BrokerageCashTransactionDto`
- `BrokeragePortfolioSyncViewDto`

Suggested endpoint additions:

- `/api/fund-accounts/{accountId}/brokerage-sync/status`
- `/api/fund-accounts/{accountId}/brokerage-sync/run`
- `/api/fund-accounts/{accountId}/brokerage-sync/positions`
- `/api/fund-accounts/{accountId}/brokerage-sync/activity`

Suggested projection additions:

- enrich `FundOperationsWorkspaceDto` with brokerage freshness and divergence posture
- let `PortfolioReadService` and `LedgerReadService` consume normalized brokerage snapshots as one input among existing run, account, and reconciliation sources
- keep workstation-level DTOs aggregated and operator-facing rather than broker-native

### 5. WPF and governance workflow integration

Current anchors:

- `src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs`
- `src/Meridian.Wpf/ViewModels/FundAccountsViewModel.cs`
- `src/Meridian.Wpf/Views/GovernanceWorkspaceShellPage.xaml`

Use the synced data to deepen existing workflows instead of creating a new root workspace.

Recommended integration points:

- `Trading`: show broker-account freshness, synced positions, and open-order divergence against local strategy/paper state
- `Governance`: show linked brokerage accounts, latest sync age, open breaks, and imported cash activity as part of fund-operations review
- `FundLedgerViewModel`: consume sync posture alongside current account, bank snapshot, reconciliation, and report-pack data

The UI should answer:

- is the external account linked and healthy
- how fresh is the last sync
- what changed since the last sync
- where are the breaks between brokerage, portfolio, bank, and ledger views

## Interfaces and Models

Name the public surface before implementation.

### Execution SDK

Add:

- `IBrokerageAccountCatalog`
- `IBrokeragePortfolioSync`
- `IBrokerageActivitySync`
- `BrokerageExternalAccountDto`
- `BrokerageCashTransactionDto`
- `BrokerageOrderDto`
- `BrokerageFillDto`

Do not move these into `Meridian.ProviderSdk`.

### Application services

Add:

- `IBrokeragePortfolioSyncService`
- `BrokeragePortfolioSyncResult`
- `BrokerageSyncRunRequest`
- `BrokerageSyncCursor`
- `BrokerageAccountLink`

Suggested path:

- `src/Meridian.Application/FundAccounts/`

### Workstation and governance DTOs

Add:

- `BrokerageSyncStatusDto`
- `BrokeragePortfolioSyncViewDto`
- `BrokerageBreakSummaryDto`
- `BrokerageLinkedAccountSummaryDto`

Suggested path:

- `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs`

## Data Flow

1. An operator links a Meridian `FundAccount` to an external brokerage account.
2. `BrokerageAccountLinkService` stores the mapping between Meridian account identity and broker-native account identity.
3. `BrokeragePortfolioSyncService` resolves the appropriate brokerage adapter from DI.
4. The service queries:
   - account catalog data when needed
   - current balances and positions
   - bounded order, fill, and cash activity since the last cursor
5. Raw broker payloads are persisted for auditability.
6. The service normalizes those payloads into Meridian-owned snapshot records keyed by fund account id.
7. Reconciliation and workstation read services consume the normalized snapshots and compute divergence or freshness summaries.
8. `FundAccountEndpoints` and `WorkstationEndpoints` expose sync status, imported state, and break summaries.
9. WPF and retained workstation API/local consumers render sync freshness, divergence, and drill-ins without exposing adapter-specific shapes directly.

## Edge Cases and Risks

- Broker account identity may not align cleanly with Meridian fund-account structure. The link model must support explicit operator mapping.
- Cash activity, settled cash, buying power, and ledger cash are related but not interchangeable. The blueprint must keep those concepts separate.
- Position sync is not enough for governance. Imported fills and cash activity are needed to explain breaks and settlement movement.
- Some brokers can route orders but do not expose complete historical activity windows. Sync capability must be feature-flagged per adapter.
- Sync failure should degrade visibly, not silently. Operators need freshness timestamps, last error, and partial-sync status.
- Sync snapshots should not overwrite statement-derived evidence. Governance workflows need both imported broker state and formal statements.
- Read-only custodians and full brokerages should both fit the model. Do not assume every sync-capable adapter also implements order routing.

## Test Plan

Target the narrowest useful validation surface per layer.

### Execution SDK and adapter tests

- adapter contract tests for account catalog, position, balance, order, fill, and cash-activity sync
- feature-matrix tests for adapters that support only a subset of sync surfaces

### Application tests

- `BrokeragePortfolioSyncService` tests for normalization, cursor advancement, idempotent re-sync, and partial-failure handling
- persistence tests for raw snapshot and normalized projection durability
- reconciliation tests that prove imported brokerage state flows into break summaries

### Endpoint tests

- API tests for link, run, status, and projection endpoints
- negative-path tests for unlinked accounts, stale cursors, and adapters without activity-sync support

### Workstation tests

- shared DTO projection tests for freshness and break summaries
- WPF view-model tests that verify brokerage sync posture appears in fund-operations and governance flows without breaking current shell navigation

Current focused coverage added with the operator-ready slice:

- `BrokeragePortfolioSyncServiceTests` covers a normal operator sync with projection/cursor/raw
  persistence, credential outage failure persistence, and cancellation before persistence.
- `AlpacaBrokerageGatewayTests` covers Alpaca read-side account, balance, position, open-order,
  fill, and cash-activity mapping through HTTP fixtures.
- `TradingWorkspaceShellPageTests` includes source-level coverage that the WPF cockpit consumes
  `TradingOperatorReadinessService`; current WPF test discovery should be checked before relying on
  that project for executable assertions.

Suggested validation commands:

```bash
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter Brokerage
dotnet test tests/Meridian.Wpf.Tests -c Release /p:EnableWindowsTargeting=true --filter FundLedger
```

## Open Questions

- Should brokerage cash transactions land first in `Meridian.Application/Banking/` as imported bank-like records, or remain in a brokerage-sync projection until reconciliation joins them?
- Which brokerages are the first target set for read-side sync: Alpaca, Interactive Brokers, Robinhood, or a custody-only adapter?
- Does the first wave need background scheduled sync, or is operator-triggered sync enough until the workflow stabilizes?
- Should raw broker payload retention follow the same lifecycle policy surface as other operational artifacts?

## Roadmap Fit

This capability belongs in two active roadmap waves:

- **Wave 3:** deepen shared `run / portfolio / ledger / reconciliation` continuity by importing external brokerage state into the same read-model seams already used across workspaces
- **Wave 4:** deepen governance and fund-operations workflows by linking broker and custodian account state to fund accounts, cash-flow, and reconciliation review

It should not be framed as a Wave 1 market-data provider-confidence task unless a specific brokerage adapter also needs separate provider-runtime validation for a different reason.
