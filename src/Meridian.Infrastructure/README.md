---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-INFRASTRUCTURE
path: src/Meridian.Infrastructure
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-06-05
---

# src/Meridian.Infrastructure

## Purpose

Infrastructure contains provider adapters, HTTP integration, ETL adapters, resilience helpers, and concrete data-source implementations.

## Layer responsibility

This layer owns external integration details while depending on lower contracts and provider abstractions. It must not depend on application orchestration.

## Key folders and files

- `Adapters/` - provider and data-source adapters.
- `Http/` and `Resilience/` - transport and retry support.
- `Etl/` and `DataSources/` - import/export and source-specific plumbing, including the concrete
  SFTP publisher adapter for the Contracts-owned ETL publisher port.

## Important workflows

Use this module for provider implementation, external service integration, and adapter behavior.

WebSocket streaming adapters that derive from `WebSocketProviderBase` expose
`IProviderConnectionDiagnosticsSource` for safe provider-level health snapshots. Consumers should
use that optional seam for connection state, heartbeat time, reconnect status, subscription health
counts, last subscription message time, and last safe error category instead of reaching into
provider-specific transport internals.

Provider registry paths normalize configured provider identifiers before factory lookup, and the
registry can hold multiple adapter contracts for one provider family ID. This allows identifiers
such as `alpaca` to resolve independently for streaming, backfill, and symbol-search contracts
without dropping one registration because another adapter uses the same family ID.

Brokerage adapter mappers preserve explicit provider fill realized P&L when a venue payload
supplies it, while adapters without a native realized-P&L field leave the SDK value null so
accounting reconciliation can distinguish source evidence from inferred values.
Read-only normalized brokerage adapters can also pass through provider corporate-action and factor
events in activity snapshots, allowing downstream reconciliation to retain split, dividend,
amortization, paydown, and factor evidence without storing provider credentials or rebuilding
vendor-specific payloads in the workstation layer.
The Plaid adapter family owns only vendor transport and file-backed connection persistence:
link-token, public-token exchange, balances, transaction sync, investments, identity, webhooks,
and sandbox-transfer calls are mapped into contract DTOs before shared workstation services attach
them to fund-account, treasury, reconciliation, or evidence workflows.
QuickBooks Online accounting-system transport moved to `src/Meridian.DataIntegration` so provider
ingestion and source-evidence mapping live in the physical Data Integration design module.
Credential and connectivity checks in this layer must stay secret-safe and read-only by default:
provider credential responses expose masked status only, file-backed vault payloads must not echo
submitted secrets, and read-only brokerage sync adapters may fetch account, portfolio, activity,
corporate-action, and factor evidence without placing orders. Adapter readiness notes should link
any write-capable live execution path back to the shared execution governance gates.
The brokerage gateway template remains an obsolete copy-target, but its scaffold behavior is
deterministic: provider-discovery metadata, option-backed identity/capabilities, configurable
connection readiness, option-backed account/position reads, and in-memory open-order tracking let
copied providers and tests prove lifecycle behavior before replacing the template seams with broker
APIs.

Streaming failover state is updated from explicit success, failure, and latency signals in addition
to the periodic evaluator. Cancellation is propagated as cancellation, not treated as a provider
failure. Backfill orchestration stores dependency job IDs on each job so chained jobs resume only
after all upstream dependencies complete.

ETL SFTP publishing is an Infrastructure adapter implementation of the Contracts-owned
`ISftpFilePublisher` port. Data Integration owns export behavior and composes the port; this layer
only owns transport connection, directory creation, and upload mechanics.

Broker statement imports hash the source file bytes and persist the resulting content hash with a
deterministic duplicate key derived from fund account, statement period, and source hash. Source
paths and original file names remain provenance metadata, not duplicate-detection inputs.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-INFRASTRUCTURE -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-INFRASTRUCTURE -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-INFRASTRUCTURE-001` | Complete W7 provider credential safety and read-only default checks | done | medium |
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Do not add an Infrastructure to Application dependency. Provider abstractions should remain in ProviderSdk or Contracts.

## Related docs

- `docs/providers/`
- `docs/architecture/module-map.md`
- `docs/status/provider-validation-matrix.md`
