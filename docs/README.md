# Meridian Documentation

**Last Reviewed:** 2026-05-13
**Scope:** Active hand-authored documentation plus generated status and reference entry points

This index is the main entry point for the active `docs/` tree. It is organized around Meridian's current direction: an evidence-backed investment operations platform where browser-first operator workflows prove trusted data, research, paper validation, portfolio/accounting review, reconciliation, approvals, and governed reporting outcomes end-to-end.

## Platform At A Glance

Meridian's current solution includes:

- a console and host application in `src/Meridian/`
- application, contracts, core, domain, infrastructure, and storage layers in `src/Meridian.Application/`, `src/Meridian.Contracts/`, `src/Meridian.Core/`, `src/Meridian.Domain/`, `src/Meridian.Infrastructure/`, `src/Meridian.Infrastructure.CppTrader/`, and `src/Meridian.Storage/`
- execution, provider, risk, strategy, and backtesting seams in `src/Meridian.Execution*/`, `src/Meridian.ProviderSdk/`, `src/Meridian.Risk/`, `src/Meridian.Strategies/`, and `src/Meridian.Backtesting*/`
- ledger, direct-lending, and F# support projects in `src/Meridian.Ledger/`, `src/Meridian.FSharp*/`, and `src/Meridian.IbApi.SmokeStub/`
- operator UI surfaces in `src/Meridian.Ui/dashboard/`, `src/Meridian.Ui/wwwroot/workstation/`, `src/Meridian.Ui.Shared/`, and `src/Meridian.Ui.Services/` with retained desktop compatibility in `src/Meridian.Wpf/`
- scripting and MCP surfaces in `src/Meridian.QuantScript/`, `src/Meridian.Mcp/`, and `src/Meridian.McpServer/`

## Start Here

- **First local setup:** [Getting Started Guide](getting-started/README.md)
- **Operator reference:** [Help and FAQ](HELP.md)
- **Operational procedures:** [Operator Runbook](operations/operator-runbook.md)
- **Docs navigation by folder:** [Plans Overview](plans/README.md), [Status Docs Index](status/README.md), [Architecture Docs](architecture/README.md), [Development Guides](development/README.md)
- **Current roadmap snapshot:** [Combined Roadmap](status/ROADMAP_COMBINED.md)
- **Current delivery plan:** [Project Roadmap](status/ROADMAP.md)
- **Target product narrative:** [Target End Product](status/TARGET_END_PRODUCT.md)
- **Product-category filter:** [Evidence-Backed Investment Operations Plan](plans/evidence-backed-investment-operations-plan.md)
- **Browser-first operator UI lane:** [Web UI Development Pivot](plans/web-ui-development-pivot.md)
- **Waves 2-4 execution alignment:** [Waves 2-4 Operator Readiness Addendum](plans/waves-2-4-operator-readiness-addendum.md)

## Documentation Zones

| Zone | Folders | Audience |
|------|---------|----------|
| Product | `getting-started/`, `providers/`, `operations/` | Users and operators |
| Web Operator UI | `plans/`, `status/`, `development/`, `ai/` | Dashboard, API, and workflow contributors |
| Engineering | `architecture/`, `adr/`, `development/`, `integrations/`, `reference/`, `diagrams/`, `ai/` | Developers and tool authors |
| Governance | `status/`, `plans/`, `evaluations/`, `audits/`, `security/` | Core team and stakeholders |

`generated/` and any file marked as auto-generated should be refreshed by script rather than edited by hand. `docs/_site/` is the built documentation site output.

## By Audience

### Users and operators

- [Getting Started](getting-started/README.md)
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

Use these documents together when planning implementation. The active operator-readiness path is:

- Wave 1: provider confidence and checkpoint evidence
- Wave 2: paper-trading cockpit hardening (web-first readiness console and cockpit posture)
- Wave 3: shared run / portfolio / ledger continuity through shared contracts and browser workflows
- Wave 4: governance and fund-operations productization on top of the delivered Security Master baseline

Use the documents below to manage that path:

1. [status/ROADMAP_COMBINED.md](status/ROADMAP_COMBINED.md) for the shortest complete roadmap snapshot
2. [status/ROADMAP.md](status/ROADMAP.md) for the full wave-structured delivery plan
3. [plans/evidence-backed-investment-operations-plan.md](plans/evidence-backed-investment-operations-plan.md) for the product-category filter and archive rule
4. [plans/web-ui-development-pivot.md](plans/web-ui-development-pivot.md) for the browser-first operator UI lane and retained desktop policy
5. [plans/waves-2-4-operator-readiness-addendum.md](plans/waves-2-4-operator-readiness-addendum.md) for owner lanes, dependencies, and wave exit criteria
6. [plans/meridian-6-week-roadmap.md](plans/meridian-6-week-roadmap.md) for the current time-boxed execution plan
7. [status/provider-validation-matrix.md](status/provider-validation-matrix.md) for the current provider-confidence evidence gate; the completed Wave 1 blueprint is archived at [../archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md](../archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md)
8. [plans/paper-trading-cockpit-reliability-sprint.md](plans/paper-trading-cockpit-reliability-sprint.md) for Wave 2 replay/session/risk reliability hardening
9. [plans/trading-workstation-migration-blueprint.md](plans/trading-workstation-migration-blueprint.md) for migration context that should be interpreted through the active web pivot
10. [plans/governance-fund-ops-blueprint.md](plans/governance-fund-ops-blueprint.md) for Wave 4 governance, reconciliation, and reporting direction
11. [plans/fund-management-pr-sequenced-roadmap.md](plans/fund-management-pr-sequenced-roadmap.md) for PR-sized governance and fund-ops implementation slices
12. [status/FEATURE_INVENTORY.md](status/FEATURE_INVENTORY.md) for capability status by area
13. [status/provider-validation-matrix.md](status/provider-validation-matrix.md) for provider-readiness evidence and gaps
14. [status/kernel-readiness-dashboard.md](status/kernel-readiness-dashboard.md) for DK gate status and operator sign-off posture
15. [status/production-status.md](status/production-status.md) for current readiness caveats
16. [status/IMPROVEMENTS.md](status/IMPROVEMENTS.md) for tracked implementation themes

The 2026-05-13 documentation refresh reconciles the active docs against the current browser
workstation evidence set: Overview Today panel, `/data/alerts` Price Alerts, `/strategy/designer`
Strategy Designer, full-console readiness checkpoint gates, and Meridian Design System reference
workbench/tokenized-color support. These are support evidence for the Waves 2-4 path; they do not
change the canonical wave statuses in `PROGRAM_STATE.md`.

## Verified Build And Run References

These commands are currently reflected in the repo's code and build scripts:

- `make help`
- `make setup-dev`
- `make run`
- `make run-backfill`
- `make run-selftest`
- `dotnet run --project src/Meridian/Meridian.csproj -- --quickstart`
- `dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080`
- `dotnet run --project src/Meridian/Meridian.csproj -- --validate-config`
- `dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableFullWpfBuild=true`

## Reference

- [API Reference](reference/api-reference.md)
- [Data Dictionary](reference/data-dictionary.md)
- [Environment Variables](reference/environment-variables.md)
- [Dependencies Reference](DEPENDENCIES.md)
- [Generated Documentation](generated/README.md)

## Archive

Historical and superseded material now lives outside the active docs tree under `archive/docs/`.

- [Archive index](https://github.com/rodoHasArrived/Meridian-main/blob/main/archive/docs/INDEX.md)
- [Archive overview](https://github.com/rodoHasArrived/Meridian-main/blob/main/archive/docs/README.md)

## Maintenance Checklist

When documentation changes in a PR:

1. Update this index if the main navigation or source-of-truth set changes.
2. Keep `status/ROADMAP*.md`, `status/OPPORTUNITY_SCAN.md`, `status/TARGET_END_PRODUCT.md`, `status/FEATURE_INVENTORY.md`, `plans/evidence-backed-investment-operations-plan.md`, and the relevant blueprint docs aligned.
3. Prefer updating folder `README.md` files when you add or retire documents.
4. Avoid editing generated docs by hand unless you are also updating the generator.
