# Security Master Institutional Readiness Review

**Scope:** Security Master schema, domain model, and extension points across equities, options,
futures, bonds, loans, cash/deposits, funds, derivatives, and structured products.
**Method:** Source review of `src/Meridian.FSharp/Domain/SecurityMaster.fs`,
`src/Meridian.Contracts/SecurityMaster/`, `src/Meridian.Application/SecurityMaster/`,
`src/Meridian.Storage/SecurityMaster/`, and the `tests/Meridian.Tests/SecurityMaster/` suite.
**Verdict:** The core taxonomy is genuinely well-factored. Three concrete defects and one
structural bypass sit between it and institutional-grade cross-asset extensibility.

---

## 1. What is solid

**Asset-class taxonomy is a single table, not scattered dispatch.**
`AssetClassRegistry` (`SecurityMaster.fs:551`) holds one `AssetClassDescriptor` per asset class and
exposes exactly one `SecurityKind` pattern match (`keyOf`, line 612). Asset-class name, derivative
predicate, and canonical `SecurityClassification` all derive from that table. Adding a class means
one `keyOf` arm plus one descriptor row. This is the right shape and it is held by tests.

**Validation is declarative and fails closed.**
`AssetClassValidatorRegistry` composes rule-based `JsonAssetClassValidator` instances from field-rule
primitives (`RequiredDate`, `OptionalPositiveNumber`, date-ordering, cross-field comparison). An
unregistered class produces `SM_ASSET_CLASS_UNSUPPORTED` rather than silently passing
(`SecurityValidationService.cs:120-128`).

**Projection fan-out is data-driven.** `PostgresSecurityMasterStore.ProjectionWriters` is a list of
`(assetClass, writerFn)` pairs, not a switch.

**Identifier model is institution-grade.** 17 kinds including LEI, PermID, BBGID, RIC, WKN, Valoren,
CIK, and OCC option symbols; temporal validity (`ValidFrom`/`ValidTo`), primary flag, provider
attribution, normalized values, and a separate scoped alias table. The `Unknown` read-tolerance
member is a notably mature touch: rows written by a newer node stay readable but are rejected on
write-back, so unrecognized kinds are never silently re-persisted.

**Bitemporality and auditability are real.** Event store + snapshot store + revision store +
rebuild orchestrator, with `Version`/`EffectiveFrom`/`EffectiveTo` on the record, optimistic
concurrency, durable conflict store, operator overrides with approvals, vendor entitlements, and
ledger period locks. 26 forward migrations.

**Schema versioning learned from a real bug.** `AssetSpecificTermsSchema` and `EconomicTermsSchema`
are deliberately separate families after an economic-terms version leaked into asset-specific-terms
acceptance. Migrate-on-read plus an upcaster chain.

**Fixed-income primitives are properly modeled.** `FaceValueLot` makes par basis, booked pool
factor, and Security Master identity explicit with enforced invariants instead of caller convention.
`StructuredCashFlowTerms` carries a typed multi-point factor schedule with `FactorAsOf` lookup and
multi-leg support.

---

## 2. What is at risk

### 2.1 A domain-bypass write path for seven asset classes *(most serious)*

`SecurityMasterService.CreateProjectionFromResult` (`SecurityMasterService.cs:325`) accepts
`assetClassOverride` and `assetSpecificTermsOverride` and re-injects the caller's raw JSON *over*
the F# aggregate's output. The overridden projection is what gets persisted
(`UpsertProjectionAsync`, line 83) and what builds the event envelope (line 73).

The gate is `IsProfileBackedCustomAsset` (line 386): asset class in {CustomAsset, OtherSecurity,
StructuredCredit, PrivateFundInterest, PrivateCompanyEquity, RealEstateHolding,
CommitmentGuarantee} **and** a non-empty `customProfileId` string in the terms.

Consequences:

- **Two records of the same asset class take different persistence paths** depending on whether one
  JSON key is present. With `customProfileId` the terms survive verbatim; without it they route
  through the F# `SecurityKind` and only the declared fields survive.
- **For profile-backed records the aggregate's invariants do not govern what is stored.** The F#
  `Amend` result's `Kind` is computed and then discarded.
- **The event stream inherits the bypass**, so replay/rebuild reproduces it rather than correcting it.

Root cause: `CustomAsset` has no `SecurityKind` case. `SecurityMasterMapping.cs:307` maps both
`"OtherSecurity"` and `"CustomAsset"` to `SecurityKind.NewOtherSecurity`, whose `OtherSecurityTerms`
has five fixed fields. Every operator-defined profile field would be dropped — so the C# layer was
given an override to route around its own domain model.

### 2.2 Asset class is re-derived by string heuristics on the write path

`TryResolveProfileBackedAlternativeAssetClass` (`SecurityMasterService.cs:400`) inspects
`profileFields` key names and fuzzy-matches free text to *relabel* the stored asset class:

```csharp
if (ProfileFieldsContain(assetSpecificTerms, "tranche", "collateralType", "originalFace", "couponOrIndex")
    && MatchesAny(profileId, category, subType, accountingClassification,
                  "structured-credit-io-po", "StructuredCredit", "MBS", "ABS", "CLO", "CMBS"))
{
    assetClass = "StructuredCredit";
```

Five such blocks cover StructuredCredit, PrivateFundInterest, PrivateCompanyEquity,
RealEstateHolding, and CommitmentGuarantee. The asset class actually stored can differ from the one
requested, decided by the content of an operator-authored profile. Renaming a custom profile field
(`originalFace` → `faceAmount`) silently reclassifies every subsequent record. This is precisely the
asset-specific workaround that should be generalized away.

### 2.3 `InvestmentFund` cannot pass the validation gate *(concrete defect)*

`InvestmentFund` — mutual funds, ETFs, hedge funds, REITs, closed-end funds — has a `SecurityKind`
case (`SecurityMaster.fs:521`), an `AssetClassRegistry` descriptor, a `SecurityAssetClassCatalog`
entry, a `SecurityMasterMapping` deserialize arm (line 388), and a catalog test asserting its
presence. It has **no entry in `AssetClassValidatorRegistry`** — the only catalog class besides
`CustomAsset` (which is covered by a profile validator) that is missing one.

Because validation fails closed, every `InvestmentFund` record yields
`SM_ASSET_CLASS_UNSUPPORTED`. The projection gap is guarded by an explicit
`IntentionallyUnprojectedAssetClasses` list, but nothing binds validator coverage to the catalog, so
this gap is silent.

### 2.4 `SecurityAssetTermsSchema` is documentation, not a contract

The file's own docstring states it exists because the field/type table was hand-maintained three
times — the F# serializer, the C# deserializer, and the projection decoders — and silently drifted
(bond coupon columns landing null). But no production code reads it:

```
$ grep -c SecurityAssetTermsSchema src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs → 0
$ grep -c AssetTermsSchema        src/Meridian.FSharp/Interop.SecurityMaster.fs                    → 0
```

It appears only in comments and its own tests, which check the table against itself and the catalog
plus a handful of hand-written anchors. It is now a *fourth* hand-maintained copy. The specific bond
bug it memorializes is guarded; the same class of drift in any other field would not fail a test.

### 2.5 Field-level provenance is declared but unimplemented

`SecurityFieldProvenance` and `SecurityRecordProvenance.ForField` have **zero production consumers** —
the only references are their own definitions. Real per-field attribution exists only where two
sources *disagreed* (`security_master_conflicts` carries `field_path`, `provider_a`, `provider_b`,
and the resolved winner). For the steady-state majority of fields there is no record of which vendor
supplied the value. For a golden-copy master serving institutional audit, "which source asserted
this coupon, as of when" is a standard question the schema currently cannot answer.

### 2.6 Codec round-trip tests do not run in the merge gate

`SecurityMasterPostgresRoundTripTests` holds 2 tests, both `[SecurityMasterDatabaseFact]`, which
skip unless Docker or an external Postgres is available. `MERIDIAN_DISABLE_DOCKER_TESTS=true` is set
in `scripts/ci.sh:12`, `.github/workflows/ci.yml:26`, and `.github/workflows/meridian-ci.yml:26` —
the authoritative `quality-gate`. Only `production-certification.yml` runs them. The strongest
existing guard on serializer/deserializer parity is absent from the gate that blocks merges.

### 2.7 Typed query surface skews away from institutional asset classes

11 of 26 catalog classes have a relational projection. The 15 without include DirectLoan,
StructuredCredit, PrivateFundInterest, PrivateCompanyEquity, RealEstateHolding,
CommitmentGuarantee, InvestmentFund, Repo, TreasuryBill, and CommercialPaper — close to the whole
institutional set. Those records are queryable only through the JSON terms blob.

This one is a *declared* trade-off, not drift: `IntentionallyUnprojectedAssetClasses` in
`SecurityAssetTermsSchemaTests` forces the gap through review. Noted as a capability boundary rather
than a defect.

---

## 3. Top priorities

**1. Make profile-backed terms a first-class domain concept, and delete the bypass.**
Add `SecurityKind.ProfileBacked of ProfileBackedTerms` carrying `profileId`, `profileVersion`, and
an opaque field map that round-trips losslessly through the F# serializer. Then remove
`assetClassOverride`/`assetSpecificTermsOverride` from `CreateProjectionFromResult` and delete
`TryResolveProfileBackedAlternativeAssetClass` entirely — with the identity preserved in the domain,
there is nothing left to re-derive. This collapses 2.1, 2.2, and the `CustomAsset` identity loss
into a single change and is the prerequisite for trusting the event stream on these asset classes.

**2. Register an `InvestmentFund` validator and bind validator coverage to the catalog.**
Add the missing validator, then add a test asserting
`AssetClassValidatorRegistry.SupportedAssetClasses` equals `SecurityAssetClassCatalog.AssetClasses`
minus an explicit declared-gap list — mirroring the pattern
`SecurityAssetTermsSchemaTests.ProjectionCoverage_PartitionsTheCatalogIntoProjectedAndDeclaredGaps`
already uses for projections. Small change; closes a live functional gap and prevents the next one.

**3. Make `SecurityAssetTermsSchema` executable.**
Cheapest effective form: a test that, for every asset class, serializes a fully populated
`SecurityKind` through the F# interop and asserts the emitted keys and JSON value types match the
schema's declared required fields — then deserializes and asserts round-trip equality. That converts
the table from a fourth copy into the guard it was written to be, without restructuring the codecs.

**4. De-gate codec round-trip coverage.**
Add per-asset-class serializer/deserializer round-trip tests against an in-memory or fake store so
they run under `MERIDIAN_DISABLE_DOCKER_TESTS=true`. Priority 3 largely delivers this if written
against the interop layer rather than Postgres.

**5. Resolve field-level provenance in one direction.**
Either persist per-field `(sourceSystem, asOf, confidence)` alongside the projection and wire
`ForField` into the ingest/conflict path, or delete `SecurityFieldProvenance` so the contract does
not read as implemented. Decide against the institutional audit requirement; the current middle
state is the worst of both.

---

## 4. Summary

The taxonomy layer is better than most: `AssetClassRegistry`, the declarative validator registry,
the data-driven projection fan-out, and the identifier model with its read-tolerance semantics are
patterns worth keeping. Cross-asset extensibility is not blocked by the schema's shape.

It is blocked by one structural decision — that operator-extensible asset classes have no domain
representation, forcing a bypass around the aggregate and a heuristic to guess back what the bypass
lost. Priority 1 addresses that. Priorities 2–4 close the guard gaps that let 2.3 and 2.4 exist
undetected, and priority 5 settles an open question rather than leaving a half-built contract in the
schema.
