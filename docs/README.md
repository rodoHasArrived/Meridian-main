# Meridian Documentation

**Last Reviewed:** 2026-05-21
**Scope:** Active hand-authored documentation plus generated status and reference entry points

This index is the main entry point for the active `docs/` tree. It is organized around Meridian's current direction: an evidence-backed investment operations platform where browser-first operator workflows prove trusted data, research, paper validation, portfolio/accounting review, reconciliation, approvals, and governed reporting outcomes end-to-end.

Current local project path: `C:\Dev\Meridian-main`.

## Platform At A Glance

Meridian's current solution includes:

- a console and host application in `src/Meridian/`
- application, contracts, core, domain, infrastructure, and storage layers in `src/Meridian.Application/`, `src/Meridian.Contracts/`, `src/Meridian.Core/`, `src/Meridian.Domain/`, `src/Meridian.Infrastructure/`, and `src/Meridian.Storage/`
- execution, provider, risk, strategy, and backtesting seams in `src/Meridian.Execution*/`, `src/Meridian.ProviderSdk/`, `src/Meridian.Risk/`, `src/Meridian.Strategies/`, and `src/Meridian.Backtesting*/`
- ledger, direct-lending, and F# support projects in `src/Meridian.Ledger/`, `src/Meridian.FSharp*/`, and `src/Meridian.IbApi.SmokeStub/`
- operator UI surfaces in `src/Meridian.Ui/dashboard/`, `src/Meridian.Ui/wwwroot/workstation/`, `src/Meridian.Ui.Shared/`, and `src/Meridian.Ui.Services/` with retained desktop compatibility in `src/Meridian.Wpf/`
- scripting and MCP surfaces in `src/Meridian.QuantScript/`, `src/Meridian.Mcp/`, and `src/Meridian.McpServer/`

## Start Here

- **First local setup:** [Getting Started Guide](getting-started/README.md)
- **Operator reference:** [Help and FAQ](HELP.md)
- **Operational procedures:** [Operator Runbook](operations/operator-runbook.md)
- **Docs navigation by folder:** [Plans Overview](plans/README.md), [Status Docs Index](status/README.md), [Architecture Docs](architecture/README.md), [Development Guides](development/README.md), [Developer Quick Guides](developer/README.md), [Design Documentation](design/README.md)
- **Repository hygiene:** [Cleanup and Maintenance](operations/cleanup-and-maintenance.md), [Disk Space Hygiene](operations/disk-space-hygiene.md)
- **Documentation cleanup intake:** [Orphaned Doc Triage Index](operations/orphaned-doc-triage-index.md)
- **Prompt and agent guidance:** [Automation Prompts](prompts/automation-prompts.md), [Repo Maintenance Prompts](prompts/repo-maintenance-prompts.md)
- **Current roadmap snapshot:** [Combined Roadmap](status/ROADMAP_COMBINED.md)
- **Structured roadmap registry:** [Roadmap Registry](roadmap/README.md)
- **Source documentation mesh:** [Source Documentation Mesh](source/README.md)
- **Consolidated planning entry point:** [Current Direction and Status](plans/current-direction-and-status.md)
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
| Engineering | `architecture/`, `adr/`, `developer/`, `development/`, `integrations/`, `reference/`, `diagrams/`, `design/`, `prompts/`, `ai/` | Developers and tool authors |
| Governance | `status/`, `plans/`, `evaluations/`, `audits/`, `security/` | Core team and stakeholders |

`generated/`, `roadmap/generated/`, `source/generated/`, and any file marked as auto-generated
should be refreshed by script rather than edited by hand. `docs/_site/` is the built documentation
site output.

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
- [Developer Setup](developer/setup.md)
- [Build, Test, Run](developer/build-test-run.md)
- [Publish Standalone EXE](developer/publish-standalone-exe.md)
- [Repository Rule Set](development/repository-rule-set.md)
- [Cleanup and Maintenance](operations/cleanup-and-maintenance.md)
- [Disk Space Hygiene](operations/disk-space-hygiene.md)
- [Provider Implementation Guide](development/provider-implementation.md)
- [Desktop Testing Guide](development/desktop-testing-guide.md)
- [Codex Desktop Workstation Workflow](development/codex-workflow.md)
- [Modular Desktop Architecture](development/modular-desktop-architecture.md)
- [Desktop Resource Management](development/desktop-resource-management.md)
- [Shared Workstation Components](development/shared-workstation-components.md)
- [Documentation Contribution Guide](development/documentation-contribution-guide.md)
- [Architecture Overview](architecture/overview.md)
- [AI Assistant Resources](ai/README.md)

### Architecture and design

- [Architecture Overview](architecture/overview.md)
- [Project Structure](architecture/project-structure.md)
- [Module Map](architecture/module-map.md)
- [Layer Boundaries](architecture/layer-boundaries.md)
- [Storage Design](architecture/storage-design.md)
- [Ledger Architecture](architecture/ledger-architecture.md)
- [Desktop Layers](architecture/desktop-layers.md)
- [WPF Shell MVVM](architecture/wpf-shell-mvvm.md)
- [MVVM Guidelines](architecture/mvvm-guidelines.md)
- [ADRs](adr/README.md)
- [Design System Usage](design/design-system-usage.md)

### Status and planning

- [Combined Roadmap](status/ROADMAP_COMBINED.md)
- [Current Direction and Status](plans/current-direction-and-status.md)
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

1. [plans/current-direction-and-status.md](plans/current-direction-and-status.md) for the consolidated current direction, status, and plan-file roles
2. [status/ROADMAP_COMBINED.md](status/ROADMAP_COMBINED.md) for the shortest complete roadmap snapshot
3. [status/ROADMAP.md](status/ROADMAP.md) for the full wave-structured delivery plan
4. [plans/evidence-backed-investment-operations-plan.md](plans/evidence-backed-investment-operations-plan.md) for the product-category filter and archive rule
5. [plans/web-ui-development-pivot.md](plans/web-ui-development-pivot.md) for the browser-first operator UI lane and retained desktop policy
6. [plans/waves-2-4-operator-readiness-addendum.md](plans/waves-2-4-operator-readiness-addendum.md) for owner lanes, dependencies, and wave exit criteria
7. [plans/meridian-6-week-roadmap.md](plans/meridian-6-week-roadmap.md) for the current time-boxed execution plan
8. [status/provider-validation-matrix.md](status/provider-validation-matrix.md) for the current provider-confidence evidence gate; the completed Wave 1 blueprint is archived at [../archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md](../archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md)
9. [plans/paper-trading-cockpit-reliability-sprint.md](plans/paper-trading-cockpit-reliability-sprint.md) for Wave 2 replay/session/risk reliability hardening
10. [plans/trading-workstation-migration-blueprint.md](plans/trading-workstation-migration-blueprint.md) for migration context that should be interpreted through the active web pivot
11. [plans/governance-fund-ops-blueprint.md](plans/governance-fund-ops-blueprint.md) for Wave 4 governance, reconciliation, and reporting direction
12. [plans/fund-management-pr-sequenced-roadmap.md](plans/fund-management-pr-sequenced-roadmap.md) for PR-sized governance and fund-ops implementation slices
13. [status/FEATURE_INVENTORY.md](status/FEATURE_INVENTORY.md) for capability status by area
14. [status/provider-validation-matrix.md](status/provider-validation-matrix.md) for provider-readiness evidence and gaps
15. [status/kernel-readiness-dashboard.md](status/kernel-readiness-dashboard.md) for DK gate status and operator sign-off posture
16. [status/production-status.md](status/production-status.md) for current readiness caveats
17. [status/IMPROVEMENTS.md](status/IMPROVEMENTS.md) for tracked implementation themes

The 2026-05-18 documentation refresh consolidates the active planning entry point and preserves the
current interpretation: browser workstation is the parallel new-operator-UI surface, WPF is the active desktop shell,
Waves 2-4 as the core operator-ready path, Wave 5-6 as later work, and mobile-specific product
lanes out of scope.

The 2026-05-21 documentation-control refresh adds structured roadmap/source registry guidance to
the active docs mesh. Roadmap truth now lives in `docs/roadmap/data/*.yml` with generated summaries
under `docs/roadmap/generated/`, while source module ownership, README coverage, TODOs, diagrams,
and hash freshness live under `docs/source/`. Use the registry validators and renderers before
editing generated outputs by hand.

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

See [developer/build-test-run.md](developer/build-test-run.md) and
[developer/publish-standalone-exe.md](developer/publish-standalone-exe.md) for
short current command paths from `C:\Dev\Meridian-main`.

## Reference

- [API Reference](reference/api-reference.md)
- [Data Dictionary](reference/data-dictionary.md)
- [Environment Variables](reference/environment-variables.md)
- [Dependencies Reference](DEPENDENCIES.md)
- [Generated Documentation](generated/README.md)

## Archive

Historical and superseded material consolidated during docs cleanup now lives under `archive/docs/`.

- [Docs archive overview](archive/README.md)

## Maintenance Checklist

When documentation changes in a PR:

1. Update this index if the main navigation or source-of-truth set changes.
2. Keep `plans/current-direction-and-status.md`, `status/ROADMAP*.md`, `status/OPPORTUNITY_SCAN.md`, `status/TARGET_END_PRODUCT.md`, `status/FEATURE_INVENTORY.md`, `plans/evidence-backed-investment-operations-plan.md`, and the relevant blueprint docs aligned.
3. Prefer updating folder `README.md` files when you add or retire documents.
4. Avoid editing generated docs by hand unless you are also updating the generator.
5. Add new prompts to the prompt catalog before creating another parallel prompt folder.
6. Keep generated artifacts in ignored output folders such as `artifacts/`, `bin/`, `obj/`, `TestResults/`, `coverage/`, and `node_modules/`.
