# Meridian Documentation

**Last Reviewed:** 2026-04-05
**Scope:** Active hand-authored documentation plus generated status and reference entry points

This index is the main entry point for the active `docs/` tree. It is organized around the current Meridian product shape: a shared platform that now spans research, trading, data operations, governance, provider readiness, and both web and WPF workstation surfaces.

## Platform At A Glance

Meridian's current solution includes:

- a console and host application in `src/Meridian/`
- application, contracts, core, domain, infrastructure, and storage layers in `src/Meridian.Application/`, `src/Meridian.Contracts/`, `src/Meridian.Core/`, `src/Meridian.Domain/`, `src/Meridian.Infrastructure/`, `src/Meridian.Infrastructure.CppTrader/`, and `src/Meridian.Storage/`
- execution, provider, risk, strategy, and backtesting seams in `src/Meridian.Execution*/`, `src/Meridian.ProviderSdk/`, `src/Meridian.Risk/`, `src/Meridian.Strategies/`, and `src/Meridian.Backtesting*/`
- ledger, direct-lending, and F# support projects in `src/Meridian.Ledger/`, `src/Meridian.FSharp*/`, and `src/Meridian.IbApi.SmokeStub/`
- web and desktop UI surfaces in `src/Meridian.Ui/`, `src/Meridian.Ui.Shared/`, `src/Meridian.Ui.Services/`, and `src/Meridian.Wpf/`
- scripting and MCP surfaces in `src/Meridian.QuantScript/`, `src/Meridian.Mcp/`, and `src/Meridian.McpServer/`

## Start Here

- **First local setup:** [Getting Started Guide](getting-started/README.md)
- **Operator reference:** [Help and FAQ](HELP.md)
- **Operational procedures:** [Operator Runbook](operations/operator-runbook.md)
- **Docs navigation by folder:** [Plans Overview](plans/README.md), [Status Docs Index](status/README.md), [Architecture Docs](architecture/README.md), [Development Guides](development/README.md)
- **Current roadmap snapshot:** [Combined Roadmap](status/ROADMAP_COMBINED.md)
- **Current delivery plan:** [Project Roadmap](status/ROADMAP.md)
- **Target product narrative:** [Target End Product](status/TARGET_END_PRODUCT.md)

## Documentation Zones

| Zone | Folders | Audience |
|------|---------|----------|
| Product | `getting-started/`, `providers/`, `operations/` | Users and operators |
| Engineering | `architecture/`, `adr/`, `development/`, `integrations/`, `reference/`, `diagrams/`, `ai/` | Developers and tool authors |
| Governance | `status/`, `plans/`, `evaluations/`, `audits/`, `security/` | Core team and stakeholders |

`generated/` and any file marked as auto-generated should be refreshed by script rather than edited by hand. `docs/_site/` is the built documentation site output.

## By Audience

### Users and operators

- [Getting Started](getting-started/README.md)
- [Workflow Guide](WORKFLOW_GUIDE.md)
- [Help and FAQ](HELP.md)
- [Provider Setup Guides](providers/README.md)
- [Operator Runbook](operations/operator-runbook.md)
- [Deployment Guide](operations/deployment.md)
- [Service Level Objectives](operations/service-level-objectives.md)

### Developers

- [Repository Organization Guide](development/repository-organization-guide.md)
- [Repository Rule Set](development/repository-rule-set.md)
- [Provider Implementation Guide](development/provider-implementation.md)
- [Desktop Testing Guide](development/desktop-testing-guide.md)
- [Documentation Contribution Guide](development/documentation-contribution-guide.md)
- [Architecture Overview](architecture/overview.md)
- [AI Assistant Resources](ai/README.md)

### Architecture and design

- [Architecture Overview](architecture/overview.md)
- [Layer Boundaries](architecture/layer-boundaries.md)
- [Storage Design](architecture/storage-design.md)
- [Ledger Architecture](architecture/ledger-architecture.md)
- [Desktop Layers](architecture/desktop-layers.md)
- [WPF Shell MVVM](architecture/wpf-shell-mvvm.md)
- [ADRs](adr/README.md)

### Status and planning

- [Combined Roadmap](status/ROADMAP_COMBINED.md)
- [Project Roadmap](status/ROADMAP.md)
- [Opportunity Scan](status/OPPORTUNITY_SCAN.md)
- [Target End Product](status/TARGET_END_PRODUCT.md)
- [Feature Inventory](status/FEATURE_INVENTORY.md)
- [Provider Validation Matrix](status/provider-validation-matrix.md)
- [Plans Overview](plans/README.md)
- [Improvements Tracker](status/IMPROVEMENTS.md)
- [Production Status](status/production-status.md)

## Current Planning Source Of Truth

Use these documents together when planning implementation:

1. [status/ROADMAP_COMBINED.md](status/ROADMAP_COMBINED.md) for the shortest complete roadmap snapshot
2. [status/ROADMAP.md](status/ROADMAP.md) for the full wave-structured delivery plan
3. [status/OPPORTUNITY_SCAN.md](status/OPPORTUNITY_SCAN.md) for prioritized repo-grounded opportunities
4. [status/TARGET_END_PRODUCT.md](status/TARGET_END_PRODUCT.md) for the intended finished product narrative
5. [plans/README.md](plans/README.md) for the active blueprint and roadmap catalog
6. [plans/meridian-6-week-roadmap.md](plans/meridian-6-week-roadmap.md) for the current time-boxed execution plan
7. [plans/trading-workstation-migration-blueprint.md](plans/trading-workstation-migration-blueprint.md) for workstation structure and migration phases
8. [plans/governance-fund-ops-blueprint.md](plans/governance-fund-ops-blueprint.md) for governance, Security Master, reconciliation, and reporting direction
9. [plans/backtest-studio-unification-blueprint.md](plans/backtest-studio-unification-blueprint.md) for the Wave 4 backtesting unification target
10. [status/FEATURE_INVENTORY.md](status/FEATURE_INVENTORY.md) for capability status by area
11. [status/provider-validation-matrix.md](status/provider-validation-matrix.md) for provider-readiness evidence and gaps
12. [status/production-status.md](status/production-status.md) for current readiness caveats
13. [status/IMPROVEMENTS.md](status/IMPROVEMENTS.md) for tracked implementation themes

## Verified Build And Run References

These commands are currently reflected in the repo's code and build scripts:

- `make help`
- `make setup-dev`
- `make run`
- `make run-ui`
- `make run-backfill`
- `make run-selftest`
- `dotnet run --project src/Meridian/Meridian.csproj -- --quickstart`
- `dotnet run --project src/Meridian/Meridian.csproj -- --mode web --http-port 8080`
- `dotnet run --project src/Meridian/Meridian.csproj -- --validate-config`
- `dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableFullWpfBuild=true`
- `npm --prefix src/Meridian.Ui/dashboard run build`

## Reference

- [API Reference](reference/api-reference.md)
- [Data Dictionary](reference/data-dictionary.md)
- [Environment Variables](reference/environment-variables.md)
- [Dependencies Reference](DEPENDENCIES.md)
- [Generated Documentation](generated/README.md)

## Archive

Historical and superseded material now lives outside the active docs tree under `archive/docs/`.

- [Archive index](../archive/docs/INDEX.md)
- [Archive overview](../archive/docs/README.md)

## Maintenance Checklist

When documentation changes in a PR:

1. Update this index if the main navigation or source-of-truth set changes.
2. Keep `status/ROADMAP*.md`, `status/OPPORTUNITY_SCAN.md`, `status/TARGET_END_PRODUCT.md`, `status/FEATURE_INVENTORY.md`, and the relevant blueprint docs aligned.
3. Prefer updating folder `README.md` files when you add or retire documents.
4. Avoid editing generated docs by hand unless you are also updating the generator.
