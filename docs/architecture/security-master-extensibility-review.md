# Security Master Architecture Review — Institutional Extensibility

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-20 (verification pass; prior pass 2026-08-14; original review 2026-08-12)
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
> [Verification pass — 2026-08-14](#verification-pass--2026-08-14) for the open list and
> re-ranked priorities as of that date.
>
> **Verification pass, 2026-08-20.** Re-read against `8760cfba`. No open item closed and no new risk
> surfaced; the 2026-08-14 open list and priority order carry forward unchanged. See
> [Verification pass — 2026-08-20](#verification-pass--2026-08-20).

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

## Verification pass — 2026-08-20

Re-read against `8760cfba`, six days after the previous pass. No code was changed by this pass; no
tests were run.

### Headline

**Nothing closed, nothing regressed, nothing new.** Every finding in the
[2026-08-14 open list](#verification-pass--2026-08-14) is still open at the same file and line, and
the re-ranked priority order carries forward unchanged. The subsystem is stable, not stalled by
defect — Security Master simply took no architectural work in this window.

605 commits landed on `main` since `4b39e9da8`. Ten touched Security Master paths, and all ten are
cross-cutting repo-wide refactors that passed through the subsystem rather than changing it:
SHA-256 call sites routed onto `Sha256Digest` (`c2b3fd44`, `00d78e81`, `68ba1ab7`), `NormalizeOptional`
clones migrated to `TextPrimitives` (`73247d6e`, `6be2c34d`), the last literal schema-version writes
replaced with named constants (`9cae1b44`), and the W9-GOV-008 authorization-declaration tranche
(`089aabee`, `95166888`, `862dc32a`).

### Newly confirmed as solid

**The Security Master write surface now declares its authorization.** Tranche `089aabee` recorded
that the `/api/security-master` route group carries `RequireViewSecurityMasterPermission` at group
level, with twenty routes additionally carrying a per-route
`RequireModifySecurityMasterPermission` filter. The guards existed before; what changed is that they
are now declarative and machine-checkable rather than inferred from filter registration. This does
not alter item 9 — the bespoke equity PATCH endpoints are permission-gated but still bypass the
maker-checker gate that generic field edits go through.

### Open items re-verified at `8760cfba`

| # | Item | Evidence at this commit |
| --- | --- | --- |
| 5 | Governed edits do not reach the golden record | Still exactly two registered publish handlers — `CoverageInvalidationHandler` and `SecurityProjectionRebuildHandler` (`WorkstationServiceCollectionExtensions.cs:526,529`); neither writes an approved override into canonical terms. `OperatorOverridesDto.Values` is still `IReadOnlyDictionary<string, string>` and its docstring still reads "without amending the canonical security terms" (`OperatorOverrides.cs:26-33`) |
| 2 | Three modeling routes for MBS/ABS/CLO | No canonical-home ruling; `Bond` subclasses, `StructuredCredit`, and `CustomAsset` all remain legitimate |
| 1 | Taxonomy outruns term model | `BondCouponStructure` is still `Fixed` / `Floating` / `ZeroCoupon` (`SecurityMaster.fs:222-225`). `BondTerms.PrincipalSchedule` remains present (`:258`), so sinking-fund economics stay computable and step-rate/inflation-linked stay label-only |
| 8 | Third factor-schedule shape | `SecurityFactorScheduleEntry` is still declared in `Meridian.Strategies` (`SecurityMasterAccountingEventService.cs:89`) with ten use sites across that project's accounting-event adapter |
| 7 | Corporate actions: wide table | `corporate_actions` still carries eight typed nullable payload columns for eighteen declared event types; no JSONB payload column in migration 003 or 021 |
| 10 | Per-process projection cache | `SecurityMasterProjectionCache` is byte-for-byte unchanged: a bare `ConcurrentDictionary` with no eviction, and a `ReplaceAll` that clears before repopulating |
| 10 | Straight-line amortization only | `ConstantYield` remains an enum member with no implementation (`BondReferenceDtos.cs:21`, `SecurityTermModules.fs:426`) |
| 9 | Bespoke equity amendments | `AmendPreferredEquityTermsAsync` / `AmendConvertibleEquityTermsAsync` still sit on the generic service (`SecurityMasterService.cs:208,229`), routed from `SecurityMasterEndpoints.cs:506` |
| 4 | ~7 registries per asset class | Both codec arms still hand-written; drift stays test-caught rather than prevented |
| — | Dead override dependency | `ProviderLedgerReconciliationService` still assigns `_operatorOverridesStore` (`:49,:77`) and never reads it |
| — | Asset-class-scoped replay | `IUflProjectionRebuilder.RebuildAsync(assetClass)` still normalizes the argument, logs it, then runs the full shared replay |

### Surface counts

Unchanged and still mutually aligned: F# `SecurityKind` DU 26, `AssetClassRegistry` descriptors 26,
`SecurityAssetTermsSchema` 26, `SecurityAssetClassCatalog` 26 plus the `Unknown` default descriptor,
relational projection writers 11, migrations 28. The 26-vs-11 projection gap remains declared and
test-guarded by `IntentionallyUnprojectedAssetClasses`.

### Priorities

No change. The 2026-08-14 ranking stands: (1) close the merge path from the governed workbench to the
golden record, (2) rule on one canonical home per instrument and enforce it, (3) deepen
`BondCouponStructure`, (4) retire the third factor-schedule shape and generalize the corporate-action
envelope, (5) make the projection cache multi-node-safe.

Priority 1 is now six days older with the surrounding machinery still fully paid for. It is the only
item on the list where the remaining work is a single publish handler rather than a design decision
or a schema migration, and it is the one whose absence an institutional reviewer would notice first:
the entire Draft → Submitted → Approved → Published apparatus still governs a side table.

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

The 2026-08-20 verification pass re-read the F# domain, the Security Master contracts, application
services, stores and migrations, the workstation passport and endpoint surfaces, and the
asset-class/terms-schema guard tests, and diffed the 605 commits between `4b39e9da8` and `8760cfba`
for Security Master paths.

No code was changed. No tests were run — this review makes no behavioral claims requiring execution.
