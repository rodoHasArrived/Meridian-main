# Archived Code

This folder preserves retired Meridian source files that are no longer part of the active build
surface but still explain a migration or compatibility decision.

## Retired Tombstone Files

- `src/Meridian.Backtesting.Sdk/Ledger/*` - comment-only ledger tombstones left after ledger types
  moved to `src/Meridian.Ledger/` and compatibility aliases were centralized in
  `src/Meridian.Backtesting.Sdk/GlobalUsings.cs`.
- `src/Meridian.QuantScript/Compilation/Contracts.cs` - comment-only tombstone left after
  QuantScript compilation contracts moved into separate files in the same folder.

Active source remains under `src/`. Files here are reference material only and should not be
reintroduced without moving them back into the correct active project and validating the build.


## Search Defaults

- Repository-wide `rg` searches exclude `archive/**` via repo-root `.rgignore` to reduce maintenance noise.
- Include archived files explicitly when needed, for example: `rg --no-ignore -g 'archive/**' "pattern"`.
