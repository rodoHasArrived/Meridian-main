# Meridian LLM Context

**Status:** generated
**Owner:** core-team
**Reviewed:** 2026-10-31

Generated at: `2026-10-31T05:29:52Z`

## Active Scope

Meridian connects financial operations, strategy formulation and validation, order management, and governed strategy execution through retained evidence and shared authority.

Scope decision: `DEC-TRADING-STRATEGY-DIRECTION-001`. Planned delivery: `W13-STRAT-001`, `W13-OMS-001`.

The strategy and OMS decision adopts product direction only. The W13 rows remain planned and unranked; supported modes, accounts, brokers, instruments, and order types require their own implementation and acceptance evidence. Promotion, account authority, risk controls, readiness, and runtime support remain separate admission checks. The trusted-close package is a bounded financial outcome, not a development ceiling.

Follow docs/ai/solo-developer-workflow.md: default to deterministic synthetic data, focused checks, and separate AI critique when risk warrants it. Ordinary development does not require live accounts, institutional operators, or production certification. AI critique is not independent human approval. The developer retains merge and sensitive activation decisions, and product approvals and required GitHub checks remain in force.

### Financial Proof Chain

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

### Strategy and Execution Evidence

- retained strategy definition and inputs
- reproducible validation and run evidence
- version-bound review and promotion
- account-scoped order intent and risk admission
- OMS lifecycle and confirmed broker outcomes
- recovery, reconciliation, and governed economic handoff

Manual orders require explicit actor/account authority and retain their true origin; they do not fabricate strategy lineage. Accounting may start from external operational evidence, and research may end with retained analysis.

### Active Operator Workspaces

`Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`

### Shared Seams

- `src/Meridian.Ui.Services/`
- `src/Meridian.Ui.Shared/`
- `src/Meridian.Contracts/`

### Deferred By Default

- mobile applications
- full payment execution
- broad client portal
- no-code workflow builder
- forecasting or enterprise-risk surfaces detached from operational records

## Recommended Load Order

- `docs/ai/solo-developer-workflow.md`
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

- [Account and Book Scopes](../../domain/account-and-book-scopes.md)
- [Account oversight evidence](../../domain/account-oversight-evidence.md)
- [Brokerage Account Snapshot](../../domain/brokerage-account-snapshot.md)
- [Business-Day Calendar](../../domain/business-day-calendar.md)
- [Contractual Schedule](../../domain/contractual-schedule.md)
- [Corporate Action Case](../../domain/corporate-action-case.md)
- [Credit Support](../../domain/credit-support.md)
- [Fund Event](../../domain/fund-event.md)
- [Intercompany consolidation — W10-CONSOL-001 first slice](../../domain/intercompany-consolidation.md)
- [Market Curve](../../domain/market-curve.md)
- [Operational Evidence Graph](../../domain/operational-evidence-graph.md)
- [OTC Contract Lifecycle](../../domain/otc-contract-lifecycle.md)
- [Recurring Journal](../../domain/recurring-journal.md)
- [Security asset-pack admission](../../domain/security-asset-pack-admission.md)
- [Security](../../domain/security.md)
- [Strategy, Orders, and Execution](../../domain/strategy-order-execution.md)

### AI Context Packs

- [Accounting Context](../../ai/context/accounting-context.md)
- [Operational Evidence Context](../../ai/context/operational-evidence-context.md)

### Decision Records

- [ADR-017: Modular Operational Monolith](../../adr/017-modular-operational-monolith.md)
- [ADR-018: Declarative Statement Mapping Profiles and the Statement Connector Library](../../adr/018-declarative-statement-mapping-profiles.md)
- [ADR-019: Production Support Matrix and Typed Deployment Posture](../../adr/019-production-support-matrix-and-deployment-posture.md)
- [ADR-020: Local Lifecycle Control Plane](../../adr/020-lifecycle-control-plane.md)
- [ADR-021: Verified Operation Outcomes and Operational Case History](../../adr/021-verified-operation-outcomes-and-case-history.md)
- [ADR-022: Canonical Asset-Class Homes for Overlapping Instrument Routes](../../adr/022-canonical-asset-class-homes.md)
- [ADR-023: Credential Ownership for Host-Wide Runtime Providers](../../adr/023-host-wide-provider-credential-ownership.md)
- [ADR-024: Terms Payload Format and Version Policy](../../adr/024-terms-payload-format-and-version-policy.md)
- [ADR-025: Contractual Schedule Kernel and Business-Day Calendar Seam](../../adr/025-contractual-schedule-kernel.md)
- [ADR-026: Stored Curves and the Valuation Seam](../../adr/026-stored-curves-and-valuation-seam.md)
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
- `Meridian.Launcher` - `src/Meridian.Launcher/Meridian.Launcher.csproj` (net10.0-windows)
- `Meridian.Ledger` - `src/Meridian.Ledger/Meridian.Ledger.csproj` (net10.0)
- `Meridian.LifecycleSupervisor` - `src/Meridian.LifecycleSupervisor/Meridian.LifecycleSupervisor.csproj` (net10.0-windows)
- `Meridian.Mcp` - `src/Meridian.Mcp/Meridian.Mcp.csproj` (net10.0)
- `Meridian.Platform` - `src/Meridian.Platform/Meridian.Platform.csproj` (net10.0)
- `Meridian.PortfolioRecords` - `src/Meridian.PortfolioRecords/Meridian.PortfolioRecords.csproj` (net10.0)
- `Meridian.ProcessIsolation` - `src/Meridian.ProcessIsolation/Meridian.ProcessIsolation.csproj` (net10.0)
- `Meridian.ProviderSdk` - `src/Meridian.ProviderSdk/Meridian.ProviderSdk.csproj` (net10.0)
- `Meridian.QuantScript` - `src/Meridian.QuantScript/Meridian.QuantScript.csproj` (net10.0)
- `Meridian.QuantScript.Worker` - `src/Meridian.QuantScript.Worker/Meridian.QuantScript.Worker.csproj` (net10.0)
- `Meridian.ReferenceData` - `src/Meridian.ReferenceData/Meridian.ReferenceData.csproj` (net10.0)
- `Meridian.Reporting` - `src/Meridian.Reporting/Meridian.Reporting.csproj` (net10.0)
- `Meridian.Risk` - `src/Meridian.Risk/Meridian.Risk.csproj` (net10.0)
- `Meridian.Setup` - `src/Meridian.Setup/Meridian.Setup.csproj` (net10.0-windows)
- `Meridian.Storage` - `src/Meridian.Storage/Meridian.Storage.csproj` (net10.0)
- `Meridian.Strategies` - `src/Meridian.Strategies/Meridian.Strategies.csproj` (net10.0)
- `Meridian.Ui.Services` - `src/Meridian.Ui.Services/Meridian.Ui.Services.csproj` (net10.0)
- `Meridian.Ui.Shared` - `src/Meridian.Ui.Shared/Meridian.Ui.Shared.csproj` (net10.0)
- `Meridian.Workflow` - `src/Meridian.Workflow/Meridian.Workflow.csproj` (net10.0)
- `Meridian.Wpf` - `src/Meridian.Wpf/Meridian.Wpf.csproj` (net10.0, net10.0-windows10.0.19041.0)
- `Meridian.Backtesting.Tests` - `tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj` (net10.0)
- `Meridian.Contracts.UnitTests` - `tests/Meridian.Contracts.UnitTests/Meridian.Contracts.UnitTests.csproj` (net10.0)
- `Meridian.Core.UnitTests` - `tests/Meridian.Core.UnitTests/Meridian.Core.UnitTests.csproj` (net10.0)
- `Meridian.DesignModules.Tests` - `tests/Meridian.DesignModules.Tests/Meridian.DesignModules.Tests.csproj` (net10.0)
- `Meridian.DirectLending.Tests` - `tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj` (net10.0)
- `Meridian.FundStructure.Tests` - `tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj` (net10.0)
- `Meridian.Lifecycle.Tests` - `tests/Meridian.Lifecycle.Tests/Meridian.Lifecycle.Tests.csproj` (net10.0)
- `Meridian.LifecycleSupervisor.Tests` - `tests/Meridian.LifecycleSupervisor.Tests/Meridian.LifecycleSupervisor.Tests.csproj` (net10.0-windows)
- `Meridian.ProcessTestHelper` - `tests/Meridian.ProcessTestHelper/Meridian.ProcessTestHelper.csproj` (net10.0)
- `Meridian.QuantScript.Tests` - `tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj` (net10.0)
- `Meridian.Setup.Tests` - `tests/Meridian.Setup.Tests/Meridian.Setup.Tests.csproj` (net10.0-windows)
- `Meridian.TestSupport` - `tests/Meridian.TestSupport/Meridian.TestSupport.csproj` (net10.0)
- `Meridian.Tests` - `tests/Meridian.Tests/Meridian.Tests.csproj` (net10.0)
- `Meridian.Ui.Tests` - `tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj` (net10.0)
- `Meridian.Wpf.Tests` - `tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj` (net10.0, net10.0-windows10.0.19041.0)
