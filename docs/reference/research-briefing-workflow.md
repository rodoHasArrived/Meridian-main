# Research Briefing Workflow

## Purpose

The Research workspace is moving from a generic landing page toward a `Market Briefing + Run Studio` workflow. The briefing surface packages the highest-frequency research context into one shared object model and keeps the existing run, portfolio, ledger, and promotion drill-ins attached to the same run lifecycle.

This slice adds the typed seam that the desktop shell can consume now and that later automation, notebook, or companion surfaces can reuse without inventing page-local models.

## Shared Contract

The shared contracts live in `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs`.

Key types:

- `InsightFeed` and `InsightWidget` for pinned research tiles
- `WorkstationWatchlist` for watchlist summaries
- `ResearchBriefingRun` and `ResearchRunDrillInLinks` for saved run cards
- `ResearchSavedComparison` for staged compare packages
- `ResearchBriefingAlert` and `ResearchWhatChangedItem` for operator prompts
- `ResearchBriefingWorkspaceSummary` and `ResearchBriefingDto` for the full shell payload

## Endpoint

The workstation endpoint is exposed at `/api/workstation/research/briefing` through `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`.

Behavior:

- prefers shared run history from `StrategyRunReadService`
- projects alert, comparison, and "what changed" items from existing run continuity
- falls back to an empty but typed briefing payload when the richer run service is unavailable
- preserves the older `/api/workstation/research` route so existing consumers do not break while the shell migrates

## Desktop Consumption

The WPF shell consumes the typed payload through `src/Meridian.Wpf/Services/WorkstationResearchBriefingService.cs`.

Behavior:

- `WorkstationResearchBriefingApiClient` requests the typed payload from `/api/workstation/research/briefing`
- `ResearchBriefingWorkspaceService` backfills watchlists from local desktop watchlists when the API payload omits them
- if the API is unavailable, the service builds a local fallback briefing from `StrategyRunWorkspaceService` plus `IWatchlistReader`

This keeps the Research shell on shared contracts first instead of creating new page-only view models that would later diverge from API or automation usage.

## Shell Binding

`src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml` and `.xaml.cs` now bind the upper part of the shell to the briefing model:

- briefing summary and freshness timestamp
- pinned insights
- watchlists
- "what changed"
- alerts
- saved comparisons

Run-opening actions from those briefing cards promote the selected run into the same lower `Run Studio` and inspector rail that the rest of the workspace already uses.

## Validation

Targeted validation for this slice lives in:

- `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs`
- `tests/Meridian.Wpf.Tests/Services/ResearchBriefingWorkspaceServiceTests.cs`
- `tests/Meridian.Wpf.Tests/Views/ResearchWorkspaceShellSmokeTests.cs`
- `tests/Meridian.Wpf.Tests/Views/ResearchWorkspaceShellWorkflowTests.cs`

The workflow test loads the Research shell in a WPF window, seeds a saved briefing card, clicks it, and verifies that:

- the active run context is updated
- the run studio reflects the selected run
- run detail and run portfolio inspectors are docked for that run
