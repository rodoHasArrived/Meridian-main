# Business-Day Calendar

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-10-23

## Definition

A Business-Day Calendar says which dates are business days for a stated purpose, such as a payment
centre or a settlement system. It is identified by a calendar id and carries a content hash that
changes whenever its content changes, so a schedule can record exactly which calendar content it
used.

The seam lives in `Meridian.Contracts.Calendars`: `IBusinessDayCalendar` (id, content hash,
coverage, `IsBusinessDay`), `HolidayBusinessDayCalendar`, `JointBusinessDayCalendar`,
`BusinessDayConvention`, `BusinessDayAdjustment`, `BusinessDayCalendars` and
`IBusinessDayCalendarSource`. The decision record is
[ADR-025](../adr/025-contractual-schedule-kernel.md).

## Relationships

- Used by a [Contractual Schedule](contractual-schedule.md) for accrual, effective-date,
  termination-date and payment adjustment, for payment lag, and as the business-day predicate
  Business/252 day count needs.
- A joint calendar combines two or more member calendars.
- Resolved by id from an `IBusinessDayCalendarSource`.
- Distinct from the exchange trading calendar (`TradingCalendar` in `Meridian.Platform`, which
  models sessions) and from the reconciliation accounting calendar (`BusinessDayAccountingCalendar`
  in `Meridian.FinancialOperations`, which resolves posting periods). The seam offers the same roll,
  add and count operations as the accounting calendar so the two can converge.
- Reconciliation SLA policies already name calendars (`ReconciliationSlaPolicy.BusinessCalendarId`
  and `HolidayCalendarIds`), and close preparation computes deadlines with its own inline
  business-day rule. Neither reads this seam yet.

## Business Rules

- **Identity.** A holiday calendar requires a non-blank id and a version of at least 1. Weekend days
  default to Saturday and Sunday; an undefined day of the week, or all seven days, is refused.
- **Content hash.** A canonical SHA-256 over the id, version, weekend days, holidays and coverage
  bounds, with weekend days and holidays sorted and holidays de-duplicated. The same content in a
  different input order gives the same hash; any change to version, weekend, holidays or coverage
  gives a different one.
- **Coverage.** A calendar may state the dates its holiday list is known to be complete for. A date
  outside coverage still gets an answer from the weekend mask and the holidays it knows, and the
  schedule kernel records one `CalendarCoverageExceeded` diagnostic per calendar and date it asked
  about outside coverage.
- **Joint calendars.** Members must be at least two, with distinct ids (ordinal, ignoring case). A
  date is a business day only if every member says so. The joint id is the members' ids sorted and
  joined with `+`; the joint hash is taken over the sorted id and hash pairs; coverage is the
  intersection of the members'. Joining a single calendar returns that calendar.
- **Conventions.** `Following` takes the date or the next business day; `Preceding` the date or the
  previous one. `ModifiedFollowing` follows unless that changes the month, then precedes;
  `ModifiedPreceding` precedes unless that changes the month, then follows. `Unadjusted` needs no
  calendar; any other convention applied without a calendar is an error.
- **Arithmetic.** Adding n business days counts from the day after the start, so the result is
  always a business day; a negative n counts backwards. The business-day count between two dates
  is signed over `(from, to]`. A calendar with no business day within 3,660 days of a roll is
  degenerate, and the roll or add throws instead of looping.
- **Sources never guess.** A source looks ids up ordinally, ignoring case, and refuses a duplicate
  id. Resolving no ids, or any unknown id, fails; one id resolves to that calendar; several resolve
  to their joint calendar. There is never a fallback to weekends only.
- **`WEEKENDS` is a declared calendar, not a default.** The built-in `WEEKENDS` calendar (version 1,
  Saturday and Sunday, no holidays, unbounded coverage) is used only where a schedule names it.
- **Purity.** Calendars read no clock and perform no I/O; the same content answers the same way
  everywhere.

## Examples

- On `WEEKENDS`, Saturday 2026-01-31 adjusts to Monday 2026-02-02 under `Following`, and to Friday
  2026-01-30 under `ModifiedFollowing`, because following would leave January.
- On `WEEKENDS`, Saturday 2026-08-01 adjusts to Friday 2026-07-31 under `Preceding`, and to Monday
  2026-08-03 under `ModifiedPreceding`, because preceding would leave August.
- Two business days after Friday 2026-01-30 is Tuesday 2026-02-03.
- A joint calendar of `TEST-A` (holiday 2025-11-27) and `TEST-B` (holiday 2025-12-26) has the id
  `TEST-A+TEST-B` in either order and rolls both holidays forward under `Following`, to 2025-11-28
  and 2025-12-29. `TEST-A` alone treats 2025-12-26 as a business day.

## Future Expansion Notes

A file-backed source (`<dataRoot>/calendars/{id}.json`, read through a source-generated JSON
context) is planned and will be registered in DI then; today no source is registered. Calendar data
governance is open: the identifier scheme (business-centre codes for payment calendars, exchange
codes reserved for exchange calendars), who maintains holiday files, and whether a calendar's hash
and evidence must be retained before a projection may use it. Whether the reconciliation and
close-preparation calendars converge on this seam is to be decided with their owners.

## Implementation and regression references

- [Calendar contract](../../src/Meridian.Contracts/Calendars/IBusinessDayCalendar.cs)
- [Built-in calendar, joint calendars and business-day operations](../../src/Meridian.Contracts/Calendars/BusinessDayCalendars.cs)
- Regression scope: `BusinessDayCalendarTests`, `BusinessDayAdjustmentTests` and
  `ScheduleAdjustmentTests`.

These references identify the implementation and its regression scope. The pull request carries
actual validation results.
