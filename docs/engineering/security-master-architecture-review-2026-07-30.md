# Security Master Architecture Review — 2026-07-30

Scope: Security Master schema, models, and extension points across equities, options, bonds, loans,
cash, funds, derivatives, and structured products, assessed against institutional-finance
requirements.

Method: static review of `src/Meridian.FSharp/Domain/SecurityMaster.fs`,
`src/Meridian.FSharp/Interop.SecurityMaster.fs`, `src/Meridian.Contracts/SecurityMaster/`,
`src/Meridian.Application/SecurityMaster/`, `src/Meridian.Storage/SecurityMaster/`, and the
corresponding `tests/Meridian.Tests/SecurityMaster/` suites. No .NET SDK was available in the review
environment, so findings are traced through source and existing test coverage rather than executed.
Every claim below cites the file and line it was read from.

## Verdict

The architecture is **not** uniformly clean and extensible. The canonical classification core is
genuinely well-built and several institutional concerns (identifier resolution, provenance, day
count, schema versioning) are handled to a high standard. But the mechanism intended to carry *new*
asset classes — profile-backed custom assets — is a hardcoded five-case heuristic rather than an
extension point, and the declarative terms schema that exists to prevent codec drift is not actually
bound to the serializer, which has already drifted.

## What's solid

- **Canonical classification registry.** `AssetClassRegistry` (`SecurityMaster.fs:551-675`) holds one
  descriptor table with a single `keyOf` pattern match; asset-class name, derivative predicate, and
  `SecurityClassification` all derive from it instead of being re-encoded per call site.
- **Capability-driven asset metadata.** `SecurityAssetClassDescriptor`
  (`SecurityAssetClassCatalog.cs:447-457`) models behavior as capability flags —
  `SupportsCashflowScheduleByDefault`, `UsesFaceValueLots`, `AmortizesTowardPar`, `RequiresMaturity`,
  `SupportsProfileBackedTerms`, `PreferredIdentifierKinds` — so downstream surfaces branch on
  capability, not on an asset-class `if` ladder.
- **Identifier resolution.** 16 identifier kinds including LEI, PermID, BBGID, WKN, Valoren, RIC, CIK
  (`SecurityIdentifiers.cs:6-41`), bitemporal `ValidFrom`/`ValidTo`, normalization, and scoped
  aliases. The `Unknown` read-tolerance member with a strict write-side rejection
  (`SecurityMasterMapping.cs:196-211`) is a correct mixed-version rollout pattern.
- **Provenance and auditability.** Record-level provenance projects onto field-level attribution
  (`SecurityMasterProvenance.cs:23-51`), with canonical conflict kinds, a conflict authority policy,
  a revision store, operator-override approvals, and an event store plus snapshot store and rebuild
  orchestrator. 26 forward-only SQL migrations.
- **Day count.** Ten ISDA-cited conventions with documented degradation paths and a single canonical
  engine every accrual and amortization path is required to route through (`DayCountConventions.cs:8-48`).
- **Schema versioning.** After a documented cross-family leak, versions are namespaced per payload
  family (`AssetSpecificTermsSchema` vs `EconomicTermsSchema`, `SecurityMasterSchemaVersions.cs`), with
  a migrate-on-read upcaster chain and deliberate pass-through of unknown future versions so
  acceptance stays one guard's decision.
- **Honest projection gap.** Only 11 of 26 catalog classes get a relational projection, and the gap is
  enumerated and test-enforced (`SecurityAssetTermsSchemaTests.cs:22-38`,
  `ProjectionCoverage_PartitionsTheCatalogIntoProjectedAndDeclaredGaps`) so it stays a conscious
  decision rather than silent drift. This is the right way to carry a known gap.

## What's at risk

### 1. Custom-asset extensibility is a heuristic, not a mechanism — P1

`SecurityMasterService.TryResolveProfileBackedAlternativeAssetClass`
(`SecurityMasterService.cs:400-444`) resolves a custom profile to a first-class asset class by
duck-typing `profileFields` key presence and string-matching `customProfileId`/`category`/`subType`
against hardcoded literals — `"structured-credit-io-po"`, `"MBS"`, `"ABS"`, `"CLO"`, `"CMBS"`,
`"private-fund-interest"`, and so on. Exactly five profiles are recognized.

A genuinely new profile — the entire purpose of the profile feature — matches nothing and falls
through to `assetClass = "CustomAsset"` (`SecurityMasterService.cs:362-364`). There is no
`SecurityKind.CustomAsset` case in the F# discriminated union (`SecurityMaster.fs:494-521`).
`SecurityMasterMapping.ToSecurityKind` maps `"CustomAsset"` onto `OtherSecurity` and requires
`GetRequiredString(json, "category")` (`SecurityMasterMapping.cs:307-312`) — a key the declared
`SecurityAssetTermsSchema["CustomAsset"]` contract (`SecurityAssetTermsSchema.cs:209-217`) does not
include. So a new profile either throws `Missing required string 'category'` on create, or — if it
happens to carry a `category` field, as the test fixture does
(`SecurityMasterAssetClassSupportTests.cs:402`) — degrades to `OtherSecurity` inside the F# aggregate
while the projection is patched back via `assetClassOverride`/`assetSpecificTermsOverride`
(`SecurityMasterService.cs:325-343`).

That override is the deeper problem: the domain aggregate and the persisted projection disagree by
construction. The aggregate rebuilt from events holds `OtherSecurity`; the projection claims
`PrivateFundInterest`. `IsProfileBackedCustomAsset` (`SecurityMasterService.cs:386-398`) hardcodes the
same seven-class allowlist, and the mapper's split between reading from `json` versus the
`profileFields`-aware `terms` (`SecurityMasterMapping.cs:230`, `327-369`) covers only those same five
classes. The allowlist appears in three places and must be edited in all three to add a sixth.

**This is the blocker for new asset classes.** Everything else in the subsystem is extensible; this
is not.

### 2. The declarative terms schema is not bound to the serializer, and has drifted — P1

`SecurityAssetTermsSchema` exists specifically to stop the three-way codec drift its own header
documents (`SecurityAssetTermsSchema.cs:55-69`). But `SecurityAssetTermsSchemaTests` only validates
schema-versus-catalog and internal consistency — nothing binds the F# serializer's emitted key set,
or the C# mapper's read set, to the table.

Live drift, present now: `Equity.votingRightsCat` is declared in the schema
(`SecurityAssetTermsSchema.cs:84`), read by the mapper (`SecurityMasterMapping.cs:236`), and persisted
into the `voting_rights_cat` projection column (`PostgresSecurityMasterStore.cs:1295`) — but the
serializer's Equity arm never emits it (`Interop.SecurityMaster.fs:107-112`). Because the projection's
terms are parsed *from* the serializer output (`SecurityMasterMapping.cs:65`),
`EquityTerms.VotingRightsCat` is dropped on every write and that column is always null. There is no
test coverage: `grep votingRights tests/` returns nothing.

This is the same class of defect as the bond nested-`coupon` incident the schema header cites as its
reason for existing. The schema was built; the conformance test that would make it load-bearing was
not.

### 3. Structured-credit factor schedules cannot round-trip as data — P2

`StructuredCreditTerms.FactorSchedule` is `string option` (`SecurityMaster.fs:405`) and is declared
`Opt("factorSchedule", ...String)` in the schema (`SecurityAssetTermsSchema.cs:244`). But
`StructuredCashFlowTermsResolver.ReadFactorSchedule` accepts only a JSON **array**
(`StructuredCashFlowTermsResolver.cs:181-188`).

So for MBS, CMO, CLO, ABS, and IO/PO strips — precisely the instruments where pool factors drive
principal cash flows — the typed multi-point `FactorSchedule` never resolves and `FactorAsOf()`
silently falls back to the scalar `CurrentFactor` (`StructuredCashFlowTerms.cs:43-54`). The domain
declares 19 structured `BondSubclass` cases (`SecurityMaster.fs:198-218`) whose economics depend on
this. `StructuredCredit` also has no relational projection.

### 4. The canonical open-lot aggregate is unwired and single-method — P2

`FaceValueLot` (`FaceValueLot.cs`) is a well-designed aggregate that makes par basis and booked factor
explicit, and `FaceValueLotExtensions.ToLedgerTaxLot` documents itself as "the single seam where the
engines' price-per-100 lot convention is applied." No production code constructs one — the only
callers of `ToLedgerTaxLot` are in `FaceValueLotTests.cs:109-118`. The seam that prevents a
per-unit-priced lot from mis-amortizing is not on any ingestion or ledger path.

Separately, `AmortizedBasisAsOf` (`FaceValueLot.cs:94-113`) hardcodes straight-line amortization and
takes no method parameter, while the domain declares five `AmortizationMethod` cases
(`SecurityTermModules.fs:422-435`) — `ConstantYield` (effective-interest, documented as the default
for most debt), `StraightLine`, `NoAmortization`, `AuctionRate`, `PurchasedCreditImpaired` — and
`SecurityCalculations.fs:120-128` already implements constant-yield. For institutional use,
effective-interest is the required method for most premium/discount amortization; the canonical lot
aggregate cannot express it.

### 5. Adding an asset class is an unguarded ten-surface change — P3

A new class requires coordinated edits to: the F# DU case, `AssetClassRegistry.keyOf`, the
`descriptors` table, the serializer arm, the mapper arm, `SecurityAssetTermsSchema`,
`SecurityAssetClassCatalog`, `SecurityKindMapping`, optionally a projection store interface plus
implementation plus migration, and the upcaster chain. Only the schema-versus-catalog pair is
test-bound.

The mapper's `_ =>` fallback degrading unknown classes to `OtherSecurity`
(`SecurityMasterMapping.cs:395-404`) is the right call for read availability — it was added after an
`InvestmentFund` read outage — but it means a half-registered class now fails *silently* rather than
loudly. Nothing detects a class registered in the catalog but missing a serializer arm.

### Minor

- `SecurityAssetPackRegistry.InferLifecycleEvent` (`SecurityAssetPackRegistry.cs:464-500`) infers
  lifecycle events from journal-template names by substring sniffing, with `"Amendment"` as the
  catch-all. Template naming silently determines accounting classification.
- `StructuredCashFlowTermsResolver` maintains its own vendor alias tables
  (`StructuredCashFlowTermsResolver.cs:15-42`) parallel to the alias lists in
  `SecurityAssetTermsSchema`. Two sources of truth for field naming, unbound to each other.

## Top priorities

1. **Make `CustomAsset` a first-class `SecurityKind`.** Add the DU case carrying
   `customProfileId`/`profileVersion`/`profileFields`/`profileApproval`, give it serializer and mapper
   arms, and delete `TryResolveProfileBackedAlternativeAssetClass` along with the
   `assetClassOverride`/`assetSpecificTermsOverride` patching. Profile-to-asset-class resolution should
   read the approved profile definition from `SecurityAssetProfileCatalog`, not sniff strings. This
   removes the aggregate/projection disagreement and is the prerequisite for every other asset-class
   extension.
2. **Make `SecurityAssetTermsSchema` load-bearing.** Add a conformance test that round-trips one
   populated instance of every `SecurityKind` through serializer → mapper and asserts the emitted key
   set equals the declared schema. Fix `votingRightsCat` as the first thing it catches.
3. **Type the factor schedule.** Change `StructuredCreditTerms.FactorSchedule` from `string option` to
   a `FactorScheduleEntry list` (mirroring `PrincipalPaymentEntry`), update the schema entry to
   `Array`, and keep the string form as a read alias in the upcaster.
4. **Wire `FaceValueLot` into the ingestion and ledger paths**, and make `AmortizedBasisAsOf` take an
   `AmortizationMethod`, delegating `ConstantYield` to the existing `SecurityCalculations`
   implementation.
5. **Codify the add-an-asset-class contract as a registry conformance test** — every
   `AssetClassRegistry` entry must have a serializer arm, a mapper arm, a schema entry, and a catalog
   descriptor, or be on an explicitly declared exception list, following the pattern
   `ProjectionCoverage_PartitionsTheCatalogIntoProjectedAndDeclaredGaps` already sets.

Priorities 1 and 2 are the ones that block extensibility to new asset classes. 3–5 are correctness and
guardrail work that can proceed in parallel.
