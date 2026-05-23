# Archived Code

This folder preserves retired Meridian source files that are no longer part of the active build
surface but still explain a migration or compatibility decision.

## Retired Tombstone Files

- `src/Meridian.Backtesting.Sdk/Ledger/*` - comment-only ledger tombstones left after ledger types
  moved to `src/Meridian.Ledger/` and compatibility aliases were centralized in
  `src/Meridian.Backtesting.Sdk/GlobalUsings.cs`.
- `src/Meridian.QuantScript/Compilation/Contracts.cs` - comment-only tombstone left after
  QuantScript compilation contracts moved into separate files in the same folder.
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
