# W12 intercompany acceptance pack (2026-11-19)

This is the development verification for six roadmap rows: W12-ICACCRUAL-001, W12-ICFLOAT-001, W12-ICFEE-001,
W12-ICCOMPOUND-001, W12-ICCONFIRM-001 and W12-ICSCHEDULE-001. It was recorded on PR 3674 (branch
`claude/compassionate-curie-ea8a1i`). The rows move to `ready_for_acceptance`; accepting them is the repository
owner's decision, and this pack does not grant it. All evidence is synthetic. It makes no claim about a live
provider, a production database or a release.

## What PR 3674 added to these rows

| Area | Change | Rows |
| --- | --- | --- |
| Journal drafts | PostgreSQL draft store (ledger migration 077), with command receipts and one PostgreSQL lock per schema, so two hosts cannot change the same draft at once. Server-held proposal identity and digests round-trip unchanged. | All six. Every row drafts through the manual journal workbench. |
| Counterpart linking | Both workstations pick the counterpart journal from the posted journals the link check accepts (`/proposals/{id}/counterpart-candidates`). The desktop opens a linked journal in the Fund Ledger. | ICACCRUAL, ICFLOAT, ICFEE, ICCOMPOUND, ICSCHEDULE |
| Close blocking | Opt-in confirmation close policy (ADR-032, proposed `DEC-IC-CONFIRM-002`, ledger migration 078). Without an active policy, confirmation stays advisory. | ICCONFIRM |
| Docs | Operator procedure refreshed. The plan no longer claims that the desktop smoke tests compile on Linux. | All six |

## Hosted runs

| Run | Commit | Result |
| --- | --- | --- |
| Meridian CI, run 44431652387 | `9159967f` | `verify-dotnet`, `verify-browser`, `verify-workflows`, `changes` and `integration-gate` (PostgreSQL integration, including migrations 077 and 078 and the draft-store and close-policy store tests) succeeded. `verify-docs`, and therefore `quality-gate`, failed. The stale reviewed docs this PR caused were regenerated in `45ccee46`. The 18 `docs/diagrams/uml/*.png` renders are also stale on `main` at `0255fbe3` (run 44420923964) and are not part of this PR. |
| Schema control, run 44431168596 | `5fab6ab0` | Succeeded. |
| Targeted Test, run 44431011339 (`windows-latest`, `dotnet-filtered`, full WPF build, `IntercompanyNativeControlSmokeTests` and `FundLedgerViewModelTests`) | `d413d08d` | 27 passed, none failed: all 8 `IntercompanyNativeControlSmokeTests` and all 19 `FundLedgerViewModelTests`, including `LoadAsync_WithJournalDeepLink_*`. Later commits change no desktop or WPF test source. |

`Prepare generated repair patch` reports `review_required`. Nine changed modules no longer match their reviewed
source-hash entries, and only the owner can do that module review. It does not affect the correctness evidence above.

## Local runs (Linux, .NET 10.0.404, PostgreSQL 16 through `MERIDIAN_TEST_POSTGRES_CONNECTION_STRING`)

| Check | Result |
| --- | --- |
| `Meridian.Tests`, filtered to intercompany, close readiness, the command center, manual journals, accounting configuration, composition and production bindings | 1,543 passed, none skipped |
| `Meridian.Ui.Tests`, intercompany | 129 passed |
| Dashboard accounting entity, close and intercompany screens (vitest) | 108 passed; typecheck, lint and the typography floor are clean |
| `schema-control inventory` | 0 errors |
| Duplicate-helper ratchet and its script tests | passed (52) |

## Per row

**W12-ICACCRUAL-001.** The remaining criteria were service-backed evidence and a Windows desktop run.
`IntercompanyAccrualPostAndDeliverPostgresTests` now posts and delivers over PostgreSQL drafts, and Windows run
44431011339 covers `InterestAccrualControl_*`. Fees, floating rates and compounding are covered by their own rows.

**W12-ICFLOAT-001.** Rate fixings, floating accrual and the evidence pins are covered by the hosted .NET and
integration lanes. The desktop `RateFixingsControl_*` passed on Windows. In-arrears or averaged rates, floors and
caps remain out of scope.

**W12-ICFEE-001.** Fixed management fees per period or per annum are covered by the hosted lanes, and the desktop
`ManagementFeeControl_*` passed on Windows. Percentage fees and loan commitment or utilisation fees remain out of
scope.

**W12-ICCOMPOUND-001.** Periodic compounding on posted accrued interest is covered by the hosted lanes. The
desktop slice rows are covered by `IntercompanyCompoundingSliceRowTests` on Linux, and the interest-accrual control
that shows them passed on Windows. Daily compounding remains out of scope.

**W12-ICCONFIRM-001.** The advisory confirmation is unchanged and its tests pass. Opt-in close blocking now exists
behind a reviewed per-book policy. Exit criterion 5 ("never changes close eligibility") holds for every book without
an active policy. A book that adopts one gets the blocking behaviour recorded in ADR-032, whose decision
`DEC-IC-CONFIRM-002` awaits the owner's acceptance. The desktop `BalanceConfirmationControl_*`, which includes the
policy section, passed on Windows.

**W12-ICSCHEDULE-001.** Maker-checker schedules, the runner and run-now are covered by the hosted lanes, and the
desktop `AccrualSchedulesControl_*` passed on Windows. Management-fee schedules remain out of scope.

## Known limits

- Pairs spanning two companies of one tenant, and posting adjustments from a dispute, are not implemented.
- Completed PostgreSQL command receipts are kept in place; they are not archived or pruned.
- The full local `scripts/ci.sh` was not run. Script tests that need PowerShell 7 could not run locally.
