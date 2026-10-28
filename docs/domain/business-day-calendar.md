# Business-Day Calendar

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-10-28

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
  `BusinessDayCalendars.JointCalendarId` gives the same id from the ids alone, without resolving
  any calendar: it drops blank ids, trims the rest and removes duplicates ignoring case before
  sorting and joining, and returns a single id as itself. Terms that name payment calendars record
  their joint calendar under this id.
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

## File-backed source

`FileBusinessDayCalendarSource` (`Meridian.Storage.Calendars`) is the source every host registers,
through `AddSecurityMasterTermsCapability`: servers over their storage root, and the WPF desktop
over the data root its configuration store reports. The Security Master engine resolves declared
`paymentCalendars` against it ([ADR-025](../adr/025-contractual-schedule-kernel.md#applications)),
and the operator procedure is
[Business-Day Calendar Files](../operators/business-day-calendar-files.md).

- **One calendar per file.** A calendar is `<dataRoot>/calendars/{ID}.json`, holding exactly
  `calendarId`, `version`, `weekendDays`, `coverage` (`from` and `to`) and `holidays`, read through
  a strict source-generated JSON context that refuses unknown and repeated members.
- **Canonical ids.** An id is an uppercase ASCII letter followed by 1 to 15 uppercase ASCII letters
  or digits (`^[A-Z][A-Z0-9]{1,15}$`), so it cannot carry the `+` that joins a joint id, and it must
  equal its file name stem exactly. The content hash includes the id as written and a joint id sorts
  its members ordinally, so `usny` and `USNY` would otherwise give two hashes and two joint ids for
  the same data.
- **Weekend days** are exact English day names, `Monday` to `Sunday`. A number, an abbreviation or
  another casing is refused, and at least one day must be a working day.
- **Coverage is an attestation.** Both bounds are required. They are the data owner's statement
  that the holiday list is complete over that range; outside it the calendar still answers from its
  weekend mask and the holidays it lists, and a schedule reports the dates as coverage gaps.
- **`WEEKENDS` is reserved.** A file cannot define it, and the built-in calendar always resolves.
- **No weekends fallback.** A refused file is logged with its reason and its id stays unresolvable;
  nothing is substituted for it. A missing `calendars` directory loads only `WEEKENDS`, with no
  warning, and so does a `calendars` path that is a file, with a warning. Two files that resolve to
  one id are both refused.
- **Loaded once.** Files are read at host start in ordinal file-name order. There is no hot reload;
  a changed file takes effect when the host restarts. At start the host logs one line per loaded
  calendar (id, version, hash and coverage) and the Security Master terms capability line, which
  lists every loaded id with its version and hash.
- **Versions.** Any change to a file's content ships under a higher `version`. The hash covers the
  version, so two nodes that report one id and version with different hashes hold different data.
- **Governance (decided 2026-10-28).** Ids are FpML business-centre codes in the canonical grammar;
  exchange codes are not used for payment calendars. Coverage is the data owner's completeness
  attestation, and any change bumps the version. A named operations owner must be recorded before
  any real calendar file is installed; no holiday file ships in the repository.
- **Retention.** No retention is needed while declared projections are not persisted and cannot
  post: the ledger gate refuses them. Lifting that gate needs a decision on retaining calendar
  evidence, together with journal identity keyed on schedule period identity.

## Future Expansion Notes

Whether the reconciliation and close-preparation calendars converge on this seam is to be decided
with their owners. A diagnostics endpoint listing each node's calendars is optional; the startup
lines are the verification today. Calendar evidence retention must be decided before declared
projections may post.

## Implementation and regression references

- [Calendar contract](../../src/Meridian.Contracts/Calendars/IBusinessDayCalendar.cs)
- [Built-in calendar, joint calendars and business-day operations](../../src/Meridian.Contracts/Calendars/BusinessDayCalendars.cs)
- [File-backed source](../../src/Meridian.Storage/Calendars/FileBusinessDayCalendarSource.cs)
- Regression scope: `BusinessDayCalendarTests`, `BusinessDayAdjustmentTests`,
  `ScheduleAdjustmentTests`, `FileBusinessDayCalendarSourceTests`,
  `BusinessDayCalendarSourceCompositionTests` and `SecurityMasterTermsCapabilityStartupTests`.

These references identify the implementation and its regression scope. The pull request carries
actual validation results.
