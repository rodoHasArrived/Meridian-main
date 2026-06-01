# Strategy Briefing Workflow

## Purpose

The Strategy workspace packages the highest-frequency run context into a shared briefing model for browser and WPF workstation surfaces. The model keeps run, portfolio, ledger, comparison, alert, and promotion drill-ins attached to the same strategy lifecycle instead of letting each UI invent page-local shapes.

## Shared Contract

The canonical shared contracts live in `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs`.

Key types:

- `InsightFeed` and `InsightWidget` for pinned strategy briefing tiles.
- `WorkstationWatchlist` for watchlist summaries.
- `StrategyBriefingRun` and `StrategyRunDrillInLinks` for saved run cards.
- `StrategySavedComparison` for staged compare packages.
- `StrategyBriefingAlert` and `StrategyWhatChangedItem` for operator prompts.
- `StrategyBriefingWorkspaceSummary` and `StrategyBriefingDto` for the full shell payload.

Older `ResearchBriefing*` DTOs remain compatibility payloads for retained clients only; new browser, WPF, and shared code should use Strategy-named contracts.

## Endpoint

The canonical workstation endpoint is `/api/workstation/strategy/briefing`, exposed through `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`.

Behavior:

- prefers shared run history from `StrategyRunReadService`
- projects alert, comparison, and "what changed" items from existing run continuity
- falls back to an empty but typed Strategy briefing payload when the richer run service is unavailable
- keeps `/api/workstation/research` and `/api/workstation/research/briefing` as compatibility aliases for retained clients

## Desktop Consumption

The WPF shell consumes the typed Strategy payload through `src/Meridian.Wpf/Services/WorkstationStrategyBriefingService.cs`.

Behavior:

- `WorkstationStrategyBriefingApiClient` requests `UiApiRoutes.WorkstationStrategyBriefing`
- `StrategyBriefingWorkspaceService` backfills watchlists from local desktop watchlists when the API payload omits them
- if the API is unavailable, the service builds a local fallback briefing from `StrategyRunWorkspaceService` plus `IWatchlistReader`

This keeps the Strategy shell on shared contracts first instead of creating new page-only view models that would later diverge from API or automation usage.

## Shell Binding

`src/Meridian.Wpf/Views/StrategyWorkspaceShellPage.xaml` and `.xaml.cs` bind the upper shell surface to the briefing model:

- briefing summary and freshness timestamp
- pinned insights
- watchlists
- "what changed"
- alerts
- saved comparisons

Run-opening actions from those briefing cards promote the selected run into the same lower Run Studio and inspector rail that the rest of the Strategy workspace already uses.

## Validation

Targeted validation for this surface lives in:

- `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs`
- `tests/Meridian.Wpf.Tests/Services/StrategyBriefingWorkspaceServiceTests.cs`
- `tests/Meridian.Wpf.Tests/Views/StrategyWorkspaceShellWorkflowTests.cs`

The workflow test loads the Strategy shell in a WPF window, seeds a saved briefing card, clicks it, and verifies that:

- the active run context is updated
- the run studio reflects the selected run
- run detail and run portfolio inspectors are docked for that run
