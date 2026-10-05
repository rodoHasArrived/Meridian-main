# Provider Integration Status Board

**Status:** archived
**Scope:** historical evidence snapshot
**Owner:** core-team  
**Reviewed:** 2026-05-20

**Run Date:** 2026-05-20  
**Source snapshots:** `docs/reference/provider-validation-matrix.md` plus the broker inventory/runbook docs referenced at the snapshot date, [`archive/docs/reference/kernel-readiness-dashboard.md`](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/reference/kernel-readiness-dashboard.md) (snapshot moved to archive for traceability; Last Updated 2026-04-27)
**Purpose:** preserve the recorded provider phases, blockers, and evidence dates without presenting them as current registration or release truth.

Use the [adapter readiness registry](../source/data/adapter-readiness.yml) and its
[generated matrix](../source/generated/adapter-readiness-matrix.md) for current implementation
coverage, and the [provider validation matrix](provider-validation-matrix.md) for operator evidence
requirements. Registration has changed since this board: the current
[`AddHostedBrokerageGateways`](../../src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs)
registers `ib`/`ibkr` and contains an optional StockSharp gateway hook. The older absence claims
below are retained as dated observations; they must not drive new implementation work without a
fresh source review. Registration alone does not establish live-provider or production acceptance.

## How to read this board

- **Phase:**
  - **Read-only** = data ingestion/research use only; no order routing.
  - **Paper** = simulation/paper-trading workflows active; no production funds.
  - **Production** = approved for production-path execution within Meridian governance gates.
- **Latest evidence timestamp (UTC):** the most recent dated evidence run or snapshot currently referenced by source-of-truth status documents.
- **Blockers:** explicit conditions that must be resolved before phase promotion.

## Unified Provider Status

| Provider/Broker | Recorded phase | DI registration completeness at review | Resolution behavior at review (factory / runtime) | Recorded blockers to next phase | Latest evidence timestamp (UTC) | Evidence sources |
| --- | --- | --- | --- | --- | --- | --- |
| Alpaca | Paper | Complete for hosted brokerage gateway and factory-created backfill/search providers | Resolves via `AddHostedBrokerageGateways` (`"alpaca"`) plus `ProviderFactory.CreateAlpacaBackfillProvider` and `CreateAlpacaSearchProvider`; missing credentials short-circuit factory creation to `null` | Production promotion checklist not yet represented in the active DK2 gate set and operator sign-off packet flow | 2026-04-27 | Wave 1 closure row in provider validation matrix; DI paths in `HostedBrokerageGatewayServiceCollectionExtensions` + `ProviderFactory` |
| Robinhood | Paper (bounded) | Partial: hosted brokerage gateway is wired; no ProviderFactory backfill/symbol-search registration path | Runtime execution/account sync resolves from `AddHostedBrokerageGateways` (`"robinhood"`); historical/search resolution is unavailable through `ProviderFactory` lists | Unofficial API posture plus required manual broker-session/runtime evidence (`auth-session`, `quote-polling`, `order-submit-cancel`, `throttling-reconnect`) must be regenerated/attached for the review run | 2026-04-27 (latest signed Wave 1 packet set); bounded scenario packet noted as not retained in current repo | Robinhood bounded row in provider validation matrix; hosted gateway registration + missing factory row linkage |
| Yahoo Finance (historical/fallback) | Read-only | Backfill-only wired through `ProviderFactory`; no hosted brokerage registration and no symbol-search registration | Resolves as historical provider through `CreateYahooBackfillProvider` when enabled; no execution or symbol-search resolution branch exists | No execution lane; keep scoped to historical/fallback provider role unless roadmap explicitly expands scope | 2026-04-27 | Yahoo historical/fallback row in provider validation matrix; ProviderFactory backfill list |
| Interactive Brokers | Read-only (deferred from active Wave 1 gate) | Partial: backfill creation path exists; no hosted brokerage gateway registration in current hosted DI extension | Backfill resolves through `CreateIbBackfillProvider` when `DataSourceKind.IB` or IB options are present; no hosted `AddBrokerageGateway("ib")` resolution branch in the app host | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix; ProviderFactory + hosted gateway DI extension snapshot |
| Tradier | Not operator-enabled; mapper/test support only | Missing in both hosted gateway DI and ProviderFactory creation paths | No runtime provider identifier branch is registered in DI/factory for Tradier; current support is mapper/test-only evidence | Canonical equity/options/order/error mapping and focused reconciliation tests exist, but no registered Tradier credential flow, transport client, readiness projection, or execution gateway is available | 2026-05-19 | [`archive/docs/operations/tradier-provider-endpoint-catalog.md`](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/tradier-provider-endpoint-catalog.md); `tests/Meridian.Tests/Execution/TradierExecutionReconciliationTests.cs`; `src/Meridian.Infrastructure/Adapters/Tradier/TradierCanonicalMappers.cs` |
| Polygon | Read-only (deferred from active Wave 1 gate) | Backfill + symbol-search paths wired in ProviderFactory; no hosted brokerage gateway registration | Resolves through `CreatePolygonBackfillProvider` and `CreatePolygonSearchProvider` when API key exists; credential absence yields `null` and no registry registration | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix; ProviderFactory backfill/search branches |
| NYSE | Read-only (deferred from active Wave 1 gate) | Dedicated service extension exists, but no root hosted registration/factory wiring in the reviewed paths | Resolution requires explicit `AddNYSEDataSource(...)` call path; provider is not created by `ProviderFactory` and no hosted gateway identifier is registered | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix; NYSE service extension vs root DI wiring |
| StockSharp | Read-only (deferred from active Wave 1 gate) | Missing in both hosted gateway DI and ProviderFactory creation paths | No registration identifier branch or factory creation path was found in reviewed infrastructure registration/factory files | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix; infrastructure registration/factory inspection |

## Recorded DI/Factory mismatch remediation queue

| Provider | Locating context (module path / registration method / provider identifier) | Mismatch summary | Remediation next action | Target sprint |
| --- | --- | --- | --- | --- |
| Robinhood | `src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs` / `AddHostedBrokerageGateways` / `"robinhood"`; `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs` / `CreateBackfillProviders` + `CreateSymbolSearchProviders` / *(no robinhood branch)* | Execution is DI-wired, but historical/search provider composition is absent from factory registration lists | Decide scope for Robinhood historical/search lane and either (a) add explicit factory branches with bounded capability flags and tests, or (b) codify execution-only posture in provider matrix + config validation guard | Wave 2 Sprint 34 |
| Interactive Brokers | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs` / `CreateIbBackfillProvider` / implicit IB provider; `src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs` / `AddHostedBrokerageGateways` / *(no `"ib"` registration)* | Backfill provider exists, but hosted execution gateway registration path is absent in app host extension | Introduce explicit hosted IB gateway registration decision record (implement `AddBrokerageGateway("ib", ...)` with guardrails or document/deprecate non-hosted path) | Wave 2 Sprint 34 |
| Tradier | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs` / provider creation methods / *(no tradier branch)*; `src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs` / `AddHostedBrokerageGateways` / *(no `"tradier"` registration)* | Tradier has mapper/reconciliation evidence but no runtime DI/factory integration path | Add minimal Tradier provider bootstrap slice (credential binding + gateway registration + readiness projection wiring) behind feature flag, with focused execution/readiness tests | Wave 2 Sprint 35 |
| NYSE | `src/Meridian.Infrastructure/Adapters/NYSE/NYSEServiceExtensions.cs` / `AddNYSEDataSource` / NYSE data-source registration path; root DI path lacks invocation | NYSE has local service-extension registration API but is not connected from reviewed root registration/factory entry points | Add or reject root composition call to `AddNYSEDataSource` in startup composition; document decision in status board and provider roadmap | Wave 2 Sprint 35 |
| StockSharp | Reviewed root wiring paths: `HostedBrokerageGatewayServiceCollectionExtensions` + `ProviderFactory` / *(no stocksharp identifier branch)* | Deferred provider has no active DI/factory branch in current infrastructure wiring | Confirm deferment contract by adding explicit “not wired” assertion/diagnostic in provider readiness checks to avoid silent expectation drift | Wave 2 Sprint 35 |

## Recorded ownership and refresh cadence

| Surface | Primary owner | Backup owner | Minimum refresh cadence | Refresh trigger |
| --- | --- | --- | --- | --- |
| `provider-integration-status.md` (this board) | Data & Provider Reliability owner | Trading Workstation owner | **Twice weekly during active integrations** (Monday + Thursday UTC) | Any provider phase move, blocker state change, new bounded-runtime evidence, or operator sign-off state change |
| `provider-validation-matrix.md` | Data & Provider Reliability owner | Shared Platform Interop owner | Weekly minimum (or same-day when evidence changes) | New `run-wave1-provider-validation.ps1` output, DK1 packet/sign-off updates, or deferred-provider scope changes |
| `../status/kernel-readiness-dashboard.md` | Trading Workstation owner | Accounting/Fund Ops owner | Weekly minimum (Mon cadence rule already defined) | Any gate-status/readiness change, operator-sign-off movement, or milestone target-date update |

## Recorded refresh workflow

The workflow below records how this board was maintained. New implementation changes belong in the
source registry and generated matrix; release and evidence decisions belong in their canonical
records. Do not advance this historical board's run date to imply that old provider checks were rerun.

1. Run provider evidence generation for the current run date (`yyyy-mm-dd`) and collect artifacts under `artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
2. Update `provider-validation-matrix.md` with new evidence references and bounded/manual notes.
3. The kernel readiness dashboard ([`archive/docs/reference/kernel-readiness-dashboard.md`](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/reference/kernel-readiness-dashboard.md)) was removed by the 2026-09-11 archive cleanup (`982eea2d`) and has no maintained replacement, so there is no dashboard to update; the link shows its last content. Restoring or replacing it is an open ownership decision.
4. Update this status board last so phase + blockers + timestamps reflect those two source snapshots.
5. Validate consistency with `python3 scripts/check_program_state_consistency.py` before publishing status updates.

## Run-date accuracy rule

- Do not advance any `Latest evidence timestamp (UTC)` in this board unless the corresponding dated artifact or snapshot update exists.
- If a source snapshot still points to an older signed packet (for example 2026-04-27), preserve that date here and list the current blocker rather than inferring freshness.
