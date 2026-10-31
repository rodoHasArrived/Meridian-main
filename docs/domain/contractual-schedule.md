# Contractual Schedule

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-10-28

## Definition

A Contractual Schedule is the ordered set of periods an instrument's terms imply between an
effective date and a termination date. Each period records:

- the unadjusted start and end that identify it,
- the business-day adjusted accrual start and end that interest accrues over,
- the date payment is due,
- its kind: regular, term, or a short or long initial or final stub,
- the unadjusted reference periods a day-count convention needs to measure it.

A schedule is generated, not stored. `ScheduleGenerator.Generate` turns a `ScheduleSpec` into a
`ScheduleGenerationResult`: either the ordered `SchedulePeriod` list or a typed failure, together
with diagnostics and the calendars it used. The decision record is
[ADR-025](../adr/025-contractual-schedule-kernel.md).

## Relationships

- Built from a `ScheduleSpec` by the owner that interprets the instrument's terms. The Security
  Master holds the terms; Instruments and Asset Operations interpret them; Financial Operations and
  the Ledger consume the resulting flows. The schedule does not own rates, notionals, coupon amounts
  or lineage.
- Uses [Business-Day Calendars](business-day-calendar.md) for accrual, effective-date,
  termination-date and payment adjustment, and for payment lag.
- Supplies the context `DayCountConventions` needs for Act/Act ICMA, 30E/360 ISDA and Business/252:
  reference periods, periods per year, the termination date and a business-day predicate
  (`ScheduleDayCount`).
- Gives each period a stable identity (`SchedulePeriodId`) that a consumer combines with its own
  scope and stream to key flows.
- Belongs to an instrument, a [Security](security.md) or a direct-lending loan, through the terms it
  is built from.

## Business Rules

- **Identity is unadjusted.** A period is identified by its unadjusted start and end. Changing a
  calendar, a business-day convention or a payment lag changes accrual and payment dates, never the
  period id or its `PeriodKey` (`yyyy-MM-dd/yyyy-MM-dd`).
- **Adjusted dates come from the grid.** Each adjusted boundary is the unadjusted boundary under its
  own adjustment, never a step from an earlier adjusted date. The first boundary uses the
  effective-date adjustment, the last the termination-date adjustment (each defaults to the accrual
  adjustment), and inner boundaries the accrual adjustment. A period whose adjusted end does not
  fall after its adjusted start is refused (`AdjustedPeriodCollapsed`).
- **Payment date.** With no lag, the payment date is the accrual end under the payment adjustment.
  With a lag of n business days, it is n business days after the accrual end on the payment
  calendar, so it is always a business day; the payment convention is not applied first.
- **Grid.** A month or year tenor steps from the generation anchor. Anchored mode places date k at
  the anchor plus k tenors and then applies the roll. Compounded mode adds one tenor to the previous
  date; it requires forward generation, the anchor-day roll and short stubs, and takes no explicit
  regular dates (`CompoundedModeRestricted`). Day and week tenors step by days, generate forward only and
  take only the anchor-day roll. A grid date past the last representable date (9999-12-31) is
  treated as an overshoot, never as an error. (The Security Master's issue-anchored walk and the
  asset-operations bond projection's compounded walk still evaluate that closing date and throw,
  as they always have, until SCH-DEF-010 is fixed; the obligation-timeline and fund-structure date
  steps likewise throw past that date, pinned as SCH-DEF-011.)
- **Roll.** Anchor day, a fixed day of the month (clamped in short months), end of month, or IMM
  (the third Wednesday). A generation anchor that is not on its roll day is refused
  (`AnchorNotOnRollDay`).
- **Direction and stubs.** Forward generation starts at the first regular date, or the effective
  date, and puts any remainder in a final stub. Backward generation starts at the last regular date,
  or the termination date, and puts any remainder in an initial stub. The stub policy chooses a
  short stub, a long stub (merged with the adjacent regular period), or refusal
  (`RequireRegular`). A first regular date requires forward generation; a last regular date on its
  own requires backward generation. When both are set, the regular grid must run exactly from one to
  the other (`RegularDatesNotOnGrid`).
- **Reference periods.** Always unadjusted and always taken from the generation grid, which is never
  re-anchored at a stub boundary. A regular or term period references itself; a short stub
  references the full notional period it sits in; a long stub references the notional periods it
  spans.
- **Failures and diagnostics.** Generation throws only for a null spec. Invalid inputs come back as
  a `ScheduleFailure` with a code, including `InvalidSpecification` for a default tenor or an
  undefined enum value and `BusinessDayAdjustmentFailed` when an adjustment or payment-lag walk
  cannot find a business day or leaves the supported date range. A date outside a calendar's coverage adds a
  `CalendarCoverageExceeded` diagnostic and generation still succeeds. `MaxPeriods` bounds the
  schedule (1,200 by default; the issue-anchored and compounded factories set no limit, as the
  generators they reproduce had none).
- **Evidence.** Every result names the kernel version (`contractual-schedule-kernel-v1`) and each
  calendar it used, with the calendar's content hash and purpose. A Security Master projection
  generated in declared mode carries that evidence with the declared-mapping version, the calendar
  Business/252 counted on, and the dates each calendar was asked about outside its coverage, and a
  discounted-cash-flow valuation of it names that evidence among its inputs.
- **Day count refuses rather than degrades.** The period-context overload refuses Act/Act ICMA
  without periods per year (a `Term` schedule has none) or without reference periods that cover the
  window, and Business/252 without a business-day predicate. Through a schedule, a window outside
  the period's accrual dates is refused, and so is Act/Act ICMA on a schedule whose accrual dates
  were adjusted (ICMA is defined on unadjusted coupon periods). The period-less
  `DayCountConventions` overloads keep their existing behaviour.
- **Correction is opt-in.** Corrected conventions apply only to records that declare them. A record
  that declares nothing keeps the schedule its generator has always produced, and the known defects
  in that behaviour are pinned in the
  [known-defect registry](../../tests/fixtures/schedules/KNOWN-DEFECTS.md).

## Kernel modes

Each existing generator is reproduced by a named kernel mode, proven equal to a verbatim copy of the
generator over an exhaustive grid before any call site moves. The legacy modes set no adjustments,
so the accrual start equals the unadjusted start and the payment date equals the unadjusted end.
The declared mode reproduces no generator: it is the corrected semantics a record opts into by
declaring conventions, and its mapping is frozen as `security-master-declared-schedule-v1`
([ADR-025](../adr/025-contractual-schedule-kernel.md#applications)).

| Mode | Spec | Reproduces | Status |
| --- | --- | --- | --- |
| Issue-anchored forward | `ScheduleSpec.IssueAnchoredForward` | Security Master cash-flow projection | Routed: `SecurityMasterCashFlowService.BuildPaymentPeriods` delegates to it |
| Compounded forward | `ScheduleSpec.CompoundedForward`, or `ScheduleGenerator.RollDates` in compounded mode, or one `ScheduleTenor.Advance` step at a time | Asset-operations bond projection, obligation timeline, fund-structure projected dates | Routed (increment 3): `AssetOperationsProjectionBuilder.BuildBondProjectedCashFlows` iterates `CompoundedForward` periods; the `AssetObligationProjectionService` steps and `InMemoryFundStructureService.RollProjectedScheduleDates` take `ScheduleTenor` steps |
| Compounded forward | `ScheduleGenerator.RollDates` in compounded mode through `DirectLendingSchedulePeriods.LegacyCompounded` | Both direct-lending projections | Routed with legacy date and whole-period cutoff parity |
| One-day forward | A one-day `ScheduleSpec` and `ScheduleDayCount` | Direct-lending daily PIK, cash-interest and commitment-fee accrual | Routed with formula-version-one parity; the terminal representable day retains its scalar edge handling |
| Backward, end-of-month roll, short initial stub | `ScheduleSpec.BackwardFromTermination` with `ScheduleRoll.ForAnchor(maturity, true)` and a 1,200-period limit | `FaceValueLot` backward coupon dates, including the quasi-coupon date | Parity proven; call site unchanged |
| Forward, regular periods only | Forward generation with `ScheduleStubPolicy.RequireRegular` | `FaceValueLot` regular coupon dates | Parity proven; call site unchanged |
| Declared | `ScheduleSpecFactory.Resolve` over a declared `scheduleConventions` block: anchored on the record's `issueDate` and `maturity`, its `paymentFrequency` as the tenor (`AtMaturity` is one `Term` period), forward unless `Backward` is declared, the declared or resolved roll, short stubs unless declared, payment adjustment on the block's `paymentCalendars` (accrual adjustment there too only when `accrualAdjusted` is true), the declared payment lag, and a 1,200-period limit | No legacy generator; the corrected semantics for declaring records | Routed (increment 5a): the declared branch of the Security Master calculated projection (`SecurityMasterCashFlowService.DeclaredConventions.cs`). Bond declarations are writable at terms version 3; configured writers require an activated capability floor and the deployment gates |

## Examples

- A one-year quarterly bond issued on 2026-01-31. Issue-anchored, its periods end 2026-04-30,
  07-31, 10-31 and 2027-01-31. Compounded over the same dates, they end 04-30, 07-30, 10-30 and
  2027-01-30, followed by a one-day short final stub to 2027-01-31: the month-end drift pinned as
  SCH-DEF-002.
- A semiannual schedule generated backward from 2027-06-15 with an effective date of 2026-08-01. The
  first period is a short initial stub [2026-08-01, 2026-12-15] referencing
  [2026-06-15, 2026-12-15], followed by a regular period to 2027-06-15. Under Act/Act ICMA the stub
  measures 136/366: 136 days over twice the 183-day reference period.
- A 5% semiannual coupon on 1,000,000 for the regular period [2025-06-15, 2025-12-15] pays
  25,000.0000 under Act/Act ICMA and 25,068.4932 under Act/Act ISDA (183/365).
- A swap-style leg from 2025-01-31 to 2026-01-31: quarterly, end-of-month roll, modified-following
  accrual and payment on `WEEKENDS`, two-day payment lag. Accrual ends 2025-04-30, 07-31, 10-31 and
  2026-01-30; payments fall on 2025-05-02, 08-04, 11-04 and 2026-02-03; the last period's key still
  ends 2026-01-31.

## Current write and consumer boundaries

The production Contracts `ScheduleSpecFactory` implements the frozen increment-5a mapping. It
reads the versioned record and descriptor, accepts monthly-or-longer coupon cadences or
`AtMaturity`, and uses only the block's payment calendars. The calculated Security Master projection
uses it with file-backed calendars and period-aware day count. A nonempty dated principal schedule
joins that grid when face is retained: principal keeps its explicit cash dates, same-day interest
precedes reduction, and residual principal pays on the final coupon payment date. Mid-period
projections reconstruct the full still-payable coupon; dated factors must agree with retained
principal movements. ADR-025 records the segment allocation and evidence rules.

`ScheduleTermsWriteRules` and the shared schema now validate declarations on canonical writes,
term-block edits and governed field-edit submission/merge. Bond and CertificateOfDeposit write
through terms version 3 and StructuredCredit through version 4. Stamp model S2 retains document
floors 1, 1 and 2 respectively: an undeclared scalar record keeps its old stamp and bytes, conventions
raise its document to their introduction version, and call/put entries raise it to theirs. Retained
containers above a document's own stamp cannot become active through an unrelated amendment.
CertificateOfDeposit and StructuredCredit still refuse a populated `scheduleConventions` block:
their schemas lack the top-level `issueDate` and `paymentFrequency` required by the frozen mapping.
Swap conventions remain readable ahead of its write version. The
[terms contract rollout](../operators/security-master-terms-contract-rollout.md) requires a reviewed
fleet inventory, data gates and explicit capability-floor activation before the first declaring
write; source availability is not deployment evidence.

Call and put entries retain the frozen window, exercise, clean-price, notice and calendar semantics
in [ADR-025](../adr/025-contractual-schedule-kernel.md#applications). Write rules require exercise
style, coherent windows and prices, and block payment calendars for American exercise or business-day
notice. Until increment 8 implements redemption scenarios, exercise/notice date generation and
make-whole amounts, consumers continue using scalar call terms and `mandatoryPutDate`. Declared
calculated projections carry mapping, kernel and calendar evidence and remain blocked from ledger
posting. Asset-operations and cash-ladder declaration support, period-keyed journal identity and
retained posting evidence remain separate work.

Both direct-lending projection loops use the shared compounded date grid. Daily PIK, cash-interest
and commitment-fee accrual use one-day kernel periods while preserving formula-version-one amounts,
including its retained Thirty360-as-Actual360 behavior. These migrations do not add missed-day
catch-up or an automatic reset calendar. Manual rate resets retain the operator's explicit effective
date; scheduled resets and capitalization require their own persisted cadence, fixing, formula and
residual policies before a new engine/formula version can opt in.

`FaceValueLot` coupon-date generators remain on their legacy implementations. A default flip for
undeclared records remains an explicit decision. Installing real calendar files still requires a
named calendar operations owner.

## Implementation and regression references

- [Schedule generator](../../src/Meridian.Contracts/SecurityMaster/Schedules/ScheduleGenerator.cs)
- [Schedule spec and legacy factories](../../src/Meridian.Contracts/SecurityMaster/Schedules/ScheduleSpec.cs)
- [Period identity](../../src/Meridian.Contracts/SecurityMaster/Schedules/SchedulePeriodId.cs)
- [Day-count engine and period-context overload](../../src/Meridian.Contracts/SecurityMaster/DayCountConventions.cs)
- [Declared mapping](../../src/Meridian.Contracts/SecurityMaster/ScheduleSpecFactory.cs) and the
  [declared branch of the Security Master projection](../../src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.DeclaredConventions.cs)
- Routed consumers: [Security Master cash-flow projection](../../src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.cs),
  [asset-operations bond projection](../../src/Meridian.Instruments/AssetOperations/AssetOperationsReadService.cs),
  [obligation timeline](../../src/Meridian.Instruments/AssetOperations/AssetObligationProjectionService.cs),
  [fund-structure projected dates](../../src/Meridian.Application/FundStructure/InMemoryFundStructureService.cs),
  [direct-lending projection periods](../../src/Meridian.Application/DirectLending/DirectLendingSchedulePeriods.cs) and
  [daily direct-lending accrual](../../src/Meridian.FSharp/Domain/DirectLending.fs)
- [Legacy coupon cadence reader](../../src/Meridian.Contracts/SecurityMaster/SecurityTermConventions.cs)
- [Known-defect registry](../../tests/fixtures/schedules/KNOWN-DEFECTS.md)
- Regression scope: `ScheduleGeneratorTests`, `ScheduleAdjustmentTests`, `SchedulePeriodIdTests`,
  `DayCountConventionsPeriodContextTests`, `ScheduleDayCountTests`,
  `ScheduleKernelLegacyParityTests`, `LegacyOracleFidelityTests`, `FaceValueLotGoldenTests`,
  `SchedulePeriodRollingRatchetTests`, `SecurityTermConventionTests`, the KnownDefect pins and the
  existing `SecurityMasterCashFlowServiceTests`, `AssetOperationsReadServiceTests`,
  `AssetObligationProjectionServiceTests` and `Meridian.FundStructure.Tests` schedule pins; for the
  declared mode, `ScheduleSpecFactoryTests`, the declared-conventions partial of
  `SecurityMasterCashFlowServiceTests` and `SecurityMasterCashFlowProjectionShapeTests`.

These references identify the implementation and its regression scope. The pull request carries
actual validation results.



## Contractual projection extension

The [calculation-only contractual companion](contractual-cash-flow-projection.md) extends exercise,
notice, make-whole, structured coupon/principal, per-leg conventions and deterministic prepayment
scenarios under its own mapping/engine version. Existing declared mapping v1 and ledger gates are
unchanged; the new companion is readable before writable.
