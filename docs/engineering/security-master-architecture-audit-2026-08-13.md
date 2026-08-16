# Security Master Architecture Audit — 2026-08-13

Source-evidence review of the Security Master subsystem against institutional finance requirements:
asset model normalization, cross-asset extensibility, identifier resolution, snapshot/projection
architecture, metadata validation, cashflow/factor schedule integration, open-lot modeling,
provenance, schema versioning, serialization compatibility, UI integration, editable workflows, and
auditability.

Method: static reading of `src/Meridian.Contracts/SecurityMaster/`,
`src/Meridian.Application/SecurityMaster/`, `src/Meridian.Storage/SecurityMaster/`,
`src/Meridian.FSharp/Domain/SecurityMaster*.fs`, `src/Meridian.FSharp/Interop.SecurityMaster.fs`,
`src/Meridian.Instruments/`, the workstation passport surfaces, and
`tests/Meridian.Tests/SecurityMaster/`. No build or test run was performed; every claim below cites
the file and line it was read from. Items phrased as "appears to" were not executed.

---

## Verdict

The subsystem is **not** clean-and-extensible-across-all-asset-classes. The identity, event-sourcing,
governance, and classification layers are genuinely strong and would hold up in an institutional
review. The **term layer is not** — it has multiple parallel, unreconciled contracts for the same
payload, and the extension points that are supposed to make new asset classes additive
(`SecurityAssetTermsSchema`, `SecurityAssetPackRegistry`) do not actually gate or drive anything.

---

## What's solid

**Identity and identifier resolution.** `SecurityIdentifierKind` carries 16 real-world kinds plus an
explicit `Unknown` read-tolerance member for mixed-version rollout, with the write-side mapping
rejecting `Unknown` so an unrecognized kind is never silently re-persisted
(`SecurityIdentifiers.cs:31`, `SecurityMasterMapping.cs:194`). `SecurityIdentifierNormalizer`
normalizes and check-digit-validates ISIN, CUSIP, SEDOL, LEI (mod 97-10), FIGI, OCC/OSI, WKN,
Valoren, and CIK. Identifiers carry `ValidFrom`/`ValidTo` temporal windows, `ProviderSymbol` is
namespaced by authoritative provider, and there is a separate scoped alias table
(`SecurityAliasDto`), a historical symbol timeline resolver, and identifier-ambiguity conflict
detection. This is the strongest part of the subsystem.

**Classification is genuinely table-driven.** `AssetClassRegistry`
(`SecurityMaster.fs:551-675`) is one descriptor table plus a single `keyOf` match; the asset-class
string, the derivative predicate, and the canonical `SecurityClassification` all derive from it
rather than being re-encoded per call site. Adding a class means one table row and one match arm.

**Event-sourced core with real rebuild.** Event store, snapshot store, aggregate rebuilder, rebuild
orchestrator, projection cache with warm-up, optimistic concurrency
(`SecurityMasterConcurrencyException`), and append-only corporate actions whose amendment/cancellation
chains are folded by `CorporateActionEffectiveStateProjector` rather than mutated in place.

**Governance and auditability.** Draft → submit → approve → publish revision lifecycle with a
server-issued revision id, mandatory justification, and durable change entries
(`SecurityMasterWorkbenchCommandService.cs:140-170`). Operator overrides carry approval status,
reviewer identity, reviewed-at, and an audit trail, all three of which `SecurityValidationService`
enforces before governed run/ledger/report-pack use. Asset profiles have versioned definitions,
lineage, rollback, and governance audit events.

**Schema versioning has real machinery.** Payload families were deliberately separated after a
documented leak where an economic-terms version reached asset-specific-terms acceptance
(`SecurityMasterSchemaVersions.cs:48-66`). There is a migrate-on-read upcaster chain (v0 unstamped →
v1; economic-terms v2 → flattened v1), a promoted queryable `schema_version` column (migration 024),
and read-tolerant enum parsing throughout.

**Open-lot modeling for par instruments.** `FaceValueLot` is a well-designed aggregate: it makes the
quote basis explicit (`ParBasis`, killing the silent price-per-100 assumption), records the pool
factor the face was booked at, and co-locates day-count-weighted amortized basis with the method the
ledger amortization engine posts.

**Validation is data-driven.** `AssetClassValidatorRegistry` composes declarative `FieldRule` /
`DateOrderRule` specs per class, covers 25 of 26 catalog classes, and fails loud
(`SM_ASSET_CLASS_UNSUPPORTED`) rather than silently passing an unregistered class.

**The projection gap is declared, not accidental.** `SecurityAssetTermsSchemaTests:21-38,107-119`
pins the exact set of intentionally unprojected classes so the 11-of-26 relational coverage is a
reviewed decision.

---

## What's at risk

### R1 — The typed domain model is not the system of record, and the seam leaks

`SecurityKind` is a closed 25-case F# DU, but it is a **lossy derivation**, not the truth.
`SecurityEconomicDefinitionRecord` carries `LegacyAssetClass` and `LegacyAssetSpecificTerms`
(`SecurityDtos.cs:118-119`) as verbatim passthrough, and those are what actually survive a rebuild
(`SecurityEconomicDefinitionAdapter.cs:44-77`). `CustomAsset` has no DU case at all — it collapses
into `OtherSecurity` (`SecurityMasterMapping.cs:308`).

The amend path *does* route through the lossy DU (`SecurityMasterService.cs:66-70`), so amend
correctness for profile-backed records depends entirely on a compensating override whose predicate
hard-codes seven asset-class strings (`SecurityMasterService.cs:386-397`). Two consequences follow
from that list being hard-coded:

- A new profile-backed asset class not added to `IsProfileBackedCustomAsset` is silently
  reclassified to `OtherSecurity` on its first amend.
- `GetProfileBackedAssetSpecificTermsOverride` (`SecurityMasterService.cs:367-380`) returns the
  **current** terms whenever the incoming patch is not itself profile-backed. A field-level patch
  against a profile-backed record therefore appears to be discarded rather than applied.

This is the single largest generalization target: the special case should become a first-class
domain case (or the passthrough should become the explicit contract), not a compensating patch
applied after the aggregate has already dropped the data.

### R2 — `SecurityAssetTermsSchema` is documentation, not an enforced contract

Its own docstring states it exists so the F# serialize side, the C# deserialize side, and the
projection decoders "can be validated against a single contract instead of against each other"
(`SecurityAssetTermsSchema.cs:55-69`). In practice it is referenced by exactly two files: its own
definition, and **comments** in `PostgresSecurityMasterStore` (lines 998, 1524). Neither
`Interop.SecurityMaster.assetSpecificTermsJson` nor `SecurityMasterMapping.ToSecurityKind` consults
it, and `SecurityAssetTermsSchemaTests` only checks internal consistency — catalog parity, no
duplicate keys, and two hand-written spot checks. The exact drift class it was built to prevent
(nested-vs-flat bond coupon, `swapType` vs `legs`) can recur unnoticed.

There is already a live divergence. The declared `CustomAsset` schema is
`customProfileId` / `profileVersion` / `profileFields` / `profileApproval`
(`SecurityAssetTermsSchema.cs:209-217`), but the deserializer's `CustomAsset` arm requires
`category` (`SecurityMasterMapping.cs:308-309`) — a field the schema does not declare. It works only
because the WPF writer hand-adds `category` to the payload
(`SettingsViewModel.AssetProfiles.cs:500-503`). Any producer that follows the declared contract —
CSV import, the API, the browser workstation — gets `Missing required string 'category'` on every
read of that row.

### R3 — Factor schedules: three readers, no writer, and a free-text domain type

- Domain: `StructuredCreditTerms.FactorSchedule : string option` — a **free-text string**
  (`SecurityMaster.fs:405`), declared `String` in the terms schema.
- `StructuredCashFlowTermsResolver` reads `factorSchedule` as an **array of objects**, across two
  container aliases and four date aliases (`StructuredCashFlowTermsResolver.cs:24-27`).
- `SecurityMasterAccountingEventSourceAdapter` reads it as an array from
  `economicTerms.factorSchedule` and `structuredProduct.factorSchedule`
  (`SecurityMasterAccountingEventSourceAdapter.cs:332-341`).

So `StructuredCashFlowTerms.HasFactorSchedule` is structurally unreachable for any record written
through the domain model, and the accounting-event service's `RequiresFactorSchedule` gate
(`SecurityMasterAccountingEventService.cs:254-262`) has no domain-model path that can satisfy it.
MBS/ABS/CLO/CMBS amortization is precisely the use case this blocks — and it is the use case
`SecurityMasterOperationalReadinessService` names as the point of the `CustomAsset` class
(`:155-163`).

### R4 — Term reading is alias-probing, a second parallel contract

`StructuredCashFlowTermsResolver` maintains roughly twenty hand-written alias arrays and walks three
blobs (asset-specific terms → nested `profileFields` → common terms) first-match-wins. Its own header
says it replaced "scattered fuzzy key-probing"; it centralized that probing but did not eliminate it,
and the alias table is derived from nothing. Adding an asset class means guessing which vendor
spellings it needs, in a file unrelated to the one where its fields are declared.

### R5 — Provenance is record-level only; field-level lineage is synthesized

`SecurityRecordProvenance.ForField()` stamps the **record's** source system onto any field path
requested (`SecurityMasterProvenance.cs:37-38`). There is no stored per-field (source, asOf,
confidence) attribution, and `Confidence` is never populated by any producer. Conflict detection
compares two sources' values and records a winner, but the winning value is not written back with its
own attribution — after a resolve, the golden record still reports the record-level source for a
field that came from elsewhere. For golden-record defensibility under audit, this is the main
structural gap.

### R6 — Operator field edits never reach the golden record

The browser passport editor takes a **free-text field path** and a **free-text value**
(`security-passport-editor.tsx:332-336`, placeholder `EconomicDefinition.Coupon` / `5.125`). The
value is written to a string→string `operator_overrides` overlay and, by explicit design comment, is
*not* appended to the economic event stream (`SecurityMasterWorkbenchCommandService.cs:93-97`).

No consumer merges overrides into any read model or projection —
`SecurityValidationService.ValidateOperatorOverridesAsync` reads them only to assert approval status,
reviewer sign-off, and audit-trail presence. The governed draft → submit → approve → publish ceremony
is real, but the edited value is functionally an annotation: the field path is unvalidated against
any schema, the value is untyped, and nothing downstream consumes it. There is no schema-driven edit
form on the browser lane at all; the WPF lane has one, but only for custom profiles.

### R7 — Asset-class coverage is uneven, and one class is unusable

`InvestmentFund` — mutual funds, ETFs, hedge funds, REITs, closed-end funds — is a first-class F# DU
case, is in `SecurityAssetClassCatalog` (`:370`), and has a terms-schema entry, but has **no
validator**. Every such record therefore raises `SM_ASSET_CLASS_UNSUPPORTED` at Error severity
(`SecurityValidationService.cs:120-129`). It also has no relational projection, and no
`SecurityAssetPackRegistry` pack claims the string (packs use `ExchangeTradedFund` / `PrivateFund`),
so `FindByAssetClass("InvestmentFund")` returns empty. There is a coverage guard binding the catalog
to projections, but none binding the catalog to validators.

Separately, the 15 unprojected classes are concentrated exactly on the private-markets and structured
classes — `DirectLoan`, `StructuredCredit`, `PrivateFundInterest`, `PrivateCompanyEquity`,
`RealEstateHolding`, `CommitmentGuarantee`, `CustomAsset`. Those are the institutional
differentiators, and their queryability is JSONB-only.

### R8 — `SecurityAssetPackRegistry` is prose compiled into C#

All ten packs share the *same* `ContractSchema`, `ValidationRules`, and `ReportingTaxonomy` constant
instances (`SecurityAssetPackRegistry.cs:383,395-396`), populated with English phrases — `"issuer"`,
`"trade date"`, `"loan-to-value"`. The 400-line validator checks that those string lists are
non-empty and that policy prose contains the substring `"core ledger"`. `InferLifecycleEvent`
(`:464-500`) maps a journal-template label to a lifecycle event by substring-matching English words
(`"interest"` → Coupon, `"release"` → Repayment, `"nav"` → Appraisal, otherwise Amendment). Pack
asset-class strings do not line up with catalog asset-class names.

It reads as an admission-policy design document that was compiled rather than written down. It does
not gate anything a new asset class must actually satisfy, so it gives false assurance that the
extension point is governed.

---

## Top 5 priorities

1. **Make the profile-backed / passthrough case first-class in the domain model.** Add a real
   `SecurityKind` case (or make the raw-terms passthrough the explicit, typed contract) so the
   seven-string `IsProfileBackedCustomAsset` special case and the post-hoc `assetClassOverride` /
   `assetSpecificTermsOverride` patching can be deleted. Fix the amend-patch discard in
   `GetProfileBackedAssetSpecificTermsOverride` as part of the same change. (R1)

2. **Turn `SecurityAssetTermsSchema` into an enforced contract.** Add codec-conformance tests that
   drive the F# serializer and the C# deserializer off the table rather than off each other, and
   reconcile the `CustomAsset` `category` divergence first — that one is a live read-failure for any
   producer other than the WPF writer. (R2)

3. **Model factor schedules as a typed, dated schedule with a single owner and a write path.**
   Replace `StructuredCreditTerms.FactorSchedule : string option`, then collapse the three
   independent array readers onto it. This is the change that unblocks MBS/ABS/CLO/CMBS
   amortization, which the readiness service already advertises as in scope. (R3, R4)

4. **Add field-level provenance and write conflict winners back with their own attribution.** Stop
   synthesizing per-field lineage from the record-level source. Pair this with a decision on
   operator overrides: either resolve them into the read model as governed field values, or rename
   the surface to what it currently is (annotations) so the passport editor does not imply the edit
   took effect. (R5, R6)

5. **Add a catalog-vs-validator coverage guard and close `InvestmentFund`.** Cheapest high-value fix
   in the list — it mirrors the existing catalog-vs-projection guard and removes a class that
   currently fails validation outright. Then decide whether `SecurityAssetPackRegistry` becomes a
   registry that actually gates admission, or moves to `docs/` where prose belongs. (R7, R8)
