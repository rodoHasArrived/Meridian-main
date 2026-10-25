# ADR-025: Contractual Schedule Kernel and Business-Day Calendar Seam

**Status:** Proposed
**Date:** 2026-10-23
**Owner:** core-team
**Reviewed:** 2026-10-24
**Deciders:** core-team (review pending)
**Supersedes:** —
**Superseded by:** —

## Context

Meridian builds contractual period schedules in eight places, and each carries its own date rules.
Locations are at `7d072638`.

| Generator | Location | Rule |
| --- | --- | --- |
| Security Master cash-flow projection | `SecurityMasterCashFlowService.BuildPaymentPeriods` (:728-755) | Issue-anchored: period k ends at the issue date plus k × m months; the period that reaches maturity closes at maturity |
| Asset-operations bond projection | `AssetOperationsReadService.cs:437-445` | Compounded: each period end is the previous end plus m months |
| Obligation timeline | `AssetObligationProjectionService.cs:922-984` | Compounded steps from an as-of or anchor date; an absent or unknown frequency reads as 3 months |
| Fund-structure projected dates | `InMemoryFundStructureService.cs:2348-2416` | Compounded steps behind a lowercase frequency switch and a 256-step guard |
| Direct lending, PostgreSQL | `PostgresDirectLendingCommandService.cs:1346-1385` | Compounded from the projection as-of date; monthly, quarterly, semiannual and annual step 1, 3, 6 and 12 months, and anything else, including Bullet, steps 12 |
| Direct lending, in memory | `InMemoryDirectLendingService.Workflows.cs:404-418` | Compounded monthly from activation, bounded by the as-of date |
| `FaceValueLot` regular coupon dates | `FaceValueLot.cs:405-438` | Forward from acquisition on an end-of-month or fixed-day roll; regular periods only |
| `FaceValueLot` backward coupon dates | `FaceValueLot.cs:561-577` | Backward from maturity with a short initial stub and a quasi-coupon date |

Between them:

- None adjusts a date for business days, applies a payment lag, or records which calendar it used.
  No business-day calendar is reachable from all of them: `TradingCalendar` lives in
  `Meridian.Platform`, `BusinessDayAccountingCalendar` in `Meridian.FinancialOperations`, and
  neither is referenced by `Meridian.Instruments` or the F# projects.
- Compounded month stepping walks a month-end anchor earlier (31 January, 28 February, 28 March),
  so the compounded generators disagree with the issue-anchored one on the same bond.
- Only `FaceValueLot` models a stub, and only a short initial one. Nothing produces the reference
  periods Act/Act ICMA needs.
- `DayCountConventions` is the single day-count engine (`DayCountConventions.cs:44-50`), but its
  period-less dispatch degrades Act/Act ICMA to Act/Act ISDA and Business/252 to Actual/365, and
  every production schedule caller uses that dispatch.
- Asset-operations flow ids are minted with `Guid.NewGuid()` on every read, so nothing can key a
  period stably.

The [extensibility review](../architecture/security-master-extensibility-review.md) recorded this
as G5 on 2026-10-21 and set priority 5: one schedule engine with calendars and call/put schedules,
folding in the direct-lending logic, plus a per-class upgrade hook.

The generators sit in four assemblies: `Meridian.Contracts` (`FaceValueLot`),
`Meridian.Application` (Security Master, fund structure, direct lending), `Meridian.Instruments`
(asset operations, obligation timeline) and the F# direct-lending aggregates. The
[domain model](../architecture/meridian-domain-model.md) gives economic interpretation to
Instruments and Asset Operations, downstream of the Security Master. A shared engine therefore has
to live below the owners that interpret terms, without taking interpretation from them.

## Decision

1. **One kernel, in Contracts.** `Meridian.Contracts.SecurityMaster.Schedules` holds
   `ScheduleGenerator`, `ScheduleSpec`, `ScheduleTenor`, `ScheduleRoll`, `SchedulePeriod`,
   `ScheduleGenerationResult`, `ScheduleDayCount` and `SchedulePeriodId`. The kernel is pure and
   deterministic: no clock, no I/O, no DI. `ScheduleGenerator.Generate(ScheduleSpec)` throws only
   for a null spec; every data problem comes back as a typed `ScheduleFailure`. Every result
   carries `KernelVersion` (`contractual-schedule-kernel-v1`).
2. **A business-day calendar seam, in Contracts.** `Meridian.Contracts.Calendars` holds
   `IBusinessDayCalendar` (id, content hash, coverage, `IsBusinessDay`),
   `HolidayBusinessDayCalendar`, `JointBusinessDayCalendar`, `BusinessDayConvention`,
   `BusinessDayAdjustment`, `BusinessDayCalendars` (the `WEEKENDS` built-in and the roll, add and
   count operations) and `IBusinessDayCalendarSource`. A source resolves the ids it is asked for or
   refuses; it never falls back to weekends only. A date outside a calendar's coverage is reported
   as a diagnostic on the schedule result, not as a failure. The seam is cross-cutting, so it sits
   outside the Security Master namespace. See [Business-Day Calendar](../domain/business-day-calendar.md).
3. **A period carries three kinds of date.** Its unadjusted start and end are its identity and the
   source of its day-count reference periods. Its accrual start and end are the business-day
   adjusted dates interest accrues over. Its payment date is the accrual end under the payment
   convention or, with a lag, that many business days after the accrual end on the payment
   calendar (FpML `paymentDaysOffset`). Adjusted dates are always derived from the unadjusted grid,
   never chained from earlier adjusted dates. Accrual, effective date, termination date and payment
   each take their own adjustment. See [Contractual Schedule](../domain/contractual-schedule.md).
4. **Period identity is keyed on unadjusted dates.** `SchedulePeriodId.Derive(scopeId, streamKey,
   period)` is a name-based GUID over a fixed namespace, the scope, the stream key and the
   unadjusted start and end, derived the way `SecurityUnderlyingGraph.LinkId` is.
   `SchedulePeriodId.PeriodKey` is the readable `yyyy-MM-dd/yyyy-MM-dd` form. The consumer chooses
   the scope: the security for asset-operations flows, the projection run for direct-lending
   flows. A calendar, convention or lag change never re-keys a period.
5. **Day count stays in `DayCountConventions`.** It gains `TryFraction` and `Fraction(convention,
   start, end, DayCountPeriodContext)`. Act/Act ICMA requires periods per year and contiguous
   reference periods that cover the window; Business/252 requires a business-day predicate; 30E/360
   ISDA applies the maturity carve-out when the end is the termination date. When the context is
   missing, the new overload refuses with a reason instead of degrading. Every other convention
   returns exactly what the period-less overload returns, and the existing overloads are unchanged.
   `ScheduleDayCount` builds the context from a generated period.
6. **Legacy generators become named kernel modes before any semantics change.** The issue-anchored
   rule is `ScheduleSpec.IssueAnchoredForward`. The compounded rule is
   `ScheduleSpec.CompoundedForward`, or `ScheduleGenerator.RollDates` in compounded mode for callers
   that only step dates. A caller that interleaves its own guard, window or failure with each step
   takes the compounded grid's step itself, `ScheduleTenor.Advance(date, 1)`. `FaceValueLot`'s backward dates are backward generation on an end-of-month
   roll with a short initial stub; its regular dates are forward generation with
   `ScheduleStubPolicy.RequireRegular`. Each mode is proven equal to a verbatim, test-only copy of
   the generator it replaces over an exhaustive grid before any call site moves.
7. **Corrected semantics are opt-in.** Records that declare schedule conventions get adjusted
   dates, explicit stubs and context day count. A record that declares nothing keeps its legacy
   output byte for byte, and its defects stay pinned in the
   [known-defect registry](../../tests/fixtures/schedules/KNOWN-DEFECTS.md) until an owner-approved,
   data-evidenced default flip.
8. **The kernel owns arithmetic, not interpretation.** It owns date and period arithmetic,
   business-day adjustment, period identity and day-count dispatch. Which terms become which
   `ScheduleSpec`, and rates, notionals, coupon amounts and lineage, stay with their owners in the
   domain-model order: Security Master, then Instruments and Asset Operations, then Financial
   Operations, then Ledger. When terms start declaring conventions, one `ScheduleSpecFactory`
   beside `StructuredCashFlowTermsResolver` maps resolved terms to specs and dispatches on
   descriptor facts (`CarriesBondCouponStructure`, `CashFlowBehavior`), never on class names.
9. **New period rolls go through the kernel.** `SchedulePeriodRollingRatchetTests` counts
   `.AddMonths` and `.AddYears` period rolls in the scanned source roots outside the kernel folder,
   against allowlists that may only shrink.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Kernel | `src/Meridian.Contracts/SecurityMaster/Schedules/ScheduleGenerator.cs` | `Generate` and `RollDates`; the kernel version |
| Schedule input | `src/Meridian.Contracts/SecurityMaster/Schedules/ScheduleSpec.cs` | Dates, tenor, direction, anchor mode, roll, stubs, adjustments, lag and period limit; the `IssueAnchoredForward`, `CompoundedForward` and `BackwardFromTermination` factories |
| Period identity | `src/Meridian.Contracts/SecurityMaster/Schedules/SchedulePeriodId.cs` | Name-based period id and period key from unadjusted dates |
| Calendar contract | `src/Meridian.Contracts/Calendars/IBusinessDayCalendar.cs` | Id, content hash, coverage and the business-day test |
| Calendar operations | `src/Meridian.Contracts/Calendars/BusinessDayCalendars.cs` | The `WEEKENDS` built-in, joint calendars, and the roll, add and count operations |
| Day count | `src/Meridian.Contracts/SecurityMaster/DayCountConventions.cs` | The period-context overload that refuses instead of degrading |
| Consumer, increment 1 | `src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.cs` | `BuildPaymentPeriods` delegates to `IssueAnchoredForward` |
| Consumer, increment 3 | `src/Meridian.Instruments/AssetOperations/AssetOperationsReadService.cs` | `BuildBondProjectedCashFlows` iterates `CompoundedForward` periods |
| Consumer, increment 3 | `src/Meridian.Instruments/AssetOperations/AssetObligationProjectionService.cs` | The obligation-timeline steps take `ScheduleTenor` steps |
| Consumer, increment 3 | `src/Meridian.Application/FundStructure/InMemoryFundStructureService.cs` | `RollProjectedScheduleDates` steps by `ScheduleTenor` |
| Legacy coupon cadence | `src/Meridian.Contracts/SecurityMaster/SecurityTermConventions.cs` | `LegacyCouponPaymentsPerYear`, the one cadence reader of the Security Master and asset-operations coupon schedulers |
| Ratchet | `tests/Meridian.Tests/Architecture/SchedulePeriodRollingRatchetTests.cs` | Period rolls left outside the kernel; allowances only shrink |
| Defect registry | `tests/fixtures/schedules/KNOWN-DEFECTS.md` | SCH-DEF-001 to 011: pinned behaviour, target behaviour and owning increment |

Increment 1 implements decisions 1 to 5 and 9, the parity proof for every mode in decision 6, and
the first move under decision 6: `SecurityMasterCashFlowService.BuildPaymentPeriods` keeps its
signature and delegates to `IssueAnchoredForward`, with output proven unchanged. Increment 1 moved
no other production code onto the kernel.

Increment 3 makes the next three moves and merges the two copies of the legacy coupon-cadence
reader into `SecurityTermConventions.LegacyCouponPaymentsPerYear`. Each move keeps the method's
output, its evaluation order and the exception type it throws past 9999-12-31; only that
exception's parameter name and message text change. The frozen copy each move was proven against
stays in `LegacyScheduleOracles` with its parity grid until a separate change after the last
increment that edits that call site.

| Generator | Runs on | Moved in | Ratchet allowance |
| --- | --- | --- | --- |
| Security Master cash-flow projection (`BuildPaymentPeriods`) | `IssueAnchoredForward`; the closing grid date is re-evaluated to keep SCH-DEF-010 | Increment 1 | Never listed |
| Asset-operations bond projection (`BuildBondProjectedCashFlows`) | `CompoundedForward`; the closing grid date is re-evaluated at the start of the closing period to keep SCH-DEF-010 | Increment 3 | 1, removed |
| Obligation timeline (`NextMonthlyDate`, `NextQuarterDate`, `NextFrequencyDate`, `AdvanceAfter`) | `ScheduleTenor.Advance`; the statement cadence compounds from its anchor | Increment 3 | 4, removed |
| Fund-structure projected dates (`RollProjectedScheduleDates`) | `ScheduleTenor.Advance`, compounded, behind the forecast's own spellings and 256-date guard | Increment 3 | 4, removed |
| Direct lending, PostgreSQL (`BuildProjectedFlows`) | Its own loop; planned for increment 10 | — | 1 |
| Direct lending, in memory (`BuildFlows`) | Its own loop; planned for increment 10 | — | 1 |
| `FaceValueLot` regular and backward coupon dates | Their own loops; planned for increment 12 | — | 2 |

No calendar source is registered in DI, and nothing in production consumes `SchedulePeriodId` or
the context day-count overload. Decision 7, the `ScheduleSpecFactory` in decision 8, and the
remaining moves are proposed; their sequence is recorded in the extensibility review's resolution
passes of 2026-10-23 and 2026-10-24.

## Rationale

`Meridian.Contracts` is the only assembly every generator can reach. `Meridian.Instruments`
references Core, Contracts, Domain, ProviderSdk and Storage; `Meridian.FSharp` references
Contracts; the direct-lending aggregates reference Contracts and FSharp; `FaceValueLot` and
`DayCountConventions` already live in Contracts. Contracts has no project references and
`LayerBoundaryTests` keeps it a leaf, so the kernel uses only the BCL plus Contracts' own
`Sha256Digest` and `TextPrimitives`. The [module map](../architecture/module-map.md) gives
Domain/Core/Contracts the business and contract types consumed by multiple surfaces.

Placing schedule arithmetic in Contracts puts it beneath the owners the domain model names for
economic interpretation. That tension is resolved the way two precedents already resolve it:
`DayCountConventions` is the single day-count engine in Contracts and the F# lane delegates to it,
and `FaceValueLot`, with its coupon-date and constant-yield arithmetic, already lives in Contracts.
Arithmetic is shared below the owners; deciding what a term means stays with them (decision 8).

Freezing each generator before touching it is what makes a shared engine safe on operator-visible
paths. The cash ladder, the schedule views, direct-lending projections and governed lot
amortization all read these dates. A verbatim oracle, an exhaustive parity grid and public-seam pins
let a call site move only when its output is provably unchanged.

Unadjusted dates are the contractual schedule under ISDA 2006 §4.16 and FpML, and they do not move
when a holiday file changes. Keying identity and reference periods on them means calendar
corrections change payment dates without re-identifying any flow.

The new day-count overload refuses rather than degrades because degraded ICMA and Business/252
are defects (SCH-DEF-004). Leaving the existing overloads untouched means no amount produced today
moves; the refusal applies only to callers that opt into the context overload.

## Alternatives Considered

### Alternative 1: Put the kernel in `Meridian.Application`

**Pros:**
- Beside the Security Master cash-flow service, its first consumer.

**Cons:**
- `Meridian.Instruments` and the F# projects cannot reference Application.

**Why rejected:** Four of the eight generators could not call it.

### Alternative 2: Put the calendars beside `TradingCalendar` in `Meridian.Platform`

**Pros:**
- An existing calendar type with weekend and holiday logic.

**Cons:**
- `Meridian.FSharp` and `Meridian.Instruments` do not reference Platform.
- `TradingCalendar` models exchange sessions, not payment business days.

**Why rejected:** Unreachable from the generators that need it.

### Alternative 3: Put the calendars beside `BusinessDayAccountingCalendar` in `Meridian.FinancialOperations`

**Pros:**
- The same weekend-mask-plus-holidays model and the same roll, add and count operations.

**Cons:**
- FinancialOperations references FSharp and Instruments, so a reference back from either would be
  a cycle.

**Why rejected:** Cycle. The Contracts seam mirrors the accounting calendar's operations so the two
can converge later.

### Alternative 4: Write the kernel in F#

**Pros:**
- Natural fit for pure date arithmetic.

**Cons:**
- `FaceValueLot` and `DayCountConventions` in Contracts are C# and cannot call F#.

**Why rejected:** The governed lot amortization could never delegate to it.

### Alternative 5: A new leaf project

**Pros:**
- A visible boundary around the kernel.

**Cons:**
- Solution, project-reference graph and schema-control contract-set changes.
- No isolation gain over a Contracts folder, since Contracts is already a leaf.

**Why rejected:** Cost without benefit.

### Alternative 6: Correct the semantics in place for every record

**Pros:**
- One behaviour everywhere, sooner.

**Cons:**
- Dates and amounts would change for every bond, swap and loan at once, in operator-visible
  projections, without data evidence (how many records are `AtMaturity`, how many carry a coupon at
  or below 1) or owner approval.

**Why rejected:** Changes must be opt-in per record until a default flip is evidenced and approved.

### Alternative 7: Key period identity on adjusted dates or sequence numbers

**Pros:**
- Matches what a payment report shows.

**Cons:**
- A holiday-file correction, a convention change or an inserted stub would re-key every downstream
  flow.

**Why rejected:** Identity must survive calendar and convention changes.

## Consequences

### Positive

- One tested implementation of stubs, rolls, business-day adjustment and payment lag, following
  ISDA 2006 §4.16, ICMA Rule 251 and FpML.
- Calendar evidence on every result: the calendars used (id, content hash, purpose) and any
  coverage gaps.
- Stable period ids are available for cash-ladder and direct-lending flows.
- Missing day-count context becomes a refusal on the new path instead of a silent fallback.
- A shrink-only ratchet makes "one engine" checkable in CI.

### Negative

- Until the later increments land, the kernel coexists with the legacy period loops not yet moved
  (four after increment 3: both direct-lending projections and `FaceValueLot`'s two coupon-date
  generators), each held at its current count by the ratchet.
- Records that declare nothing keep the defects listed in the registry.
- Contracts gains public types, so the schema-control contracts manifest must be refreshed.
- Test-only oracles carry maintenance until the generators they copy are retired and the last
  increment that edits each call site has landed.
- Calendar data governance (identifiers, who maintains holiday files, evidence retention) is open.

### Neutral

- Increments 1 and 3 change no persisted shape, API shape, term schema, terms version,
  engine-version string (`fixed-income-cash-flow-projection-v1` and
  `asset-obligation-projection-v1` included) or DI registration. Increment 3 adds one public
  Contracts method, `SecurityTermConventions.LegacyCouponPaymentsPerYear`.
- `StaticBusinessDayCalendarSource` is not registered in DI; a file-backed source is planned for a
  later increment.

## Compliance

### Code Contracts

```csharp
// Generation: pure; data problems are typed failures, never exceptions.
ScheduleGenerationResult result = ScheduleGenerator.Generate(spec);
if (!result.Succeeded) { /* result.Failure.Code, result.Failure.Message */ }

// Legacy modes: bit-identical to the generators they replace.
ScheduleSpec.IssueAnchoredForward(issueDate, maturity, ScheduleTenor.Months(m));
ScheduleSpec.CompoundedForward(start, termination, ScheduleTenor.Months(m));

// Identity: unadjusted dates only; the consumer chooses the scope.
Guid id = SchedulePeriodId.Derive(scopeId, streamKey, period);

// Day count with period context: refuses instead of degrading.
bool ok = DayCountConventions.TryFraction(convention, start, end, context, out var fraction, out var refusal);

// Calendars: a source resolves or refuses; there is no weekends-only fallback.
bool resolved = source.TryResolve(calendarIds, out var calendar);
```

### Runtime Verification

- `BusinessDayCalendarTests` and `BusinessDayAdjustmentTests` — weekend and holiday rolls, the
  four conventions, signed business-day counts, joint calendars, content hashes, degenerate
  calendars and source resolution.
- `ScheduleGeneratorTests` — forward, backward, explicit first and last regular dates, short and
  long stubs, rolls (anchor day, fixed day, end of month, IMM), every refusal code and range edges.
- `ScheduleAdjustmentTests` and `SchedulePeriodIdTests` — per-purpose adjustment, payment lag from
  the accrual end, coverage diagnostics, collapsed periods, and ids that survive a convention change.
- `DayCountConventionsPeriodContextTests` and `ScheduleDayCountTests` — parity with the period-less
  overload for every other convention, and ICMA, 30E/360 ISDA and Business/252 with and without
  context.
- `ScheduleKernelLegacyParityTests` and `LegacyOracleFidelityTests` — each kernel mode against a
  verbatim copy of the generator it replaces; the Security Master, asset-operations,
  obligation-timeline, fund-structure and `FaceValueLot` copies against their production methods by
  reflection (the first four also at the end of the date range, where the legacy arithmetic threw);
  and the coupon-cadence copy against `SecurityTermConventions.LegacyCouponPaymentsPerYear` directly.
  The direct-lending copies are proven against the kernel only, until increment 10 moves them.
- `FaceValueLotGoldenTests` — 576 constant-yield values compared with exact equality.
- The KnownDefect pins (SCH-DEF-001 to 011), the 25 existing `SecurityMasterCashFlowServiceTests`
  and the existing asset-operations, obligation-timeline and fund-structure suites, unchanged;
  `SecurityTermConventionTests` pins the legacy cadence reader to explicit values.
- `SchedulePeriodRollingRatchetTests` — period rolls outside the kernel only shrink.
- Verify paths in the Implementation Links table from the repository root:

```bash
dotnet run --project build/dotnet/DocGenerator/DocGenerator.csproj -- verify-adrs --adr-dir docs/adr --src-dir src
```

This command verifies referenced files and directories exist; it does not validate behavior.

## References

- [Contractual Schedule](../domain/contractual-schedule.md) and
  [Business-Day Calendar](../domain/business-day-calendar.md) domain pages
- [Contractual-schedule known-defect registry](../../tests/fixtures/schedules/KNOWN-DEFECTS.md)
- [Security Master extensibility review](../architecture/security-master-extensibility-review.md)
  (G5, and the resolution passes of 2026-10-23 and 2026-10-24)
- [ADR-024: Terms Payload Format and Version Policy](024-terms-payload-format-and-version-policy.md)
  (declared conventions arrive as versioned flat terms)
- [ADR-022: Canonical Asset-Class Homes](022-canonical-asset-class-homes.md)
- ISDA 2006 Definitions §4.16 (day count fractions); ICMA Rule 251 (accrued interest); FpML
  business-day adjustments and payment date offsets

---

*Last Updated: 2026-10-25*

## Applications

- **Declared conventions arrive as versioned flat terms (2026-10-25).** Increment 4a adds the
  optional `scheduleConventions` block to Bond, CertificateOfDeposit and StructuredCredit terms and
  to each Swap leg. It is readable at the next terms version but not yet writable
  ([ADR-024](024-terms-payload-format-and-version-policy.md), readable before writable), so no
  record declares conventions through a write path yet, and decision 7's opt-in is still dormant.
  The block's spellings and its mapping are frozen now, so every node reads the same row the same
  way:
  - **Mapping to `ScheduleSpec`.** `direction` (`Forward` or `Backward`) maps to `Direction`,
    `stubPolicy` to `StubPolicy` and `rollConvention` to `Roll`. `firstRegularDate` and
    `lastRegularDate` map to the members of the same name, and `paymentLagBusinessDays` to
    `PaymentLagBusinessDays`. `businessDayConvention`, `accrualAdjusted` and `paymentCalendars`
    build the accrual and payment adjustments, with the payment calendars joined under
    `BusinessDayCalendars.JointCalendarId`. `endOfMonthRule` is recorded for the factory, which
    decides how it combines with `rollConvention`. `couponRateUnit` (`Percent` or `Fraction`) and
    `couponCashBasis` (`Level` or `DayCount`) are coupon-amount inputs, not schedule inputs.
  - **Spellings.** `SecurityTermConventions` parses `businessDayConvention` (`Unadjusted`/`None`,
    `Following`/`F`, `ModifiedFollowing`/`MF`, `Preceding`/`P`, `ModifiedPreceding`/`MP`),
    `rollConvention` (`AnchorDay`, `EndOfMonth`/`EOM`, `Imm`, a day of the month from 1 to 31, or
    `FixedDay(n)`) and `stubPolicy` (`Short`, `Long`, `RequireRegular`/`NoStub`). `NEAREST`, `FRN`
    and positional stub spellings such as `ShortInitial` are refused. Named spellings match ignoring
    case and separators, so `IMM` and `Modified-Following` are accepted too.
  - **Economic `Accrual` fill.** The F# legacy upgrade fills the economic `Accrual` module from a
    declared block for Bond, CertificateOfDeposit and StructuredCredit only, and only when the
    block names a business-day convention or a non-blank payment calendar. The convention is kept
    verbatim: `accrualAdjusted = false` stays in the flat block and does not turn it into
    `Unadjusted`. The holiday calendar is the joint id of the payment calendars, with no fallback to
    `commonTerms.holidayCalendarId`. A Swap leg's block fills nothing, because the economic swap
    document has no per-leg accrual module.
  - **Calendar precedence.** The block's `paymentCalendars` govern the schedule's payment calendar,
    and `commonTerms.holidayCalendarId` stays the security-level calendar; the bond accrual
    projection keeps reading it. Whether increment 5's `ScheduleSpecFactory` falls back to it when a
    block names no payment calendar is increment 5's decision.
