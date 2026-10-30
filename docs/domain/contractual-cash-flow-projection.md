# Contractual Cash-Flow Projection

**Status:** calculation capability; production terms write activation remains gated
**Owner:** Security Master / Direct Lending
**Reviewed:** 2026-10-29

The shared `ContractualCashFlowEngine` projects explicit exercise scenarios and deterministic
prepayments. It reads a versioned calculation contract and request, without accessing the clock,
storage or ledger. Security Master and direct-lending adapters resolve retained source facts and
immutable curve pins before calculation. Results are always nonpostable.

## Contracts and source authority

`ContractualCashFlowContract` version 1 contains contractual origination/maturity dates, currency,
streams and exercise entries. Rates are annual fractions; exercise prices are clean percentages.
Each stream declares principal, cadence, day count, fixed/stepped or floating rate, direction,
principal exchanges, and optional explicit coupon periods/amounts, principal or notional schedules,
caps/floors, cash/PIK split, amortization and prepayment permission/penalty.

A contract projects one currency. Retained cross-currency swap legs require separate currency-specific
inputs and are blocked rather than being relabeled or implicitly converted.

The optional `cashFlowContract` companion is readable at Bond/CD terms version 4,
StructuredCredit 5, Swap 3 and DirectLoan 2. Existing write ceilings and document stamp floors
are unchanged. The companion is decoded only at its introduction stamp and outside profile
envelopes. A stray key on an older row stays inert. Unsupported members and contract versions
refuse; existing call/put entry shapes and declared mapping v1 remain frozen. The generic terms
writer cannot activate this companion until a later reviewed write-capability rollout.
Swap leg schedule conventions likewise take effect only from their declared terms version 2;
stray convention blocks on unstamped or version-1 rows do not change projected rates or cash.

Existing retained scalar terms are adapted where complete. Missing origination dates are never
replaced by effective-from dates. Factor-based balances without reconcilable history, custom loan
amortization, later drawdowns/payments without a dated history, and unsupported structures return
blockers. A request may supply an explicit `contractOverride`, marked hypothetical in evidence;
that does not amend terms or replace an assigned provider/client schedule.

## Calculation rules

- Explicit coupon periods replace generation, cover the whole term without gaps/overlaps, and
  carry ICMA reference periods where required. Adjusted dates and payment lags must agree with
  declared conventions. Generated periods use the shared kernel and preserve separate contractual,
  accrual and payment dates.
- Accrual splits at interior rate/reset, principal/notional and exercise dates. PIK capitalizes
  at accrual end. On a cash date, coupon/capitalization and scheduled principal precede redemption;
  forecast prepayment follows on the surviving balance. Full payoff ends future accrual. Unpaid
  earned coupons retain their declared payment dates after principal reduction or redemption;
  only the open period's accrued interest settles with redemption.
- Formula amortization supports equal principal and level payments. A recast recalculates remaining
  formula installments; preserved payments shorten the term. Arbitrary schedules cannot recast.
  Absolute notional resets cannot also carry elective balance reductions.
- European entries exercise on their start date; American eligibility is an inclusive business-day
  window; Bermudan dates recur through the kernel or use coupon payment dates when cadence is
  absent. Business notice counts exclude exercise day and use declared payment calendars, with no
  calendar fallback or implicit rolling of notice dates.
- Whole-only and pro-rata redemption use outstanding principal. By-lot scenarios require selected
  positive lot amounts. Contradictory elections are rejected. The preview service currently accepts
  hypothetical notices; a claimed retained notice blocks until a source verifier can establish it.
- `BenchmarkCurvePvV1` discounts remaining contractual cash with a pinned benchmark discount-factor
  curve plus a separately compounded spread. `BenchmarkYieldPvV1` discounts with a pinned fractional
  yield benchmark plus spread. Compounding 0 means continuous; positive values are periods/year.
  Both support maturity or an explicit par-call horizon, clean floors and accrued interest added
  once. Missing benchmark coverage or accreted value blocks. Voluntary forecasts do not reduce
  make-whole continuation cash.
- CPR uses annual survival with Actual/365 Fixed; SMM uses monthly survival prorated by calendar
  month. PSA ramps annual CPR by 0.2 percentage points per seasoning month to 6%, scaled by the
  multiplier (1 = 100% PSA), with monthly survival and explicit seasoning origin. Nonoverlapping
  dated assumptions and explicit prepayment amounts are supported. Contractual penalties remain
  separate from principal and redemption premiums.

## Shared APIs and retention

`POST /api/security-master/{securityId}/cashflow-projections/preview` and
`POST /api/loans/{loanId}/projections/preview` accept `ContractualCashFlowRequest`. The request names
valuation date, knowledge cutoff, optional horizon, rate scenario, exercise selections, prepayment
assumptions and immutable curve pins. Future floating resets require pinned forward curves;
historical resets require observed fixings. Source terms and curve versions must satisfy cutoffs.

View permission permits calculation. Invalid requests return 400, missing subjects 404, and
unavailable economic inputs return a 200 result with structured blockers and no cash events.
Legacy projection routes do not change. A record declaring the new companion blocks legacy
calculated projection/amortization rather than allowing those consumers to ignore its economics.

To retain a run, submit `{ "runId": "<uuid>", "projection": { ... } }` to
`POST /api/security-master/{subjectId}/cashflow-projections/runs` or
`POST /api/loans/{subjectId}/projections/contractual-runs`. Retention requires Modify Security
Master or Manage Direct Lending respectively, and loan writes retain the existing tenant gate.
Read the same path with `/{runId}` appended using view permission.

Packets under `<dataRoot>/contractual-cashflow-runs/<kind>/<subject>/<run>.json` retain normalized
contract, exact request, source version/hash, results, authenticated actor and recording time.
Calendar answer snapshots, original coverage/hash and immutable curve versions allow replay without
live dependencies. Input/output hashes detect inconsistent packets. Publication uses a flushed
temporary file and write-once rename; conflicting identity retries return 409, while matching retries
return the retained packet before looking up live terms or curves. Back up these packets with the
data root. Hashes establish consistency, not an externally anchored tamper-proof audit chain.

This calculation lane does not execute exercises, record actual notices/payments, post forecasts,
change amortization accounting v1/v2/v3, value optimal exercise/OAS, model behavioral prepayment,
or calculate deal waterfalls. Those operations retain their existing authorities and gates.

## Validation

Pure arithmetic, compatibility and replay tests live in `ContractualCashFlowEngineTests` in
`Meridian.Contracts.UnitTests`. Adapter, cutoff, provider, durable packet and permission tests
live in `ContractualCashFlowPreviewTests` and `ContractualCashFlowEndpointTests` in `Meridian.Tests`.
Terms-version manifests retain all earlier versions and append only the new companion versions.
