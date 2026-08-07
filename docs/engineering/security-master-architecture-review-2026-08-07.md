# Security Master Architecture Review — 2026-08-07

**Status:** point-in-time review
**Scope:** `Meridian.Contracts/SecurityMaster`, `Meridian.Application/SecurityMaster`,
`Meridian.FSharp/Domain/SecurityMaster*.fs`, `Meridian.FSharp/Interop.SecurityMaster.fs`,
`Meridian.Storage/SecurityMaster`, `Meridian.ReferenceData/SecurityMaster`,
`Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs`, browser + WPF workstation surfaces.
**Method:** source reading only. No build or test run was performed; every claim below cites a file
and line so it can be re-checked.

---

## 1. Verdict

The Security Master is **not** a thin reference-data table — it is a genuine event-sourced,
bitemporal, governed master with real institutional bones: check-digit-validated identifiers, a
draft→submit→approve→publish edit lifecycle, multi-source conflict detection, migrate-on-read schema
upcasting, and a typed cash-flow/factor-schedule engine. Several patterns here are better than what
is typically shipped at this stage.

It is **not yet clean across all asset classes.** Support is stratified: roughly five asset classes
are first-class end to end, another dozen are modelled but only partially wired, and the designated
extensibility escape hatch (`CustomAsset`) is the least coherent path in the subsystem. The core
structural risk is that **asset-class identity is expressed as a closed F# discriminated union plus
~8 independent, hand-maintained string-keyed registries**, with no single conformance gate binding
them. Adding an asset class today is an 8-to-15-file archaeology exercise, and the most recently
added class (`InvestmentFund`) demonstrably did not complete the circuit.

---

## 2. What is solid

**Identifier model and normalization.** `SecurityIdentifierKind`
(`src/Meridian.Contracts/SecurityMaster/SecurityIdentifiers.cs:6`) covers 15 real institutional kinds
including LEI, PermID, BBGID, WKN, Valoren, RIC and CIK — not just ticker/CUSIP/ISIN.
`SecurityIdentifierNormalizer` (`SecurityIdentifierNormalizer.cs:61`) performs genuine format *and
check-digit* validation for ISIN, CUSIP, SEDOL, LEI, FIGI (Luhn-style MOD 10 at line 402), OCC/OSI
symbols and RIC. Identifiers are temporal (`ValidFrom`/`ValidTo`) and carry both raw and normalized
provider values.

**Read-tolerance discipline.** Enums carry explicit `Unknown` read-tolerance members with documented
asymmetric semantics — readable on mixed-version rollout, rejected on write-back
(`SecurityIdentifiers.cs:33-39`, `SecurityDtos.cs:11-16`,
`SecurityMasterCashFlow.cs:17-21`). `SecurityMasterMapping.ToSecurityKind` has a `_ =>` fallback
(`SecurityMasterMapping.cs:396-405`) that degrades an unrecognized asset class to `OtherSecurity`
with the raw class preserved as `category`, instead of a per-row read outage. This is exactly right
for a rolling deployment.

**Schema versioning.** Payload families are correctly *separated*:
`AssetSpecificTermsSchema` vs `EconomicTermsSchema` (`SecurityMasterSchemaVersions.cs`), with an
explicit comment recording the historical bug where an economic-terms version leaked into
asset-specific-terms acceptance. The composed migrate-on-read chain
(`SecurityAssetSpecificTermsUpcasterChain.cs`) handles unstamped (v0) and cross-family (v2) payloads
through one entry point. The alias tolerance in `StructuredCashFlowTermsResolver.cs:21-38` — accepting
`dayCountConvention`/`dayCount`/`dayCountBasis`, `factorSchedule`/`factorSchedules`, and per-leg
vendor spellings — is the right shape for multi-vendor ingest.

**Cash-flow and factor-schedule integration.** `StructuredFactorScheduleEntry`,
`StructuredCashFlowLeg` (with explicit nullable `Direction` and an honest documented flat-forward
simplification, `SecurityMasterCashFlow.cs:105-129`), and `StructuredCashFlowStaleness` as a *hard
posting gate* rather than an advisory log (`SecurityMasterCashFlow.cs:40-59`) are all well-judged.
`StructuredCashFlowProjectionDto.TermsUsed` carries the resolved terms forward so ledger math cannot
re-resolve and diverge — a subtle and correct decision.

**Open-lot modelling for par instruments.** `FaceValueLot` (`FaceValueLot.cs`) is the strongest
single file in the subsystem. It makes previously-implicit caller conventions explicit — `ParBasis`
(no silent price-per-100 assumption), `BookedFactor`, and a mandatory Security Master identity link —
and owns all derived economics (`CostBasis`, `PremiumDiscount`, `CurrentFace`, `AmortizedBasisAsOf`)
so consumers cannot compute them differently. Constructor invariants are enforced.

**Editable workflow and auditability.** `SecurityMasterWorkbenchCommandService.UpdateSecurityFieldAsync`
(`SecurityMasterWorkbenchCommandService.cs:80`) requires a justification, enforces optimistic
concurrency against the canonical version, rejects phantom securities, and — critically — routes
operator field edits to an **overlay** store rather than the economic event stream, with the reason
documented inline (a partial field-edit payload replayed through `FromEconomicPayload` would clobber
the economic definition). Overrides carry their own approval status and durable audit trail
(`OperatorOverrides.cs:15-46`). The event store is append-only with a `unique (security_id,
stream_version)` constraint (`Migrations/001_security_master.sql:14`) and version-checked appends
(`PostgresSecurityMasterEventStore.cs:37`).

**Bitemporality.** Both axes are implemented and distinguished: `RebuildAsOfAsync` (valid time) and
`RebuildRecordedAsOfAsync` (transaction time), `SecurityMasterQueryService.cs:28,35`. Because every
event payload carries the full definition rather than a delta, as-of reconstruction is a scan-to-last
rather than a fold — simple and hard to get wrong.

**Declared gaps are tested, not accidental.** `SecurityAssetTermsSchemaTests` binds
`PostgresSecurityMasterStore.ProjectedAssetClasses` to the catalog and forces the 15 unprojected
classes to be listed explicitly, so the 26-vs-11 projection gap must be re-affirmed in review
(`tests/Meridian.Tests/SecurityMaster/SecurityAssetTermsSchemaTests.cs:21-37,107`). This is the
pattern the rest of the subsystem needs and mostly lacks.

---

## 3. What is at risk

### 3.1 Asset-class identity is a closed union fanned out into ~8 unsynchronized registries

`SecurityKind` (`src/Meridian.FSharp/Domain/SecurityMaster.fs:494`) is a 25-case closed discriminated
union. `AssetClassRegistry` (`SecurityMaster.fs:549`) is a good local improvement — one descriptor
table, one `keyOf` match — but it only governs *classification*. Around it sit independent
string-keyed tables that must agree and are not jointly enforced:

| Registry | File |
|---|---|
| `SecurityKind` DU + serializer | `Meridian.FSharp/Domain/SecurityMaster.fs`, `Interop.SecurityMaster.fs:67` |
| C# deserializer | `Application/SecurityMaster/SecurityMasterMapping.cs:228` |
| Declarative field/type schema | `Contracts/SecurityMaster/SecurityAssetTermsSchema.cs:78` |
| Asset-class capability catalog | `Contracts/SecurityMaster/SecurityAssetClassCatalog.cs` |
| Asset-pack registry | `Contracts/SecurityMaster/SecurityAssetPackRegistry.cs` |
| Instrument-type descriptors | `Contracts/SecurityMaster/InstrumentTypeDescriptorCatalog.cs` |
| Corporate-action applicability | `Contracts/SecurityMaster/CorporateActionTypeDescriptorCatalog.cs` |
| InstrumentType bridge | `ReferenceData/SecurityMaster/SecurityKindMapping.cs:21` |
| Per-class validators | `Application/SecurityMaster/Validation/AssetClassValidatorRegistry.cs` |
| Operational readiness | `Application/SecurityMaster/SecurityMasterOperationalReadinessService.cs:95` |
| Relational projection stores | `Storage/SecurityMaster/Postgres*ReferenceProjectionStore.cs` (11 of 26) |

**Measured evidence that this drifts.** `InvestmentFund` — the newest class — appears in only 8 source
files. It is present in the DU, the interop serializer, the terms schema, the class catalog, the kind
mapping and the C# mapper, and **absent** from `SecurityAssetPackRegistry`,
`InstrumentTypeDescriptorCatalog`, `AssetClassValidatorRegistry`,
`SecurityMasterOperationalReadinessService`, `SecurityAssetProfileCatalog`, and the asset-class
support test matrix (`SecurityMasterAssetClassSupportTests.cs:13-30` enumerates 17 classes and omits
`InvestmentFund` and `CustomAsset`). Practically: an investment-fund security can be created and
persisted but has no validator, no readiness definition, and no operational-readiness evidence
requirements. Nothing fails; the class is simply silently second-class.

Only `SecurityAssetTermsSchemaTests.Schema_DeclaresEveryCatalogAssetClass` enforces cross-registry
completeness, and it covers exactly one of the eleven registries.

### 3.2 `CustomAsset` — the extensibility escape hatch — is incoherent across the stack

This is the most concrete defect found.

1. **No domain representation.** `CustomAsset` has no `SecurityKind` case. `SecurityMasterMapping.cs:308`
   maps `"OtherSecurity" or "CustomAsset"` onto `SecurityKind.NewOtherSecurity`, reading only
   `category`/`subType`/`maturity`/`issuerName`/`settlementType`. `customProfileId`, `profileVersion`,
   `profileFields` and `profileApproval` are **dropped on the floor** by the domain aggregate.

2. **The declared schema and the wire contract disagree.** `SecurityAssetTermsSchema["CustomAsset"]`
   (`SecurityAssetTermsSchema.cs:209-217`) declares exactly four fields and does **not** declare
   `category`. But the deserializer's `CustomAsset` arm calls `GetRequiredString(json, "category")`
   (`SecurityMasterMapping.cs:309`, throwing at `:597-600`). A schema-conformant `CustomAsset` payload
   therefore *throws on create*. The only production writer — WPF
   (`SettingsViewModel.AssetProfiles.cs:498-512`) — happens to send `category`, `subType` and
   `evidenceLinks`, none of which the schema declares. The schema is not the contract; the WPF view
   model is.

3. **The write path bypasses the domain to compensate.** Because the aggregate loses the profile
   envelope, `SecurityMasterService` runs the F# command and then **overwrites** the resulting
   projection's `AssetClass` and `AssetSpecificTerms` with the caller's raw JSON
   (`SecurityMasterService.cs:330-343`, invoked from `ExecuteCreateAsync` at `:211-218`). This is a
   shadow write path: for profile-backed records the domain aggregate's output is computed, validated,
   and then discarded.

4. **Asset class is inferred by heuristic field-sniffing.**
   `TryResolveProfileBackedAlternativeAssetClass` (`SecurityMasterService.cs:399-443`) guesses the real
   asset class by probing `profileFields` for magic key sets and matching `profileId`/`category`/
   `subType`/`accountingClassification` against hardcoded string lists — `"structured-credit-io-po"`,
   `"MBS"`, `"ABS"`, `"CLO"`, `"CMBS"`, `"private-fund-interest"`, `"409A"`-adjacent spellings, and so
   on. A custom profile whose fields happen to include `counterparty`, `committedAmount` and
   `effectiveDate` will be silently reclassified as `CommitmentGuarantee`, changing its accounting
   treatment. This is the single most fragile pattern in the subsystem.

The intent is clearly right — a governed, profile-backed extension point that avoids a code change per
exotic asset. The implementation currently achieves the opposite: it hardcodes six asset classes'
worth of heuristics into the service layer.

### 3.3 Provenance is record-level only; there is no stored field-level attribution

`SecurityRecordProvenance` (`SecurityMasterProvenance.cs:26`) is one `{sourceSystem, sourceRecordId,
asOf, updatedBy, reason}` document per security version. `SecurityFieldProvenance` exists — but
`ForField` (`:37`) *derives* it by projecting the record-level document onto a field path, with
`confidence` supplied by the caller (correctly never fabricated).

For a golden-record master this is the wrong shape. The institutional requirement is per-field
attribution: maturity from Bloomberg as of T-1, coupon from the trustee report as of T-3, sector from
an operator override. `SecurityMasterConflictKinds.EconomicTermMismatch` and the conflict detection
service exist precisely because two vendors disagree per field — but once a conflict resolves, the
winning field's true source is not persisted alongside the field. `SecurityMasterConflict` carries
`ResolvedWinnerSource` on the *conflict row*, not on the field. Reconstructing "where did this
maturity come from" requires joining the resolved-conflict history rather than reading the record.

### 3.4 Operator overrides are untyped free-form strings

`OperatorOverridesPatchRequest.SetValues` is `IReadOnlyDictionary<string, string>`
(`OperatorOverrides.cs:54`), documented as "free-form string key/value pairs … (e.g. ratings, sector
classification, factor adjustments)". Nothing validates that the field path exists in the asset
class's declared term schema, or that the value parses as the field's declared
`SecurityAssetTermFieldType`. `SecurityAssetTermsSchema` already holds exactly the metadata needed to
type-check these, and is not consulted. A typo'd field path or a non-numeric factor adjustment is
accepted, persisted, approved, and surfaces as a silently-ignored overlay.

### 3.5 Coverage asymmetries that constrain new asset classes

- **Create workflow:** only `Equity` and `CustomAsset` set `SupportsBasicCreateWorkflow: true`
  (`SecurityAssetClassCatalog.cs`). Every other class — including Bond, Option, Future — is
  ingest-or-import only from the operator UI. Combined with §3.2, the *only* generic operator create
  path runs through the least coherent code path in the subsystem.
- **Open-lot modelling:** `FaceValueLot` covers the 8 par-denominated classes flagged
  `UsesFaceValueLots: true`. There is no equivalent canonical unit/share lot aggregate for equities,
  funds or private interests; unit-based lot logic lives in `Meridian.Execution/TaxLotAccounting/`
  under a separate model. Cost-basis semantics are therefore defined twice, in two shapes, with no
  shared abstraction.
- **Relational projections:** 11 of 26 classes. This gap is explicitly declared and tested (§2), so
  it is a conscious decision — but it means query surfaces over private credit, private funds, real
  estate and commitments fall back to JSONB probing.
- **UI parity:** the profile-backed create flow exists only in WPF
  (`SettingsViewModel.AssetProfiles.cs`); the browser workstation's Security Master surfaces
  (`security-passport-editor*`, `accounting-screen.security-master-*`) cover the governed edit
  lifecycle but not custom-asset creation. Per `CLAUDE.md`, WPF is meant to be closing parity *to*
  the web lane, so this is inverted.

### 3.6 Lower-severity notes

- **`factorSchedule` is typed `String` for `StructuredCredit`** in the declared schema
  (`SecurityAssetTermsSchema.cs:244`) and read as `GetOptionalString`
  (`SecurityMasterMapping.cs:335`), while the typed `StructuredFactorScheduleEntry[]` is resolved from
  the *economic-terms* document (`StructuredCashFlowTermsResolver.cs:181`). Two representations of the
  same concept in two payload families.
- **The v2→v1 upcaster is lossy by construction** (`SecurityAssetSpecificTermsUpcasterChain.cs:49-88`):
  it carries maturity/coupon/payment/accrual/discount only. Asset-specific economics (strike, expiry,
  tranche, gpSponsor) are dropped. The XML doc states the authoritative record is stored separately
  and this feeds read-compatibility only — correct, but it means a v2 payload landing in the
  asset-specific slot reads as a *silently thinner* record rather than an error.
- **Snapshots are latest-only.** `security_snapshots` is keyed `security_id uuid primary key`
  (`Migrations/001_security_master.sql:81`), so an as-of rebuild for an older version cannot use the
  snapshot and replays the full stream — `LoadAsync(securityId)` returns the whole stream and filters
  in memory (`SecurityMasterAggregateRebuilder.cs:38-42`).
- **No cross-security uniqueness on identifiers.** `security_identifiers` PK is `(security_id,
  identifier_kind, identifier_value, valid_from)` (`Migrations/001_security_master.sql:56`), so two
  securities may claim the same ISIN. This is a defensible multi-vendor mastering choice — ambiguity
  is caught by `SecurityMasterConflictKinds.IdentifierAmbiguity` detection — but it is detect-after-write,
  not prevent-on-write, and should be a documented decision rather than an implicit one.

---

## 4. Top priorities

**P1 — Make `CustomAsset` a real, single, non-heuristic path.**
Give the profile envelope a first-class domain representation (a `SecurityKind.CustomAsset of
CustomAssetTerms` case carrying `customProfileId`/`profileVersion`/`profileFields`, or an explicit
profile envelope on `SecurityMasterRecord` orthogonal to `SecurityKind`). Then delete
`TryResolveProfileBackedAlternativeAssetClass` and the projection-override path in
`SecurityMasterService.cs:330-443`: an operator picking a profile should *declare* the target asset
class, not have it guessed from field-name bingo. Reconcile
`SecurityAssetTermsSchema["CustomAsset"]` with what WPF actually sends (`category`, `subType`,
`evidenceLinks`) and with what the deserializer requires. Highest severity: this is both a
correctness bug (schema-conformant payload throws) and a silent-misclassification risk.

**P2 — Add one cross-registry conformance gate for asset classes.**
Extend the pattern already proven in `SecurityAssetTermsSchemaTests`: a single test that asserts every
`SecurityKind` case is present in *every* registry — asset-pack, instrument-type descriptor, validator,
readiness, profile catalog, kind mapping, support-test matrix — or is listed in an explicit,
reviewed exemption array, exactly as `IntentionallyUnprojectedAssetClasses` does today. This converts
"adding an asset class" from archaeology into a compile-or-test-driven checklist, and would have
caught the `InvestmentFund` gap on the commit that introduced it.

**P3 — Persist field-level provenance.**
Store `(fieldPath, sourceSystem, asOf, confidence)` alongside the record rather than deriving it from
record-level provenance. Write the winning source onto the field when a conflict resolves, so
`SecurityMasterConflict.ResolvedWinnerSource` becomes the *audit* of a decision already reflected in
the data, not the only record of it. This is the largest remaining gap against institutional
golden-record expectations and it blocks credible per-field vendor-quality reporting.

**P4 — Type-check operator overrides against the declared term schema.**
Validate `OperatorOverridesPatchRequest.SetValues` keys against `SecurityAssetTermsSchema.Field(assetClass, key)`
and values against the declared `SecurityAssetTermFieldType` before staging. The metadata already
exists; wiring it is small and removes a whole class of silently-ignored overlays.

**P5 — Unify open-lot modelling.**
Extract the shared abstraction behind `FaceValueLot` and the unit-based lot logic in
`Meridian.Execution/TaxLotAccounting/` so cost-basis, relief and amortization semantics are defined
once and specialize by quantity basis (face vs units vs commitment) rather than by parallel model.
`FaceValueLot` is the right template — it should be the par-denominated *specialization* of a general
lot, not a separate concept.

---

## 5. Not addressed by this review

No build, test run, or database migration was executed. Runtime behaviour of the profile-backed create
path was inferred from source, not reproduced; the `CustomAsset` schema/deserializer mismatch in §3.2.2
in particular deserves a direct test before it is treated as confirmed. Performance characteristics of
the as-of rebuild path were reasoned about from code shape, not measured.
