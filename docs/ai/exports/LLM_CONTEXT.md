# Meridian LLM Context

**Status:** generated
**Owner:** core-team
**Reviewed:** 2026-07-12

Generated at: `2026-07-12T02:52:49Z`

## Active Scope

Meridian proves operational records from source evidence to governed output.

### Proof Chain

- source evidence
- normalized record
- validation
- reconciliation
- exception resolution
- journal / ledger impact
- capital account impact
- close package
- report line
- delivery record
- audit evidence

### Active Operator Workspaces

`Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`

### Shared Seams

- `src/Meridian.Ui.Services/`
- `src/Meridian.Ui.Shared/`
- `src/Meridian.Contracts/`

### Deferred By Default

- mobile applications
- full live trading
- full payment execution
- broad client portal
- no-code workflow builder
- forecasting or enterprise-risk surfaces detached from operational records

## Recommended Load Order

- `docs/ai/navigation/README.md`
- `docs/ai/generated/repo-navigation.md`
- `docs/product/meridian-design-document.md`
- `docs/architecture/meridian-development-intelligence-framework.md`
- `docs/architecture/meridian-vision.md`
- `docs/architecture/meridian-domain-model.md`
- `docs/ai/context/*.md relevant to the task`
- `docs/domain/*.md relevant to the task`

## MDIF Sources

### Project Constitution

- [Meridian Development Intelligence Framework (MDIF)](../../architecture/meridian-development-intelligence-framework.md)
- [Meridian Domain Model](../../architecture/meridian-domain-model.md)
- [Meridian Vision](../../architecture/meridian-vision.md)

### Domain Dictionary

- [Fund Event](../../domain/fund-event.md)
- [Operational Evidence Graph](../../domain/operational-evidence-graph.md)
- [Security](../../domain/security.md)

### AI Context Packs

- [Accounting Context](../../ai/context/accounting-context.md)
- [Operational Evidence Context](../../ai/context/operational-evidence-context.md)

### Decision Records

- [ADR-017: Modular Operational Monolith](../../adr/017-modular-operational-monolith.md)
- [ADR-018: Declarative Statement Mapping Profiles and the Statement Connector Library](../../adr/018-declarative-statement-mapping-profiles.md)
- [Architectural Decision Records (ADRs)](../../adr/README.md)
- [ADR-XXX: [Title]](../../adr/_template.md)

## Projects

- `Meridian` - `src/Meridian/Meridian.csproj` (net10.0)
- `Meridian.Application` - `src/Meridian.Application/Meridian.Application.csproj` (net10.0)
- `Meridian.Audit` - `src/Meridian.Audit/Meridian.Audit.csproj` (net10.0)
- `Meridian.Backtesting` - `src/Meridian.Backtesting/Meridian.Backtesting.csproj` (net10.0)
- `Meridian.Backtesting.Sdk` - `src/Meridian.Backtesting.Sdk/Meridian.Backtesting.Sdk.csproj` (net10.0)
- `Meridian.Contracts` - `src/Meridian.Contracts/Meridian.Contracts.csproj` (net10.0)
- `Meridian.Core` - `src/Meridian.Core/Meridian.Core.csproj` (net10.0)
- `Meridian.DataIntegration` - `src/Meridian.DataIntegration/Meridian.DataIntegration.csproj` (net10.0)
- `Meridian.Documents` - `src/Meridian.Documents/Meridian.Documents.csproj` (net10.0)
- `Meridian.Domain` - `src/Meridian.Domain/Meridian.Domain.csproj` (net10.0)
- `Meridian.Entities` - `src/Meridian.Entities/Meridian.Entities.csproj` (net10.0)
- `Meridian.Execution` - `src/Meridian.Execution/Meridian.Execution.csproj` (net10.0)
- `Meridian.Execution.Sdk` - `src/Meridian.Execution.Sdk/Meridian.Execution.Sdk.csproj` (net10.0)
- `Meridian.FinancialOperations` - `src/Meridian.FinancialOperations/Meridian.FinancialOperations.csproj` (net10.0)
- `Meridian.IbApi.SmokeStub` - `src/Meridian.IbApi.SmokeStub/Meridian.IbApi.SmokeStub.csproj` (net10.0)
- `Meridian.Identity` - `src/Meridian.Identity/Meridian.Identity.csproj` (net10.0)
- `Meridian.Infrastructure` - `src/Meridian.Infrastructure/Meridian.Infrastructure.csproj` (net10.0)
- `Meridian.Instruments` - `src/Meridian.Instruments/Meridian.Instruments.csproj` (net10.0)
- `Meridian.Ledger` - `src/Meridian.Ledger/Meridian.Ledger.csproj` (net10.0)
- `Meridian.Mcp` - `src/Meridian.Mcp/Meridian.Mcp.csproj` (net10.0)
- `Meridian.Platform` - `src/Meridian.Platform/Meridian.Platform.csproj` (net10.0)
- `Meridian.PortfolioRecords` - `src/Meridian.PortfolioRecords/Meridian.PortfolioRecords.csproj` (net10.0)
- `Meridian.ProviderSdk` - `src/Meridian.ProviderSdk/Meridian.ProviderSdk.csproj` (net10.0)
- `Meridian.QuantScript` - `src/Meridian.QuantScript/Meridian.QuantScript.csproj` (net10.0)
- `Meridian.ReferenceData` - `src/Meridian.ReferenceData/Meridian.ReferenceData.csproj` (net10.0)
- `Meridian.Reporting` - `src/Meridian.Reporting/Meridian.Reporting.csproj` (net10.0)
- `Meridian.Risk` - `src/Meridian.Risk/Meridian.Risk.csproj` (net10.0)
- `Meridian.Storage` - `src/Meridian.Storage/Meridian.Storage.csproj` (net10.0)
- `Meridian.Strategies` - `src/Meridian.Strategies/Meridian.Strategies.csproj` (net10.0)
- `Meridian.Ui.Services` - `src/Meridian.Ui.Services/Meridian.Ui.Services.csproj` (net10.0)
- `Meridian.Ui.Shared` - `src/Meridian.Ui.Shared/Meridian.Ui.Shared.csproj` (net10.0)
- `Meridian.Workflow` - `src/Meridian.Workflow/Meridian.Workflow.csproj` (net10.0)
- `Meridian.Wpf` - `src/Meridian.Wpf/Meridian.Wpf.csproj` (net10.0, net10.0-windows10.0.19041.0)
- `Meridian.Backtesting.Tests` - `tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj` (net10.0)
- `Meridian.DesignModules.Tests` - `tests/Meridian.DesignModules.Tests/Meridian.DesignModules.Tests.csproj` (net10.0)
- `Meridian.DirectLending.Tests` - `tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj` (net10.0)
- `Meridian.FundStructure.Tests` - `tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj` (net10.0)
- `Meridian.QuantScript.Tests` - `tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj` (net10.0)
- `Meridian.TestSupport` - `tests/Meridian.TestSupport/Meridian.TestSupport.csproj` (net10.0)
- `Meridian.Tests` - `tests/Meridian.Tests/Meridian.Tests.csproj` (net10.0)
- `Meridian.Ui.Tests` - `tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj` (net10.0)
- `Meridian.Wpf.Tests` - `tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj` (net10.0, net10.0-windows10.0.19041.0)
