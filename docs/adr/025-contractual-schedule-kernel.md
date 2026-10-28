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
| Declared mapping, increment 5a | `src/Meridian.Contracts/SecurityMaster/ScheduleSpecFactory.cs` | `Resolve` and `ResolveShape`: a declared `scheduleConventions` block to a `ScheduleSpec` or a typed refusal; `DeclaredMappingVersion` |
| Versioned terms, increment 5a | `src/Meridian.Contracts/SecurityMaster/SecurityVersionedTerms.cs` | The container keys, the decode predicate, the shared block reader and the declared reads; the one `DeclaredScheduleMappingVersion` literal |
| Schedule evidence, increment 5a | `src/Meridian.Contracts/SecurityMaster/SecurityMasterCashFlow.cs` | `StructuredCashFlowScheduleEvidence`, carried by `StructuredCashFlowProjectionDto.ScheduleEvidence` |
| Calendar files, increment 5a | `src/Meridian.Storage/Calendars/FileBusinessDayCalendarSource.cs` | `<dataRoot>/calendars/{ID}.json`, loaded once at host start; refused files are listed and never replaced |
| Capability line, increment 5a | `src/Meridian.Application/Composition/SecurityMasterTermsCapabilityHostedService.cs` | `AddSecurityMasterTermsCapability`: registers the calendar source and logs the terms capability line at host start |
| Consumer, increment 5a | `src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.DeclaredConventions.cs` | The declared branch of the Security Master calculated projection |
| Ledger gate, increment 5a | `src/Meridian.Application/SecurityMaster/CashFlow/StructuredCashFlowLedgerGate.cs` | Refuses a projection that carries schedule evidence |
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

Increment 5a implements decision 7 for the Security Master calculated projection and the
`ScheduleSpecFactory` of decision 8. It registers the file-backed calendar source on every host,
and the projection's declared branch is the first production caller of the context day-count
overload, through `ScheduleDayCount`. No write path can store a declaring block before increment
5b-2, so the declared branch stays dormant in production until then. Nothing in production consumes
`SchedulePeriodId` yet. The remaining moves are proposed; their sequence is recorded in the
extensibility review's resolution passes from 2026-10-23 on.

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
- Calendar data governance is decided except for the named operations owner, who must be named
  before any real calendar file is installed, and calendar evidence retention must be decided
  before declared projections may post (the increment 5a application below).
- No holiday data ships in the repository. Until files are installed, a declaring record that needs
  a holiday calendar (for a business-day convention, a payment lag or Business/252) can name only
  `WEEKENDS`; any other id blocks its projection with a reason that names the id.

### Neutral

- Increments 1 and 3 change no persisted shape, API shape, term schema, terms version,
  engine-version string (`fixed-income-cash-flow-projection-v1` and
  `asset-obligation-projection-v1` included) or DI registration. Increment 3 adds one public
  Contracts method, `SecurityTermConventions.LegacyCouponPaymentsPerYear`.
- Increment 5a registers `FileBusinessDayCalendarSource`, which holds its calendars in a
  `StaticBusinessDayCalendarSource`, on every host. A node with no calendar files resolves only
  `WEEKENDS`. Increment 5a changes no persisted shape, term schema, terms version or
  engine-version string; the projection DTO gains one optional member, omitted from JSON when null.

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

// Declared mode (increment 5a): a declared block maps to a spec or a typed refusal, never a guess.
DeclaredScheduleResolution declared = ScheduleSpecFactory.Resolve(security, terms, calendars);
if (declared.Status == DeclaredScheduleStatus.Refused) { /* declared.Refusal.Code, .Message */ }

// Calendar files: <dataRoot>/calendars/{ID}.json, loaded once; refused files are listed.
FileBusinessDayCalendarSource files = FileBusinessDayCalendarSource.Load(dataRoot, logger);
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
- `ScheduleSpecFactoryTests` (increment 5a) — the declared mapping with its exact texts: records
  that declare nothing, the exact spec of a minimal declared Bond, cadence, roll, spellings,
  anchors, ambiguity, profile envelopes, adjustments, conflicts, calendars, refusal precedence, leg
  selection, totality over malformed terms, `ResolveShape` and determinism.
- `SecurityMasterCashFlowServiceTests`, declared-conventions partial (increment 5a) — the
  SCH-DEF-001, 003, 004 and 010 twins reach their targets for declaring records; adjusted and
  lagged payment dates; calendar, principal, `Level`, day-count (ICMA on a `Term` period included)
  and precedence refusals; the normalized-per-100 wrapper keeping a generated schedule's evidence;
  records that declare nothing project exactly as their clean twins; and the Debug coverage-gap
  line.
- `SecurityMasterCashFlowProjectionShapeTests` (increment 5a) — an undeclared projection's JSON
  property set is unchanged, and a declared one adds exactly `scheduleEvidence`.
- `FileBusinessDayCalendarSourceTests`, `BusinessDayCalendarSourceCompositionTests`,
  `SecurityMasterTermsCapabilityStartupTests` and the WPF `AppServiceRegistrationTests` (increment
  5a) — file loading, every refusal (a file whose `calendarId` and stem name different ids
  included), hashes independent of formatting, no fallback, and the capability line on servers and
  desktops without a projection request; `UiServerLoggingTests` builds a real `UiServer` and reads
  the inventory lines from the process's Serilog logger.
- `StructuredCashFlowLedgerBridgeTests` and `SecurityMasterAmortizationLedgerBridgeTests`
  (increment 5a) — a declared projection is refused after any `BlockedReason` and posts nothing.
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
  (G5, and the resolution passes from 2026-10-23 on)
- [Business-day calendar files](../operators/business-day-calendar-files.md) and
  [Security Master terms contract rollout](../operators/security-master-terms-contract-rollout.md)
  operator procedures
- [ADR-024: Terms Payload Format and Version Policy](024-terms-payload-format-and-version-policy.md)
  (declared conventions arrive as versioned flat terms)
- [ADR-022: Canonical Asset-Class Homes](022-canonical-asset-class-homes.md)
- ISDA 2006 Definitions §4.16 (day count fractions); ICMA Rule 251 (accrued interest); FpML
  business-day adjustments and payment date offsets

---

*Last Updated: 2026-10-28*

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
    block names no payment calendar is increment 5's decision (decided by the increment 5a entry
    below: it never does).
- **Declared call and put schedules arrive as versioned flat terms (2026-10-25).** Increment 4b
  adds the optional `callSchedule` and `putSchedule` arrays to Bond and CertificateOfDeposit terms
  at version 3 and to StructuredCredit terms at version 4. Like the block, they are readable but not
  yet writable ([ADR-024](024-terms-payload-format-and-version-policy.md), readable before
  writable), so no record declares a schedule through a write path yet. A call entry has twelve
  members: `callDate`, `callEndDate`, `exerciseStyle` (`European`, `American` or `Bermudan`),
  `exerciseFrequency` (a coupon-frequency spelling), `callPrice`, `priceBasis` (`PercentOfPar` or
  `PercentOfAccretedValue`), `priceType` (`Fixed` or `MakeWhole`), `makeWholeSpreadBps`,
  `partialRedemption` (`WholeOnly`, `ProRata` or `ByLot`), `minimumNoticeDays`,
  `maximumNoticeDays` and `noticeDayType` (`Business` or `Calendar`). A put entry has nine:
  `putDate`, `putEndDate`, `exerciseStyle`, `exerciseFrequency`, `putPrice`, `priceBasis` and the
  three notice members. Every member is optional and its text is kept verbatim. Both member lists
  are frozen once readable
  (`tests/fixtures/security-master/terms-element-shapes/versioned-containers.json`), so a later
  member needs a new container key at a new readable version, or the guard and reader change that
  ADR-024 describes. The entries carry no make-whole benchmark, notice calendar or contingent call
  or put terms (for example, tax and regulatory calls or a survivor's option); any of these can
  arrive later as a top-level term at a new readable version. The meanings below are normative for
  stored data and are frozen before any node writes an entry, so every node reads the same row the
  same way:
  - **Window.** An entry's window runs from its unadjusted `callDate` or `putDate` through its
    `callEndDate` or `putEndDate`, inclusive. Without an end date it runs to the day before the
    next entry of the same schedule, taking entries in ascending order of unadjusted start date
    rather than stored order, or to the instrument's maturity for the last entry; without a
    maturity the window cannot be resolved. Two entries of one schedule with the same start date
    are invalid, and increment 5's write validation is to refuse them.
  - **Exercise style.** `European` exercises on the start date only, `American` on any business
    day in the window, and `Bermudan` on dates recurring at `exerciseFrequency` within the window
    or, with no frequency, on the instrument's coupon payment dates within it. An absent
    `exerciseStyle` is not stated and has no default. Under every style the earliest exercise date
    is the start date.
  - **Price.** `callPrice` or `putPrice` is a clean percentage of what `priceBasis` names (absent
    means `PercentOfPar`), with accrued interest paid on top, and it applies throughout the
    window. An absent `priceType` means `Fixed`; a put entry carries no price type. For a
    `MakeWhole` entry, `callPrice` is the floor and `makeWholeSpreadBps` is the spread over the
    indenture's benchmark. An absent price is not stated; it never means par.
  - **Partial redemption.** `WholeOnly` allows a call of the whole outstanding amount only,
    `ProRata` reduces every holding proportionally, and `ByLot` selects the bonds redeemed by lot.
    Absent means not stated.
  - **Notice.** The issuer's notice of a call, or the holder's election of a put, falls no later
    than `minimumNoticeDays` and no earlier than `maximumNoticeDays` before the exercise date.
    `Calendar` counts calendar days. `Business` counts business days on the block's
    `paymentCalendars`, excluding the exercise date itself. Counts without a `noticeDayType`
    cannot be resolved.
  - **Scope and identity.** `putSchedule` holds holder-elective puts only; a mandatory tender stays
    `mandatoryPutDate`. An entry is identified by its schedule key and its unadjusted start date,
    and an entry without a start date is not a term.
  - **Scalars and consumers.** Until increment 5 enforces consistency between the scalar call terms
    and the schedules, the scalars stay authoritative for every current consumer. Increment 8 is
    to consume the domain record's `CallPutSchedules`, never the economic module.
  - **Economic `Call` fill.** The F# legacy upgrade fills the economic `Call` module (`call` in the
    economic terms) for Bond, CertificateOfDeposit and StructuredCredit. A class's own scalar call
    terms feed `firstCallDate` and `callPrice`, and feed `isCallable` where the class has one:
    Bond's `isCallable` and `callDate` become `isCallable` and `firstCallDate`, and
    CertificateOfDeposit is callable when `callableDate` is present, which is its first call date.
    `callPrice` stays null, because no flat class carries a scalar call price. StructuredCredit has
    no scalar call terms, and `nonCallPeriodEnd` never feeds the module: it stays null until a
    schedule is declared, then takes `isCallable` from the declared calls and leaves `firstCallDate`
    and `callPrice` null. The declared schedules feed the rest: `callSchedule` and `putSchedule`,
    each sorted stably by date; `isPuttable`, set when a put is declared; and `makeWholeSpreadBps`,
    the spread of the first `MakeWhole` entry in that order that carries one. An entry's price
    (`callPx`, `putPx`) is carried only when it passes the price gate, a `Fixed` price on a
    `PercentOfPar` basis, an absent `priceType` counting as `Fixed` and an absent `priceBasis` as
    `PercentOfPar`, because the economic price is a percentage of par. `isParCall` marks a carried
    price of exactly 100. `callType` is `MakeWhole` for a make-whole entry, otherwise the exercise
    style verbatim. Every spelling is compared exactly and case-sensitively. The module therefore
    drops the end dates, `exerciseFrequency`, `partialRedemption`, the notice members, a put's
    exercise style and a make-whole call's, `priceBasis` and `priceType` beyond the price gate and
    `callType`, every make-whole spread but the one it keeps, a spread on an entry that is not
    `MakeWhole`, the stored order, and the price of any entry that fails the price gate (make-whole,
    accreted-value or an unrecognized spelling). The retained economic-terms fold is unchanged: a
    declared, non-empty list replaces the retained one, and an empty list is still filled from it.
  - **Proposed for increment 8, not frozen (the roll and calendar parts are amended by the
    increment 5a entry below).** Increment 8 adopts or amends these when it generates
    exercise and notice dates: Bermudan dates from a `ScheduleSpec` generated `Forward`, with
    `Roll` from the block's `rollConvention` (falling back to `AnchorDay`), `Short` stubs and the
    same business-day adjustment as payment dates; the calendar for American windows and for
    `Business` notice counts when the block names no payment calendar, decided together with
    increment 5's calendar fallback; the business-day adjustment of exercise and notice dates; and
    how the make-whole amount is computed, and how engines exclude or warn on `MakeWhole` entries
    until a curve seam exists.
- **Declared mode in the Security Master engine and the file-backed calendar source (increment
  5a) (2026-10-28).** The Security Master calculated projection
  (`GET /api/security-master/{securityId}/cashflow-projections`, and the discounted-cash-flow
  valuation through `GetProjectionAsOfAsync`) follows a record's declared `scheduleConventions`
  block through the kernel, `ScheduleSpecFactory` (decision 8) maps the block to a `ScheduleSpec`,
  and every host registers a file-backed calendar source. No terms version changes and no write
  path can store a populated block until increment 5b-2 raises the write versions, so decision 7's
  opt-in stays dormant in production until 5b-2's first declaring write.
  The rules below are frozen as `security-master-declared-schedule-v1`. That is the one literal,
  `SecurityVersionedTerms.DeclaredScheduleMappingVersion`, which
  `ScheduleSpecFactory.DeclaredMappingVersion` aliases and which the startup capability line and
  every declared schedule's evidence report. The owner confirmed these rules on 2026-10-28
  (decisions 5 to 9, 12 to 14 and 16 of the increment-5 plan, recorded in the
  [extensibility review](../architecture/security-master-extensibility-review.md)). Any change to
  how a declared block maps to a schedule ships under a new mapping version.
  - **Who declares.** A record declares conventions when the shared reader
    (`SecurityVersionedTerms.ReadDeclaredScheduleConventions`) decodes a non-empty root block, the
    C# twin of F# `ScheduleConventions.isEmpty`: `{"accrualAdjusted":false}` alone declares, while
    `null`, `{}`, all-null members and whitespace-only calendars do not. The block is read only
    where the Read-mode mapping decodes it: the class declares it, the row passes the read guard's
    stamp check, the payload is not a profile envelope, and the row's own stamp is at or above the
    block's `Since` ([ADR-024](024-terms-payload-format-and-version-policy.md#applications),
    increment 5a entry). There is no flat-format check, because the mapping has none, so an untagged
    Bond that carries `customProfileId` and `profileFields` declares its root block. Call and put
    arrays alone do not declare.
  - **Dispatch by descriptor fact.** The factory switches on
    `SecurityAssetClassDescriptor.CashFlowBehavior`, never on a class name. `SwapLegs` reads the
    leg blocks from the container `SecurityMasterMapping.GetSwapLegItems` reads: the first of
    `legs`, `swapLegs` and `cashFlowLegs`, matched ignoring case, that holds a non-empty array. A
    declaring leg is refused, because declared mode does not follow legs yet, and a row the mapping
    cannot read (an earlier alias holds something other than an array) declares nothing.
    `ContractualSchedule` and `FactorAmortizing` read the root block, and a declaring record whose
    resolved terms carry legs is refused the same way. Every other behaviour declares nothing.
  - **The read guard, mirrored (fail closed).** A row of a class with a versioned schedule
    container (Bond, CertificateOfDeposit, StructuredCredit, and Swap through its legs) stamped
    outside 1 to the class's `ReadsTermsThrough` is refused with the read guard's reason, whether
    or not it carries a block. Only a newer writer can produce such a row, and every other read path
    already refuses it; without this, its legacy projection could post. A payload that declares a
    non-asset format (economic terms) declares nothing here and keeps its legacy projection.
  - **Anchors, by schema fact.** The effective date is the record's `issueDate`, the termination
    date its `maturity` and the cadence its `paymentFrequency`, each read by exact key at the
    payload root, as the Bond mapping reads them, and only where the class schema declares that key
    with that type. There is no fallback to the record's `EffectiveFrom` or to an annual cadence. A
    root value that differs from what `StructuredCashFlowTermsResolver` resolves (it also reads
    aliases, `profileFields` and the common terms) is refused as ambiguous, and so is a root value
    of a kind the anchors do not read (a timestamp `issueDate` or `maturity`, a numeric or boolean
    `paymentFrequency`) when the resolver still resolves a value. A present `issueDate` the
    resolver cannot read either is refused as `EffectiveDateMissing` with a text that names the
    value rather than calling it absent. The cadence maps through
    `SecurityTermConventions.TryParsePaymentFrequency`, `IsSchedulableCoupon` and
    `ScheduleTenor.TryFromPaymentFrequency`, so `AtMaturity` is one `Term` period (SCH-DEF-001)
    and a sub-monthly or irregular cadence is refused. The CertificateOfDeposit and StructuredCredit
    schemas declare no top-level `issueDate` or `paymentFrequency`, so a declaring row of either
    class is refused with that schema fact.
  - **Spellings.** `businessDayConvention`, `rollConvention` and `stubPolicy` parse through
    `SecurityTermConventions` (the 4a spellings above); a blank spelling is refused, not read as
    absent. `direction`, `couponRateUnit` and `couponCashBasis` must equal one of the block
    schema's allowed values exactly, case included.
  - **Conflicts and members with no effect.** Refused: a `rollConvention` other than `EndOfMonth`
    with `endOfMonthRule: true`, and `EndOfMonth` with `endOfMonthRule: false`;
    `accrualAdjusted: true` without a convention other than `Unadjusted`; a convention other than
    `Unadjusted` with `accrualAdjusted` absent (decision 5 of the increment-5 plan: the block must
    say whether the convention reaches accrual dates); a convention other than `Unadjusted` with
    `accrualAdjusted: false` and a payment lag above 0, because the kernel counts a lag from the
    unadjusted accrual end and applies no payment convention with it; and a negative lag. Other
    members that change nothing are accepted: a `Term` tenor ignores the roll, yet a roll that
    contradicts `endOfMonthRule` is refused there too.
  - **Defaults.** Forward generation, short stubs, the anchored grid, a 1,200-period limit
    (`ScheduleSpec.DefaultMaxPeriods`) and a lag of 0. The roll is the declared `rollConvention`;
    otherwise, with `endOfMonthRule: true`, `ScheduleRoll.ForAnchor(anchor, true)`, which is end of
    month for a month-end anchor and the anchor's day otherwise, the anchor being `firstRegularDate`
    or the issue date going forward and `lastRegularDate` or the maturity going backward; otherwise
    `AnchorDay`. A `Term` tenor ignores the roll. An absent `direction` stays `Forward` even when
    only `lastRegularDate` is declared; the kernel then refuses with
    `DirectionConflictsWithRegularDates`, and backward generation is never inferred.
  - **Calendars, with no fallback.** A convention other than `Unadjusted`, or a lag above 0, needs
    the block's own `paymentCalendars`: neither `commonTerms.holidayCalendarId` nor `WEEKENDS` is
    assumed. Whenever `paymentCalendars` is non-empty it must resolve, even when no convention or
    lag uses it, since Business/252 counts on it. A node with no calendar source is refused, and so
    is an id the node has not loaded; the refusal names the missing ids, trimmed and de-duplicated
    ignoring case, in declared order. Several ids resolve to their joint calendar (`GBLO+USNY`).
  - **Adjustments.** Accrual dates are adjusted only when `accrualAdjusted` is true, under the
    declared convention on the resolved calendar. Payment dates take the declared convention, or
    `Unadjusted`, on the resolved calendar, and stay unadjusted when no calendar is declared. The
    effective-date and termination-date adjustments are not set, so they take the accrual
    adjustment. A lag counts business days from the accrual end on the payment calendar.
  - **What the economic `Accrual` module's `BusinessDayConvention` means.** The 4a fill copies the
    declared convention verbatim, and it means that convention: it always governs payment dates,
    and it governs accrual dates only when the flat block's `accrualAdjusted` is true. The economic
    module does not carry `accrualAdjusted`, so it cannot say on its own whether accrual dates
    move; since decision 5 of the increment-5 plan requires `accrualAdjusted` with any convention
    other than `Unadjusted`, the flat block always says.
  - **Coupon treatment.** Coupon, face, factor and day count still come from
    `StructuredCashFlowTermsResolver`, as on the legacy path. `couponRateUnit` `Percent` divides the
    coupon by 100 and `Fraction` takes it as written (SCH-DEF-003); an absent unit keeps the legacy
    reading of a coupon at or below 1 as a fraction. The scenario shift is added afterwards, and the
    rate floors at 0. An absent `couponCashBasis`, or `DayCount`, accrues every period by its
    day-count fraction. `Level` pays the rate over periods per year on each regular period and the
    day-count fraction on a stub, and is refused on a `Term` period with a rate above 0.
  - **Interest and dates.** Interest accrues on the balance outstanding at each period's start, over
    its adjusted accrual dates [`AccrualStart`, `AccrualEnd`), through
    `ScheduleDayCount.TryPeriodFraction`, so Act/Act ICMA and Business/252 get their period context
    (SCH-DEF-004). A day-count refusal blocks the projection: ICMA on adjusted accrual dates, ICMA
    on a `Term` period (an `AtMaturity` cadence has no coupon periods per year), and Business/252
    without a calendar. A rate above 0 needs a recognized `dayCount`; Actual/365 is never assumed.
    Each row is dated on its period's payment date, and periods whose payment date is before the
    projection's as-of date are dropped: today's UTC date for `GetProjectionAsync` (the projection
    route and both ledger bridges) and the requested date for `GetProjectionAsOfAsync` (the
    discounted-cash-flow valuation's date). A kernel failure blocks the projection and never throws,
    including the period bound a far-dated maturity reaches (SCH-DEF-010).
  - **Principal, Swap and leg blocks.** The calculated bullet, the calculated sinker (equal
    principal per remaining period) and an asserted bullet (`principalSchedule: []`) are followed
    as on the legacy walk, after the same factor seeding. A non-empty, in-window contractual
    `principalSchedule` with a positive face is blocked, as are declared leg blocks and declared
    blocks beside leg-based terms. A per-leg declared mode waits for a later Swap contract step,
    and declared contractual principal for a later change.
  - **Precedence and texts.** A declaring record is checked in this order: an unsupported class and
    a missing maturity (texts unchanged); the factory's refusals, in the order of the table below
    and before the legacy issue-date fallback; step-coupon or inflation-linked terms and a missing
    coupon rate (texts unchanged); then the declared walk's refusals. A blocked projection carries
    its `TermsUsed` and no `LegSchedules`, and the normalized-per-100 wrapper keeps the declared
    reason. `{read-guard reason}` is "Unsupported assetClassTermsVersion '{stamp}' for asset class
    '{assetClass}'; this node reads versions 1 through {readsThrough}."; `{term}` is `issue date`,
    `maturity` or `payment frequency`; dates are `yyyy-MM-dd`. The third `EffectiveDateMissing`
    text and the first `TermsAmbiguous` text are for a root key that is present and not null but
    not of the kind the anchors read, the `EffectiveDateMissing` one when the cash-flow terms
    resolve no issue date: `{value}` is a string in single quotes, a number or boolean as written, or `an object` or
    `an array`, and `{kind}` is `date` for the issue date and the maturity and `string` for the
    payment frequency. The second `TermsAmbiguous` text is for a root key that is absent or null.

    | Refusal | Text |
    | --- | --- |
    | `TermsVersionUnreadable` | Declared schedule conventions cannot be read: {read-guard reason} |
    | `LegConventionsNotFollowed` | Declared schedule conventions are not yet followed on leg-based projections; the projection is blocked until per-leg declared schedules are supported. |
    | `EffectiveDateMissing` | Declared schedule conventions need the contractual issue date, and '{assetClass}' terms declare no issueDate; the record's effective-from date is not used as a schedule anchor. |
    | `EffectiveDateMissing` | Declared schedule conventions need the contractual issue date; the record carries no issueDate, and its effective-from date is not used as a schedule anchor. |
    | `EffectiveDateMissing` | Declared schedule conventions need the contractual issue date; the record's 'issueDate' is {value}, which is not a date the schedule anchors read, and its effective-from date is not used as a schedule anchor. |
    | `TermsAmbiguous` | Declared schedule conventions need one unambiguous {term}; the record's '{key}' is {value}, which is not a {kind} the schedule anchors read, but the cash-flow terms resolve '{resolved}'. |
    | `TermsAmbiguous` | Declared schedule conventions need one unambiguous {term}; the record carries no '{key}' but the cash-flow terms resolve '{resolved}' from another key or source. |
    | `TermsAmbiguous` | Declared schedule conventions need one unambiguous {term}; the record's '{key}' is '{root}' but the cash-flow terms resolve none. |
    | `TermsAmbiguous` | Declared schedule conventions need one unambiguous {term}; the record's '{key}' is '{root}' but the cash-flow terms resolve '{resolved}' from another key or source. |
    | `EffectiveDateNotBeforeTermination` | Declared schedule conventions need an issueDate before the maturity; the record carries no maturity. |
    | `EffectiveDateNotBeforeTermination` | Declared schedule conventions need an issueDate before the maturity; the record's issueDate {issueDate} is not before its maturity {maturity}. |
    | `CadenceUnavailable` | Declared schedule conventions need a paymentFrequency that is monthly or longer, or AtMaturity, and '{assetClass}' terms declare no paymentFrequency. |
    | `CadenceUnavailable` | Declared schedule conventions need a paymentFrequency that is monthly or longer, or AtMaturity; the record carries none. |
    | `CadenceUnavailable` | Declared schedule conventions need a paymentFrequency that is monthly or longer, or AtMaturity; '{raw}' is not one. |
    | `ConventionUnrecognized` | Declared {member} '{raw}' is not a recognized value. |
    | `ConventionConflict` | Declared rollConvention '{raw}' conflicts with endOfMonthRule true. |
    | `ConventionConflict` | Declared rollConvention '{raw}' conflicts with endOfMonthRule false. |
    | `ConventionConflict` | Declared accrualAdjusted true needs a businessDayConvention other than Unadjusted. |
    | `ConventionConflict` | Declared businessDayConvention '{raw}' needs accrualAdjusted stated: true adjusts accrual and payment dates, false adjusts payment dates only. |
    | `ConventionConflict` | Declared businessDayConvention '{raw}' has no effect: with paymentLagBusinessDays {n} and accrualAdjusted false, the payment date counts business days from an unadjusted accrual end. |
    | `ConventionConflict` | Declared paymentLagBusinessDays {n} must be zero or more. |
    | `CalendarRequired` | Declared businessDayConvention '{raw}' needs paymentCalendars; neither the common terms' holiday calendar nor weekends only is assumed. |
    | `CalendarRequired` | Declared paymentLagBusinessDays {n} needs paymentCalendars; neither the common terms' holiday calendar nor weekends only is assumed. |
    | `CalendarSourceUnavailable` | Declared paymentCalendars cannot be resolved: no business-day calendar source is registered on this node. |
    | `CalendarUnresolvable` | Declared paymentCalendars name calendars this node has not loaded: '{id}', '{id}'; a missing calendar is never replaced by weekends only. |
    | Declared walk: contractual principal | Declared schedule conventions are not yet followed for a contractual principal schedule; the projection is blocked. |
    | Declared walk: day count | Declared schedule conventions need a recognized dayCount to compute interest; the record carries none. |
    | Declared walk: day count | Declared schedule conventions need a recognized dayCount to compute interest; '{raw}' is not one. |
    | Declared walk: kernel | Declared schedule could not be generated: {kernel failure message} |
    | Declared walk: `Level` | Declared couponCashBasis 'Level' needs a periodic paymentFrequency. |
    | Declared walk: interest | Declared schedule interest could not be computed: {day-count refusal} |

  - **Evidence.** A generated declared projection with at least one remaining period carries
    `ScheduleEvidence` (`scheduleEvidence` in JSON, omitted when null, so an undeclared projection
    keeps its wire shape). It holds `kernelVersion` from the kernel's result,
    `declaredMappingVersion`, `calendars` and `coverageGaps`. `calendars` lists each distinct id,
    content hash and purpose used: the kernel's `CalendarsUsed`, named by `ScheduleCalendarPurpose`,
    plus a `DayCount` row for the calendar Business/252 counted business days on, which the kernel
    does not record. `coverageGaps` has one row per calendar, with the first and last date asked
    outside its coverage, both by the kernel (`CalendarCoverageExceeded`) and by a Business/252
    accrual window. A projection the factory refuses or the declared walk blocks carries no
    evidence, and neither does a declaring record with no period paying on or after the as-of date.
    The normalized-per-100 wrapper keeps a generated schedule's evidence: a declaring record with no
    par or face is generated, carries its evidence, and is blocked with the normalized-basis reason.
  - **Coverage gaps are the one named exception to "never guess" (decision 2).** A date outside a
    calendar's coverage is answered from the weekend mask and the holidays the calendar lists, so a
    holiday may be missing there. The gap is reported in the evidence and logged at Debug
    ("Declared schedule for {SecurityId} used calendar dates outside coverage: {CoverageGapCount}
    calendar(s), first {FirstCalendarId} from {FirstDate}") and never blocks the projection. Every
    other gap in what a record declares is refused.
  - **Ledger gate.** `StructuredCashFlowLedgerGate` refuses a projection that carries schedule
    evidence, after the projection's own `BlockedReason` and before the normalized-basis check:
    "Declared-schedule projections are not postable until journal identity keys on schedule period
    identity and calendar evidence retention is decided." Ledger journal ids key on payment dates,
    which a calendar change moves. The block lifts only when both hold: journal identity keys on
    `SchedulePeriodId`, and calendar evidence retention is decided.
  - **Interim calendar retention.** None is needed while projections are not persisted and posting
    is blocked: the evidence travels with each projection, which is computed on request. The
    discounted-cash-flow valuation, also computed on request, carries it as inputs: after
    `DiscountCurveVersion` and `SecurityTerms`, a `ScheduleKernel` and a `DeclaredScheduleMapping`
    input, each referencing its version, and one `BusinessDayCalendar` input per calendar,
    referencing `{CalendarId}:{Purpose}` with the content hash as its `ContentSha256`. Each coverage
    gap adds the diagnostic "CalendarCoverageGap: calendar '{id}' was asked about {first} to {last},
    outside its declared coverage; a holiday on those dates may be missing from the calendar data
    the schedule used.", with `yyyy-MM-dd` dates. A valuation of a record that declares nothing is
    unchanged. A valuation thus names the calendar content it used, and nothing retains that
    content.
  - **Calendar files.** `FileBusinessDayCalendarSource` (`Meridian.Storage.Calendars`) reads
    `<dataRoot>/calendars/{ID}.json`, one calendar per file, once at host start in ordinal file-name
    order; a restart reloads them. Each file carries exactly `calendarId`, `version`, `weekendDays`,
    `coverage` (`from` and `to`) and `holidays`, read through a strict source-generated context
    that refuses unknown and repeated members. A file is refused, with a Warning, when it is
    malformed, misses a member, has a version below 1, an id outside `^[A-Z][A-Z0-9]{1,15}$` or not
    equal to its file stem exactly, the reserved id `WEEKENDS`, a weekend day that is not exactly an
    English day name, no working day, coverage that starts after it ends, data
    `HolidayBusinessDayCalendar` rejects, or an id another file that passes these checks also
    defines (both are refused). A refused file loads nothing. Its Warning says whether each id it
    names (its `calendarId`, and its file name stem when some casing of it is a canonical id, both
    ignoring case) still resolves, from another file that loaded or as the built-in `WEEKENDS`;
    any other id stays unresolvable, is never replaced by weekends only, and is named as such when
    another id the file names still resolves. A missing directory loads only the built-in
    `WEEKENDS`, with no Warning; a `calendars` path that is a file, or a directory that cannot be
    listed, does too, with a Warning. Ids are canonical because the content hash includes the id as
    written and joint ids sort ordinally. Coverage is the data owner's attestation that the holiday list is
    complete over that range. See [Business-Day Calendar](../domain/business-day-calendar.md) and
    the [calendar file procedure](../operators/business-day-calendar-files.md).
  - **Startup capability line.** `AddSecurityMasterTermsCapability` registers the source and a
    hosted service on every host: servers through `StorageFeatureRegistration` over
    `StorageOptions.RootPath`, and the WPF desktop, which composes its own graph and can write
    Security Master terms in-process, from `App.ConfigureServices` over `ConfigStore.GetDataRoot()`.
    At host start it loads the calendars (one Information line each) and logs one Information line,
    "Security Master terms capability: declared mapping {DeclaredMappingVersion}, schedule kernel
    {KernelVersion}, terms versions {TermsVersions}, business-day calendars {CalendarCount}:
    {Calendars}", so a node's build and calendar set are visible before any record depends on them
    ([terms contract rollout](../operators/security-master-terms-contract-rollout.md)). On a server
    these lines reach the process's Serilog logger, whose file sink writes
    `<dataRoot>/_logs/meridian-<yyyyMMdd>.log`: a `HostStartup` graph logs through Serilog from
    Information, and `UiServer` (`--mode workstation`, which the lifecycle supervisor starts, and
    the UI half of `--mode desktop`) leaves its default providers as before, at Warning unless a
    configured `Logging` level lowers them, and sends these two categories alone to Serilog from
    Information. A `--mode desktop` host composes both graphs, so it logs each line twice. The WPF
    desktop logs them to the generic host's default providers only.
  - **`ResolveShape`.** It applies every factory rule above except calendar resolution, then runs
    the kernel over the spec with no adjustments and no lag; a kernel failure is `KernelRefused`,
    "Declared schedule could not be generated: {kernel failure message}". Increment 5b-1's write
    rules call it, so no factory refusal and no calendar-free kernel failure can block a written
    declaring Bond. That is all it guarantees: every check that runs after the factory can still
    block one. These are calendar availability and the calendar-dependent kernel failures
    (`BusinessDayAdjustmentFailed`, `AdjustedPeriodCollapsed`); step-coupon or inflation-linked
    terms and a missing coupon rate; and the declared walk's refusals: a contractual principal
    schedule, a missing or unrecognized `dayCount` with a rate above 0, `Level` on a `Term` period
    with a rate above 0, and the day-count refusals with a rate above 0 (Act/Act ICMA on adjusted
    accrual dates or on a `Term` period, Business/252 without a calendar). Bond's `couponRate` and
    `dayCount` are optional in its schema, so a written block does not imply either. Of these,
    5b-1's write rules are to refuse only Business/252 without block calendars, ICMA with
    `accrualAdjusted: true` and `Level` with an `AtMaturity` frequency; the others, ICMA with an
    `AtMaturity` frequency among them, stay projection-time blocks unless a later change adds write
    rules for them.
  - **The increment-8 proposals, amended.** Bermudan exercise dates take the roll the factory
    resolves (a declared `rollConvention`, then `endOfMonthRule`, then `AnchorDay`), not
    `rollConvention` alone. American windows and `Business` notice counts use the block's
    `paymentCalendars`, with no fallback to `commonTerms.holidayCalendarId` or `WEEKENDS`, and
    increment 5b-1 requires block calendars on every American entry and on `Business` notice. The
    rest of the proposal stands for increment 8 to adopt or amend.
