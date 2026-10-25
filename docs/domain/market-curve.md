# Market Curve

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-10-25

## Definition

A Market Curve is a dated set of pillars that states a market input for a currency or an index on
an economic date: discount factors for a currency (a Discount curve), projected fixings of a
floating-rate index (a Forward curve), or projected levels of an inflation index (an Inflation
curve). A curve has a write-once identity (its key, kind, currency, index, day count, interpolation
and source) and an append-only history of published versions. Each version carries the economic
as-of date its points describe, the knowledge time it was recorded, the vendor timestamp when known,
the actor who published it, the points and a canonical content hash.

The seam lives in `Meridian.Contracts.SecurityMaster`: `SecurityCurveDefinitionDto`,
`SecurityCurveVersionDto`, `SecurityCurvePointDto`, `ISecurityCurveStore` and the pure rules in
`SecurityCurveRules`. Stored curves live in the `security_master` schema under the governance log.
The decision record is [ADR-026](../adr/026-stored-curves-and-valuation-seam.md).

## Relationships

- A [Security](security.md) names an index in its terms (a floating leg's `index`, a bond's
  `floatingIndex` or `inflationIndex`), which the underlying graph records as an `Index` link. A
  Forward curve is found by that index name; there is no index master yet, so the match is the
  trimmed name, ignoring case.
- A [Contractual Schedule](contractual-schedule.md) supplies the payment dates a Discount curve
  discounts and the accrual starts a Forward curve projects fixings for. The curve supplies only the
  rate or factor; the leg supplies the day count.
- A valuation (`ISecurityValuationService`, `SecurityValuationResult`) is a rebuildable view over a
  security's terms, its projection and the curve versions it names. It is never posting authority:
  nothing it produces becomes a raw price, a golden copy, a mark or a ledger entry on its own.
- Price evidence (the pricing hierarchy, raw observations and selection receipts) is the quoted-price
  input; a curve is the model input. The two are distinct kinds of evidence and are kept apart.
- Every definition and every published version appends to `security_governance_events` on the
  `Curves` stream, in the same transaction as the write, with the actor and the curve id in the
  payload (a curve is not a security, so the event's `security_id` is null).

## Business Rules

- **Identity is write-once.** A curve key is unique, ignoring case. Re-defining a key with the same
  facts returns the stored definition; different facts are refused, and a new identity needs a new
  key. The description is descriptive and not a fact.
- **Values carry their kind.** A Discount value is a factor in (0, 1], and a pillar on the as-of
  date is 1. A Forward value is an annual rate stored as a fraction: publish 0.04, never 4; there is
  no percent heuristic on the curve. An Inflation value is a positive index level.
- **Points are dated.** Every point is dated on or after the version's as-of date; one value per
  date; a tenor is a label and names at most one point. Points are stored sorted by date.
- **Day count is canonical.** A definition's day count must parse to a known convention and is stored
  as that convention's name, so a curve never carries an unparseable convention.
- **Versions append and never change.** A new version never overwrites an earlier one; the database
  refuses update, delete and truncate. Publishing the same content for the same curve and as-of
  returns the retained version and writes nothing, so re-sending a vendor file is idempotent.
- **Reads are bitemporal.** The curve in force for a valuation is the latest version whose as-of is
  on or before the valuation date and whose knowledge time is on or before the knowledge cutoff
  (now when none is given), the same `as_of` and `recorded_at` predicate the pricing hierarchy uses.
- **Content is hashed.** The canonical SHA-256 over the as-of and the sorted points identifies the
  content whatever the input order; a projection or valuation records the curve key, version and
  hash it used.
- **Interpolation is declared.** `Linear` reads a value linearly in day number between pillars, flat
  before the first pillar; after the last pillar a Forward or Inflation curve holds flat and a
  Discount curve refuses, so a payment beyond the curve blocks rather than being extrapolated.
- **A discount factor is rebased.** The factor for a payment is the curve's value at the payment
  date divided by its value at the valuation date (1 when the valuation date is on or before the
  first pillar); a payment on or before the valuation date has factor 1.
- **Ambiguity fails closed.** Several Forward curves for one index leave the leg flat forward, as a
  host without curves is today; several Discount curves in a currency block a valuation until one is
  pinned. Nothing guesses.
- **Zero is never substituted.** A missing curve, a payment past the last pillar, a currency
  mismatch, a blocked projection or a missing model each produce a blocked valuation with no number.

## Examples

- The curve `USD-DISCOUNT-TEST`, as of 2026-01-01, with pillars (2026-01-01, 1.00),
  (2027-01-01, 0.96) and (2028-01-01, 0.92) values a 1,000,000 par, 5% annual 30/360 bond issued
  2026-01-01 and maturing 2028-01-01 at 50,000 x 0.96 + 1,050,000 x 0.92 = 1,014,000.00: price
  101.4000 percent of par, and 507,000.00 on 500,000 face.
- A one-year swap receiving fixed 4% semiannually and paying SOFR + 25 bp quarterly on 1,000,000,
  with a 3% current fixing and a flat 4% forward curve, nets -8,125, +9,375, -10,625 and +9,375 per
  quarter (0.00 undiscounted); on quarterly discount pillars 1.00, 0.99, 0.98, 0.97 and 0.96 its
  value is -162.50. Without a forward curve the floating leg stays flat at 3.25% and the
  undiscounted value is +7,500, reported with the `FloatingForecastFlatAtCurrentIndexRate`
  diagnostic and `UnobservableInputs` observability.
- The pillar pair (2027-01-01, 0.96) and (2027-12-31, 0.92) reads 0.94 on 2027-07-02, 182 of 364
  days along.

## Future Expansion Notes

An index master (currency-qualified identity for `SOFR`, `SOFR 3M` and `USD-SOFR-COMPOUND`) is
open; today ambiguity fails closed. Only `Linear` interpolation exists: log-linear discount factors,
bootstrapping from par or swap quotes and tenor-to-date resolution are not modelled. Discount
factors above 1 (negative-rate currencies) are refused pending a definition flag. Per-request
pinning of a forward version, a knowledge-time cutoff for curves inside the projection, a persisted
valuation receipt, the clean price and accrued interest split, scenarios other than Base, and curve
ingest from vendors are not yet designed. Whether a model value may enter a close-price set, and how
the mark lane maps `SecurityValuationObservability` to the ledger's fair-value level, are decided
with those lanes.

## Implementation and regression references

- [Curve contract and rules](../../src/Meridian.Contracts/SecurityMaster/SecurityCurves.cs)
- [Valuation contract](../../src/Meridian.Contracts/SecurityMaster/SecurityValuation.cs)
- [PostgreSQL curve store](../../src/Meridian.Storage/SecurityMaster/PostgresSecurityCurveStore.cs)
- [Migration 052](../../src/Meridian.Storage/SecurityMaster/Migrations/052_security_curves.sql) and
  [migration 053](../../src/Meridian.Storage/SecurityMaster/Migrations/053_security_governance_events_curves_stream.sql)
- Regression scope: `SecurityCurveRulesTests`, `SecurityCurvesMigrationTests`,
  `DiscountedCashFlowValuationModelTests`, `QuotedPriceValuationModelTests`,
  `SecurityValuationServiceTests`, `SecurityCurvesPostgresTests` and
  `SecurityMasterCurveEndpointsTests`.

These references identify the implementation and its regression scope. The pull request carries
actual validation results.
