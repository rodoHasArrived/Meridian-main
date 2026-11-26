# Historical Architectural Decision Records

**Status:** archived
**Owner:** core-team
**Reviewed:** 2026-11-12

These eleven records are retained because current source still names their IDs through
`ImplementsAdr` attributes. They were recovered from the
[complete historical ADR snapshot](https://github.com/rodoHasArrived/Meridian-main/tree/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr).
Their decision text, original status, and dates are preserved; relative references to material
outside this restored set now point to that immutable snapshot. Recovery does not reapprove a
decision or establish that every historical implementation claim remains true.

Use the [current ADR index](../../../docs/adr/README.md),
[architecture references](../../../docs/architecture/README.md), and
[Engineering](../../../docs/engineering/README.md) for maintained guidance.

| Historical record | Example current source reference |
| --- | --- |
| [001: Provider Abstraction Pattern](001-provider-abstraction.md) | [BackfillCoordinator](../../../src/Meridian.Application/Backfill/BackfillCoordinator.cs) |
| [002: Tiered Storage Architecture](002-tiered-storage-architecture.md) | [PortableDataPackager](../../../src/Meridian.Storage/Packaging/PortableDataPackager.cs) |
| [004: Async Streaming Patterns](004-async-streaming-patterns.md) | [AlpacaBrokerageGateway](../../../src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs) |
| [005: Attribute-Based Provider Discovery](005-attribute-based-discovery.md) | [AlpacaBrokerageGateway](../../../src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs) |
| [006: Domain Events Polymorphic Payload Pattern](006-domain-events-polymorphic-payload.md) | [OptionDataCollector](../../../src/Meridian.Domain/Collectors/OptionDataCollector.cs) |
| [007: Write-Ahead Log and Event Pipeline Durability](007-write-ahead-log-durability.md) | [DeadLetterSink](../../../src/Meridian.Application/Pipeline/DeadLetterSink.cs) |
| [010: HttpClientFactory for HTTP Client Lifecycle Management](010-httpclient-factory.md) | [ConnectivityProbeService](../../../src/Meridian.Application/Services/ConnectivityProbeService.cs) |
| [013: Bounded Channel Pipeline Policy with Backpressure](013-bounded-channel-policy.md) | [OrderManagementSystem](../../../src/Meridian.Execution/OrderManagementSystem.cs) |
| [014: High-Performance JSON Serialization via Source Generators](014-json-source-generators.md) | [RobinhoodOptionsChainProvider](../../../src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodOptionsChainProvider.cs) |
| [015: Strategy Execution Contract](015-strategy-execution-contract.md) | [BrokerageGatewayAdapter](../../../src/Meridian.Execution/Adapters/BrokerageGatewayAdapter.cs) |
| [016: Platform Architecture Migration Mandate](016-platform-architecture-migration.md) | [StrategyLifecycleManager](../../../src/Meridian.Strategies/Services/StrategyLifecycleManager.cs) |

The historical snapshot also contains a second record numbered 016,
[custody-position and cash reconciliation break typing](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/016-custody-cash-reconciliation-break-typing.md).
The restored platform-architecture record is the one named by `StrategyLifecycleManager`'s
attribute description. The duplicate historical number is retained in provenance rather than
silently renumbered or treated as a new decision.
