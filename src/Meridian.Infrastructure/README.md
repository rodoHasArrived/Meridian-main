---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-INFRASTRUCTURE
path: src/Meridian.Infrastructure
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-07-15
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

Every `IMarketDataClient` now inherits the ProviderSdk-owned
`IProviderConnectionDiagnosticsSource` contract. Adapters without a lifecycle supervisor receive a
conservative compatibility snapshot that never claims a live connection; supervised adapters must
override it with runtime evidence. `WebSocketProviderBase` supplies shared WebSocket state,
heartbeat, reconnect, and safe failure diagnostics; Robinhood supplies the same normalized shape
from `PollingProviderBase`; NYSE and Interactive Brokers retain subscription-health and
transport-specific lifecycle evidence. For polling, raw-socket, simulation, and fallback
diagnostics, `WebSocketState` is `None`. Consumers should use this contract instead of reaching
into provider-specific transport internals.

Alpaca streaming keeps equities, options, crypto, and news as explicit adapters with their own
WebSocket endpoints. Consumers resolve them through the capability-aware Alpaca asset-stream router, which
fails closed when a requested stream has no usable entitlement rather than falling back to equities. Its diagnostics carry a per-stream selected feed and entitlement (for
example, IEX versus SIP and indicative versus OPRA), so a connected price socket cannot be
misrepresented as consolidated or OPRA-entitled data.

Alpaca reference-data search preserves broker-supplied marginability, shortability,
easy-to-borrow, fractionability, and minimum/increment constraints on symbol details. Search no
longer silently defaults omitted asset-class filters to equities; it can discover equities, crypto,
options, and fixed-income assets when the configured Alpaca API entitlement exposes them.

The public diagnostics interface, lifecycle/failure enums, and snapshot record retain their
existing namespaces but are owned by ProviderSdk so plugin contracts do not depend on concrete
Infrastructure. Infrastructure publishes type forwarders for adapters compiled against the former
assembly location.

Provider registry paths normalize configured provider identifiers before factory lookup, and the
registry can hold multiple adapter contracts for one provider family ID. This allows identifiers
such as `alpaca` to resolve independently for streaming, backfill, and symbol-search contracts
without dropping one registration because another adapter uses the same family ID.
Composite historical failover treats provider rate limits as structured signals only:
`RateLimitException` (including wrapped instances) or `HttpRequestException.StatusCode` equal to
HTTP 429. Adapter implementations should map vendor 429 responses at the HTTP boundary instead of
relying on exception message text such as "rate limit" or "too many requests".

Brokerage adapter mappers preserve explicit provider fill realized P&L when a venue payload
supplies it, while adapters without a native realized-P&L field leave the SDK value null so
accounting reconciliation can distinguish source evidence from inferred values.
Read-only normalized brokerage adapters can also pass through provider corporate-action and factor
events in activity snapshots, allowing downstream reconciliation to retain split, dividend,
amortization, paydown, and factor evidence without storing provider credentials or rebuilding
vendor-specific payloads in the workstation layer.
Provider corporate-action backfill adapters, including Polygon retained actions, Alpha Vantage
adjusted-daily dividend/split extraction, Nasdaq Data Link dataset dividend/split extraction,
Finnhub dividend/split endpoint extraction, Tiingo adjusted-EOD dividend/split extraction, and
Twelve Data paid-plan fundamentals dividend/split extraction, append retained actions through the Contracts-owned
`ISecurityMasterCorporateActionCommandService` seam so Infrastructure keeps vendor
transport concerns here while Application owns validation, append ordering, and structured audit
metadata.
Alpha Vantage symbol search is implemented as an opt-in `ISymbolSearchProvider` over the
credential-gated `SYMBOL_SEARCH` endpoint so its constrained free-tier quota is not consumed unless
the Alpha Vantage backfill family is explicitly enabled.
Twelve Data symbol search is implemented as a credential-gated `ISymbolSearchProvider` over the
`/symbol_search` discovery endpoint. It reuses `TWELVEDATA_API_KEY`, keeps Twelve Data's
8-request/minute free-tier pacing, and applies asset/exchange filtering client-side.
Tiingo symbol search is implemented as a credential-gated `ISymbolSearchProvider` over the
`/tiingo/utilities/search` endpoint. It reuses `TIINGO_API_TOKEN`, keeps Tiingo's
50-request/hour pacing, skips malformed or inactive rows, and applies asset/exchange filtering
client-side.
FRED symbol search is implemented as a credential-gated `ISymbolSearchProvider` over the official
`series/search` endpoint. It reuses `FRED_API_KEY`, keeps FRED's 120-request/minute pacing, maps
economic series IDs as reference-discovery results, and applies asset/exchange filtering
client-side.
Nasdaq Data Link symbol search is implemented as a credential-gated `ISymbolSearchProvider` over
the dataset-search endpoint. It reuses `NASDAQ_DATA_LINK_API_KEY`, returns exact
`DATABASE/DATASET` codes to avoid dataset/ticker ambiguity, supports database-code filtering, and
keeps the conservative 50-request/day pacing already used by the Nasdaq Data Link backfill family.
The shared symbol-search base class rejects non-positive result limits before the rate limiter and
HTTP path so direct secondary-provider calls cannot spend free-tier quota on invalid requests.
OpenFIGI symbol/reference-data enrichment prefers exchange-scoped mapping candidates when callers
provide an exchange hint, while preserving upstream ordering as the fallback when no exchange can be
normalized.
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
Interactive Brokers contract construction resolves default SecType values from the Contracts-owned
`InstrumentTypeDescriptorCatalog`, while still honoring explicit provider SecType overrides such
as `GOVT` for government bonds. The IBKR gateway exposes its actual execution mode to the shared
OMS, so guidance or smoke builds are simulation-only and cannot be promoted into live routing by
configuration. It also owns account catalog, portfolio snapshot, and connected-session execution
sync: source-identified TWS execution callbacks provide fills and open-order evidence, while
account-scoped Flex imports remain the controlled reconciliation backstop for fees, cash,
dividends, interest, FX conversions, corporate actions, and prior-session activity.
The IB vendor runtime also exposes an entitlement-aware `IBDataServices` seam for scanner discovery,
contract details, option chains, news, fundamentals, tick-by-tick data, account P&L, market rules,
and depth-exchange metadata. Its request lineage begins `Unknown` and must retain the actual IB
live/frozen/delayed status, exchange, market rules, and subscription descriptor alongside any
materialized result. `IBDataResultMaterializer` is the Infrastructure-to-storage composition seam:
it commits each `WatchAsync` update to `IIBDataResultStore` before making that update available to
operator readers; successful request submission is not evidence of a live entitlement.
Its richer request callbacks publish bounded, request-correlated ProviderSdk read-model updates for
option discovery, scanners, real-time bars, historical ticks, account/model-account P&L, and market
rules. Callbacks that do not correlate to an active `IBDataServices` request are ignored. Each returned
request and observation carries required provenance: provider and configured connection identity, source
and receipt times, reported entitlement/feed/availability, request descriptor,
provider-native identity, correlation, and a deterministic de-duplication key. Vendor SDK absence remains simulation/fail-closed and cannot advertise live IB capability.
The brokerage gateway template remains an obsolete copy-target, but its scaffold behavior is
deterministic: provider-discovery metadata, option-backed identity/capabilities, configurable
connection readiness, option-backed account/position reads, and in-memory open-order tracking let
copied providers and tests prove lifecycle behavior before replacing the template seams with broker
APIs.

Streaming failover state is updated from explicit success, failure, and latency signals in addition
to the periodic evaluator. Latency-triggered failover decisions use a bounded recent-sample window
and ignore impossible latency samples so stale spikes or malformed telemetry do not distort current
routing posture. Candidate backups must satisfy the same recent-latency threshold before selection,
so failover does not route into a provider that is already breaching the rule's SLA window.
Primary recovery uses that same latency threshold, so success pings alone cannot switch routing
back to a provider that still breaches the current rule window.
Cancellation is propagated as cancellation, not treated as a provider failure.
Backfill worker and queue orchestration consumes ProviderSdk-owned job descriptors and stores
dependency job IDs on each job so chained jobs resume only after all upstream dependencies complete.

ETL SFTP publishing is an Infrastructure adapter implementation of the Contracts-owned
`ISftpFilePublisher` port. Data Integration owns export behavior and composes the port; this layer
only owns transport connection, pinned host-key verification, directory creation, and upload
mechanics. SFTP source and destination definitions must provide a SHA-256 host-key fingerprint so
imports and exports fail closed before trusting a remote server identity.
SFTP source imports now resolve credentials through `ISftpCredentialResolver` before opening a
session, expose `ISftpCapabilityService` for runtime readiness diagnostics, and support explicit
post-import source handling (`leave`, `delete`, `archive`, `error`, or `.done` marker) without
weakening the pinned-host-key requirement. SFTP locations are strict `sftp://` URIs with a host and
absolute remote path; user info, query strings, fragments, traversal segments, and files outside the
configured source root are rejected before opening a session. Local and SFTP ETL source readers
discover both CSV and XLSX partner files by default, with semicolon-delimited file patterns for
scoped exchanges. Publisher uploads use temporary remote names and rename into place so readers do
not observe partial exports; the SSH.NET transfer calls remain synchronous, with cancellation
checked before sessions, listing, downloads, and each upload.

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
