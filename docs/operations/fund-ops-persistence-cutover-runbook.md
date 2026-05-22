# Fund Operations Persistence Cutover Runbook

## Scope
Fund Structure, Fund Accounts, Direct Lending, Banking, and Money Market domains move from in-memory-first reads to persisted-projection reads in phased order.

## Pre-checks
1. Confirm shadow-write mode is enabled and reads remain `LegacyInMemory` for the target domain.
2. Confirm reconciliation job has produced at least one full cycle with no critical discrepancies.
3. Validate workstation API + UI parity for:
   - `src/Meridian.Wpf/` workflows (Fund Accounts, Fund Ledger, Direct Lending)
   - `src/Meridian.Ui/` workstation endpoints/read models.

## Phase flow (per domain)
1. Enable shadow writes.
2. Observe reconciliation discrepancies and fix mapping gaps.
3. Switch read mode to `PersistedProjection` behind feature toggle.
4. Run focused regression tests for WPF and Ui.Shared endpoint maps.
5. Keep rollback toggle available until two consecutive validation windows pass.

## Rollback
1. Flip domain read mode back to `LegacyInMemory`.
2. Keep shadow writes enabled for evidence retention.
3. Re-run reconciliation and capture discrepancy report.

## Sign-off evidence
- Config snapshot showing domain mode values.
- Reconciliation logs with discrepancy count and timestamps.
- WPF regression run output.
- API/workstation endpoint regression output.
