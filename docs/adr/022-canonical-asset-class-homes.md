# ADR-022: Canonical Asset-Class Homes for Overlapping Instrument Routes

**Status:** Accepted
**Date:** 2026-08-24
**Owner:** core-team
**Reviewed:** 2026-08-24
**Deciders:** core-team
**Supersedes:** —
**Superseded by:** —

## Context

The Security Master extensibility review
([security-master-extensibility-review.md](../architecture/security-master-extensibility-review.md),
finding 2) identified instruments with three or four legitimate modeling routes:

- **MBS / ABS / CLO / CMBS / CDO tranches and IO/PO strips** could be modeled as `Bond` with a
  securitized `BondSubclass` (`AssetBacked`, `MortgageBacked`, `AgencyMbs`, `CommercialMbs`, `Cmo`,
  `Clo`, `Cdo`, `PrincipalOnly`, `InterestOnly`, `InverseInterestOnly`), as `StructuredCredit`, or
  as `CustomAsset` with a governed profile — and the operational readiness catalog labeled
  `CustomAsset` as literally "MBS / ABS / CLO / CMBS / private assets".
- **Stable-NAV money-market vehicles** could be modeled as `MoneyMarketFund` or as `InvestmentFund`
  with `isStableNav = true`.
- **Sweep vehicles** could be modeled as `CashSweep` or `MoneyMarketFund`.

Multiple legitimate homes for the same instrument mean reporting, risk, and reconciliation cannot
assume a stable partition: the same CLO tranche lands in different pipelines depending on which
route the ingesting operator or provider mapping happened to pick.

## Decision

One canonical home per instrument family, enforced by the asset-class validators:

1. **Securitized products (MBS, ABS, CLO, CMBS, CDO tranches, and IO/PO strips) are modeled as
   `StructuredCredit`.** Only `StructuredCreditTerms` carries the tranche, pool, original face,
   current factor, dated factor schedule, and maturity anchor that pool amortization and
   structured cash-flow projection require; a securitized `BondSubclass` is a label those
   pipelines cannot act on. The Bond validator raises Error-severity
   `SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL` for the ten securitized subclasses, which blocks
   governed workflows until the record is re-modeled through a governed amendment. The subclasses
   remain in the `BondSubclass` union for read tolerance — existing rows stay readable and
   re-playable — but they are no longer an accepted destination.
2. **Profile-backed securitized records must resolve to `StructuredCredit`.** A `CustomAsset`
   pinned to a registered reclassifying profile (e.g. `structured-credit-io-po`) already resolves
   to `StructuredCredit` at the write seam; a generic `CustomAsset` whose classification metadata
   names a securitized product raises Warning-severity `SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL`.
   `CustomAsset` remains the home for private and other assets with no first-class kind.
3. **Stable-NAV money-market vehicles are modeled as `MoneyMarketFund`.** `InvestmentFund` is the
   generic wrapper for ETFs, mutual funds, hedge funds, REITs, and closed-end funds; a record
   flagged `isStableNav = true` raises Warning-severity
   `SM_INVESTMENT_FUND_STABLE_NAV_NONCANONICAL`. Warning rather than Error because the
   `InvestmentFundTerms` contract explicitly documents the flag, so existing records are steered,
   not blocked.
4. **`CashSweep` models sweep programs, `MoneyMarketFund` models the fund vehicles they sweep
   into.** A sweep program (program name, vehicle type, frequency, target account) is a cash
   operation; the money-market fund it sweeps into is a security with `SweepEligible = true`. No
   validator rule is needed — the term shapes do not overlap — but the partition is normative.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Enforcement rules | `src/Meridian.Application/SecurityMaster/Validation/AssetClassValidatorRegistry.cs` | `DisallowedStringValuesRule` / `DiscouragedTrueBooleanRule` and the Bond, InvestmentFund, and CustomAsset entries |
| Readiness catalog | `src/Meridian.Application/SecurityMaster/SecurityMasterOperationalReadinessService.cs` | StructuredCredit named the securitized home; CustomAsset relabeled |
| Profile reclassification | `src/Meridian.Application/SecurityMaster/SecurityMasterService.cs` | `KnownProfileAssetClasses` maps `structured-credit-io-po` → StructuredCredit |
| Asset family | `src/Meridian.FSharp/Domain/SecurityClassification.fs` | `AssetFamily.SecuritizedCredit` is the securitized family; `StructuredCash` reverts to naming cash vehicles only, so the two no longer share a label |
| Accounting classification | `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs` | `AccountingInstrumentClass` declares StructuredCredit's accounting home, so every vendor spelling of a securitized tranche posts under one class |
| Coverage read model | `src/Meridian.Ui.Shared/Services/MultiAssetCoverageReadService.cs` | Structured evidence that names no security routes to StructuredCredit, not CustomAsset |
| Tests | `tests/Meridian.Tests/SecurityMaster/SecurityValidationServiceTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityAccountingInstrumentClassTests.cs` | Canonical-home rule coverage and the declared accounting classification |

## Rationale

The review ranked this "cheapest to decide, compounding cost to defer": the decision needs no new
data model, only a ruling plus validators that reject the other routes. `StructuredCredit` wins for
securitized products because it is the only kind whose terms the structured cash-flow resolver and
factor-based amortization can consume; picking `Bond` would have required duplicating factor
schedules onto `BondTerms`, and picking `CustomAsset` would have made the extension point the
default home for a first-class asset family.

## Alternatives Considered

### Alternative 1: Deepen BondTerms to carry securitized economics

**Pros:**
- Bonds already flow through fixed-income pricing and amortization lanes.

**Cons:**
- Duplicates `StructuredCreditTerms` (tranche, pool, factor schedule) onto a second kind.
- Leaves the partition unstable — both homes would be legitimate.

**Why rejected:** it multiplies modeling routes instead of collapsing them.

### Alternative 2: Leave the partition conventional (documentation only)

**Pros:**
- No migration pressure on existing records.

**Cons:**
- Reporting, risk, and reconciliation still cannot assume a stable partition; drift accrues with
  every ingested securitized record.

**Why rejected:** the review's core finding is that convention without enforcement is what allowed
three routes to coexist.

## Consequences

### Positive

- Reporting, risk, and reconciliation can assume every securitized instrument is a
  `StructuredCredit` record with computable factor economics.
- Operational readiness routing no longer advertises `CustomAsset` as the securitized home.

### Negative

- Existing Bond records with securitized subclasses surface Error-severity validation issues until
  re-modeled through a governed amendment.

### Neutral

- `BondSubclass` keeps its securitized members for read tolerance; the union is not shrunk.

## Compliance

### Code Contracts

```csharp
// The Bond validator must reject securitized subclasses:
// SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL (Error) — see AssetClassValidatorRegistry.
// InvestmentFund stable-NAV and CustomAsset securitized metadata raise Warning-severity
// SM_INVESTMENT_FUND_STABLE_NAV_NONCANONICAL / SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL.
```

### Runtime Verification

- Covered by `SecurityValidationServiceTests` canonical-home cases.

## References

- [Security Master extensibility review](../architecture/security-master-extensibility-review.md)
  — finding 2 and re-ranked priority 2.

---

*Last Updated: 2026-08-24*
