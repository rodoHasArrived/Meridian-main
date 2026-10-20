# Security Master Review Remediation Plan — 2026-10-20

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-20
**Scope:** Engineering
**Source review:** [Security Master Architecture Review](../../architecture/security-master-extensibility-review.md),
2026-10-20 pass and post-merge re-check

This plan sequences the open findings from the 2026-10-20 Security Master review into separately
mergeable slices. Each slice names the files it touches, the test that locks it, and the point at
which a human must decide. Slices are ordered by what they put at risk: wrong ledger numbers
first, wrong accounting routing second, extensibility cost after that.

Slices 1 and 2 are correctness. Slices 3–5 are extensibility and can run in parallel lanes once
slice 1 has merged, since they touch the term schema and the registry that slice 1 extends.

---

## Slice 1 — G1: declare coupon rate units, remove the magnitude heuristics

**Risk today.** `SecurityMasterCashFlowService.NormalizeAnnualRate` (`:793`),
`SecurityMasterAccountingEventService.NormalizeRate` (`:875`) and
`AssetObligationProjectionService.NormalizeCouponRatePercent` (`:868`) each guess a rate's unit from
its magnitude, and they disagree: a bond `couponRate` of `0.5` is 50% in the first two and 0.5% in
the third. Sub-1% coupons are mispriced 100× in cash-flow projection and in accrual and coupon
postings.

**Design.** Rate unit becomes a declared term convention, the same mechanism day count, payment
frequency and settlement type already use (`SecurityTermConventions`,
`SecurityAssetTermField.Convention`).

1. **Contract.** Add `SecurityRateUnit { Fraction, PercentagePoints, BasisPoints }` and
   `SecurityTermConventionKind.RateUnit` in `SecurityTermConventions.cs`. Add
   `SecurityAssetTermField.RateUnit` (default `None`) with a `WithRateUnit(...)` builder; a decimal
   field that declares a unit is a rate field. One canonical storage unit for every rate field:
   **fraction** (`0.045` = 4.5%). Basis-point fields (`spreadBps`, `fixedCouponBps`) stay in basis
   points and declare `BasisPoints` so the reader converts, not the writer.
2. **Schema.** Mark every rate field in `SecurityAssetTermsSchema.cs`: `couponRate` (Bond,
   TreasuryBill, StructuredNote), `interestRate` (Deposit), `discountRate` (CommercialPaper,
   TreasuryBill), `repoRate`, `yieldRate`, `currentCouponRate`, `weightedAverageCoupon`, `feeRate`,
   `forwardRate`, `strikeRate`, `capStrike`/`floorStrike`, `capRate`/`floorRate`,
   `participationRate`, `recoveryRateAssumption`, swap-leg `fixedRate` and `currentIndexRate`.
   `SecurityAssetClassRegistryCoverageTests` gains a rule: every decimal field whose key ends in
   `Rate`, `Coupon` or `Bps` declares a unit, so a new class cannot add an untyped rate.
3. **Write path.** `SecurityMasterMapping` write mode (where conventions are already enforced,
   `:585-644`) and `SecurityAssetTermsFieldEditValidator` refuse a rate outside `[0, 1]` for a
   `Fraction` field with the precise message pattern the other conventions use. The workstation and
   WPF editors show the unit from `/api/security-master/asset-classes/{assetClass}` (new `rateUnit`
   on the field model) so operators type `4.5` into a percent box and the client stores `0.045`.
4. **Read path.** Add `SecurityTermConventions.ReadRate(terms, field)` that returns a fraction for
   any declared unit. Replace the three heuristics with it: `SecurityMasterCashFlowService` (`:366`,
   `:661`, `:673`), `SecurityMasterAccountingEventService` (`:432`, `:475`) and
   `AssetObligationProjectionService` (`:389`). Delete `NormalizeAnnualRate`, `NormalizeRate` and
   `NormalizeCouponRatePercent`.
5. **Legacy rows — human decision.** Existing payloads carry no unit declaration. Do not guess at
   read time. Add a data-quality rule (`SecurityMasterDataQualityService`, alongside MA/VC rules)
   that flags every rate field with a value `> 1` on a `Fraction` field as `SM_RATE_UNIT_AMBIGUOUS`,
   and a one-shot report listing affected securities. The operator decides per security (or per
   source system) through the governed field-edit route; the plan does not add a silent migration.
   The asset-class terms version for Bond, Deposit and the other affected classes increments
   (`SecurityAssetClassDescriptor.TermsVersion`) so a node that does not know the unit refuses
   newer payloads instead of misreading them.

**Tests.** `SecurityTermsRateUnitTests` (new): the three consumers project the same cash flows for
a 0.5% bond written as `0.005`; a `couponRate` of `4.5` on a `Fraction` field is refused on write;
the data-quality rule flags it on read. `StructuredCashFlowProjectionTests` and
`AssetObligationProjectionServiceTests` add a sub-1% case each. The Deposit fraction test added in
`7d276fc6` is rewritten to expect refusal of `4.5`, not normalisation.

**Validation.** `dotnet test tests/Meridian.Tests --filter "FullyQualifiedName~SecurityMaster|FullyQualifiedName~AssetOperations|FullyQualifiedName~Amortization"`,
then the PostgreSQL Security Master suite against a local database. Human review required: the
canonical unit choice and the legacy-row policy are governance decisions.

---

## Slice 2 — F1: every catalogued class states its accounting disposition

**Risk today.** `StructuredNote` declares no `AccountingInstrumentClass`; the taxonomy fallback
(`SecurityAssetClassCatalog.cs:764`) resolves `FixedIncome → Bond`, so a principal-at-risk note
accrues and redeems like a vanilla bond.

**Design.**

1. Make `AccountingInstrumentClass` required on `SecurityAssetClassDescriptor`, with an explicit
   `SecurityAccountingInstrumentClasses.OutsideSlice` constant for classes the accounting lane does
   not model yet. `StructuredNote`, `Swaption`, `CapFloor`, `FxForward`, `NonDeliverableForward` and
   `CreditDefaultSwap` take `OutsideSlice` unless an owner asserts otherwise.
2. Restrict `ResolveAccountingInstrumentClass`'s taxonomy fallback to names the catalog does not
   contain (keep the existing unmodelled-name test). A catalogued class resolves only from its
   descriptor.
3. `SecurityMasterAccountingEventService.IsFixedIncome` and the readiness service treat
   `OutsideSlice` as "no accounting events"; the readiness report shows it as a distinct row state
   rather than folding it into the Bond slice.
4. `SecurityAssetClassRegistryCoverageTests` adds a rule: no catalogued class resolves through the
   taxonomy fallback.

**Tests.** `SecurityAccountingInstrumentClassTests`: a `StructuredNote` with
`PrincipalProtectionPct < 1` produces no bond accrual; the fallback still applies to an unknown
string. Human decision: which of the six new classes actually belongs in the Bond slice.

---

## Slice 3 — profile-to-class routing lives on the profile definition

**Risk today.** `KnownProfileAssetClasses` and `AssetClassMetadataKeywords`
(`SecurityMasterService.cs:1857-1899`) map profile ids to asset classes by hand; the same ids are
declared in `SecurityAssetProfileCatalog` and repeated in
`SecurityMasterOperationalReadinessService.cs:312`. A new reclassifying profile needs edits in
three files and silently resolves to `CustomAsset` if one is missed.

**Design.**

1. Add `ResolvesToAssetClass : string?` to `SecurityAssetProfileDefinitionDto`
   (`SecurityAssetProfiles.cs:76`), carried through the profile governance service, lineage and the
   JSON seed in `SecurityAssetProfileCatalog`. The seeds set it for the five mapped profiles.
2. `SecurityMasterService` resolves the class through `ISecurityAssetProfileCatalog` (already
   injected for governance) and deletes both dictionaries. The metadata-keyword corroboration goes
   with them: the review already notes that metadata "may corroborate but never decide", so it
   decides nothing and need not exist.
3. The readiness service's `structured-credit-io-po` literal reads the same field.
4. Profile governance validation refuses a `ResolvesToAssetClass` that is not in the catalog or whose
   descriptor lacks `SupportsProfileBackedTerms`.

**Tests.** `SecurityMasterAssetClassSupportTests`: a create with a seeded profile resolves to the
declared class; a profile with no declared class resolves to `CustomAsset`; a profile declaring an
unknown class is refused at approval. A registry coverage rule holds every seeded profile with a
declared class to the catalog.

---

## Slice 4 — the cash-flow term reader reads aliases from the schema

**Risk today.** `StructuredCashFlowTermsResolver` carries ~20 hand-written alias arrays
(`:15-27`) and a per-class `"DirectLoan"` arm (`:96`). `SecurityAssetTermField` already has an
`Aliases` list (`SecurityAssetTermsSchema.cs:87`), so the reader maintains a second, undeclared
vocabulary beside the declared one.

**Design.**

1. Give each resolver concept a declared **cash-flow role** rather than an alias list: a new
   `SecurityCashFlowTermRole` enum (`Maturity`, `Issue`, `PrincipalFace`, `CurrentFactor`,
   `CouponRate`, `PaymentFrequency`, `DayCount`, `FactorSchedule`) on `SecurityAssetTermField`,
   separate from the existing `SecurityAssetTermRole` (`Economic` / `Operational`), which says what
   kind of term a field is, not which cash-flow input it supplies. Mark the owning field per class.
2. `StructuredCashFlowTermsResolver.Resolve(assetClass, sources)` looks up the field for each role
   on the class, then reads the field's key and its declared `Aliases` through `SecurityTermReader`.
   The hand-written arrays and the `"DirectLoan"` arm are deleted. Where a class declares no field
   for a role, the resolver reports the gap (as `SecurityMasterCashFlowService` already does for
   missing coupons) instead of probing other classes' spellings.
3. `SecurityAssetClassRegistryCoverageTests`: every class with `CashFlowBehavior != None`
   declares a `Maturity` and `PrincipalFace` role field, and a `CouponRate` role field when it
   accrues.

**Tests.** The existing `StructuredCashFlowProjectionTests` fixtures must project identically
before and after; add a golden comparison that runs each class's populated fixture through the old
and new resolver on the first commit, then delete the old one.

---

## Slice 5 — decide the asset pack registry

**Risk today.** `SecurityAssetPackRegistry` (888 lines) is consumed only by the readiness report
(`SecurityMasterOperationalReadinessService.cs:296`, `:873`), shares one prose contract schema across
all packs and validates policy by substring (`:341`, `:721-731`). It gates nothing, and its
asset-class names do not match the catalog.

**Options — human decision, recommended option first.**

- **A (recommended).** Move the prose to `docs/domain/asset-packs.md` (one section per pack, same
  content), keep a thin `SecurityAssetPackCoverage` record for the readiness report sourced from the
  catalog's `AssetClassDefinition` (lifecycle events, identifiers, capabilities already live there),
  and delete the registry and its validator. The readiness report's pack rows become catalog rows.
- **B.** Keep it as a registry but make it gate: a pack must name catalogued asset classes, declare a
  typed `ContractSchema` built from `SecurityAssetTermsSchema`, and `InferLifecycleEvent` switches on
  `SecurityLifecycleEvent` instead of English substrings. This is more code for the same report.

**Tests.** For A, `SecurityMasterOperationalReadinessServiceTests` pins the readiness pack rows to
the catalog; the `SecurityAssetPackRegistryTests` suite is removed with the registry.

---

## Standing items not scheduled here

| Item | Why it waits |
| --- | --- |
| F3 — OTC instrument vs. trade identity | A modelling decision (one security per trade vs. shared instrument) that changes the lot model for three classes. Needs a blueprint (`meridian-blueprint`) and an ADR before code. |
| Option projection writer | Re-keying the option tables on `security_id` and giving the quote path its own tables is a migration with a data backfill; schedule after slice 1's terms-version bump so one migration window carries both. |
| Codec generation from the term schema | `retainedEconomicTerms` and `UnrecoverableKeys` make the loss visible; generation is the long-term fix and is tracked in the review's finding 4. |
| A4 — profile `IsProjected` / `IsSearchable` | Documented as promotion intent; becomes live when a profile-driven projection descriptor exists (after slice 4 establishes roles). |

---

## Sequencing and validation

| Order | Slice | Depends on | Lane |
| --- | --- | --- | --- |
| 1 | G1 rate units | — | correctness; human sign-off on unit and legacy policy |
| 2 | F1 accounting disposition | — | correctness; can run beside slice 1 |
| 3 | profile routing | slice 1 merged (shares `SecurityMasterService`) | extensibility |
| 4 | term-reader roles | slice 1 merged (shares the schema) | extensibility |
| 5 | pack registry | decision A/B | cleanup |

Each slice is one PR against `main` on a `codex/<slice-name>` branch, validated with the filtered
Security Master test run and the PostgreSQL suite where a store changes, with
`Meridian CI / quality-gate` as the merge gate. The review document gets a dated disposition row
per slice when it merges, per the review's own update policy.
