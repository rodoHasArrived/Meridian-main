# Security Master Architecture Review — Institutional Extensibility

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-12
**Scope:** Engineering
**Review Cadence:** Per significant Security Master change

---

## Purpose

An evidence-based assessment of the Security Master against institutional-finance reference-data
requirements: cross-asset normalization, identifier resolution, snapshot/projection architecture,
metadata validation, cashflow and factor schedules, open-lot modeling, provenance, schema
versioning, serialization compatibility, UI integration, editable workflows, and auditability.

This is a review document, not a plan. Findings cite current source. Where a gap is already a
documented, guarded decision, the review says so rather than reporting it as drift.

---

## Verdict

The Security Master is **structurally sound and unusually well governed for its stage**, but it is
**not yet uniformly extensible across asset classes**. The write model (F# `SecurityKind`) is a
closed 25-case discriminated union whose economics are shallower than the taxonomy it advertises,
and adding an asset class currently requires coordinated hand-edits across roughly seven
independent registries. The governed edit workflow governs an *annotation overlay*, not the golden
record.

Nothing here is a correctness emergency. The risks are extensibility and institutional-completeness
risks that compound as new asset classes land.

---

## What's Solid

**Asset classification has one authoritative table.** `AssetClassRegistry`
(`src/Meridian.FSharp/Domain/SecurityMaster.fs:551`) is the single place `SecurityKind` is
pattern-matched for classification; the asset-class string, derivative predicate, and
`SecurityClassification` all derive from one descriptor list. Downstream projections update
automatically from one new arm.

**Identifier resolution is genuinely institutional.** Sixteen identifier kinds with real check-digit
validation — ISIN, CUSIP, SEDOL, LEI (ISO 17442 mod 97-10), FIGI, OCC OSI, RIC, WKN, Valoren, CIK
(`SecurityIdentifierNormalizer.cs:61-97`). Identifiers carry `ValidFrom`/`ValidTo` and provider
namespacing, aliases have their own timeline, and there is a dedicated
`SecurityMasterHistoricalSymbolTimelineResolver` plus a `SecurityMasterTickerChangeService`. Point-
in-time symbol resolution is a first-class concern rather than a lookup table.

**Validation is data-driven, not hand-coded per class.** `AssetClassValidatorRegistry` composes
declarative `FieldRule` / `DateOrderRule` specs per asset class
(`Validation/AssetClassValidatorRegistry.cs:64-260`), with composite validators and a
profile-backed validator for custom assets. Adding rules does not mean adding code paths.

**Bitemporal reconstruction exists and is deliberate.** `SecurityMasterAggregateRebuilder` offers
three distinct as-of semantics: `RebuildAsOfAsync` (transaction time, projection fallback),
`RebuildRecordedAsOfAsync` (strict — retained history only, so "a projection-only current
definition cannot masquerade as history"), and `RebuildEconomicDefinitionAsync`. Aliases are
filtered by both recorded-at and effective-at. That distinction is exactly the one accounting as-of
workflows need and it is rarely got right.

**Serialization compatibility is defensively designed.** Unknown enum values degrade to explicit
`Unknown` read-tolerance members (`SecurityStatusDto`, `StructuredCashFlowSourceKind`,
`StructuredCashFlowStaleness`) rather than failing the row. Unknown asset classes degrade to
`OtherSecurity` with the class name preserved as `category`
(`SecurityMasterMapping.cs:400-408`), and the raw document survives via
`SecurityEconomicDefinitionRecord.LegacyAssetSpecificTerms`. Schema families are separated so an
economic-terms version cannot leak into asset-specific-terms acceptance
(`SecurityMasterSchemaVersions.cs`), with an upcaster chain for migrate-on-read.

**Coverage gaps are declared and test-guarded, not silent.** `SecurityAssetTermsSchemaTests` holds
an explicit `IntentionallyUnprojectedAssetClasses` list and asserts the catalog-vs-projection
partition, so "adding a catalog class without a projection forces this list to be updated in
review". `SecurityAssetClassCatalogTests` locks the C# catalog to `AssetClassRegistry.assetClasses`.
This is the right governance instinct.

**Open-lot modeling for par instruments is careful.** `FaceValueLot` makes previously implicit
conventions explicit — quote basis (`ParBasis`, so a per-unit-priced lot cannot silently
mis-amortize through math assuming 100), booked pool factor, and Security Master identity — and owns
all derived economics so consumers cannot diverge.

**Operational readiness is modeled per asset class.** `SecurityMasterOperationalReadinessService`
carries per-class identifier, term, evidence, and ledger-depth requirements with hard-blocker flags.
Few systems at this stage know what "ready" means per asset class.

**Auditability is durable.** Migration 025 moved the conflict store and revision-lifecycle store off
process-local memory specifically so "a publish only ever runs against a revision that was durably
approved". Conflict resolution commits winner + close atomically. Override approvals carry a
durable audit trail with reviewer identity derived from the authenticated principal, not the
request body.

---

## What's At Risk

### 1. The taxonomy outruns the term model

`BondSubclass` declares 21 subclasses — `SinkingFund`, `StepRate`, `FixedToFloat`,
`InflationLinked`, `Vrdn`, `AuctionRate`, `Cmo`, `Clo`, `PrincipalOnly`, `InterestOnly`,
`InverseInterestOnly` and more (`SecurityMaster.fs:174-219`). But `BondCouponStructure` has exactly
three cases — `Fixed`, `Floating`, `ZeroCoupon` (`:222-225`) — and `BondTerms` has no principal
schedule, no step schedule, and no inflation index ratio.

A `StepRate` bond can be classified but its coupon schedule cannot be represented. A `SinkingFund`
bond cannot carry its sink schedule, even though `DirectLoanTerms` already has
`PrincipalSchedule: PrincipalPaymentEntry list`. An `InflationLinked` bond has nowhere to put an
index ratio. The subclass becomes a label that downstream cashflow, amortization, and NAV math
cannot act on.

### 2. One concept, three or four modeling routes

MBS/ABS/CLO/CMBS can be modeled as `Bond` with `BondSubclass.MortgageBacked` / `Cmo` / `Clo`, or as
`StructuredCredit`, or as `CustomAsset` with a governed profile — and the operational readiness
catalog labels `CustomAsset` as literally "MBS / ABS / CLO / CMBS / private assets"
(`SecurityMasterOperationalReadinessService.cs:155-157`). Three legitimate homes for the same
instrument means reporting, risk, and reconciliation cannot assume a stable partition.

Similarly, `MoneyMarketFund` and `InvestmentFund` overlap (both are funds; `InvestmentFundTerms`
even carries `isStableNav` for "stable-NAV money market and government liquidity funds"), and
`CashSweep` overlaps `MoneyMarketFund` for sweep vehicles.

### 3. `CustomAsset` — the designated extension point — does not round-trip

`CustomAsset` is declared in `SecurityAssetTermsSchema` (profile envelope: `customProfileId`,
`profileVersion`, `profileFields`, `profileApproval`), has a dedicated required-profile validator,
its own schema version (`AssetSpecificTermsSchema.CustomAssetProfile = 3`), a governance service,
and a readiness entry. But there is **no `SecurityKind.CustomAsset` case**. On deserialization it
collapses into `OtherSecurity`:

```csharp
// SecurityMasterMapping.cs:308 (pre-fix state this review found)
"OtherSecurity" or "CustomAsset" => SecurityKind.NewOtherSecurity(
    new OtherSecurityTerms(GetRequiredString(json, "category") /* … */));
```

The profile envelope is not among the five fields `OtherSecurityTerms` salvages. Since
`AmendTermsAsync` re-serializes from the domain `SecurityKind`
(`SecurityMasterService.cs:56-67`), the write model cannot represent what the extension point
stores. The raw payload survives in `LegacyAssetSpecificTerms`, so reads and replays hold, but the
extension point is a read-side and validation-side construct only — it never becomes a first-class
domain concept.

The same collapse applies to any asset class a node does not recognize. Read tolerance is correct;
amend-after-degrade is a write-side data-loss path that the tolerance comment does not cover.

### 4. Adding an asset class touches ~7 registries

A new class must be added to: the F# `SecurityKind` DU **and** `AssetClassRegistry.keyOf` **and**
`descriptors`; `SecurityAssetClassCatalog`; `SecurityAssetTermsSchema`; `SecurityKindMapping`;
`AssetClassValidatorRegistry`; `SecurityMasterOperationalReadinessService`; the F# interop
serializer (`Interop.SecurityMaster.fs`); the C# deserializer (`SecurityMasterMapping.ToSecurityKind`);
and optionally a projection interface + Postgres store + migration + DI registration.

The count of declared classes already differs by surface: F# DU 25, catalog 26, terms schema 26,
kind mapping 26, projection writers 11. Tests lock catalog↔registry and catalog↔terms-schema, but
nothing locks the *codec* surfaces — and `SecurityAssetTermsSchema`'s own docstring is explicit
that the field table was "hand-maintained three times … which let them silently drift (e.g. the
projection store reading a nested `coupon` object the serializer never wrote, so bond coupon columns
landed null)". The schema table is a drift **detector**; it is not yet a drift **eliminator**,
because both codec sides are still hand-written.

### 5. The governed edit workflow does not reach the golden record

`SecurityMasterWorkbenchCommandService.UpdateSecurityFieldAsync` stages edits as
`Dictionary<string, string>` entries in `IOperatorOverridesStore`, deliberately outside the economic
event stream. The contract states it plainly: "free-form string key/value pairs … **without amending
the canonical security terms**" (`OperatorOverrides.cs:25-29`).

Three consequences:

- **No type or existence check.** `fieldPath` is arbitrary text — the browser editor's input is a
  free-text box with placeholder `EconomicDefinition.Coupon`
  (`security-passport-editor.tsx:332`). Nothing validates the path against
  `SecurityAssetTermsSchema` for that security's asset class, and `newValue` is stored as a string
  regardless of the field's declared type.
- **No merge path.** No consumer reads the override store except the validation service, which only
  *flags* unapproved overrides (`SecurityValidationService.cs:570-604`). An approved correction to a
  coupon rate has no effect on cashflow projection, amortization, pricing, or NAV.
- **The approval gate protects an annotation.** Draft → Submitted → Approved → Published with
  independent-reviewer requirements, restatement resolution, and affected-ledger-book scoping — all
  applied to a side table that by design never changes the record it annotates.

The overlay decision is documented and intentional (`docs/plans/security-master-passport-workbench.md`,
decision D2 as amended: a partial field-edit payload appended to the economic stream would clobber
the definition on replay). The rationale is right. The gap is that no *typed amendment* path was
built alongside it, so the workbench remains an annotation surface rather than a correction surface.

### 6. Provenance is record-level; field-level attribution is synthesized

`Provenance` carries one `(sourceSystem, sourceRecordId, asOf, updatedBy, reason)` per record.
`SecurityFieldProvenance` exists but is *derived* by projecting record-level provenance onto a field
path via `ForField(...)`, with confidence null unless a caller supplies one
(`SecurityMasterProvenance.cs:37-38`). Nothing persists per-field attribution.

For a golden record assembled from multiple vendors this is the central missing fact: after conflict
resolution picks a winner per field, the system cannot durably answer "which vendor supplied this
maturity, as of when, at what confidence". `security_master_conflicts` records the resolution
event, but the resolved field's provenance is not written back onto the record.

### 7. Corporate actions use one wide table with per-event-type columns

`corporate_actions` (migration 003, extended by 021) has `dividend_per_share`, `split_ratio`,
`distribution_ratio`, `acquirer_security_id`, `exchange_ratio`, `subscription_price_per_share`,
`rights_per_share`, `redemption_price_percent_of_par` — eight typed payload columns for eighteen
declared event types (`CorporateActionEventTypes.cs`). `TenderOffer`, `CryptoFork`,
`ReturnOfCapital`, `PrincipalPaydown`, `OptionContractAdjustment`, and `Delisting` have no columns
of their own; `CorporateActionDto` mirrors the same shape as a 18-parameter positional record with
nullable one-off fields.

The lifecycle design around it is good — append-only with `supersedes_corp_act_id` chains folded by
`CorporateActionEffectiveStateProjector`, and a descriptor catalog with provider aliases and ISO
15022 CAEV alignment. The *envelope* is what does not generalize: each new event type is another
nullable column.

### 8. Factor schedules exist twice, in incompatible shapes

The write model stores `StructuredCreditTerms.FactorSchedule: string option` — free text — and the
terms schema declares it `String`. The read model has typed
`StructuredFactorScheduleEntry(AsOfDate, Factor)` with `FactorAsOf(asOf)` lookup, resolved by
`StructuredCashFlowTermsResolver` from a `factorSchedule` **array** it probes out of the economic
terms. A third type, `SecurityFactorScheduleEntry`, is declared separately in
`Meridian.Strategies/Services/SecurityMasterAccountingEventService.cs:74`.

The typed contract's own docstring says it "replaces the free-text 'factor schedule' term" — but the
free-text term is still what the domain model and terms schema declare, so the replacement is
half-landed.

### 9. Equity has bespoke amendment endpoints no other class has

`AmendPreferredEquityTermsAsync` and `AmendConvertibleEquityTermsAsync` sit on the generic
`SecurityMasterService` alongside the generic `AmendTermsAsync`, with matching
`PreferredEquityTermsDto` / `ConvertibleEquityTermsDto` query surfaces and dedicated event types
(`PreferredTermsAmended`, `ConvertibleTermsAmended`). There is no equivalent for a bond call
schedule or a swap leg. This is the clearest instance of the pattern the review was asked to look
for: a per-asset workaround on a surface that is otherwise generic.

### 10. Smaller items

- **`SecurityMasterProjectionCache` is a per-process `ConcurrentDictionary`** with no eviction and a
  `Snapshot()` that materializes every record. Publishing on node A does not invalidate node B, and
  `ReplaceAll` clears before repopulating (a reader between the two sees an empty master). Migration
  025's own rationale cites "horizontal scale-out"; this cache has not followed.
- **`IUflProjectionRebuilder` ignores its `assetClass` argument** and does a full shared replay
  (`UflProjectionRebuilder.cs:34`). Documented as Phase-0 and accepted in the plan, but the
  signature promises scoping it does not deliver.
- **`SecurityAssetPackRegistry` is 802 lines of string lists** — term names, counterparty roles,
  date names, lifecycle events, validation rules — described as "registry metadata for introducing
  asset packs without changing core ledger contracts". Nothing enforces it. It reads as
  documentation shaped like code, which invites the assumption that it is a working extension point.
- **Amortization is straight-line only.** `FaceValueLot.AmortizedBasisAsOf` applies day-count-weighted
  straight-line premium/discount amortization, and its docstring notes the cost-basis relief and
  ledger amortization engines use the same method. `BondAmortizationMethod.ConstantYield` exists in
  `Contracts/FixedIncome/BondReferenceDtos.cs:21` and `SecurityTermModules.fs:426`, but no effective-
  interest implementation was found. US GAAP (ASC 310-20) requires effective-interest for most
  premium amortization; straight-line is an immaterial-difference accommodation.
- **`securities` holds a single current row** per security. Valid-time term history — "what was this
  bond's coupon effective 2024-06-30", as opposed to "what did we believe on 2024-06-30" — is only
  reachable by replaying the event stream. Identifiers are properly effective-dated; terms are not.

---

## Missing or Incomplete Subsystems Blocking New Asset Classes

| Subsystem | State | Blocks |
| --- | --- | --- |
| Codec generation from `SecurityAssetTermsSchema` | Table exists; both codec sides hand-written | Every new class needs two hand-edited codec arms that only tests can catch drifting |
| `SecurityKind.CustomAsset` domain case | Absent — collapses to `OtherSecurity` | Profile-backed classes cannot be amended without losing the profile envelope |
| Per-field provenance persistence | Type exists, no storage | Multi-vendor golden record, conflict-winner lineage, vendor scorecards |
| Typed amendment path from the workbench | Overlay only, by documented design | Operator corrections reaching pricing/ledger/NAV |
| Generic corporate-action payload envelope | Wide table, 8 columns / 18 types | Tender offers, forks, returns of capital, paydowns carrying their own economics |
| Effective-interest amortization | Enum only | GAAP-compliant premium amortization for material portfolios |
| Bond principal / step / inflation schedules | Absent | Sinking funds, step-rate, TIPS — already classifiable, not computable |
| Asset-class-scoped projection replay | Argument ignored | Bounded rebuild cost as class count grows |
| Distributed projection cache invalidation | Per-process only | Multi-node deployment coherence |
| Relational projections for 15 of 26 classes | Declared gap, test-guarded | Any query path that needs typed columns for private/alternative assets |

---

## Top 5 Refactoring Priorities

> **Implementation status (2026-08-12):** all five priorities below have landed a first slice on
> this branch. (1) `SecurityAssetTermsSchemaRoundTripTests` now walks every declared asset class
> with a fully-populated payload and asserts the F#→C#→F# codec loop is byte-stable and that the
> serialized field set equals the declared schema exactly — the guard immediately surfaced and
> fixed two live drifts (equity `votingRightsCat` was never serialized; `EquityClassification.Other`
> values failed every read). (2) `SecurityKind.CustomAsset` is a first-class case carrying the
> profile envelope plus the verbatim document, and amend/deactivate now refuse records whose stored
> asset class the node cannot round-trip. (3) Migration 027 adds `security_field_provenance`;
> conflict resolution writes the winning attribution in the same transaction that closes the
> conflict, and operator field edits record overlay lineage under a distinct origin.
> (4) `SecurityAssetTermsFieldEditValidator` anchors `assetSpecificTerms.*` workbench edits to the
> declared schema (key must exist, value must coerce to the declared type). (5) `BondTerms` gained a
> typed `principalSchedule` and `StructuredCreditTerms` a typed dated `factorScheduleEntries` that
> the structured cash-flow resolver now consumes. Remaining follow-ups: full typed-amendment publish
> flow (complete-event emission), step/inflation coupon structures, and the canonical-home ruling
> for MBS/ABS/CLO.

**1. Generate the codecs from `SecurityAssetTermsSchema`, or assert them against it.**
The table already names every field, type, requiredness, and alias. Make the F# serializer and the
C# `ToSecurityKind` either source-generated from it or covered by a round-trip test that walks
`SecurityAssetTermsSchema.AssetClasses` and asserts every declared field survives
serialize → deserialize → serialize for a synthesized record. This closes the drift class that
already caused the bond-coupon null-column bug, and it is the single change that most reduces the
cost of every subsequent asset class. *Highest leverage, lowest risk.*

**2. Give `CustomAsset` a real domain case, and stop amend-after-degrade.**
Add `SecurityKind.CustomAsset of CustomAssetTerms` (profile id, version, `profileFields` as an
opaque document, approval) so the extension point round-trips. Separately, make
`AmendTermsAsync` refuse — or preserve verbatim — when the current record deserialized via the
unknown-class fallback, so a node cannot silently rewrite terms it could not parse. Read tolerance
should not imply write tolerance.

**3. Persist field-level provenance and write conflict winners back onto the record.**
Add a `security_field_provenance` table keyed `(security_id, field_path)` carrying source system,
as-of, actor, and confidence; have conflict resolution write the winning attribution as part of the
same transaction that closes the conflict. Without this the golden record cannot defend its own
values in an audit, and `SecurityFieldProvenance` remains a type with no data behind it.

**4. Build a typed amendment path beside the overlay.**
Keep the overlay for annotations — the D2 rationale is sound. Add a schema-validated field-edit that,
on publish, emits a **complete** economic-definition event (current definition + the one typed
field change) rather than a partial payload, so replay stays correct. Validate `fieldPath` against
`SecurityAssetTermsSchema` for the record's asset class and coerce `newValue` to the declared type
at submit time. This turns the existing, well-built approval lifecycle into a correction workflow
instead of an annotation workflow.

**5. Deepen fixed-income terms to match the subclass taxonomy, and pick one home per instrument.**
Extend `BondCouponStructure` with a step schedule and an inflation-linked case, and add a principal
schedule to `BondTerms` (reuse `PrincipalPaymentEntry`, already proven on `DirectLoanTerms`).
Promote `StructuredCreditTerms.FactorSchedule` from `string option` to
`StructuredFactorScheduleEntry list`, retiring the duplicate `SecurityFactorScheduleEntry`. Then
document one canonical modeling route for MBS/ABS/CLO and make the validators reject the others, so
the partition is enforced rather than conventional.

*Deferred but worth tracking:* generic corporate-action payload envelope (item 7);
effective-interest amortization (item 10); distributed projection-cache invalidation (item 10).

---

## Method

Reviewed `src/Meridian.FSharp/Domain/SecurityMaster*.fs`, `src/Meridian.FSharp/Interop.SecurityMaster.fs`,
`src/Meridian.Contracts/SecurityMaster/` (47 files), `src/Meridian.Application/SecurityMaster/` (53 files
plus `Validation/`, `Rebuild/`, `CorporateActions/`, `CashFlow/`), `src/Meridian.Storage/SecurityMaster/`
(43 files plus 26 migrations), `src/Meridian.ReferenceData/SecurityMaster/`, `src/Meridian.Instruments/`
projection services, `src/Meridian.Ui/dashboard/src/` Security Master screens and the passport editor,
`tests/Meridian.Tests/SecurityMaster/` (65 files), and `docs/plans/security-master-passport-workbench.md`.

No code was changed. No tests were run — this review makes no behavioral claims requiring execution.
