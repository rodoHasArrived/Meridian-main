# Duplicate And Deprecated Implementation Audit - 2026-05-17

## Scope

This pass looked for duplicate, deprecated, retired, and compatibility implementations outside
generated build output and the existing archive. WPF code was considered archivable when reference
evidence showed it was no longer active.

## Archived Code

| Classification | Old path | New path | Reason |
| --- | --- | --- | --- |
| `archive-code` | `src/Meridian.Backtesting.Sdk/Ledger/BacktestLedger.cs` | `archive/code/src/Meridian.Backtesting.Sdk/Ledger/BacktestLedger.cs` | Comment-only tombstone; `BacktestLedger` compatibility is handled by `src/Meridian.Backtesting.Sdk/GlobalUsings.cs`. |
| `archive-code` | `src/Meridian.Backtesting.Sdk/Ledger/JournalEntry.cs` | `archive/code/src/Meridian.Backtesting.Sdk/Ledger/JournalEntry.cs` | Comment-only tombstone; active type lives in `src/Meridian.Ledger/JournalEntry.cs`. |
| `archive-code` | `src/Meridian.Backtesting.Sdk/Ledger/LedgerAccount.cs` | `archive/code/src/Meridian.Backtesting.Sdk/Ledger/LedgerAccount.cs` | Comment-only tombstone; active type lives in `src/Meridian.Ledger/LedgerAccount.cs`. |
| `archive-code` | `src/Meridian.Backtesting.Sdk/Ledger/LedgerAccounts.cs` | `archive/code/src/Meridian.Backtesting.Sdk/Ledger/LedgerAccounts.cs` | Comment-only tombstone; active type lives in `src/Meridian.Ledger/LedgerAccounts.cs`. |
| `archive-code` | `src/Meridian.Backtesting.Sdk/Ledger/LedgerAccountType.cs` | `archive/code/src/Meridian.Backtesting.Sdk/Ledger/LedgerAccountType.cs` | Comment-only tombstone; active type lives in `src/Meridian.Ledger/LedgerAccountType.cs`. |
| `archive-code` | `src/Meridian.Backtesting.Sdk/Ledger/LedgerEntry.cs` | `archive/code/src/Meridian.Backtesting.Sdk/Ledger/LedgerEntry.cs` | Comment-only tombstone; active type lives in `src/Meridian.Ledger/LedgerEntry.cs`. |
| `archive-code` | `src/Meridian.QuantScript/Compilation/Contracts.cs` | `archive/code/src/Meridian.QuantScript/Compilation/Contracts.cs` | Comment-only tombstone; active contracts live in the split `IQuantScriptCompiler`, `IScriptRunner`, `ScriptDiagnostic`, `ScriptCompilationResult`, and `ScriptRunResult` files. |

## Kept Active

| Classification | Path | Evidence |
| --- | --- | --- |
| `active` | `src/Meridian.Wpf/Copy/WorkspaceCopyCatalog.cs` | Despite the `Copy` folder name, it is referenced by WPF XAML, shell services, navigation catalog code, welcome view model, and `tests/Meridian.Wpf.Tests/Copy/WorkspaceCopyCatalogTests.cs`. |
| `active` | `src/Meridian.Wpf/Assets/Icons/data-quality.svg` and `governance.svg` | Exact duplicate SVG content, but both filenames are documented as separate icon identities in `src/Meridian.Wpf/Assets/Icons/README.md`; consolidation would require UI asset contract changes. |
| `active` | `src/Meridian.Wpf/Assets/Icons/settings.svg` and `storage-optimization.svg` | Exact duplicate SVG content, but both filenames are documented as separate icon identities in `src/Meridian.Wpf/Assets/Icons/README.md`; consolidation would require UI asset contract changes. |
| `active` | `src/Meridian/app.ico` and `src/Meridian.Wpf/Assets/app.ico` | Exact duplicate binary content, but each project consumes its own icon path through project and packaging metadata. |
| `active` | `docs/status/api-docs-report.md` | Looks stale because it reports deprecated endpoints, but it is automation-owned by `build/scripts/docs/run-docs-automation.py` and `.github/workflows/documentation.yml`. |
| `active` | `docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md` | Date-stamped, but strongly referenced by roadmap, program-state validation, status README, and planning docs as the active normalized backlog. |
| `active` | `src/Meridian.Ui/dashboard/artifacts/automation/**` | Contains duplicate screenshots and smoke artifacts, but they are tracked evidence outputs. Moving them would be an evidence-retention policy change, not a retired-implementation cleanup. |

## Validation Notes

- Exact duplicate-content scan found only tombstone source files, duplicate WPF assets/icons, desktop
  screenshot duplicates, and tracked dashboard automation evidence duplicates after excluding build
  output and existing archive material.
- Explicit stale-marker scan found many active compatibility surfaces (`legacy` routes, migration
  fallback code, generated reports). These were not archived without strong reference evidence.
