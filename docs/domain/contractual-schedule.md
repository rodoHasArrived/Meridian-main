# Contractual Schedule

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-10-24

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
  calendar it used, with the calendar's content hash and purpose.
- **Day count refuses rather than degrades.** The period-context overload refuses Act/Act ICMA
  without periods per year or without reference periods that cover the window, and Business/252
  without a business-day predicate. Through a schedule, a window outside the period's accrual dates
  is refused, and so is Act/Act ICMA on a schedule whose accrual dates were adjusted (ICMA is
  defined on unadjusted coupon periods). The period-less `DayCountConventions` overloads keep their
  existing behaviour.
- **Correction is opt-in.** Corrected conventions apply only to records that declare them. A record
  that declares nothing keeps the schedule its generator has always produced, and the known defects
  in that behaviour are pinned in the
  [known-defect registry](../../tests/fixtures/schedules/KNOWN-DEFECTS.md).

## Kernel modes

Each existing generator is reproduced by a named kernel mode, proven equal to a verbatim copy of the
generator over an exhaustive grid before any call site moves. The legacy modes set no adjustments,
so the accrual start equals the unadjusted start and the payment date equals the unadjusted end.

| Mode | Spec | Reproduces | Status |
| --- | --- | --- | --- |
| Issue-anchored forward | `ScheduleSpec.IssueAnchoredForward` | Security Master cash-flow projection | Routed: `SecurityMasterCashFlowService.BuildPaymentPeriods` delegates to it |
| Compounded forward | `ScheduleSpec.CompoundedForward`, or `ScheduleGenerator.RollDates` in compounded mode, or one `ScheduleTenor.Advance` step at a time | Asset-operations bond projection, obligation timeline, fund-structure projected dates | Routed (increment 3): `AssetOperationsProjectionBuilder.BuildBondProjectedCashFlows` iterates `CompoundedForward` periods; the `AssetObligationProjectionService` steps and `InMemoryFundStructureService.RollProjectedScheduleDates` take `ScheduleTenor` steps |
| Compounded forward | `ScheduleSpec.CompoundedForward` | Both direct-lending projections | Parity proven; call sites unchanged |
| Backward, end-of-month roll, short initial stub | `ScheduleSpec.BackwardFromTermination` with `ScheduleRoll.ForAnchor(maturity, true)` and a 1,200-period limit | `FaceValueLot` backward coupon dates, including the quasi-coupon date | Parity proven; call site unchanged |
| Forward, regular periods only | Forward generation with `ScheduleStubPolicy.RequireRegular` | `FaceValueLot` regular coupon dates | Parity proven; call site unchanged |

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

## Future Expansion Notes

Planned in sequence, each behind its own proof:

- Schedule terms declared on Bond, CertificateOfDeposit, StructuredCredit and swap legs arrive
  expand-then-contract under the terms version policy of
  [ADR-024](../adr/024-terms-payload-format-and-version-policy.md), through the per-class terms
  upgrade hook that has landed. The `scheduleConventions` block (business-day convention, payment
  calendars, payment lag, roll, first and last regular dates, direction, stub policy, coupon unit and
  coupon cash basis) is readable at the next terms version of each class but not yet writable
  (increment 4a). Call and put schedules with notice periods follow at the readable version after
  that (increment 4b), and a later contract step makes both writable.
- The block's spellings are frozen ([ADR-025](../adr/025-contractual-schedule-kernel.md#applications)):
  `businessDayConvention` is `Unadjusted`, `Following`, `ModifiedFollowing`, `Preceding` or
  `ModifiedPreceding` (with `None`, `F`, `MF`, `P` and `MP` accepted); `rollConvention` is
  `AnchorDay`, `EndOfMonth`, `IMM` or a day of the month from 1 to 31; `stubPolicy` is `Short`,
  `Long` or `RequireRegular`; `direction` is `Forward` or `Backward`; `couponRateUnit` is `Percent`
  or `Fraction`; and `couponCashBasis` is `Level` or `DayCount`.
- A file-backed calendar source and a declared mode in the Security Master cash-flow engine.
- Stable asset-operations flow ids keyed by period, and a schedule book built from engine output in
  both the browser and WPF workstations.
- Reset and fixing dates, and expected lifecycle events generated from the schedule.
- Direct-lending projections and servicing accrual on the kernel, behind opt-in engine and formula
  versions, and `FaceValueLot`'s coupon-date generators last.

Calendar data governance and the default flip for records that declare nothing are open
decisions. The coupon vocabulary for declared records is fixed by `couponRateUnit` and
`couponCashBasis`; how the cash-flow engine applies them is decided with the contract step.

## Implementation and regression references

- [Schedule generator](../../src/Meridian.Contracts/SecurityMaster/Schedules/ScheduleGenerator.cs)
- [Schedule spec and legacy factories](../../src/Meridian.Contracts/SecurityMaster/Schedules/ScheduleSpec.cs)
- [Period identity](../../src/Meridian.Contracts/SecurityMaster/Schedules/SchedulePeriodId.cs)
- [Day-count engine and period-context overload](../../src/Meridian.Contracts/SecurityMaster/DayCountConventions.cs)
- Routed consumers: [Security Master cash-flow projection](../../src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.cs),
  [asset-operations bond projection](../../src/Meridian.Instruments/AssetOperations/AssetOperationsReadService.cs),
  [obligation timeline](../../src/Meridian.Instruments/AssetOperations/AssetObligationProjectionService.cs) and
  [fund-structure projected dates](../../src/Meridian.Application/FundStructure/InMemoryFundStructureService.cs)
- [Legacy coupon cadence reader](../../src/Meridian.Contracts/SecurityMaster/SecurityTermConventions.cs)
- [Known-defect registry](../../tests/fixtures/schedules/KNOWN-DEFECTS.md)
- Regression scope: `ScheduleGeneratorTests`, `ScheduleAdjustmentTests`, `SchedulePeriodIdTests`,
  `DayCountConventionsPeriodContextTests`, `ScheduleDayCountTests`,
  `ScheduleKernelLegacyParityTests`, `LegacyOracleFidelityTests`, `FaceValueLotGoldenTests`,
  `SchedulePeriodRollingRatchetTests`, `SecurityTermConventionTests`, the KnownDefect pins and the
  existing `SecurityMasterCashFlowServiceTests`, `AssetOperationsReadServiceTests`,
  `AssetObligationProjectionServiceTests` and `Meridian.FundStructure.Tests` schedule pins.

These references identify the implementation and its regression scope. The pull request carries
actual validation results.
