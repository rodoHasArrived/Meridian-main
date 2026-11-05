# Direct Lending: PIK Capitalization Policy and Floating-Rate Reset Scheduling

**Status:** Implemented (2026-11-05); independent schema/storage review required before merge.
**Owner:** Direct Lending + Ledger
**Reviewed:** 2026-11-05
**Registered in:** [`docs/engineering/blueprints/README.md`](README.md)

> **Source:** [Security Master extensibility review](../../architecture/security-master-extensibility-review.md)
> lists "automatic capitalization and reset scheduling" as missing (latest state, 2026-10-28), and both
> sat outside [remediation slices 1–6](../plans/security-master-review-remediation-plan.md).
> **Roadmap:** supports `W12-CREDIT-001` exit criteria (PIK kept distinct from cash interest; floating
> resets retain index, spread, floor and cap; posted accruals need observed fixings).

## Summary

Two opt-in, persisted policies on `DirectLendingTermsDto`, executed by the daily accrual worker:

1. **`PikCapitalization`**: the PIK share of each day's interest accrues as a PIK receivable and is
   capitalized into principal on the policy's dates, so it compounds from the following day.
2. **`RateResetSchedule`**: on each reset date the floating rate is set from an index fixing observed
   a lookback earlier, bounded by a floor/cap on the index or the all-in rate. A missing fixing
   blocks accrual (fail closed).

Rates in both policies use **declared units** (`DeclaredRate` + `RateUnitNormalizer`), never magnitude.
Loans without the new terms behave exactly as before and keep their terms hashes.

## State before this change (verified 2026-11-05)

| Concern | Before | Evidence |
|---|---|---|
| PIK | Whole-loan toggle; 100% of daily interest capitalized to principal the same day | `ServicingAggregate.postDailyAccrual`, `LoanServicingStateDto.IsPikToggled` |
| PIK ledger | Dr `LoanPrincipal` / Cr `InterestIncome` daily | `LoanAccountingProjector`, `DirectLendingOutboxDispatcher` |
| Rate resets | Manual `POST /api/loans/{id}/rate-resets` only; single `CurrentRateReset`; no fixing store | `ServicingAggregate.applyRateReset` |
| Rate units | Fractions for rates, bps for spreads, by convention; no declared unit; `RateUnitNormalizer` did not exist | `DirectLendingDtos.cs`; remediation slice 4 still draft |
| Worker | Day-by-day accrual catch-up with period gating | `DailyAccrualWorker` |

## Design

### Declared rate units

`src/Meridian.Contracts/DirectLending/RateUnitNormalizer.cs` adds `DirectLendingRateUnit`
(`Fraction`, `Percent`, `BasisPoints`), the `DeclaredRate(Value, Unit)` record, and
`RateUnitNormalizer.Normalize(rate, field)`. There is no "unspecified" unit and no magnitude
inference: `0.5 Percent` is 0.005 and `0.5 Fraction` is 0.5. Field-specific ranges:

| Field | Rule |
|---|---|
| `IndexFixing`, `Spread`, `Floor` | May be negative |
| `Cap` | Must not be negative |
| `PikShare` | Must normalize into [0, 1] |
| All | \|fraction\| ≤ 10 (catches a value declared in the wrong unit, e.g. 500 as Percent) |

This is **scoped to direct lending**. The Security Master cash-flow, accounting and preview readers
(defect D10) are unchanged; slice 4 can adopt the type once its canonical units are approved by the
domain owner.

### Terms (expand-only, hash-stable)

Both members are `init` properties with `[JsonIgnore(WhenWritingNull)]`, the same pattern as
`ExcessPaymentApplication`: absent, they are omitted from the terms JSON and every pinned hash is
unchanged; present, they are material terms and change the hash. C# and F# compute identical hashes
(`DirectLendingServicingPolicyTermsHashTests`).

```csharp
public sealed record PikCapitalizationPolicy(
    PikElection Mode,                 // Always | WhenToggled (honours IsPikToggled)
    DeclaredRate PikShare,            // share of daily interest paid in kind
    PikCapitalizationFrequency Frequency, // Monthly | Quarterly | SemiAnnual | Annual | OnInterestPaymentDate
    DateOnly AnchorDate);

public sealed record FloatingRateResetSchedule(
    string IndexName,                 // must equal InterestIndexName
    RateResetFrequency Frequency,     // Monthly | Quarterly | SemiAnnual | Annual
    DateOnly FirstResetDate,
    int LookbackDays,                 // 0..31
    LookbackDayKind LookbackDayKind,  // Calendar | Business (weekdays)
    DeclaredRate Spread,
    DeclaredRate? Floor = null,
    DeclaredRate? Cap = null,
    FloorCapApplication FloorCapAppliesTo = FloorCapApplication.Index, // Index | AllIn
    IReadOnlyList<DateOnly>? ResetDates = null); // explicit dates replace the cadence
```

Validation (`DirectLendingServicingPolicyValidator`, called from both services' terms validation)
requires floating terms for a schedule, a matching index name, dates within origination..maturity,
cap ≥ floor, and normalizable units.

### Date rules (`DirectLendingServicingSchedule`, F#)

- Reset and capitalization dates are `anchor.AddMonths(k × months)` up to maturity, computed from the
  anchor so a 31st anchor gives Feb 28, Mar 31, Apr 30.
- Explicit `ResetDates` are filtered to maturity, de-duplicated and sorted.
- Fixing observation date: calendar lookback subtracts days. Business lookback rolls a weekend reset
  date back to Friday, then steps back weekdays. **No holiday calendar** (follow-up below).
- Maturity is always a capitalization date, so no accrued PIK is left uncapitalized.
- The *due* reset for a date is the latest scheduled reset on or before it that is newer than the
  current reset. This also initializes a loan that has no reset yet.

### Servicing decisions (F# aggregate)

| Decision | Rule |
|---|---|
| `postDailyAccrual` with a policy | Interest = principal × rate × day fraction, rounded 2 dp. PIK = round(interest × share, 2 dp, away from zero); cash = interest − PIK. PIK adds to `PikInterestAccruedUnpaid`; principal unchanged. Entry carries `PikCapitalizationDeferred = true`. |
| `postDailyAccrual` without a policy | Unchanged legacy toggle behaviour. |
| Floating loan with a schedule and no reset | Refuses to accrue (no silent zero rate). |
| `capitalizePik` | Requires a policy date, accrual posted through that date, and no later capitalization. Moves `PikInterestAccruedUnpaid` into principal, records a `PikCapitalizationDto(period start..end, amount)`. |
| `applyScheduledRateReset` | Requires a schedule date and the fixing for its observation date. Index mode: `bound(fixing) + spread`; AllIn mode: `bound(fixing + spread)`. Records `ResetKind = Scheduled` and `FixingObservationDate`. |

Loans with a schedule keep a `RateResetHistory` (manual resets included, tagged `Manual`). Legacy
loans keep only `CurrentRateReset`, so their servicing JSON is unchanged.

### Index fixings

`IDirectLendingIndexFixingStore` (Postgres: `loan_index_fixing`; in-memory for the nonproduction
service) is insert-only. Recording a fixing for an existing (index, date) adds a revision. The
highest revision is effective, and its id and revision go into the reset's `SourceRef` and event
payload. Index names are trimmed and upper-cased. The declared value and unit are stored next to the
normalized fraction.

### Worker order (per catch-up date)

1. Posting-period check (unchanged).
2. **Scheduled reset**, if one is due. `FixingMissing` raises an operator work item
   (`direct-lending-fixing-missing:{loan}:{yyyyMMdd}`, kind `ProviderTrustGate`, scope
   `DirectLendingRateFixing`) and **stops that loan's catch-up**. The date is retried next cycle.
3. **Daily accrual** (unchanged call).
4. **PIK capitalization**, if the date is a capitalization date not yet capitalized.

Each step is idempotent through state checks: a re-applied scheduled reset or a repeated
capitalization date is a no-op.

### Events and ledger

| Event | Ledger (synchronous projector and outbox draft journal) |
|---|---|
| `loan.daily-accrual-posted` with `pikCapitalizationDeferred: true` | Dr `AccruedInterestReceivable` / Cr `InterestIncome` (cash part); Dr **`PikInterestReceivable`** / Cr **`PikInterestIncome`** (PIK part) |
| `loan.daily-accrual-posted` (legacy) | Unchanged: PIK Dr `LoanPrincipal` / Cr `InterestIncome` |
| `loan.daily-accrual-reversed` | Mirrors whichever of the two forms the payload carries |
| **`loan.pik-capitalized`** (new) | Dr `LoanPrincipal` / Cr `PikInterestReceivable` |
| `loan.rate-reset-applied` (+ `resetKind`, `fixingObservationDate`, `fixingId`, `fixingRevision` when scheduled) | No ledger impact (unchanged) |

Legacy event payloads are byte-identical. The new fields appear only on loans that use the policies.
`DirectLendingEventRebuilder` replays both new shapes, and scenario tests assert rebuild parity.

### Storage: direct-lending migration 011

`011_direct_lending_pik_capitalization_and_rate_resets.sql` (scaffolded with
`schema-control.py new-migration --migration-set direct-lending`; 010 stays reserved for PRD-115) is
additive and re-runnable:

- `loan_index_fixing`: unique (index_name, observation_date, revision); unit check constraint.
- `loan_rate_reset_projection` (pk loan_id, effective_date) and `loan_pik_capitalization_projection`
  (pk loan_id, capitalization_date). Both are replaced from servicing state on every save, inside the
  existing Serializable save transaction.
- `loan_servicing_projection.pik_interest_accrued_unpaid`, `.last_pik_capitalization_date`;
  `accrual_entry_projection.pik_capitalization_deferred`; `loan_terms_version.pik_capitalization_json`,
  `.rate_reset_schedule_json` (all defaulted or nullable).

Applied scheduled resets retain `FixingId` and `FixingRevision` (on `RateResetDto`, the event payload
and `loan_rate_reset_projection`), so a later fixing correction cannot obscure which revision a reset
used.

**Rollout constraint: no mixed-version writers.** Older builds do not know the new terms or servicing
fields; if one saves a policy-bearing loan it drops them from `loan_state` and the terms projection.
Upgrade every writer process before setting either policy on any loan.

The schema-control manifests and generated database docs were promoted from a snapshot and
re-verified. **Independent schema/storage review is a merge gate** (see the
[remediation plan](../plans/security-master-review-remediation-plan.md)); the PR records it as pending.

### API (`/api/loans`)

| Route | Permission | Purpose |
|---|---|---|
| `POST /index-fixings` | ManageDirectLending | Record or revise a fixing (`RecordIndexFixingRequest`) |
| `GET /index-fixings?index=&from=&to=` | View | Effective fixings |
| `POST /{loanId}/rate-resets/scheduled` | ManageDirectLending | Apply a scheduled reset from the store; **422** when the fixing is missing |
| `POST /{loanId}/pik/capitalizations` | ManageDirectLending | Operator or catch-up capitalization |
| `GET /{loanId}/schedules/servicing?from=&to=` | View | Reset dates (observation date, fixing available, applied) and capitalization dates; window ≤ 3,660 days |

`DirectLendingErrorCode.FixingMissing` is new and maps to HTTP 422.

## Tests

| Area | Tests |
|---|---|
| Rate units | `RateUnitNormalizerTests` |
| Policy validation | `DirectLendingServicingPolicyValidatorTests` |
| Terms hash | `DirectLendingServicingPolicyTermsHashTests` (pins unchanged, C#/F# equality) |
| Aggregate and date rules | `DirectLendingPikCapitalizationAndResetTests.fs` |
| Ledger | `AccrualLedgerServiceTests` (deferred PIK, legacy PIK, capitalization) |
| Worker order and fail-closed | `DailyAccrualWorkerServicingScheduleTests` |
| HTTP | `DirectLendingServicingScheduleEndpointsTests` |
| Golden scenario and rebuild | `DirectLendingServicingScheduleScenarioTests` (in-memory) |
| PostgreSQL / migration 011 | `DirectLendingServicingSchedulePostgresTests` (gated by `DirectLendingDatabaseFact`) |

## Known limits and follow-ups

- **Holiday calendars:** business lookback skips weekends only. A follow-up should use the
  business-day calendar sources in `src/Meridian.Storage/Calendars/`.
- **Compounded-in-arrears (daily SOFR) and observation shift** are out of scope; the reset uses one
  term fixing per reset period.
- **Accrual formula v2 / SCH-DEF-006** (Thirty360 per calendar day) is unchanged; PIK and resets use
  formula one.
- **Inbox kind:** missing fixings reuse `OperatorWorkItemKindDto.ProviderTrustGate`. A dedicated kind
  would need browser, WPF and contract-compatibility updates.
- **In-memory service** keeps its existing unrounded daily cash interest (pre-existing divergence);
  the PIK share and capitalization decisions use the F# aggregate.
- **Security Master readers** still guess rate units by magnitude (D10); slice 4 remains open.
- **Projection precision:** rate columns keep 10 dp and amounts 8 dp (principal 2 dp, as before);
  `loan_state` JSON remains the source of truth for the aggregate.
