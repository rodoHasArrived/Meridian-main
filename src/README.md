---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-ROOT
path: src
status: active
owner_lane: Core Team
last_reviewed: 2026-10-07
---

# src

## Purpose

`src/` contains Meridian's .NET 10 operational-finance and trading-platform code, including the browser and Windows desktop workstations. Fund management is a first-class specialization of the shared financial-operations model.

Registered modules have local READMEs that explain purpose, ownership, boundaries, validation commands, and roadmap traceability. Start with the owning module's README before editing its source.

## Layer responsibility

The source tree separates host composition, application orchestration, domain and contract foundations, provider/storage infrastructure, and financial-operations and trading modules.

The browser workstation and WPF desktop workstation are active, co-equal operator UI lanes. Both consume shared contracts, API endpoints, and read models through `Meridian.Ui.Services` and `Meridian.Ui.Shared` so their presentation layers do not fork product state or business rules.

## Key folders and files

Paths below are relative to `src/`. Use the [source-module index](../docs/source/generated/source-module-index.md) for the complete registered inventory and owner lanes.

| Area | Entry points and responsibility |
| --- | --- |
| Runtime host | [Meridian](Meridian/README.md) owns the host, CLI, and runtime composition. |
| Installed startup | [Meridian.Launcher](Meridian.Launcher/README.md) opens the workstation after readiness; [Meridian.LifecycleSupervisor](Meridian.LifecycleSupervisor/README.md) owns installed host and dedicated database lifecycle. |
| Application | [Meridian.Application](Meridian.Application/README.md) owns use cases, orchestration, commands, and pipelines. |
| Shared foundations | [Meridian.Core](Meridian.Core/README.md), [Meridian.Domain](Meridian.Domain/README.md), and [Meridian.Contracts](Meridian.Contracts/README.md) provide shared primitives, domain contracts, and DTOs. |
| Providers and storage | [Meridian.ProviderSdk](Meridian.ProviderSdk/README.md), [Meridian.Infrastructure](Meridian.Infrastructure/README.md), and [Meridian.Storage](Meridian.Storage/README.md) own provider extension seams, adapters, and persistence. |
| Financial operations | [Meridian.Ledger](Meridian.Ledger/README.md), [Meridian.FinancialOperations](Meridian.FinancialOperations/README.md), [Meridian.PortfolioRecords](Meridian.PortfolioRecords/README.md), and [Meridian.Reporting](Meridian.Reporting/README.md) own accounting, reconciliation, portfolio records, and reporting. |
| Trading and research | [Meridian.Strategies](Meridian.Strategies/README.md), [Meridian.Backtesting](Meridian.Backtesting/README.md), [Meridian.Execution](Meridian.Execution/README.md), and [Meridian.Risk](Meridian.Risk/README.md) own strategy, simulation, execution, and risk workflows. |
| Browser workstation | [Meridian.Ui/dashboard](Meridian.Ui/dashboard/README.md) owns browser UI source; [Meridian.Ui](Meridian.Ui/README.md) owns host-served UI assets, including the built `wwwroot/workstation/` bundle. |
| Desktop workstation | [Meridian.Wpf](Meridian.Wpf/README.md) owns the active Windows desktop UI, including web-UI parity work over shared contracts. |
| Shared workstation services | [Meridian.Ui.Services](Meridian.Ui.Services/README.md) and [Meridian.Ui.Shared](Meridian.Ui.Shared/README.md) provide API clients, workflow services, endpoint support, and read models consumed by both workstations. |
| AI integration | [Meridian.Mcp](Meridian.Mcp/README.md) exposes MCP tools and repository-navigation resources. |

## Important workflows

1. Read the owning module README and its entry in the [source-module registry](../docs/source/data/source-modules.yml) for module ID, owner lane, roadmap links, diagrams, and validation commands.
2. Check the [module map](../docs/architecture/module-map.md) before changing dependencies or moving responsibilities across projects.
3. Put common workstation behavior behind shared contracts, API endpoints, or read models before composing browser or WPF presentation.
4. Run the narrowest validation that covers the change. Update the owning README and registry when behavior, ownership, validation, diagrams, or TODO scope changes.

For setup, build, test, and run commands, use [Start](../docs/start/README.md) and [Engineering](../docs/engineering/README.md).

## Diagrams

The [diagram registry](../docs/source/data/diagram-index.yml) maps registered source diagrams to modules and roadmap items. Their Mermaid sources live under `docs/architecture/diagrams/`; the [diagram hub](../docs/diagrams/README.md) routes to additional architecture and workflow visuals.

## Roadmap traceability

Root-level source traceability is managed through registered child modules. Use the [source-module registry](../docs/source/data/source-modules.yml) and [roadmap registry](../docs/roadmap/README.md) for ownership and delivery state.

The WPF lane's current parity work is described in the [WPF/web alignment plan](../docs/engineering/plans/wpf-web-ui-alignment-plan.md) and tracked by `W8-WPF-PARITY-001`.

## TODO checklist

Add module-level TODOs to the [source TODO registry](../docs/source/data/source-todos.yml). Keep the root README as a navigation guide; generators own marked roadmap and TODO blocks in registered module READMEs.

## Validation

Run from the repository root after source-documentation changes:

```bash
python3 build/scripts/docs/validate-source-readmes.py --summary
python3 build/scripts/docs/render-source-docs.py --summary
git diff --check
```

Review renderer output before committing. For code changes, also run the owning module's validation commands; see the [source documentation standard](../docs/source/source-documentation-standard.md) for reviewed hash-baseline updates.

## Change rules

- Keep product scope grounded in the [design charter](../docs/product/meridian-design-document.md), current source evidence, and roadmap registry.
- Browser workstation source belongs in `Meridian.Ui/dashboard/`; build it to refresh `Meridian.Ui/wwwroot/workstation/`.
- WPF desktop work belongs in `Meridian.Wpf/`. Preserve shared API/read-model behavior across both active workstation lanes.
- There is no mobile development lane. Responsive browser validation supports the browser workstation.
- Edit registry inputs or generators for generated documentation; preserve generated blocks in module READMEs.

## Related docs

- [Documentation front door](../docs/README.md)
- [Source documentation mesh](../docs/source/README.md)
- [Source documentation standard](../docs/source/source-documentation-standard.md)
- [Project structure](../docs/architecture/project-structure.md)
- [Module map](../docs/architecture/module-map.md)
- [Roadmap registry](../docs/roadmap/README.md)
