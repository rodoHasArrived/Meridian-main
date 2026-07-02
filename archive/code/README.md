# Archived Code

This folder preserves retired Meridian source files that are no longer part of the active build
surface but still explain a migration or compatibility decision.

## Retired Tombstone Files

- `src/Meridian.Backtesting.Sdk/Ledger/*` - comment-only ledger tombstones left after ledger types
  moved to `src/Meridian.Ledger/` and compatibility aliases were centralized in
  `src/Meridian.Backtesting.Sdk/GlobalUsings.cs`.
- `src/Meridian.QuantScript/Compilation/Contracts.cs` - comment-only tombstone left after
  QuantScript compilation contracts moved into separate files in the same folder.
- `src/Meridian.McpServer/Tombstone.cs` - comment-only tombstone for the market-data MCP server
  retired on 2026-05-23. The operational AI gateway (backfill, storage, symbol, and provider tools)
  was removed following a policy decision to limit AI-facing code to developer-assistance tooling
  only. Developer-facing MCP tooling remains active in `src/Meridian.Mcp/`.
- `src/Meridian.Ui.Shared/Services/ChiefOfStaffServices.cs` - comment-only tombstone after
  Chief of Staff runtime was removed from the active codebase (2026-05-23). Interfaces:
  `IChiefOfStaffRuntimeClient`, `IChiefOfStaffApprovalRouter`, `IChiefOfStaffTraceStore`,
  `IChiefOfStaffSessionService`.
- `src/Meridian.Ui.Shared/Services/ChiefOfStaffOptions.cs` - comment-only tombstone after
  Chief of Staff options removed (2026-05-23).
- `src/Meridian.Ui.Shared/Endpoints/WorkstationChiefOfStaffEndpoints.cs` - comment-only tombstone
  after Chief of Staff workstation API endpoints removed (2026-05-23).
- `src/Meridian.Ui.Shared/Serialization/ChiefOfStaffJsonContext.cs` - comment-only tombstone after
  Chief of Staff source-generated JSON context removed (2026-05-23).
- `src/Meridian.Contracts/Workstation/ChiefOfStaffDtos.cs` - comment-only tombstone after Chief of
  Staff contract DTOs removed (2026-05-23).
- `src/Meridian.Ui/dashboard/src/screens/overview-screen*.ts*` - comment-only tombstones after the
  legacy browser workstation overview screen was retired; `/overview/*` remains a redirect handled
  by the active app shell (2026-07-02).
- `src/Meridian.Ui/dashboard/src/screens/today-panel.view-model*.ts` - comment-only tombstones after
  the legacy overview Today panel was retired with the orphaned overview screen (2026-07-02).
- `src/Meridian.Ui/dashboard/src/screens/settings-admin-operations-console.tsx` - comment-only
  tombstone after the unrouted Settings admin operations console was retired (2026-07-02).

Active source remains under `src/`. Files here are reference material only and should not be
reintroduced without moving them back into the correct active project and validating the build.

## Retired Native Host Prototypes

- `native/cpptrader-host/` - archived native C++ host scaffold retained for historical reference.

## Retired ADK Scaffold and Codex Skill

- `tools/chief-of-staff-runtime/` - ADK node pipeline Python scaffold for the out-of-process Chief
  of Staff runtime (archived 2026-05-23).
- `.codex/skills/cos-runtime-development/` - Codex skill for building and extending CoS ADK nodes
  (archived 2026-05-23).

## Retired Tests

- `tests/Meridian.Tests/Ui/ChiefOfStaffEndpointsTests.cs` - endpoint integration tests for the
  Chief of Staff workstation API (archived 2026-05-23).
- `tests/Meridian.Wpf.Tests/ViewModels/AgentViewModelTests.cs` - WPF local-agent view-model tests
  archived after app-runtime AI surfaces were removed from the active desktop product lane; active
  AI/MCP code remains limited to the MCP projects.

## Guardrails

- `archive/code/src/` files must remain comment-only tombstones with the `ARCHIVE TOMBSTONE`
  marker.
- Active build surfaces (`*.sln`, `*.csproj`, `*.fsproj`, `Directory.Build.*`) must not include
  `archive/code/src/`.
- The repository enforcement check lives in
  `tests/scripts/test_archive_code_tombstones.py`.


## Search Defaults

- Repository-wide `rg` searches exclude `archive/**` via repo-root `.rgignore` to reduce maintenance noise.
- Include archived files explicitly when needed, for example: `rg --no-ignore -g 'archive/**' "pattern"`.
