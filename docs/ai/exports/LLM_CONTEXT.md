# Meridian LLM Context

**Status:** generated
**Owner:** core-team
**Reviewed:** 2026-06-16

Generated at: `2026-06-16T03:31:18Z`

## MDIF Sources

### Project Constitution

- [Meridian Development Intelligence Framework (MDIF)](docs/architecture/meridian-development-intelligence-framework.md)
- [Meridian Domain Model](docs/architecture/meridian-domain-model.md)
- [Meridian Vision](docs/architecture/meridian-vision.md)

### Domain Dictionary

- [Domain Dictionary](docs/domain/README.md)
- [Security](docs/domain/security.md)

### AI Context Packs

- [Accounting Context](docs/ai/context/accounting-context.md)

### Decision Records

- [Meridian ADR: ADR-001: Provider Abstraction Pattern](docs/adr/001-provider-abstraction.md)
- [Meridian ADR: ADR-002: Tiered Storage Architecture](docs/adr/002-tiered-storage-architecture.md)
- [Meridian ADR: ADR-003: Microservices Decomposition](docs/adr/003-microservices-decomposition.md)
- [Meridian ADR: ADR-004: Async Streaming Patterns](docs/adr/004-async-streaming-patterns.md)
- [Meridian ADR: ADR-005: Attribute-Based Provider Discovery](docs/adr/005-attribute-based-discovery.md)
- [Meridian ADR: ADR-006: Domain Events Polymorphic Payload Pattern](docs/adr/006-domain-events-polymorphic-payload.md)
- [Meridian ADR: ADR-007: Write-Ahead Log (WAL) + Event Pipeline Durability](docs/adr/007-write-ahead-log-durability.md)
- [Meridian ADR: ADR-008: Multi-Format Composite Storage Sink Pattern](docs/adr/008-multi-format-composite-storage.md)
- [Meridian ADR: ADR-009: F# Type-Safe Domain with C# Interop Bridge](docs/adr/009-fsharp-interop.md)
- [Meridian ADR: ADR-010: HttpClientFactory for HTTP Client Lifecycle Management](docs/adr/010-httpclient-factory.md)
- [Meridian ADR: ADR-011: Centralized Configuration and Credential Management](docs/adr/011-centralized-configuration-and-credentials.md)
- [Meridian ADR: ADR-012: Unified Monitoring and Alerting Pipeline](docs/adr/012-monitoring-and-alerting-pipeline.md)
- [Meridian ADR: ADR-013: Bounded Channel Pipeline Policy with Backpressure](docs/adr/013-bounded-channel-policy.md)
- [Meridian ADR: ADR-014: High-Performance JSON Serialization via Source Generators](docs/adr/014-json-source-generators.md)
- [Meridian ADR: ADR-015: Strategy Execution Contract](docs/adr/015-strategy-execution-contract.md)
- [Meridian ADR: ADR 016: Distinct custody-position and cash reconciliation breaks with shared workflow envelope](docs/adr/016-custody-cash-reconciliation-break-typing.md)
- [Meridian ADR: ADR-016: Platform Architecture Migration Mandate](docs/adr/016-platform-architecture-migration.md)
- [ADR-017: Modular Operational Monolith](docs/adr/017-modular-operational-monolith.md)
- [Architectural Decision Records (ADRs) — migration index](docs/adr/README.md)
- [Meridian ADR: ADR-XXX: [Title]](docs/adr/_template.md)

## Projects

- `Meridian` — `src/Meridian/Meridian.csproj` (net10.0)
- `Meridian.Application` — `src/Meridian.Application/Meridian.Application.csproj` (net10.0)
- `Meridian.Audit` — `src/Meridian.Audit/Meridian.Audit.csproj` (net10.0)
- `Meridian.Backtesting` — `src/Meridian.Backtesting/Meridian.Backtesting.csproj` (net10.0)
- `Meridian.Backtesting.Sdk` — `src/Meridian.Backtesting.Sdk/Meridian.Backtesting.Sdk.csproj` (net10.0)
- `Meridian.Contracts` — `src/Meridian.Contracts/Meridian.Contracts.csproj` (net10.0)
- `Meridian.Core` — `src/Meridian.Core/Meridian.Core.csproj` (net10.0)
- `Meridian.DataIntegration` — `src/Meridian.DataIntegration/Meridian.DataIntegration.csproj` (net10.0)
- `Meridian.Documents` — `src/Meridian.Documents/Meridian.Documents.csproj` (net10.0)
- `Meridian.Domain` — `src/Meridian.Domain/Meridian.Domain.csproj` (net10.0)
- `Meridian.Entities` — `src/Meridian.Entities/Meridian.Entities.csproj` (net10.0)
- `Meridian.Execution` — `src/Meridian.Execution/Meridian.Execution.csproj` (net10.0)
- `Meridian.Execution.Sdk` — `src/Meridian.Execution.Sdk/Meridian.Execution.Sdk.csproj` (net10.0)
- `Meridian.FinancialOperations` — `src/Meridian.FinancialOperations/Meridian.FinancialOperations.csproj` (net10.0)
- `Meridian.IbApi.SmokeStub` — `src/Meridian.IbApi.SmokeStub/Meridian.IbApi.SmokeStub.csproj` (net10.0)
- `Meridian.Identity` — `src/Meridian.Identity/Meridian.Identity.csproj` (net10.0)
- `Meridian.Infrastructure` — `src/Meridian.Infrastructure/Meridian.Infrastructure.csproj` (net10.0)
- `Meridian.Instruments` — `src/Meridian.Instruments/Meridian.Instruments.csproj` (net10.0)
- `Meridian.Ledger` — `src/Meridian.Ledger/Meridian.Ledger.csproj` (net10.0)
- `Meridian.Mcp` — `src/Meridian.Mcp/Meridian.Mcp.csproj` (net10.0)
- `Meridian.Platform` — `src/Meridian.Platform/Meridian.Platform.csproj` (net10.0)
- `Meridian.PortfolioRecords` — `src/Meridian.PortfolioRecords/Meridian.PortfolioRecords.csproj` (net10.0)
- `Meridian.ProviderSdk` — `src/Meridian.ProviderSdk/Meridian.ProviderSdk.csproj` (net10.0)
- `Meridian.QuantScript` — `src/Meridian.QuantScript/Meridian.QuantScript.csproj` (net10.0)
- `Meridian.ReferenceData` — `src/Meridian.ReferenceData/Meridian.ReferenceData.csproj` (net10.0)
- `Meridian.Reporting` — `src/Meridian.Reporting/Meridian.Reporting.csproj` (net10.0)
- `Meridian.Risk` — `src/Meridian.Risk/Meridian.Risk.csproj` (net10.0)
- `Meridian.Storage` — `src/Meridian.Storage/Meridian.Storage.csproj` (net10.0)
- `Meridian.Strategies` — `src/Meridian.Strategies/Meridian.Strategies.csproj` (net10.0)
- `Meridian.Ui.Services` — `src/Meridian.Ui.Services/Meridian.Ui.Services.csproj` (net10.0)
- `Meridian.Ui.Shared` — `src/Meridian.Ui.Shared/Meridian.Ui.Shared.csproj` (net10.0)
- `Meridian.Workflow` — `src/Meridian.Workflow/Meridian.Workflow.csproj` (net10.0)
- `Meridian.Wpf` — `src/Meridian.Wpf/Meridian.Wpf.csproj` (net10.0, net10.0-windows10.0.19041.0)
- `Meridian.Backtesting.Tests` — `tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj` (net10.0)
- `Meridian.DesignModules.Tests` — `tests/Meridian.DesignModules.Tests/Meridian.DesignModules.Tests.csproj` (net10.0)
- `Meridian.DirectLending.Tests` — `tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj` (net10.0)
- `Meridian.FundStructure.Tests` — `tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj` (net10.0)
- `Meridian.QuantScript.Tests` — `tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj` (net10.0)
- `Meridian.Tests` — `tests/Meridian.Tests/Meridian.Tests.csproj` (net10.0)
- `Meridian.Ui.Tests` — `tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj` (net10.0)
- `Meridian.Wpf.Tests` — `tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj` (net10.0, net10.0-windows10.0.19041.0)
