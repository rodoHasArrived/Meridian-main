# Security Master Architecture Review — Institutional Extensibility

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-24 (verification pass; earlier passes 2026-08-14, 2026-08-12)
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
closed 26-case discriminated union whose economics are shallower than the taxonomy it advertises,
and adding an asset class currently requires coordinated hand-edits across roughly seven
independent registries. The governed edit workflow governs an *annotation overlay*, not the golden
record.

Nothing here is a correctness emergency. The risks are extensibility and institutional-completeness
risks that compound as new asset classes land.

> **Verification pass, 2026-08-14.** Re-read against current source at `4b39e9da8`. The findings
> below stand as written except where a **Status (2026-08-14)** note says otherwise; four of the ten
> risk items have since closed or materially narrowed. See
> [Verification pass — 2026-08-14](#verification-pass--2026-08-14) for that pass's open list.
>
> **Verification pass, 2026-08-24.** Re-read against `9ed072df` (416 commits later). Nothing on the
> 2026-08-14 open list closed in that window; two new findings were added, both concrete defects
> rather than extensibility risks. See [Verification pass — 2026-08-24](#verification-pass--2026-08-24)
> for the current open list and re-ranked priorities.

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

`BondSubclass` declares 24 cases — `SinkingFund`, `StepRate`, `FixedToFloat`,
`InflationLinked`, `Vrdn`, `AuctionRate`, `Cmo`, `Clo`, `PrincipalOnly`, `InterestOnly`,
`InverseInterestOnly` and more (`SecurityMaster.fs:174-219`). But `BondCouponStructure` has exactly
three cases — `Fixed`, `Floating`, `ZeroCoupon` (`:222-225`) — and `BondTerms` has no principal
schedule, no step schedule, and no inflation index ratio.

A `StepRate` bond can be classified but its coupon schedule cannot be represented. A `SinkingFund`
bond cannot carry its sink schedule, even though `DirectLoanTerms` already has
`PrincipalSchedule: PrincipalPaymentEntry list`. An `InflationLinked` bond has nowhere to put an
index ratio. The subclass becomes a label that downstream cashflow, amortization, and NAV math
cannot act on.

> **Status (2026-08-14): partially closed.** `BondTerms.PrincipalSchedule: PrincipalPaymentEntry list`
> now exists (`SecurityMaster.fs:257`), is declared in `SecurityAssetTermsSchema` as
> `principalSchedule`, and is read by `StructuredCashFlowTermsResolver.ReadPrincipalSchedule` — so
> sinking-fund and amortizing bonds are now representable and computable. `BondCouponStructure`
> remains three cases (`Fixed` / `Floating` / `ZeroCoupon`), so **step-rate and inflation-linked
> bonds are still classifiable but not computable**. Multiple `BondSubclass` cases still have no
> term data that distinguishes them economically.

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

> **Status (2026-08-14): closed.** `SecurityKind.CustomAsset of CustomAssetTerms` is a first-class DU
> case (`SecurityMaster.fs:566`), the profile envelope round-trips through both codec sides, and
> amend/deactivate refuse records whose stored asset class the node cannot round-trip. All four
> class-count surfaces now agree at 26 (F# DU, `AssetClassRegistry`, `SecurityAssetTermsSchema`,
> `SecurityAssetClassCatalog` — the catalog's 27th entry is the `Unknown` default descriptor).

### 4. Adding an asset class touches ~7 registries

A new class must be added to: the F# `SecurityKind` DU **and** `AssetClassRegistry.keyOf` **and**
`descriptors`; `SecurityAssetClassCatalog`; `SecurityAssetTermsSchema`; `SecurityKindMapping`;
`AssetClassValidatorRegistry`; `SecurityMasterOperationalReadinessService`; the F# interop
serializer (`Interop.SecurityMaster.fs`); the C# deserializer (`SecurityMasterMapping.ToSecurityKind`);
and optionally a projection interface + Postgres store + migration + DI registration.

The count of declared classes already differs by surface: F# DU 25, catalog 26, terms schema 26,
kind mapping 26, projection writers 11 (*as of 2026-08-12; the DU/catalog/schema surfaces now all
read 26 — see below*). Tests lock catalog↔registry and catalog↔terms-schema, but
nothing locks the *codec* surfaces — and `SecurityAssetTermsSchema`'s own docstring is explicit
that the field table was "hand-maintained three times … which let them silently drift (e.g. the
projection store reading a nested `coupon` object the serializer never wrote, so bond coupon columns
landed null)". The schema table is a drift **detector**; it is not yet a drift **eliminator**,
because both codec sides are still hand-written.

> **Status (2026-08-14): narrowed, not closed.** `SecurityAssetTermsSchemaRoundTripTests` (558 lines)
> walks every entry in `SecurityAssetTermsSchema.AssetClasses` and asserts byte-stable round-trip for
> all 26 classes. Non-`CustomAsset` classes must match the declared field set exactly; `CustomAsset`
> must preserve its required envelope while allowing dynamic profile keys. The codecs are locked to
> the schema table. **The codecs are still two hand-written arms per class** — the guard now catches
> drift at commit time rather than in production, but adding an asset class still means editing both
> sides by hand plus the ~7 registries above.

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

> **Status (2026-08-14): first bullet closed; second and third still open — this is now the single
> largest institutional gap.** `SecurityAssetTermsFieldEditValidator` anchors every
> `assetSpecificTerms.*` edit to the declared schema for the record's asset class (key must exist or
> resolve through a declared alias, value must coerce to the declared type, aliases collapse to one
> canonical persisted path). But `OperatorOverridesDto.Values` is still
> `IReadOnlyDictionary<string, string>` and its docstring still reads "*without amending the
> canonical security terms*". The two registered `ISecurityMasterRevisionPublishedHandler`
> implementations are `CoverageInvalidationHandler` and `SecurityProjectionRebuildHandler` — neither
> writes an approved override back into the canonical terms. An approved coupon correction still has
> no effect on cash-flow projection, amortization, pricing, or NAV.
>
> Related: `ProviderLedgerReconciliationService` injects `IOperatorOverridesStore` and never reads it
> (`ProviderLedgerReconciliationService.cs:48,63,76`) — a dead dependency that reads as an
> override-aware reconciliation path but is not one.

### 6. Provenance is record-level; field-level attribution is synthesized

`Provenance` carries one `(sourceSystem, sourceRecordId, asOf, updatedBy, reason)` per record.
`SecurityFieldProvenance` exists but is *derived* by projecting record-level provenance onto a field
path via `ForField(...)`, with confidence null unless a caller supplies one
(`SecurityMasterProvenance.cs:37-38`). Nothing persists per-field attribution.

For a golden record assembled from multiple vendors this is the central missing fact: after conflict
resolution picks a winner per field, the system cannot durably answer "which vendor supplied this
maturity, as of when, at what confidence". `security_master_conflicts` records the resolution
event, but the resolved field's provenance is not written back onto the record.

> **Status (2026-08-14): closed.** Migration 027 adds `security_field_provenance`; migration 028 adds
> versioned attribution. `SecurityFieldProvenanceRecord` is keyed `(security_id, field_path, origin)`
> with a typed origin vocabulary (`ConflictResolution` / `OperatorFieldEdit` / `CanonicalWrite`), an
> `OriginReference` back to the conflict or revision that asserted it, and a `SourceVersion` commit
> ordinal so a late-arriving attribution for v2 cannot overwrite v3's. Conflict resolution writes the
> winning attribution in the same transaction that closes the conflict. This is a stronger design
> than the original recommendation — the `CanonicalWrite` origin means the true incumbent is
> nameable even for fields that were never conflicted.

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

> **Status (2026-08-14): narrowed.** `StructuredCreditTerms` now carries
> `FactorScheduleEntries: FactorScheduleEntry list` alongside the retained free-text
> `FactorSchedule: string option`, declared as `factorScheduleEntries` in the terms schema and
> consumed by `StructuredCashFlowTermsResolver`'s `FactorAsOf` lookup; a `Maturity` anchor was added
> so the schedule has a production effect. **Three shapes still coexist** — the free-text legacy
> term, the domain `FactorScheduleEntry`, and the separately-declared `SecurityFactorScheduleEntry`
> in `Meridian.Strategies/Services/SecurityMasterAccountingEventService.cs:90`. The duplicate in
> `Meridian.Strategies` is now the remaining half of this item.

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

## Verification pass — 2026-08-14

Re-read against current source at `4b39e9da8`, two days and roughly four review rounds after the
original assessment. No code was changed by this pass; no tests were run.

### Closed since 2026-08-12

| # | Item | Evidence |
| --- | --- | --- |
| 3 | `CustomAsset` does not round-trip | `SecurityKind.CustomAsset` is a first-class DU case; amend refuses non-round-trippable classes |
| 6 | Field-level provenance is synthesized | Migrations 027/028; `security_field_provenance` with typed origins, origin references, and version ordering |
| — | Class counts differ by surface | F# DU, `AssetClassRegistry`, terms schema, and catalog all read 26 |
| — | Codec drift is undetected | `SecurityAssetTermsSchemaRoundTripTests` checks byte-stable round-trip for all 26 classes; exact field-set equality applies to non-`CustomAsset` classes, while `CustomAsset` preserves its required envelope |

### Narrowed but still open

| # | Item | What remains |
| --- | --- | --- |
| 1 | Taxonomy outruns term model | `BondTerms.PrincipalSchedule` landed; `BondCouponStructure` is still 3 cases — step-rate and inflation-linked remain classifiable but not computable |
| 4 | ~7 registries per asset class | Drift is now test-caught, not prevented; both codec arms are still hand-written |
| 5 | Governed edits do not reach the golden record | Field paths and types are now schema-validated; **no merge path exists** — no publish handler writes an approved override into canonical terms |
| 8 | Factor schedules exist in incompatible shapes | Typed `FactorScheduleEntries` landed and is consumed; the third shape (`SecurityFactorScheduleEntry` in `Meridian.Strategies`) is still duplicated |

### Unchanged and open

| # | Item | Current state |
| --- | --- | --- |
| 2 | Three modeling routes for MBS/ABS/CLO | No canonical-home ruling; `Bond` subclasses, `StructuredCredit`, and `CustomAsset` all remain legitimate |
| 7 | Corporate actions: wide table, per-event-type columns | Migration 021 added four more nullable columns; 8 typed payload columns for 18 declared event types, no JSONB envelope |
| 9 | Equity has bespoke amendment endpoints | `PATCH` on preferred/convertible equity terms routes straight to `ISecurityMasterService.Amend…`, bypassing the workbench Draft→Submitted→Approved→Published gate that every generic field edit goes through. Permission-gated and rate-limited, but no maker-checker. No equivalent exists for a bond call schedule or swap leg |
| 10 | Straight-line amortization only | `BondAmortizationMethod.ConstantYield` remains an enum member with no implementation |
| 10 | Per-process projection cache | `SecurityMasterProjectionCache` is still a `ConcurrentDictionary` with no eviction; `ReplaceAll` still clears before repopulating, so a reader between the two sees an empty master |
| 10 | Valid-time term history | `securities` still holds one current row; term history is reachable only by event replay. Identifiers are effective-dated, terms are not |
| — | Relational projections | 11 asset projection stores for 26 classes; the 15 uncovered are the private/alternative classes central to fund operations. Declared and test-guarded via `IntentionallyUnprojectedAssetClasses` |

### Re-ranked priorities

The original priority order was right for its date. With (2) `CustomAsset`, (3) field provenance, and
the round-trip guard from (1) landed, the ranking shifts:

**1. Close the merge path from the governed workbench to the golden record.**
Everything else about this workflow is now built — durable revision lifecycle, independent-reviewer
gate, period-aware restatement resolution, schema-validated field paths, typed value coercion,
provenance lineage under a distinct origin. The one missing link is a publish handler that emits a
*complete* economic-definition event (current definition plus the one typed field change) so replay
stays correct. Until that lands, the entire approval apparatus governs a side table, and an approved
correction to a coupon rate still cannot reach NAV. *Highest institutional value; the surrounding
machinery is already paid for.*

**2. Rule on one canonical home per instrument, and enforce it.**
Item 2 is the only finding that makes downstream reporting, risk, and reconciliation unable to assume
a stable partition. It needs a decision, not an implementation — then validators that reject the
other routes. *Cheapest to decide, compounding cost to defer.*

**3. Deepen `BondCouponStructure` to match the subclass taxonomy.**
Add step-schedule and inflation-linked cases. The principal-schedule slice proved the pattern and the
round-trip guard now covers the codec cost. Ten `BondSubclass` members are still labels.

**4. Retire the third factor-schedule shape and generalize the corporate-action envelope.**
Collapse `SecurityFactorScheduleEntry` onto the domain `FactorScheduleEntry`. Separately, move
corporate-action economics to a JSONB payload keyed by event type — eight columns for eighteen types
means every new type is another nullable column, and six declared types already have none.

**5. Make the projection cache multi-node-safe.**
Per-process with no invalidation, and a clear-then-fill `ReplaceAll` that exposes an empty master to
concurrent readers. Migration 025 moved the conflict and revision stores off process-local memory
specifically for scale-out; this cache did not follow.

*Deferred but worth tracking:* effective-interest amortization (GAAP materiality question, not an
architecture question); relational projections for the private/alternative classes; valid-time term
history; asset-class-scoped projection replay.

---

## Verification pass — 2026-08-24

Re-read against `9ed072df`, ten days and 416 commits after the previous pass. No code was changed by
this pass; no tests were run. Every claim below cites the file and line it was read from.

**Headline:** the write-side, codec, provenance, and governance layers held up — none regressed, and
the 2026-08-14 verdict ("structurally sound, unusually well governed, not yet uniformly extensible")
still stands. But **no item on the 2026-08-14 open list closed in this window**, and two concrete
defects surfaced that had not been named as such before: `InvestmentFund` fails validation outright,
and CSV import covers 9 of 26 asset classes off a private hard-coded table.

### Closed since 2026-08-14

| # | Item | Evidence |
| --- | --- | --- |
| — | Profile-backed amend silently discarded field patches | `GetProfileBackedAssetSpecificTermsOverride` no longer restores the previous envelope when a non-envelope patch arrives — it throws with actionable guidance ("submit the full envelope with the updated profileFields, or edit individual fields through the workbench field-edit route") (`SecurityMasterService.cs:921-949`). The write-side data-loss path that pass named is gone. |
| — | Repinning a profile-backed record kept the stale class | An amendment now resolves its class from the SUBMITTED envelope, not the stored projection, so a repin routes into the resolved class's validators exactly as an identical create would (`SecurityMasterService.cs:88-107, 894-919`). |

### New findings

#### N1 — `InvestmentFund` is declared everywhere and wired nowhere; every such record fails validation

`InvestmentFund` — mutual funds, ETFs, hedge funds, REITs, closed-end funds — is a first-class F# DU
case (`SecurityMaster.fs:563`), has an `AssetClassRegistry` descriptor (`:648`), a catalog entry
(`SecurityAssetClassCatalog.cs:369`), a terms-schema entry (`SecurityAssetTermsSchema.cs:333`), a
codec arm on both sides (`Interop.SecurityMaster.fs:387`, `SecurityMasterMapping.cs:403`), and a
round-trip test payload (`SecurityAssetTermsSchemaRoundTripTests.cs:322`).

It has **no validator**. `DefaultAssetClassValidators.Create` registers 24 `JsonAssetClassValidator`
classes plus profile validators for `OtherSecurity` and `CustomAsset` — 25 of the catalog's 26 real
classes (`AssetClassValidatorRegistry.cs:64-70`). `InvestmentFund` is the omission. Any record
carrying that class therefore falls to the registry's else-branch and raises Error-severity
`SM_ASSET_CLASS_UNSUPPORTED` on every validation run (`SecurityValidationService.cs:116-129`),
which is what governed run, ledger, and report-pack use gate on.

The gap is structural, not incidental: there are coverage guards binding the catalog to the terms
schema (`SecurityAssetTermsSchemaTests.Schema_DeclaresEveryCatalogAssetClass`) and to relational
projections (`IntentionallyUnprojectedAssetClasses`), but **none binding the catalog to validators**.
Nothing fails when a class is added without one.

It is also unreachable through every other route: `SecurityKindMapping` maps it to an empty
`InstrumentType` list (`SecurityKindMapping.cs:49`), so the market-data bridge has no pricing route
for it; no `SecurityAssetPackRegistry` pack claims the string; it is in
`IntentionallyUnprojectedAssetClasses`; and it is absent from the CSV importer (N2). Funds are a
core fund-operations asset class for this product, and the one class that is declared but unusable
is a fund class.

*This was raised as R7 in `docs/engineering/security-master-architecture-audit-2026-08-13.md` and
ranked its fifth priority ("cheapest high-value fix in the list"). Eleven days and 416 commits later
it is unchanged.*

#### N2 — CSV import covers 9 of 26 asset classes off a private table bound to nothing

`SecurityMasterCsvParser.AssetClassMapping` is a private 9-entry dictionary — Equity, Option, Future,
Bond, Crypto/CryptoCurrency, Commodity, CFD, Warrant (`SecurityMasterCsvParser.cs:13-24`). Rows
naming any other catalog class are rejected with "Unknown AssetClass". Every fund-operations class
the product differentiates on — `Deposit`, `MoneyMarketFund`, `CertificateOfDeposit`,
`CommercialPaper`, `TreasuryBill`, `Repo`, `CashSweep`, `DirectLoan`, `StructuredCredit`,
`PrivateFundInterest`, `PrivateCompanyEquity`, `RealEstateHolding`, `CommitmentGuarantee`,
`InvestmentFund`, `Swap`, `FxSpot` — is unimportable by CSV.

The table is not derived from `SecurityAssetClassCatalog` and no test binds it there, so this is the
same failure mode as N1: adding an asset class leaves a bulk-onboarding path silently behind, and
nothing reports it. This is the eleventh registry a new class must be hand-edited into.

#### N3 — The profile-backed compensating patch grew rather than generalized

`IsProfileBackedCustomAsset` still hard-codes seven asset-class strings
(`SecurityMasterService.cs:956-968`), and the layer around it has since gained a five-entry
`KnownProfileAssetClasses` profile→class map (`:980-988`) and an `AssetClassMetadataKeywords`
table (`:991-999`), plus `TryResolveProfileBackedAlternativeAssetClass`,
`GetProfileBackedAssetClassOverride` (three overloads), and `EnsureProfileBackedTermsAreCatalogValid`.

Each addition fixed a real bug — the correctness of this path is materially better than on 08-14 —
but the shape is unchanged: the aggregate still drops the profile envelope, and an application-layer
override patches it back on afterwards (`CreateProjectionFromResult` applies `assetClassOverride`
and `assetSpecificTermsOverride` to the projection the domain returned, `:860-875`). A new
profile-backed class that is not added to all three hard-coded tables is still silently misrouted.
The generalization target from the 08-13 audit (R1) stands, and the compensating layer it named is
now roughly three times larger.

### Still open, unchanged from 2026-08-14

Re-verified individually; each is as that pass described it.

| # | Item | Current evidence |
| --- | --- | --- |
| 5 | Governed edits do not reach the golden record | `OperatorOverridesDto.Values` is still `IReadOnlyDictionary<string, string>` and its docstring still reads "without amending the canonical security terms" (`OperatorOverrides.cs:25-33`). Still exactly two `ISecurityMasterRevisionPublishedHandler` implementations — `CoverageInvalidationHandler` and `SecurityProjectionRebuildHandler` — neither of which merges. The overrides endpoint still serves a side dictionary (`SecurityMasterEndpoints.cs:938-952`); no read model exposes an effective merged value. |
| 5a | Browser passport editor is still free-text | `security-passport-editor.tsx:332-336` is still two bare `Input`s with placeholders `EconomicDefinition.Coupon` and `5.125`. Note the placeholder itself names an **annotation-namespace** path: `SecurityAssetTermsFieldEditValidator` only governs `assetSpecificTerms.*` (`:19-35`), so the example the UI suggests is precisely the unvalidated case. No schema-driven form exists on the browser lane. |
| 5b | Dead override dependency | `ProviderLedgerReconciliationService` still injects and stores `IOperatorOverridesStore` without ever reading it (`ProviderLedgerReconciliationService.cs:49,64,77`). |
| 1 | Taxonomy outruns term model | `BondCouponStructure` is still `Fixed` / `Floating` / `ZeroCoupon` (`SecurityMaster.fs:222-225`). Step-rate and inflation-linked bonds remain classifiable, not computable. |
| 8 | Third factor-schedule shape | `SecurityFactorScheduleEntry` is still separately declared in `Meridian.Strategies` (`SecurityMasterAccountingEventService.cs:89`), and `SecurityMasterAccountingEventSourceAdapter` still probes both the typed `factorScheduleEntries` and the legacy array/free-text spellings across four containers (`:581-625`). |
| 4 | Registries per asset class | Now count **eleven** hand-edited surfaces, not seven: F# DU + `AssetClassRegistry` descriptors, interop serializer, `SecurityMasterMapping.ToSecurityKind`, `SecurityAssetClassCatalog`, `SecurityAssetTermsSchema`, `AssetClassValidatorRegistry`, `SecurityKindMapping`, `SecurityMasterOperationalReadinessService`, `SecurityMasterCsvParser`, the round-trip payload table, and optionally a projection + migration + store. Guards exist for three of them. |
| 2 | Three modeling routes for MBS/ABS/CLO | No canonical-home ruling. `KnownProfileAssetClasses` now routes `structured-credit-io-po` profiles to `StructuredCredit` (`SecurityMasterService.cs:982`), which narrows the *profile* route but leaves `Bond`-with-subclass and direct `StructuredCredit` both legitimate. |
| 7 | Corporate actions: wide table | Unchanged; no migration since 021 touches the payload columns (latest is 028). |
| 9 | Equity bespoke amendment endpoints | `AmendPreferredEquityTermsAsync` / `AmendConvertibleEquityTermsAsync` still sit on the generic `ISecurityMasterService` (`:7-8`) with dedicated endpoints (`SecurityMasterEndpoints.cs:519,576,1122`), bypassing the workbench maker-checker gate. No equivalent for a bond call schedule or swap leg. |
| 10 | Per-process projection cache | `ReplaceAll` still calls `_byId.Clear()` before repopulating (`SecurityMasterProjectionCache.cs:17-25`), so a concurrent reader still sees an empty master. Still no eviction, still no cross-node invalidation. |
| 10 | Straight-line amortization only | `ConstantYield` remains an enum member in two places with no implementation (`BondReferenceDtos.cs:21`, `SecurityTermModules.fs:426`). |
| 10 | `SecurityAssetPackRegistry` is prose compiled into C# | Still 802 lines; still consumed by exactly one caller (`SecurityMasterOperationalReadinessService`); `InferLifecycleEvent` still maps journal-template labels to lifecycle events by substring-matching English words (`:464-500`). It gates nothing a new asset class must satisfy. |
| 10 | Valid-time term history | `securities` still holds one current row; identifiers are effective-dated, terms are not. |
| — | Relational projections | Still 11 projections (migrations 005-015) for 26 classes; the 15 uncovered are still the private/alternative classes. Declared and test-guarded. |

### Re-ranked priorities

The ranking shifts because two cheap, concrete defects now outrank the deeper architectural items.

**1. Add a catalog↔validator coverage guard and give `InvestmentFund` a validator.** (N1)
A test mirroring the existing catalog↔schema and catalog↔projection guards, plus one
`JsonAssetClassValidator` entry. This removes an asset class that currently fails validation
outright, and — more importantly — makes the *next* omission fail at commit time instead of in a
governed report pack. Hours of work; highest value-per-effort in this document.

**2. Derive `SecurityMasterCsvParser.AssetClassMapping` from `SecurityAssetClassCatalog`.** (N2)
The catalog already knows every class and its `SupportsBasicCreateWorkflow` flag. Deriving the
importer's accepted set from it (with vendor spellings as an alias overlay, not the source of truth)
deletes a hand-maintained list and closes bulk onboarding for 17 classes. Same shape of fix as
priority 1, same order of effort.

**3. Close the merge path from the governed workbench to the golden record.** (item 5)
Unchanged from the 2026-08-14 ranking and still the largest *institutional* gap — everything around
it is built and paid for, and an approved coupon correction still cannot reach NAV. It drops to
third only because two fixes above it are an order of magnitude cheaper, not because it got smaller.

**4. Make the profile-backed passthrough a first-class domain contract.** (N3, item 4)
The compensating-override layer in `SecurityMasterService` has tripled while staying the same shape.
Either give the domain a case that carries the profile envelope through amend intact, or make the
raw-terms passthrough the explicit typed contract — then delete the three hard-coded tables and the
post-hoc projection patching. This is the change that stops new profile-backed classes from being
silently misrouted, and it shrinks the eleven-registry count.

**5. Rule on one canonical home per instrument, and retire the third factor-schedule shape.**
(items 2 and 8) Both are decisions more than implementations. `KnownProfileAssetClasses` shows the
partition can be enforced once ruled; collapsing `SecurityFactorScheduleEntry` onto the domain
`FactorScheduleEntry` removes the last duplicate of a type that already has a typed owner.

*Deferred but worth tracking, unchanged:* generic corporate-action payload envelope; effective-interest
amortization; multi-node projection-cache invalidation; relational projections for the
private/alternative classes; valid-time term history; the standing question of whether
`SecurityAssetPackRegistry` becomes a registry that gates admission or moves to `docs/`.

---

## Method

Reviewed `src/Meridian.FSharp/Domain/SecurityMaster*.fs`, `src/Meridian.FSharp/Interop.SecurityMaster.fs`,
`src/Meridian.Contracts/SecurityMaster/` (47 files), `src/Meridian.Application/SecurityMaster/` (53 files
plus `Validation/`, `Rebuild/`, `CorporateActions/`, `CashFlow/`), `src/Meridian.Storage/SecurityMaster/`
(43 files plus 26 migrations), `src/Meridian.ReferenceData/SecurityMaster/`, `src/Meridian.Instruments/`
projection services, `src/Meridian.Ui/dashboard/src/` Security Master screens and the passport editor,
`tests/Meridian.Tests/SecurityMaster/` (65 files), and `docs/plans/security-master-passport-workbench.md`.

The 2026-08-14 verification pass re-read the F# domain and interop, the 47 `Meridian.Contracts`
Security Master contracts, the 58 `Meridian.Application` Security Master services, the 46
`Meridian.Storage` stores and 28 migrations, the workstation endpoint surfaces, and the codec
round-trip and asset-class-support test suites.

The 2026-08-24 verification pass re-read, at `9ed072df`: the F# `SecurityKind` DU, `AssetClassRegistry`,
and term records; `Interop.SecurityMaster.fs` and `SecurityMasterMapping.ToSecurityKind`;
`SecurityAssetClassCatalog`, `SecurityAssetTermsSchema`, `SecurityAssetTermsFieldEditValidator`,
`SecurityAssetPackRegistry`, `SecurityMasterProvenance`, `OperatorOverrides`, `FaceValueLot`, and
`StructuredCashFlowTermsResolver`; `SecurityMasterService` (profile-backed override layer),
`AssetClassValidatorRegistry`, `SecurityValidationService`, `SecurityMasterWorkbenchCommandService`,
`SecurityMasterCsvParser`, and the revision-published handlers; `SecurityMasterProjectionCache`,
`ISecurityFieldProvenanceStore`, and migrations 004-028; `SecurityKindMapping` and `InstrumentType`;
`SecurityMasterEndpoints` and the browser `security-passport-editor`; and the
`SecurityAssetTermsSchema*`, `SecurityAssetClassCatalog`, and `SecurityMasterMappingInterop` test
suites.

No code was changed. No tests were run — this review makes no behavioral claims requiring execution.
