# Security Master Architecture Review — 2026-08-06

Scope: the Security Master surface across `src/Meridian.FSharp/Domain/SecurityMaster.fs`,
`src/Meridian.Contracts/SecurityMaster/`, `src/Meridian.Application/SecurityMaster/`,
`src/Meridian.Storage/SecurityMaster/`, and `src/Meridian.ReferenceData/SecurityMaster/`
(~27k lines), assessed against institutional reference-data requirements.

Verdict: **not clean and extensible across all asset classes.** The identity, governance, and
audit layers are institutional-grade. The *asset model* layer is not — asset class is declared in
eight places with divergent membership, asset-specific terms are an untyped JSON bag whose
declared schema has no runtime enforcement, and the newest asset classes are supported by
heuristic field-sniffing rather than by the domain model.

---

## 1. What is solid

**Identifier resolution.** `SecurityIdentifierKind` carries 16 real-world kinds (ISIN, CUSIP,
SEDOL, FIGI, LEI, PermID, BBGID, WKN, Valoren, RIC, CIK, OCC option symbol, PermTicker, …) plus an
`Unknown` read-tolerance member. `SecurityIdentifierNormalizer` (422 lines) implements genuine
check-digit validation for ISIN, CUSIP, SEDOL, LEI (mod 97-10) and FIGI. Identifiers are
temporally scoped (`ValidFrom`/`ValidTo`), provider-scoped, and carry a primary flag with
normalized value/provider projections. This is the strongest part of the subsystem.

**Governance and auditability.** `ISecurityMasterEventStore` appends with `expectedVersion`
optimistic concurrency; `ISecurityMasterRevisionStore` enforces a durable
Draft → Submitted → Approved → Published/Rejected gate with workflow binding, so a publish cannot
be triggered from an unapproved revision id. Conflict detection (`SecurityMasterConflictKinds`),
an authority policy, operator overrides, corporate-action inbox state, and a rebuild orchestrator
are all present. Record-level provenance (source system, source record id, as-of, updated-by,
reason) is modelled and tolerantly read.

**Asset-class dispatch in F#.** `AssetClassRegistry` is the right pattern: one `keyOf` match over
`SecurityKind`, one descriptor table, and every downstream projection (asset-class string,
derivative predicate, `SecurityClassification`) derived from it. Adding a class means one arm and
one row.

**Validation.** `AssetClassValidatorRegistry` is data-driven — `JsonAssetClassValidator` composed
from declarative `FieldRule` / `DateOrderRule` specs, with `CompositeAssetClassValidator` for
classes needing layered rules. This is the extensibility model the rest of the subsystem should
have copied.

**Face-value lot economics.** `FaceValueLot` makes previously-implicit conventions explicit — the
quote basis (`ParBasis`, no silent price-per-100 assumption), the pool factor the face was booked
at (`BookedFactor`), and the Security Master identity it amortizes against — and owns cost basis,
premium/discount, factor-restated face, and day-count-weighted amortized basis in one place.

**Mixed-version read tolerance.** `Unknown` enum members plus degrade-to-`OtherSecurity` on
unrecognized asset classes mean a newer node's writes do not cause a total read outage per
security, and the strict write path still refuses to re-persist `Unknown`.

---

## 2. What is at risk

### 2.1 "Asset class" is declared in eight places with divergent membership

| Registry | Members |
|---|---|
| `SecurityKind` DU (F#) | 25 |
| `SecurityAssetTermsSchema` | 26 (adds `CustomAsset`) |
| `SecurityAssetClassCatalog` | 27 (adds `Unknown`) |
| `AssetClassValidatorRegistry` defaults | 24 + 2 profile validators |
| Relational projection stores | 11 |
| `SecurityAssetPackRegistry` (802 lines) | separate table |
| `InstrumentTypeDescriptorCatalog` (438 lines) | separate table |
| `SecurityMasterOperationalReadinessService` specs | separate table |

Nothing generates these from one another. `SecurityAssetTermsSchemaTests` binds two of them and
freezes the rest as an allow-list. Concretely: **`InvestmentFund` has a `SecurityKind` case, a
catalog entry, and a terms-schema entry, but no validator and no relational projection** — mutual
funds, ETFs, REITs, and closed-end funds pass validation because no rule exists for them.

### 2.2 The declared terms schema has zero runtime consumers

`SecurityAssetTermsSchema` was introduced (per its own doc comment) because the same field/type
table was hand-maintained three times — the F# serializer, `SecurityMasterMapping.ToSecurityKind`,
and the Postgres projection decoders — and silently drifted. But the table is referenced **only in
comments and tests**; no codec reads it. The three surfaces are still hand-maintained and can
still drift. The schema documents the contract; it does not enforce it.

Evidence it has already drifted from runtime: the `CustomAsset` schema entry declares
`customProfileId`, `profileVersion`, `profileFields`, `profileApproval` — and **not** `category`,
which `SecurityMasterMapping` requires (`GetRequiredString(json, "category")`) and
`SecurityMasterOperationalReadinessService` lists as a required field.

### 2.3 `CustomAsset` has no domain representation, and the workaround is heuristic inference

There is no `SecurityKind.CustomAsset` case. `SecurityMasterMapping` collapses
`"OtherSecurity" or "CustomAsset"` into `OtherSecurity`, discarding `customProfileId`,
`profileVersion`, and `profileFields`. To recover, `SecurityMasterService` re-patches
`AssetClass` and `AssetSpecificTerms` back over the projection *after* the F# domain has produced
it — the application layer routing around the canonical model.

Worse, `TryResolveProfileBackedAlternativeAssetClass` recovers the asset class by **sniffing field
names and fuzzy-matching strings**:

```csharp
if (ProfileFieldsContain(assetSpecificTerms, "tranche", "collateralType", "originalFace", "couponOrIndex")
    && MatchesAny(profileId, category, subType, accountingClassification,
                  "structured-credit-io-po", "StructuredCredit", "MBS", "ABS", "CLO", "CMBS"))
{
    assetClass = "StructuredCredit";
```

Asset class drives accounting treatment. Inferring it from the shape of a JSON bag is a control
weakness, not just a code smell — a profile that happens to carry a `tranche` field is classified
as structured credit.

The predicate is also duplicated with divergent authority: `SecurityMasterService`
hardcodes eight asset-class strings in `IsProfileBackedCustomAsset`, while `SecurityMasterMapping`
asks `SecurityAssetClassCatalog.SupportsProfileBackedTerms` (seven classes). Two answers to the
same question.

### 2.4 `ToSecurityKind` reads two different JSON roots depending on the arm

`ResolveAssetTermsJson` unwraps a `profileFields` envelope into `terms`. Six arms
(StructuredCredit, PrivateFundInterest, PrivateCompanyEquity, RealEstateHolding,
CommitmentGuarantee, and the profile-aware paths) read `terms`; the other nineteen read `json`
directly. Any profile-backed record whose class is not in that six will parse against the outer
envelope and find none of its fields — silently, since most fields are optional. This is a
special-case that will bite the next class granted `SupportsProfileBackedTerms: true`.

### 2.5 Factor schedules cannot survive a domain write

Three contradictory declarations of the same field:

- F# domain: `StructuredCreditTerms.FactorSchedule: string option`
- `SecurityAssetTermsSchema`: `Opt("factorSchedule", SecurityAssetTermFieldType.String)`
- `StructuredCashFlowTermsResolver.ReadFactorSchedule`: requires `JsonValueKind.Array` of
  `{asOfDate, factor}` objects

`SecurityMasterMapping` reads it with `GetOptionalString`, which returns null for an array. So an
array-shaped factor schedule is dropped at ingest, the F# serializer writes a string (or null),
and the typed `StructuredFactorScheduleEntry` path — the whole point of which was to replace the
free-text term so amortization could be seeded from a dated schedule — resolves empty for every
record that has been through the write path. Structured-credit amortization silently falls back
to the scalar `currentFactor`.

Compare `DirectLoanTerms.PrincipalSchedule: PrincipalPaymentEntry list`, which is properly typed.
Schedule modelling is inconsistent between two asset classes that need the same thing.

### 2.6 Schema versioning runs backwards

The asset-specific-terms family is versioned `Legacy = 1`, `CustomAssetProfile = 3` — there is no
2, and `Default = Legacy`. The only upcaster,
`SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster`, converts the richer structured v2 economic
document **down** into the flat v1 shape. The versioning machinery therefore serves read
compatibility, not forward migration: the canonical storage shape is still the original flat bag,
and every richer structure gets flattened into it on the way in.

### 2.7 Relational projection coverage is 11 of 26

Projection stores exist for Bond, CD, Commodity, Crypto, Deposit, Equity, Future, FxSpot,
MoneyMarketFund, Option, Swap. `SecurityAssetTermsSchemaTests` freezes the other fifteen in an
`IntentionallyUnprojectedAssetClasses` allow-list — which is honest bookkeeping, but the fifteen
are *precisely* the private-markets and alternatives classes (DirectLoan, StructuredCredit,
PrivateFundInterest, PrivateCompanyEquity, RealEstateHolding, CommitmentGuarantee, InvestmentFund,
Repo, TreasuryBill, CommercialPaper, CashSweep, Cfd, Warrant, OtherSecurity, CustomAsset). Adding
a class today costs a hand-written interface, a hand-written Postgres store, a migration, and a
hand-written decoder.

### 2.8 Open lots are three unrelated models, only one of which knows the Security Master

| Model | Key | Quantity | SecurityId |
|---|---|---|---|
| `Execution.Sdk.TaxLot` | `string Symbol` | `long` | no |
| `Backtesting.Sdk.OpenLot` | `string Symbol` | `long` | no |
| `Contracts.SecurityMaster.FaceValueLot` | `Guid SecurityId` | `decimal` face | yes |

Equity and fund lots are symbol-keyed with integer quantity — no fractional shares, no linkage to
the security identity that owns the corporate-action and day-count reference data, and no path for
a symbol change to follow the lot. Only par-denominated lots are properly identified.

### 2.9 Amortization is straight-line only

`FaceValueLot.AmortizedBasisAsOf` applies straight-line premium/discount amortization weighted by
day-count year fraction. Institutional practice (ASC 310-20, IFRS 9 EIR) is effective-interest /
constant-yield, with yield-to-worst treatment for callable and pre-refunded bonds. The domain
already models `CallDate`, `PreRefundDate`, `MandatoryPutDate`, and `LegalFinalMaturity` — the
inputs exist; the method does not use them.

### 2.10 Field-level provenance is synthesized, not stored

`SecurityRecordProvenance.ForField(...)` projects one record-level provenance row onto an arbitrary
field path. There is no stored per-field lineage, so "which vendor asserted this bond's day count,
and when" is answerable only at record granularity. For a multi-source master with a conflict
authority policy, per-field attribution is the thing the policy should be arbitrating over.

---

## 3. Top priorities

1. **Make `SecurityAssetTermsSchema` the enforced contract, not documentation.** Drive
   `ToSecurityKind`, the F# serializer, and the Postgres decoders from the table — or, at minimum,
   add conformance tests that fail when any of the three diverges from it. Start by fixing the two
   known divergences: `CustomAsset`'s undeclared `category`, and `factorSchedule`'s
   String-vs-Array contradiction. Lowest effort, highest immediate defect yield.

2. **Give profile-backed assets a first-class domain representation.** Add a
   `SecurityKind.CustomAsset of CustomAssetTerms` case carrying `customProfileId`,
   `profileVersion`, and the profile field bag, then delete the post-hoc projection patching in
   `SecurityMasterService` and — critically — the heuristic
   `TryResolveProfileBackedAlternativeAssetClass`. Asset class should be declared on the record and
   validated against the approved profile, never inferred from field names.

3. **Collapse the eight asset-class registries to one generated source.** Pick the F#
   `AssetClassRegistry` descriptor table as canonical and generate (or test-bind) the terms schema,
   the catalog, the validator set, the pack registry, and the readiness specs from it. The
   acceptance test is that adding an asset class requires editing exactly one table and that
   `InvestmentFund`-shaped holes become impossible.

4. **Unify open-lot modelling on `SecurityId` and `decimal` quantity.** One lot abstraction with
   an asset-class-appropriate quantity basis (shares / contracts / face), replacing the
   symbol-keyed `long`-quantity `TaxLot` and `OpenLot`. This blocks fractional shares, cross-asset
   cost-basis reporting, and corporate-action-safe lot tracking today.

5. **Replace the hand-written per-class projection stores with a generic decoder, and add
   effective-interest amortization.** A schema-driven projection writer would close the 11-of-26
   gap in one change rather than fifteen. Effective-interest/constant-yield amortization (with
   yield-to-worst for callable and pre-refunded bonds) is the remaining correctness gap in an
   otherwise well-modelled `FaceValueLot`.

---

## Validation

Static review only — no build or test run was performed as part of this assessment. Every claim
above is anchored to a specific file and construct; the concrete defects in §2.2, §2.4, and §2.5
should be confirmed with targeted tests before remediation.
