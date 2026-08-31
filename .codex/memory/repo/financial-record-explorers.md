---
id: repo:financial-record-explorers
tier: repo
scope: repo
file: .codex/memory/repo/financial-record-explorers.md
tags:
  - financial-record-explorer
  - accounting
  - reporting
  - workstation
  - saved-views
load_when:
  skills:
    - meridian-blueprint
    - meridian-code-architecture
    - meridian-implementation-assurance
    - meridian-docs
    - modular-desktop-mvvm
    - dense-data-grid-inspector-panel
    - workstation-screen-composition
  paths:
    - src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs
    - src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.FinancialRecordExplorers.cs
    - src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs
    - src/Meridian.Ui.Shared/Services/FinancialRecordExplorerSavedViewStore.cs
    - src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.tsx
    - src/Meridian.Ui/dashboard/src/screens/accounting-screen*
    - src/Meridian.Wpf/ViewModels/FinancialRecordExplorerViewModel.cs
    - src/Meridian.Wpf/Views/FinancialRecordExplorerPage.xaml*
    - tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs
    - tests/Meridian.Wpf.Tests/ViewModels/FinancialRecordExplorerViewModelTests.cs
  intents:
    - financial-record-explorer
    - accounting
    - reporting
    - desktop-ui
    - browser-workstation
    - implementation
    - validation
  branches: []
  tags:
    - financial-record-explorer
    - saved-views
    - accounting
    - reporting
  task:
    ids: []
    work_modes:
      - planning
      - implementation
      - validation
    intents:
      - financial-record-explorer
      - accounting
      - reporting
      - desktop-ui
      - browser-workstation
    paths:
      - src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs
      - src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.FinancialRecordExplorers.cs
      - src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs
      - src/Meridian.Ui.Shared/Services/FinancialRecordExplorerSavedViewStore.cs
      - src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.tsx
      - src/Meridian.Ui/dashboard/src/screens/accounting-screen*
      - src/Meridian.Wpf/ViewModels/FinancialRecordExplorerViewModel.cs
      - src/Meridian.Wpf/Views/FinancialRecordExplorerPage.xaml*
      - tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs
      - tests/Meridian.Wpf.Tests/ViewModels/FinancialRecordExplorerViewModelTests.cs
exclude_when:
  intents:
    - ai-tooling
    - skill-routing
confidence: high
freshness: fresh
source_refs:
  - docs/product/README.md
  - docs/roadmap/data/roadmap-items.yml
  - src/Meridian.Contracts/README.md
  - src/Meridian.Ui.Shared/README.md
  - src/Meridian.Ui/dashboard/README.md
  - src/Meridian.Wpf/README.md
  - src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs
  - src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.FinancialRecordExplorers.cs
  - src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs
  - src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.tsx
  - src/Meridian.Wpf/ViewModels/FinancialRecordExplorerViewModel.cs
  - tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs
  - tests/Meridian.Wpf.Tests/ViewModels/FinancialRecordExplorerViewModelTests.cs
review_after: 2026-09-20
invalidates_when:
  - Financial Record Explorer DTO shape or endpoint routes change materially.
  - Saved-view partitioning or tenant scoping changes.
  - Browser consumption changes materially, WPF compatibility is reactivated, or WPF compatibility is retired.
  - W5X Financial Record Explorer product scope changes materially.
---

# Financial Record Explorers Memory

Use this memory for Ledger Explorer, Portfolio Explorer, Security & Instrument Explorer,
Report-Line Provenance Explorer, saved views, and shared proof-drawer work.

- The shared DTO contract lives in
  `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs`. It owns explorer id, scope,
  saved views, summaries, filters, columns, rows, selected record detail, proof actions, record
  graph, `Used In`, and `Impacts`.
- Shared HTTP endpoints are registered in
  `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.FinancialRecordExplorers.cs` under
  `/api/workstation/financial-record-explorers/{explorerId}`, record detail routes, and saved-view
  routes. Unknown explorer ids return 404.
- `src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs` recognizes `ledger`,
  `portfolio`, `security-instrument`, and `report-line-provenance`. Missing source projections
  should return empty or blocked DTO state with disabled actions and reasons, not synthetic
  balances or client-local proof rows.
- Saved views are persisted through `FinancialRecordExplorerSavedViewStore` and are partitioned by
  authenticated workstation tenant plus explorer id. Preserve normalization of filters, search
  text, and column ids.
- Browser rendering is the shared `FinancialRecordExplorerShell` in
  `src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.tsx`. It applies
  saved-view filters/search and selected columns locally over the server DTO, while proof action,
  relationship, record graph, and blocked-state semantics come from the DTO.
- WPF rendering exists as retained compatibility through `FinancialRecordExplorerPage` and
  `FinancialRecordExplorerViewModel`, mapping the same columns, rows, selected-record proof
  actions, `Used In`, and `Impacts` into dense table/inspector controls. Do not add new WPF
  explorer product/UI work unless the lane is explicitly reactivated.
- Report-line provenance must preserve the retained chain from source record through instrument,
  position or transaction, reconciliation, journal, approval, delivery/restatement evidence, report
  line, and audit links. Browser surfaces and retained WPF compatibility must not rebuild that
  lineage locally.
- Narrow validation for shared explorer changes starts with
  `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~WorkstationFinancialRecordExplorerEndpointTests" /p:EnableWindowsTargeting=true`
  and adds `npm --prefix src/Meridian.Ui/dashboard run test` when the browser projection changes;
  use `tests/Meridian.Wpf.Tests` only for explicitly approved WPF maintenance or compatibility
  changes.
