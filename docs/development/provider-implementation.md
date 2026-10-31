# Data Provider Implementation Guide

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-30

## Overview

Implement a provider against the contract its caller actually consumes, then wire the corresponding
factory and host path. A class, metadata flag, attribute, or DI registration alone is not runtime
activation. The [capability matrix](../reference/provider-capability-matrix.md) separates shared
contract coverage, implementation maturity, configured availability, and live-provider acceptance.

This guide replaces the former `Adapters/Streaming`, `Adapters/Backfill`, `Adapters/Historical`,
and `Adapters/SymbolSearch` recipes. Concrete implementations live in direct provider-family
folders under `src/Meridian.Infrastructure/Adapters/`; shared adapter machinery lives in `Core/`.

## Provider SDK (`Meridian.ProviderSdk`)

The runtime inventory is declared by
[`ProviderCapabilityDescriptorCatalog`](../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs):

| Capability slot | Contract |
| --- | --- |
| Streaming | `IMarketDataClient` |
| Historical | `IHistoricalDataProvider` |
| Search | `ISymbolSearchProvider` |
| Options | `IOptionsChainProvider` |
| Corporate actions | `ICorporateActionProvider` |
| Brokerage | `IBrokerageGateway` |

These contracts do not all live in ProviderSdk. Follow the catalog's actual types and the current
source declaration rather than deriving a namespace from a project name. For example,
[`IMarketDataClient`](../../src/Meridian.ProviderSdk/IMarketDataClient.cs) lives in ProviderSdk
but currently declares namespace `Meridian.Infrastructure`.

The catalog also records `ISymbolResolver` and `IDataSource` compatibility surfaces. OpenFIGI's
resolver is not `ISymbolSearchProvider`; NYSE's compatibility history is not
`IHistoricalDataProvider`; hosted Polygon corporate-action ingestion is not the on-demand contract.
Do not advertise the six shared slots merely because a separate interface offers similar behavior.

## Architecture Decision Records

Preserve cancellation, bounded buffering, serialization, HTTP lifetime, and provider identity
requirements in the current implementation. `[DataSource]` discovery belongs to the `IDataSource`
compatibility lane; it is not a universal activation mechanism for every shared-contract adapter.
Traceability attributes do not replace registration, credential gating, or regression tests.

## 1. Streaming Providers (`IMarketDataClient`)

Read the [actual interface](../../src/Meridian.ProviderSdk/IMarketDataClient.cs) before implementing
it. It includes connection diagnostics and simulation identity in addition to lifecycle and
trade/depth subscription methods. Override metadata and diagnostics to describe supported behavior
conservatively; default metadata must not accidentally advertise unsupported operations.

Providers publish through their injected collectors/publishers, not an invented `GetEventsAsync`
method on `IMarketDataClient`. Preserve the real provider `Source`, venue/stream identity, and
provider sequence when available; do not manufacture an upstream sequence or disguise simulated
prices as observed market data. Implement cancellation and resource disposal across the entire path.

Connection readiness must reflect provider-specific authentication and subscription acknowledgments,
not merely an open socket. Test failed authentication, unsupported subscriptions, disconnect/reconnect,
and restoration of acknowledged subscriptions with deterministic fixtures.

## 2. Historical (Backfill) Providers (`IHistoricalDataProvider`)

Use the current
[`IHistoricalDataProvider`](../../src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs)
and existing family implementations. `BaseHistoricalDataProvider` provides shared HTTP/backfill
behavior where appropriate; preserve its validation, pacing, and error semantics rather than
copying an incomplete stand-alone HTTP example.

Implement `GetDailyBarsAsync` and only advertise extended capabilities that the adapter actually
implements. Default quote/trade/auction methods can return empty results; this is not evidence that
those capabilities work. Preserve `IsSimulated` and per-bar provenance through composite providers.

Define interval, timestamp, adjustment, symbol, ordering, pagination, and empty-result semantics.
Test authentication failure, throttling, malformed rows, incomplete pages, and cancellation distinctly
from a verified empty response. Repository pacing values are not a guarantee of commercial quotas.

### Registration in CompositeHistoricalDataProvider

Do not assume that adding a service to DI makes the composite discover it. Add the compatible
historical descriptor and its typed factory, or use the established module-owned factory path;
verify the configured host actually supplies the intended provider to historical orchestration.
Test enabled, disabled, missing-credential, and fallback behavior at that composition boundary.

## 3. Symbol Search Providers (`ISymbolSearchProvider`)

Implement the search contract consumed by the shared search service. Keep exact identifiers,
provider attribution, asset/exchange filters, limit behavior, and unsupported filters explicit.
Identifier resolution through `ISymbolResolver` is a different contract and does not fill this slot.
Put provider-specific implementations in the provider-family folder, not `Core/`.

## 4. Unified DataSource Hierarchy (`IDataSource`)

The `IDataSource` / `IRealtimeDataSource` / `IHistoricalDataSource` hierarchy remains a separate
compatibility surface. Use `DataSourceRegistry` and `IProviderModule` only for the registration path
that actually consumes them. Do not label this hierarchy a universal replacement for shared adapter
contracts or infer that an attributed class has a production factory.

### `IProviderModule`

The catalog distinguishes descriptor-owned and module-owned factories. Its registration enumeration
withholds module-owned factories until successful module registration publishes them. FRED is an
existing example of this boundary. Preserve final configuration/credential checks even when a
module has been discovered; discovery alone must not bypass activation controls.

## 5. `IProviderMetadata` — Unified Provider Discovery

Keep declared capabilities aligned with compatible types, typed factories, and the data each
operation actually returns. Broad family instrument coverage does not imply identical streaming,
search, and options coverage. Add explicit exclusions for unsupported interfaces rather than
inventing placeholder implementations or promoting mapper-only assets.

The catalog checks type compatibility and requires a factory for advertised descriptor-owned
slots. A factory returning `null` means that capability is disabled by configuration or credentials;
it does not authorize substituting another provider family. Exercise these refusals in tests.

## 6. Credential Management

Follow [Provider Credential Operations](../operators/provider-credentials.md). Catalog-managed
providers use the encrypted store; a selected scoped record owns its complete credential set.
Missing fields must not be filled from another account's environment/configuration. Development/Test
or explicitly enabled migration fallback is separate and is disabled by default in packaged/customer
builds. Saving configuration or credentials does not itself activate a host runtime binding.

Do not copy the former universal Vault → File → Environment → Config precedence recipe into a new
adapter. Verify the host's actual credential context, connection ownership, account/environment,
and enabled state. Use synthetic credentials and deterministic failure fixtures for routine tests;
live credentials are a separate, explicitly authorized validation concern. Never commit secrets.

## 7. Channel Buffering (`EventPipelinePolicy`)

Use the [current policy](../../src/Meridian.Core/Pipeline/EventPipelinePolicy.cs), choosing behavior
according to loss tolerance rather than applying `HighThroughput` indiscriminately:

| Preset | Overflow behavior and intended boundary |
| --- | --- |
| `DurableStreaming` | `Wait`; persistence-critical streaming where producers must observe backpressure. |
| `HighThroughput`, `Default`, `MessageBuffer` | `DropOldest`; intentionally lossy queues, not proof of durable delivery. |
| `MaintenanceQueue`, `CompletionQueue` | `Wait`; work/completion notifications must not be silently discarded. |
| `Logging` | `DropOldest`; not a durable business-event journal. |

A `Wait` channel requires handling a failed `TryWrite` or using an appropriate cancellable asynchronous
write; never ignore the result. Do not block provider callbacks on synchronous waits. Test overload
and cancellation through the chosen publisher path. The
[event pipeline](../../src/Meridian.Application/Pipeline/EventPipeline.cs) explicitly separates
in-memory admission from WAL/sink durability; queued does not mean persisted.

## 8. Configuration

Extend the real configuration type, binding/validation path, and sample only where the new provider
needs them. An arbitrary environment variable or JSON property is not automatically consumed by
`ProviderFactory`. Inspect the analogous configured family and prove both accepted and rejected
settings. Keep secrets out of configuration samples and distinguish paper, live, and simulated modes.

## 9. Directory Structure

```text
src/Meridian.Infrastructure/Adapters/
  Core/                 # shared catalog, factories, historical/search abstractions
  Alpaca/               # family-specific adapters and supporting partial classes
  Fred/                 # includes the module-owned registration example
  <ProviderName>/       # add the new family's concrete implementation here
  Failover/             # composite orchestration, not a new vendor family
  Templates/            # scaffold assets, not runtime provider activation
```

Consult the capability matrix for the complete folder inventory. Do not recreate obsolete layered
adapter directories or assume a provider mentioned by an old design has an implemented adapter.

## 10. Testing Providers

Cover type/descriptor/factory parity, configured construction, disabled or incomplete credentials,
source and simulation identity, data normalization, boundary failures, and cancellation. Streaming
needs acknowledgment/reconnect/overload cases; historical paths need ordering, range, pagination,
and partial-response cases. Brokerage needs host-registration and operation-specific tests.

Keep corporate-action release status and acceptance evidence separate from data retrieval. A
review-only provider must not gain acceptance authority through consensus or command content.
Use the source and test links in the capability matrix to extend the relevant existing suites.
A fixture pass proves the tested synthetic behavior, not live connectivity or account entitlements.

## 11. Step-by-Step: Adding a New Provider

1. Identify the consuming contract and source-owned family scope; read the nearest source README.
2. Implement the adapter in its direct family folder with truthful metadata and provenance.
3. Add compatible catalog types and typed factories, or the established module-owned path.
4. Wire actual host/configuration/credential consumption and test refusal as well as success.
5. Extend focused regression tests and the source-owned adapter readiness registry; regenerate its
   output rather than editing the generated matrix. Update capability and operator guidance.
6. Run risk-appropriate checks through [Engineering](../engineering/README.md), record the tested
   revision and limitations, and leave merge or sensitive activation to the owner and repository rules.

## Performance Considerations

Measure the actual provider/publisher path before changing batching, allocations, compression, or
pacing. A faster lossy queue is not an improvement to a durability contract. Keep vendor/account
limits distinct from repository defaults and avoid unmeasured throughput claims.

## Common Mistakes to Avoid

Do not confuse a metadata flag with an implementation, a descriptor with an enabled instance,
a socket with an authenticated subscription, an empty result with complete coverage, or a test pass
with production acceptance. Do not replace a scoped credential failure with another account's secret.

## Related Documentation

- [Provider capability matrix](../reference/provider-capability-matrix.md)
- [Credential operations](../operators/provider-credentials.md)
- [Engineering and validation](../engineering/README.md)
- [AI provider guide](../ai/claude/CLAUDE.providers.md)
- [Storage durability guide](../ai/claude/CLAUDE.storage.md)
