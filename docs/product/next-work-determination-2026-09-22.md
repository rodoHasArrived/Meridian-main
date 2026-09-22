# What To Work On Next — Meridian (2026-09-22)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-09-22
**Baseline:** `main` at `ab58115b`
**Supersedes as current input:** [`next-work-determination-2026-09-20.md`](next-work-determination-2026-09-20.md)
**Method:** re-read the roadmap registry (`docs/roadmap/data/*.yml`), the production-readiness
tracker (`docs/product/implementation-todo-list.md`), and live GitHub Actions and pull-request
state; then ran a source scan for clock-coupled fixtures. Every claim is anchored to a run, a
commit, a PR number, or a `file:line`.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate.

**Validation caveat:** the environment this determination was produced in has **no .NET SDK**
(`dotnet: command not found`), so nothing below was compiled or executed. Every source claim is
from reading code. No C# change is proposed as landed here, deliberately — see
[What this session did not do](#what-this-session-did-not-do).

## Delta since 2026-09-20

Two days. One thing moved.

| | 2026-09-20 | 2026-09-22 | |
| --- | --- | --- | --- |
| `main` CI | **red** | **green** | resolved |
| Production Certification | red | **red** | unchanged |
| Open PRs | 40 | **43** | worse |
| `W9-CORPACT-011` | in_progress / red | in_progress / red | unchanged |
| `W9-SAFETY-007` | ready_for_acceptance (19d) | ready_for_acceptance (**21d**) | unchanged |
| W10 rows in progress | 3 | 3 | unchanged |
| `program-state.yml` snapshot | 2026-08-30 | 2026-08-30 | unchanged |

The trunk recovered: the futures expiry fix landed via #2978 and CI run
[#6348](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35691889651) is green on
`ab58115b` (2026-09-22). That was the previous determination's only Tier 0 item and it is closed.

Nothing else on that list moved, and the pull-request queue grew by three. **The ranking below is
therefore mostly the previous one, promoted by one tier**, plus one new finding that the trunk
recovery does not cover.

## Tier 0 — The certification gate is now the top blocker

`Production Certification` has failed on its last two scheduled runs:
[#75](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35486834227) (2026-09-20,
`531776b1`) and [#74](https://github.com/rodoHasArrived/Meridian-main/actions/runs/34735719426)
(2026-09-13). It runs weekly, so **it has not yet run against a green `main`** — the next scheduled
run is the first that will.

This is the point worth internalizing: **a green trunk does not mean a green gate.** The two
certification failures live in the `deterministic PostgreSQL integration and coverage evidence` job,
and both failing tests are Postgres integration tests —
`AtomicTaxLotJournalStoreTests.AppendAssetPostingAsync_…` is marked `[LedgerDatabaseFact]` and opens
a `LedgerPostgresTestDatabase` (`tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.cs:599`).
Ordinary CI never executes them. The green checkmark on `ab58115b` says nothing about either.

Both failures are carried forward from the 2026-09-20 determination, which has the full diagnosis.
One correction to it, from reading the source this pass: the guard the tax-lot test expects **does
exist** and the message matches —
`src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.AtomicTaxLots.cs:1241` throws
"Atomic tax-lot journal requires one exact asset-account {side} equal to authoritative lot cost
basis." The test is not asserting against a deleted guard; it is being pre-empted by the newer
Security Master provenance guard at
`src/Meridian.Storage/Ledger/LedgerPeriodPostingGuard.cs:204`. So the earlier reading —
guard-ordering drift, not a missing guard — holds, and the fix is a deliberate decision about which
guard speaks first, not a restoration.

**Do this first.** It is the P0 release gate, it has been red for at least nine days, and it is the
only red signal the trunk recovery did not touch.

## Tier 1 — New finding: three trading calendars, and the operator-facing one is wrong

This is new in this pass and is **not** in any registry row, risk entry, or tracker line.

Meridian has **three** independent US market holiday implementations:

| # | Location | Coverage | Juneteenth |
| --- | --- | --- | --- |
| 1 | `src/Meridian.Platform/Scheduling/TradingCalendar.cs` (705 lines, `IOperationalTradingCalendar`) | generates per year on demand (`EnsureHolidayCoverage`) | yes |
| 2 | `DataCalendarService.GetUsMarketHolidays` (`src/Meridian.Ui.Services/Services/DataCalendarService.cs:390`) | generates per year | yes |
| 3 | `TradingCalendarService` (`src/Meridian.Ui.Services/Services/DataCompletenessService.cs:575`) | **hard-coded, 2026 only, 9 dates** | **no** |

Implementation 3 is the defective one, and it is live: it is constructed by `DataCalendarService`
(`:14`), `SmartRecommendationsService` (`:27`), and `AnalysisExportWizardService` (`:30`), and its
`GetTradingDays` drives the completeness denominator at
`DataCompletenessService.cs:43` and `:502`, which feeds `DaysWithGaps` (`:640`) and the coverage
percentage.

Two distinct defects follow.

**1. It is wrong today.** The hard-coded list has nine entries; the NYSE 2026 calendar has ten. The
missing one is **Juneteenth, 2026-06-19 — a Friday**. So implementation 3 counts it as an expected
trading day, and any symbol without a bar on that date is reported to the operator as a coverage
gap that does not exist.

**2. It goes blank on 2027-01-01 — 101 days away.** `AddHolidays2026()` is the only seed. From
2027-01-01 (itself a Friday, and itself a holiday), `IsHoliday` returns `false` for *every* date,
`IsTradingDay` returns `true` for every weekday, and roughly ten phantom gaps per year enter the
completeness numbers. Nothing fails; the numbers just quietly get worse.

**3. The same screen already disagrees with itself.** `DataCalendarService` shades each day cell
with `IsMarketHoliday` (`:108`), which uses implementation **2**, while the completeness figures on
that same view come from implementation **3**. On 2026-06-19 the cell renders as a holiday *and*
is counted as a missing trading day.

**Why this ranks here:** `program-state.yml` lists "Data confidence and retained source evidence"
as the **first** item under `current_focus`. This is a live, dated, operator-visible correctness
defect in exactly that lane, and the fix is convergence onto an implementation the repository
already has — a deletion, not new algorithm authorship.

**Recommended fix shape:** retire implementation 3 and route `DataCompletenessService` at the
canonical `Meridian.Platform` calendar behind `IOperationalTradingCalendar`, then collapse
implementation 2 onto the same seam so the calendar view and the completeness numbers cannot
diverge. Note this crosses a layer boundary (`Meridian.Ui.Services` → `Meridian.Platform`); check
`docs/architecture/module-map.md` before adding the project reference, and inject the interface
rather than constructing a concrete calendar in three constructors. Seed a regression test at the
2027 boundary and on Juneteenth so the next seeded-year cliff fails loudly.

## Tier 2 — The clock-coupling class, now measured

The previous determination recommended an audit of clock-coupled fixtures after the futures bomb,
and estimated the exposure at 201 `src` files reading `DateTime.UtcNow` and 324 test files carrying
calendar literals. Those counts are real but not actionable — they are the population, not the
defect.

Narrowing it this pass: **217 near-future date literals across 60 files** fall in the window
2026-09-22 → 2028-03-25. Intersecting those with "the subject under test reads the clock" reduces
it to **nine candidate files**. Spot-checking the two soonest disproved both —
`SecurityMasterWorkbenchCommandServiceTests` (2026-11-01) passes its dates as opaque profile-field
payloads, and `InMemoryInstrumentPositionProjectionStoreSlice3Tests` (2026-10-25) passes explicit
as-of dates despite a `futureState` variable name.

So the honest finding is that **the intersection is small and mostly benign** — the futures case
was not the tip of an iceberg. The remaining seven candidates are worth one pass each, by date:

| Fires | Test | Subject |
| --- | --- | --- |
| 2026-11-26 | `Meridian.Ui.Tests/Services/DataCompletenessServiceTests.cs` | `DataCompletenessService` — **this is Tier 1** |
| 2026-12-18 | `Meridian.Tests/Execution/OrderManagementSystemTests.cs` | `OrderManagementSystem` |
| 2026-12-31 | `Meridian.Tests/Ui/AccountingConfigurationServiceTests.cs` | `AccountingConfigurationService` |
| 2027-01-01 | `Meridian.Tests/AssetOperations/AssetOperationsReadServiceTests.cs` | `AssetOperationsReadService` |
| 2027-01-15 | `Meridian.Tests/Infrastructure/Providers/IBDataServicesTests.cs` | `IBDataServices` |
| 2027-02-01 | `Meridian.Tests/Ui/AccountingReportPackageServiceTests.cs` | `AccountingReportPackageService` |
| 2027-03-15 | `Meridian.Wpf.Tests/Services/CredentialServiceTests.cs` | `CredentialService` |

The Tier 1 finding surfaced from this scan, which is the argument for running it: the scan's value
was not the time bombs it found in tests but the **production** defect it found behind one.

**Downgrade the blanket `TimeProvider` migration** the previous determination proposed as P0. On
this evidence it does not earn a repository-wide sweep. Inject a clock where a service filters on
"now" *and* a test must pin the result — that is a handful of places, not 201.

## Tier 3 — The stranded backlog, now worse

**43 open pull requests**, up from 40:

| Group | Count | Change |
| --- | --- | --- |
| `codex/*` P0/P1 remediation branches | 22 | — |
| Dependabot | 11 | +2 (#2980, #2981) |
| Recurring "Security Master … pass" drafts | **8** | +1 (#2982) |
| Feature / other | 2 | — |

Nothing landed and nothing closed in two days. The recurring Security Master pass produced its
**eighth** unconsumed draft on 2026-09-21. A recurring job whose output nobody merges is a net
negative: it costs review attention and buries the one pass that might differ. #2937 is titled
"a no-drift interval" — a result that should close its own PR rather than open one.

The 2026-09-20 recommendation stands unchanged and is now two days more expensive: triage the
remediation branches into land / re-cut / close, take the narrow ones first (#2929, #2930, #2921),
push each to a hosted `quality-gate` run, and land them one at a time. **Do not open new
remediation branches until this queue drains.**

## Tier 4 — The one red roadmap row, and the free one

- **`W9-CORPACT-011`** remains the only row with `health: red`, with the same precisely stated gap
  (`TransitionCaseAsync` refuses every transition to `ReadyForApproval` with `ProjectionStale`
  because the durable exact-version accounting projection authority is not persisted). #2947 already
  targets the PostgreSQL persistence proof it needs — and #2947 is one of the 22 stranded branches
  in Tier 3. **These are the same problem.** Landing #2947 is both backlog drain and progress on the
  only red row.
- **`W9-SAFETY-007`** has sat at `ready_for_acceptance` since 2026-09-01 — **21 days** — with all
  four exit criteria evidenced. Taking or declining that decision requires no engineering and
  clears a lane.

## Tier 5 — W10 sequencing (unchanged)

Still 3 in progress, 9 planned, and with W8/W9 still open the project is running **nine lanes at
once**. Closing order is unchanged from 2026-09-20 and the reasoning still holds:

1. **`W10-LOT-002`** — the only `critical` row, and its substrate is Tier 0's second certification
   failure. Same code.
2. **`W10-MARK-001`** (sequence 1) — implementation evidenced; live WPF rendering and preview
   certification remain. Closes `RISK-STALE-MARK-001`.
3. **`W10-SEAM-001`** (sequence 7) — implementation evidenced; closing it removes a dependency from
   the WPF parity lane.
4. Only then **`W10-RECON-001`**.

## Tier 6 — Registry and tracker hygiene

- `program-state.yml` still carries `snapshot_date: 2026-08-30` (23 days stale);
  `implementation-todo-list.md` still reads `Reviewed: 2026-08-19` (34 days stale). Its
  release-gate snapshot describes evidence frozen at `65dc0107`, a commit with no green `main`
  behind it.
- The risk register has four entries. Tier 1 — a seeded-year calendar that silently expires inside
  a `current_focus` lane — is a better candidate for a fifth than the clock-coupling class was,
  because it has a known trigger date.

## What this session did not do

No C# was changed. The two highest-value defects — the certification guard ordering (Tier 0) and
the trading-calendar convergence (Tier 1) — both sit in code this environment cannot compile or
test. Tier 0 additionally needs a PostgreSQL instance to reproduce at all. Pushing an unvalidated
change to either, including one that adds a cross-layer project reference, would trade a diagnosed
problem for an undiagnosed one. Both are specified above in enough detail to implement directly
where a build is available.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| P0 | Two certification failures (Security Master conflict persistence; tax-lot guard ordering) | P0 release gate red 9+ days; green trunk does not cover these tests |
| P0 | Retire the hard-coded 2026 trading calendar | Wrong today (Juneteenth); blank on 2027-01-01; first `current_focus` lane |
| P1 | Land #2947 | Drains the backlog *and* unblocks the only red roadmap row |
| P1 | Triage and land the ~22 stranded remediation branches | Implemented P0/P1 value, two more days of drift |
| P1 | Take or decline the `W9-SAFETY-007` acceptance | 21 days idle, zero engineering cost |
| P2 | Collapse or stop the recurring Security Master pass | 8 unconsumed drafts |
| P2 | Close `W10-LOT-002`, then `W10-MARK-001`, then `W10-SEAM-001` | Finish three lanes before opening a fourth |
| P3 | Seven remaining clock-coupled fixtures | Dated, cheap, one pass each |
| P3 | Refresh `program-state.yml` and the tracker | 23 and 34 days stale |

**Not now:** new product surface (the scope gate is explicit and nine lanes are open); #2977, a
whole-repository restyle, ahead of a green certification lane; re-litigating the six accepted W9
rows.
