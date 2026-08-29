# Security Master Architecture Review — Institutional Extensibility

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-28 (scheduled institutional-requirements pass; resolution pass 2026-08-26; scheduled institutional-requirements pass 2026-08-26; independent verification pass, post-resolution 2026-08-24; resolution pass 2026-08-24; verification pass 2026-08-14; original review 2026-08-12)
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

> **Superseded on 2026-08-28 — do not read the two sentences above as current.** They were accurate
> for the findings that pass had. The 2026-08-28 scheduled pass filed three shipped-behaviour defects
> that are correctness and access-control problems rather than extensibility ones: the desktop lane
> mutates the golden record with no authorization check (P5), the legacy preferred-terms PATCH route
> bypasses the governed-amendment gate (P1), and editing an alias erases it from earlier recorded-as-of
> views (P3b). The architectural assessment below is unaffected.

> **Verification pass, 2026-08-14.** Re-read against current source at `4b39e9da8`. The findings
> below stand as written except where a **Status (2026-08-14)** note says otherwise; four of the ten
> risk items have since closed or materially narrowed. See
> [Verification pass — 2026-08-14](#verification-pass--2026-08-14) for the current open list and
> re-ranked priorities.

> **Resolution pass, 2026-08-24.** An implementation pass addressed the open findings from the
> 2026-08-14 verification. See [Resolution pass — 2026-08-24](#resolution-pass--2026-08-24) for
> what landed per finding; **Status (2026-08-24)** notes below mark items individually. The
> remaining declared-and-deferred items are relational projections for the private/alternative
> classes and valid-time term history.
>
> **Independent verification pass, 2026-08-24 (post-resolution).** A separately-run review of the
> same subsystem, re-verified against `780aeb9e` after the resolution merged. Two findings survive
> it: no catalog-to-validator parity guard, and a CSV import path that fails for **every** asset
> class because the parser never populates the terms payloads the create path requires. See
> [Independent verification pass](#independent-verification-pass--2026-08-24-post-resolution).
>
> **Scheduled institutional-requirements pass, 2026-08-26.** Re-read against `2917848a`. The verdict
> stands; both surviving findings above are still open; six new items are filed. See
> [Scheduled institutional-requirements pass — 2026-08-26](#scheduled-institutional-requirements-pass--2026-08-26).
> Its highest-severity finding is new: `CashSweep` and `StructuredCredit` share the
> `AssetFamily.StructuredCash` label and the accounting adapter reads that label as securitized, so
> cash-sweep vehicles resolve to an asset-backed-security accounting class.
>
> **Resolution pass, 2026-08-26.** Four items from that pass are closed — the family split (N1), both
> parity guards (V1 and N3), CSV import (V2), and the prose-sniffing classification it shared with
> N2. N4, N5 and N6 stay open. See
> [Resolution pass — 2026-08-26](#resolution-pass--2026-08-26), which also records the intended
> behaviour deltas.
>
> **Scheduled institutional-requirements pass, 2026-08-28.** Re-read against `d3793290`, then
> extended under review.
>
> **The architectural verdict stands; the risk verdict above it does not, and is superseded here.**
> The design assessment is unchanged — the asset model, extension points and cross-asset seams are
> sound, and every closure claimed by the 2026-08-26 resolution was independently re-verified against
> source rather than taken on the resolution's word, and all of them hold. But the opening line
> "nothing here is a correctness emergency… the risks are extensibility and institutional-completeness
> risks" was written before this pass, and three of the six items filed below are neither: an
> authorization bypass on a configured desktop host (P5), an ungated maker-checker route (P1's legacy
> PATCH), and recorded-history loss on alias edits (P3b). Those are shipped-behaviour defects in
> correctness and access control. The 2026-08-13 wording is left in place as the record of what that
> pass concluded; read it as superseded from here, not as current.
>
> N4, N5, N6 and the three deferred items are unchanged. Six new items are filed
> (P1–P4 plus P3b and P5, which review surfaced). See
> [Scheduled institutional-requirements pass — 2026-08-28](#scheduled-institutional-requirements-pass--2026-08-28).
>
> **Its highest-severity finding (P5) is that the desktop lane mutates the golden record with no
> authorization check at all.** Every HTTP mutation route requires `ModifySecurityMaster`; the WPF
> edit, deactivate, import and trading-parameter backfill commands — the last of which amends every
> active security at once — reach the same `ISecurityMasterService` in process and check
> nothing, on a shell whose `HasPermission` fails closed for a configured host and is simply never
> called here. So an operator holding only `ViewSecurityMaster` is refused every mutation over HTTP
> and permitted every one of them through the workstation. It is filed apart from P1 because it is an
> authorization defect rather than an attribution one, and it must be fixed first: deriving the actor
> before gating the write would attach a real operator's name to changes that should have been
> refused.
>
> **The second is a live bypass of a shipped control**: the legacy
> `PATCH …/preferred-terms` route reaches `AmendPreferredEquityTermsAsync` without calling
> `RequireGovernedTermAmendmentRoute`, so a deployment that enables `RequireGovernedTermAmendments`
> to force maker-checker still has an ungated amendment path. That is one call to close, and it also
> makes the 2026-08-24 resolution's "gates all three routes uniformly" incomplete.
>
> **The third shipped-behaviour defect (P3b, priority 3) loses history today**: an alias upsert
> overwrites the existing row's `created_at`, and `RebuildRecordedAsOfAsync` filters aliases by it, so
> correcting an alias removes it from every recorded-as-of view earlier than the correction — an
> identifier recorded in January and corrected in June vanishes from the January view. Unlike the gate
> bypass it is not a one-call fix: it needs versioned or event-backed alias state, or an explicit
> narrowing of what recorded-as-of promises for aliases.
>
> The pass began on the bulk-import path and its scope widened materially under review: P1 is now a
> property of the whole `ISecurityMasterService` mutation surface rather than of import, P3 concerns
> the asset-pack registry, and P4 spans three ingest paths plus a cancellation defect class. Where
> this pass's own claims were corrected — several were — the corrections are recorded in place rather
> than silently applied.

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
>
> **Status (2026-08-24): closed.** `BondCouponStructure` gained `Step of StepCouponEntry list`
> (a dated rate schedule with `couponRateAsOf` resolution) and `InflationLinked of realRate ×
> indexName × baseIndexValue × indexRatio`, declared in the terms schema
> (`stepSchedule`, `inflationIndex`, `inflationBaseIndexValue`, `inflationIndexRatio`), wired
> through both codec sides, guarded by domain invariants (non-empty schedule, unique step dates
> within maturity, positive index values), consumed by `StructuredCashFlowTermsResolver`
> (`StepCouponSchedule` + `CouponRateAsOf`), and covered by round-trip variants. Fixing the codec
> also surfaced and closed a live drift: six declared `BondSubclass` cases (`SinkingFund`,
> `StepRate`, `FixedToFloat`, `Vrdn`, `AuctionRate`, `BankLoan`) were missing from the C#
> deserializer and degraded to `Other`, and the `Other` case re-wrapped itself one level deeper on
> every serialize pass. The securitized subclasses remain labels by design — ADR-022 rules
> `StructuredCredit` their canonical home (see finding 2).

### 2. One concept, three or four modeling routes

MBS/ABS/CLO/CMBS can be modeled as `Bond` with `BondSubclass.MortgageBacked` / `Cmo` / `Clo`, or as
`StructuredCredit`, or as `CustomAsset` with a governed profile — and the operational readiness
catalog labels `CustomAsset` as literally "MBS / ABS / CLO / CMBS / private assets"
(`SecurityMasterOperationalReadinessService.cs:155-157`). Three legitimate homes for the same
instrument means reporting, risk, and reconciliation cannot assume a stable partition.

Similarly, `MoneyMarketFund` and `InvestmentFund` overlap (both are funds; `InvestmentFundTerms`
even carries `isStableNav` for "stable-NAV money market and government liquidity funds"), and
`CashSweep` overlaps `MoneyMarketFund` for sweep vehicles.

> **Status (2026-08-24): closed by ruling + enforcement.**
> [ADR-022](../adr/022-canonical-asset-class-homes.md) rules one canonical home per instrument
> family: securitized products belong in `StructuredCredit` (the Bond validator now raises
> Error-severity `SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL` for the ten securitized subclasses;
> `CustomAsset` records with securitized classification metadata raise a Warning), stable-NAV
> vehicles belong in `MoneyMarketFund` (`SM_INVESTMENT_FUND_STABLE_NAV_NONCANONICAL`, Warning), and
> `CashSweep` models sweep programs, not the fund vehicles they sweep into. The operational
> readiness catalog no longer labels `CustomAsset` as "MBS / ABS / CLO / CMBS".

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
>
> **Status (2026-08-24): closed.** `ApprovedFieldEditCanonicalMergeHandler` (Order = 5, registered
> ahead of the projection rebuild) merges an approved `assetSpecificTerms.*` field edit into the
> canonical terms on publish by emitting a **complete** economic-definition amendment through
> `ISecurityMasterAmender` — the current definition plus the one typed field change — so replay
> stays correct and the correction reaches cash-flow projection, amortization, pricing, and NAV.
> The handler is idempotent (a retried publish detects the already-merged document and skips),
> fails the publish retryably on error, names `operator-workbench` as the amendment source (so
> per-field `CanonicalWrite` attribution records the operator as the incumbent), and best-effort
> auto-resolves the vendor-versus-operator conflict its own amendment opens — the maker-checker
> approval already adjudicated that value. Annotation-surface paths and CLEARs stay overlay-only by
> the documented D2 design. The `OperatorOverridesDto` docstring now states the two-surface
> contract. The `ProviderLedgerReconciliationService` dependency was already live again by this
> pass (it feeds override history into provider passports).

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

> **Status (2026-08-24): closed.** Migration 029 adds a generic `payload jsonb` column keyed by
> event type; `CorporateActionDto` carries it as `Payload`, the Postgres store round-trips it, and
> `CorporateActionPayloads` documents the well-known keys per column-less event type (tender
> offers, crypto forks, returns of capital, principal paydowns, option contract adjustments,
> delistings) with tolerant typed readers. The eight typed columns stay authoritative for the
> event types that declared them; a new event type never needs another nullable column.

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
>
> **Status (2026-08-24): closed.** The `Meridian.Strategies` type turned out not to be a third
> schedule shape but a per-period factor *observation* (prior→current pair with source and evidence
> lineage) that the source adapter derives FROM the canonical typed schedule. It is renamed to
> `SecurityFactorObservation` with a docstring stating exactly that relationship, so the canonical
> dated factor point exists in one shape (`FactorScheduleEntry` / `StructuredFactorScheduleEntry`)
> and the observation type no longer reads as a competing schedule term.

### 9. Equity has bespoke amendment endpoints no other class has

`AmendPreferredEquityTermsAsync` and `AmendConvertibleEquityTermsAsync` sit on the generic
`SecurityMasterService` alongside the generic `AmendTermsAsync`, with matching
`PreferredEquityTermsDto` / `ConvertibleEquityTermsDto` query surfaces and dedicated event types
(`PreferredTermsAmended`, `ConvertibleTermsAmended`). There is no equivalent for a bond call
schedule or a swap leg. This is the clearest instance of the pattern the review was asked to look
for: a per-asset workaround on a surface that is otherwise generic.

> **Status (2026-08-24): governance closed; asymmetry accepted.**
> `SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments` now gates all three direct
> term-amendment routes uniformly — the generic amend endpoint and both bespoke equity `PATCH`es —
> refusing them (HTTP 403 with workbench guidance) when a deployment requires maker-checker, so the
> bespoke routes can no longer bypass a gate the generic route enforces. With the canonical-merge
> publish handler landed (finding 5), the workbench field-edit path is a full typed alternative for
> single-field corrections, so the direct routes are an ingest/ops surface, not the only correction
> path. The bespoke endpoints themselves are retained: they are the whole-block replacement surface
> for nested preferred/convertible structures the flat field-edit path does not model.

### 10. Smaller items

> **Status (2026-08-24):** the cache's clear-then-fill window is closed (`ReplaceAll` now swaps a
> fully-populated dictionary atomically; a `Remove` eviction and a deactivate-path cache upsert were
> added, and `ProjectionCacheRefreshMinutes` gives multi-node deployments a bounded-staleness
> periodic re-warm). `IUflProjectionRebuilder` now honors its `assetClass` argument
> (`SecurityMasterRebuildOrchestrator.RebuildAssetClassAsync` re-folds only that class's
> securities). Effective-interest amortization is implemented
> (`FaceValueLot.ConstantYieldAmortizedBasisAsOf`, with method routing over
> `BondAmortizationMethod`). `SecurityAssetPackRegistry` is now enforced: `ValidateAll()` and
> full catalog-class coverage are test-locked (the lock immediately caught five catalog classes no
> pack claimed). Valid-time term history remains reachable only by event replay — still declared
> and deferred.

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

## Resolution pass — 2026-08-24

An implementation pass addressed the open findings from the 2026-08-14 verification. Per-finding
**Status (2026-08-24)** notes above carry the detail; the summary:

### Closed this pass

| # | Item | Resolution |
| --- | --- | --- |
| 5 | Governed edits do not reach the golden record | `ApprovedFieldEditCanonicalMergeHandler` (publish fan-out Order = 5) merges approved `assetSpecificTerms.*` edits into canonical terms as a complete economic-definition amendment; idempotent, retry-safe, provenance-recorded, with best-effort auto-resolution of the merge's own operator-vs-vendor conflict |
| 2 | Three modeling routes for MBS/ABS/CLO | [ADR-022](../adr/022-canonical-asset-class-homes.md): `StructuredCredit` is the canonical securitized home, enforced by `SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL` (Error) and `SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL` / `SM_INVESTMENT_FUND_STABLE_NAV_NONCANONICAL` (Warnings) |
| 1 | Taxonomy outruns the term model | `BondCouponStructure.Step` (dated rate schedule + `couponRateAsOf`) and `BondCouponStructure.InflationLinked` (real rate, index, base index value, index ratio) landed through schema, both codecs, invariants, resolver, and round-trip guards; the fix also closed a live subclass-codec drift (six declared subclasses missing from the C# reader, `Other` re-wrapping per pass) |
| 8 | Factor schedules exist in incompatible shapes | The `Meridian.Strategies` type renamed to `SecurityFactorObservation` and documented as a per-period observation derived FROM the canonical typed schedule — one canonical dated-factor shape remains |
| 7 | Corporate actions: wide table, per-event-type columns | Migration 029 adds the generic `payload jsonb` envelope; `CorporateActionDto.Payload` round-trips it and `CorporateActionPayloads` documents well-known keys with tolerant readers |
| 9 | Equity has bespoke amendment endpoints | `RequireGovernedTermAmendments` gates the generic and bespoke direct amendment routes uniformly behind the workbench maker-checker path; the bespoke endpoints stay as the whole-block replacement surface |
| 10 | Per-process projection cache | Atomic-swap `ReplaceAll` (no empty-master window), eviction, deactivate-path coherence, and a `ProjectionCacheRefreshMinutes` bounded-staleness re-warm for multi-node deployments |
| 10 | Straight-line amortization only | `FaceValueLot.ConstantYieldAmortizedBasisAsOf` implements the effective-interest method, with routing across `BondAmortizationMethod` (NoAmortization, AuctionRate, StraightLine fallbacks) |
| — | Asset-class-scoped projection replay | `RebuildAssetClassAsync` re-folds only the requested class; `IUflProjectionRebuilder` now delivers what its signature promises |
| — | `SecurityAssetPackRegistry` unenforced | `ValidateAll()` plus full catalog-class coverage are test-locked; five previously unclaimed catalog classes (`Commodity`, `CryptoCurrency`, `Cfd`, `Warrant`, `InvestmentFund`) are now claimed by packs |
| — | Overlay contract docstring | `OperatorOverridesDto` now states the two-surface contract (annotations stay overlay-only; approved asset-terms corrections merge into canonical terms on publish) |

### Still declared and deferred

| Item | Posture |
| --- | --- |
| Relational projections for the private/alternative classes | Declared and test-guarded via `IntentionallyUnprojectedAssetClasses`; unchanged |
| Valid-time term history (`securities` holds one current row) | Terms remain reachable as-of only via event replay; identifiers stay effective-dated; unchanged |
| Codec generation from `SecurityAssetTermsSchema` | Both codec arms remain hand-written; the round-trip guard remains the commit-time drift eliminator (and caught this pass's subclass drift) |

---

## Independent verification pass — 2026-08-24 (post-resolution)

A second, independently-run review of the same subsystem landed the same day as the resolution pass
above, reading `9ed072df` before the resolution merged. Most of what it found the resolution pass
has since closed; this section records only the findings **re-verified against the post-resolution
tree** (`780aeb9e`). No code was changed by this pass and no tests were run.

For the record, the pass independently reached the same verdict as the 2026-08-14 review — the
identity, event-sourcing, codec, provenance, and governance layers hold up under an institutional
read — and independently identified findings 5, 2, 8 and the pack-registry item that the resolution
pass had already fixed. Two of its findings survive.

V2 below was materially sharpened by automated review on the pull request that recorded this
section: the pass had originally described CSV import as *narrow* (eight of twenty-six classes
accepted), and review correctly identified that it is in fact *entirely broken*, and that the
remediation the pass proposed would have made things worse. Both corrections were verified against
source before being adopted, and the finding is restated accordingly.

### V1 — There is still no catalog-to-validator coverage guard

The resolution pass gave `InvestmentFund` a validator (`AssetClassValidatorRegistry.cs:286`),
closing the instance: before it, every mutual-fund/ETF/REIT record fell to the registry's
else-branch and raised Error-severity `SM_ASSET_CLASS_UNSUPPORTED`
(`SecurityValidationService.cs:116-129`), which governed run, ledger, and report-pack use gate on.

The **class of defect is still open**. `SecurityAssetClassCatalogTests` now locks the catalog to
four separate surfaces — the F# `AssetClassRegistry`
(`Catalog_StaysInLockstepWithTheFSharpAssetClassRegistry`), the pack registry
(`AssetPackRegistry_ValidatesCleanly_AndCoversEveryCatalogAssetClass`), the terms schema
(`SecurityAssetTermsSchemaTests.Schema_DeclaresEveryCatalogAssetClass`), and relational projections
(`IntentionallyUnprojectedAssetClasses`) — but **not** the validator registry.
`AssetClassValidatorRegistry` appears in the test suite only as a constructed dependency, never
asserted for catalog parity; the sole reference to `SupportedAssetClasses` outside the registry is
in `CorporateActionTypeDescriptorCatalogTests:113`, which is not a parity guard.

So the next catalog class added without a validator fails the same way `InvestmentFund` did, and
nothing fails at commit time to say so. The fix is a fifth guard mirroring the four that exist —
the cheapest durable item in this document.

### V2 — CSV import is broken for every asset class, not merely narrow

**The parser discards the columns the create path requires.** `SecurityMasterCsvParser.ParseRow`
constructs its `CreateSecurityRequest` with `CommonTerms` and `AssetSpecificTerms` both hardcoded to
the empty document `{}` (`SecurityMasterCsvParser.cs:143-155`) — the `Name` and `Currency` columns it
just parsed are never carried into the payload. `SecurityMasterImportService` then hands that request
to `SecurityMasterService.CreateAsync` (`:164`), whose command mapping calls
`ToCommonTerms(request.CommonTerms)`, and that requires both fields:
`GetRequiredString(json, "displayName")` and `GetRequiredString(json, "currency")`
(`SecurityMasterMapping.cs:214-217`), each throwing `Missing required string '<name>'` when absent
(`:698-701`).

So **every CSV row fails at create time, for every asset class** — including a well-formed Equity row
naming one of the eight accepted spellings. The empty `assetSpecificTerms` compounds it: classes with
required term fields (Option's `underlyingId` / `putCall` / `strike` / `expiry`, for instance) would
still fail strict write-mode mapping even after the common-terms payload is fixed. CSV import is a
dead path, not a narrow one.

**The accepted-class table is a second, downstream gap.**
`SecurityMasterCsvParser.AssetClassMapping` is a private dictionary of nine spellings resolving to
eight distinct classes — Equity, Option, Future, Bond, CryptoCurrency, Commodity, Cfd, Warrant
(`:13-24`) — not derived from `SecurityAssetClassCatalog` and bound by no test. Rows naming any other
catalog class are rejected with "Unknown AssetClass" before the outage above is even reached. That
is the same shape as V1 (a registry governing asset-class behavior with no catalog parity guard), and
it is worth closing — but only after the payload path works, since widening the table alone changes
nothing.

Note for whoever picks this up: **do not derive the accepted set from
`SecurityAssetClassCatalog.SupportsBasicCreateWorkflow`.** That flag describes the workstation's basic
create flow and is true for exactly two classes, `Equity` and `CustomAsset`; deriving from it would
drop the seven other spellings CSV accepts today and admit `CustomAsset`, whose required profile
envelope an empty terms payload cannot satisfy. Closing this properly needs a CSV-specific capability
on the catalog (or another invariant that actually models importability), plus a parser that
populates the terms payloads it already parses.

### Noted, not re-filed

The application-layer compensating-override layer for profile-backed records
(`IsProfileBackedCustomAsset`'s seven hard-coded asset-class strings, `KnownProfileAssetClasses`,
`AssetClassMetadataKeywords`, and the post-hoc `assetClassOverride` / `assetSpecificTermsOverride`
patching in `CreateProjectionFromResult`) remains as finding 4 describes it: the aggregate drops the
profile envelope and the service patches it back afterwards. Its correctness has improved markedly —
a non-envelope patch now refuses rather than silently discarding the requested values
(`SecurityMasterService.cs:921-949`), and a repin resolves its class from the submitted envelope
(`:88-107, 894-919`) — but the shape is unchanged and the hard-coded tables have grown. It stays
part of the standing eleven-registry cost in finding 4 rather than a separate item.

---

## Scheduled institutional-requirements pass — 2026-08-26

Re-read against `2917848a` (254 commits after `780aeb9e`, of which seven touch Security Master).
The verdict above stands unchanged. The prior passes' two open verification findings were re-checked
against current source and **both remain open**; five findings below are new to this document. No
code was changed by this pass and no tests were run — every claim is a source read.

### Re-verified as still open

| # | Item | Evidence at `2917848a` |
| --- | --- | --- |
| V1 | No catalog-to-validator coverage guard | `AssetClassValidatorRegistry` still appears in `tests/Meridian.Tests/` only as a constructed dependency; no test asserts `SupportedAssetClasses` against `SecurityAssetClassCatalog`. Parity currently *holds* — all 26 catalog classes have a validator — which is exactly why the missing guard is invisible until the next class lands. |
| V2 | CSV import is broken for every asset class | `SecurityMasterCsvParser.cs:146-147` still hardcodes `CommonTerms` and `AssetSpecificTerms` to `{}`, and `SecurityMasterMapping.cs:216-217` still requires `displayName` and `currency`. The path is live in **both** UI lanes — `SecurityMasterViewModel` (WPF) and `SecurityMasterEndpoints.cs:901` (HTTP) — so it is an operator-facing dead route, not a dormant one. |

The three declared-and-deferred items are unchanged and remain governed rather than drifting: the
15 unprojected classes are still enumerated in `SecurityAssetTermsSchemaTests.IntentionallyUnprojectedAssetClasses`
and locked to "projected ∪ declared-gap = catalog", terms still have no valid-time history, and both
codec arms remain hand-written behind the round-trip guard.

### N1 — `CashSweep` and `StructuredCredit` share an asset family, and accounting reads the family as securitized

The highest-severity item this pass. Three independently reasonable decisions compose into a
cross-asset misclassification:

1. `AssetClassRegistry` assigns `Family = AssetFamily.StructuredCash` to **both** `CashSweep`
   (`SecurityMaster.fs:667`) and `StructuredCredit` (`:675`).
2. `AssetFamily.StructuredCash` serializes to the literal string `"StructuredCash"`
   (`SecurityClassification.fs:105`), which is what reaches `SecurityEconomicDefinitionRecord.AssetFamily`.
3. `SecurityMasterAccountingEventSourceAdapter.IsStructuredCredit` matches that exact literal
   (`:725-727`), and `ResolveAccountingAssetClass` (`:643`) tests it against `AssetFamily` among four
   fields.

So every `CashSweep` record resolves to accounting asset class `"AssetBackedSecurity"`. It then passes
`SecurityMasterAccountingEventService.IsFixedIncome` (`:291-303`) and enters the fixed-income slice.
Because `ToAccountingRule` returns null unless the record carries an `accountingClassification`
(`:341-351`), the typical cash-sweep record raises `SECURITY_ACCOUNTING_RULE_MISSING` at
**High** severity (`SecurityMasterAccountingEventService.cs:222-231`) — where correct classification
would have produced the benign `SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT` at **Info** (`:211-220`). A
sweep vehicle carrying an `accountingClassification` is worse, not better: it proceeds into coupon and
factor coverage validation it has no terms to satisfy.

The mechanism is certain from source. What is not established without a test is how many cash-sweep
positions reach the accounting event path in practice, and therefore how much spurious close-blocking
break volume this produces today. That test is the cheapest way to size it.

The root cause is the shared family label, not the predicate: `StructuredCash` is being asked to mean
both "structured cash vehicle" and "securitized credit". Splitting the family (`StructuredCash` for
sweep vehicles, a distinct `SecuritizedCredit` for tranches) fixes it at the source and removes the
need for `IsStructuredCredit` to special-case a family name at all.

### N2 — The coverage read model routes securitized evidence to `CustomAsset`, against ADR-022

`MultiAssetCoverageReadService.CandidateAssetClass` (`:339`) and `ScheduleAssetClass` (`:357`) both
return `"CustomAsset"` when `IsStructuredAssetEvidence` (`:386`) fires. ADR-022, accepted two days
earlier, makes `StructuredCredit` the canonical home for MBS/ABS/CLO/CMBS and the validator now warns
`SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL` for exactly that modeling
(`AssetClassValidatorRegistry.cs:314`). The read model therefore steers operators toward the class the
write model has just been taught to warn against.

Both methods also classify by substring-sniffing a concatenation of five free-text fields
(`CandidateType`, `RequiredFeed`, `Symbol`, `SecurityDisplayName`, `Reason`), so any security whose
display name or reason text happens to contain "loan", "structured", "trustee", or "warehouse" is
classified accordingly. This is the same shape as `ResolveAccountingAssetClass` in N1: asset class
inferred from prose rather than read from the record.

### N3 — `SecurityAssetPackRegistry` declares 18 asset classes the domain cannot represent

The resolution pass closed the direction that was tested — every catalog class is now claimed by a
pack. The reverse direction is unguarded, and diverges: the packs name

`Art`, `BankAccount`, `Cash`, `CreditFacility`, `ExchangeTradedFund`, `Forward`, `Guarantee`,
`InsurancePolicy`, `IntercompanyLoan`, `Mortgage`, `PartnershipInterest`, `PrivateCredit`,
`PrivateFund`, `RealEstate`, `RealEstateInterest`, `SpecializedHolding`, `UnfundedCommitment`, `Vehicle`

— none of which has a `SecurityKind` arm, a terms schema entry, or a validator. `Cash` and
`BankAccount` are the ones that matter for fund operations: the registry advertises deep accounting
automation for a cash-and-bank pack whose two headline classes do not exist as instruments.

This surfaces to operators. `SecurityMasterOperationalReadinessService:295` projects
`SecurityAssetPackRegistry.All` into the readiness report, so the readiness surface reports asset-pack
coverage for instruments the system cannot hold. The fix is a second parity guard mirroring the first.

### N4 — `ValidateAll()` cannot report the overlap rule it implements

`ValidateCandidateSet` filters its asset-class-overlap check to groups containing at least one
*candidate* pack (`SecurityAssetPackRegistry.cs:280`). `ValidateAll()` calls it with an empty candidate
list (`:249-252`), so `candidateIds` is empty and the filter rejects every group: the built-in overlap
rule can never fire. Two overlaps stand today — `DirectLoan` claimed by both `private-loan-credit` and
`mortgage-facility-intercompany`, and `CreditFacility` by three packs — so `FindByAssetClass` returns
an ambiguous set for them while an identical claim from a new pack would be rejected as Critical. The
rule is asymmetric between incumbents and newcomers, which is the opposite of what a registry guard
should be.

### N5 — The pack registry's "contract schema" is one shared prose object, not a per-pack contract

Every pack is constructed with the same three static instances — `ContractSchema` (`:37`),
`StandardValidationRules` (`:117`), `StandardReportingTaxonomy` (`:153`) — whose members are English
phrases (`"issuer"`, `"trade date"`, `"cash variance"`, `"market price without market identifier or
retained price evidence"`). `ValidateDescriptor` checks them only for non-emptiness
(`RequireNonEmpty`, `:508-521`), so the validation cannot fail for any pack and cannot distinguish
one pack's contract from another's. `InferLifecycleEvent` (`:464-500`) then derives a pack's lifecycle
mapping by substring-matching those English journal-template names, so a template renamed for clarity
can silently re-route to a different lifecycle event.

Read as documentation-as-code the registry is useful. Read as the extensibility seam its docstring
claims — "introducing asset packs without changing core ledger contracts" — it enforces nothing an
asset pack could get wrong. Worth either promoting the fields to structured, per-pack, checkable
values, or restating the type as descriptive metadata so no future work mistakes it for a gate.

### N6 — Projection fan-out writes to every asset class on every upsert

`UpsertProjectionCoreAsync` runs all 11 registered writers for every record
(`PostgresSecurityMasterStore.cs:386-389`), and each writer whose class does not match issues a
delete instead of returning (`:392-402`). A single equity upsert therefore issues roughly seventeen
`DELETE` statements — four for the bond tables, one per remaining projected class — against rows that
by construction cannot exist. The registry design is right (adding a class is one additive line); the
per-record cost is what bulk vendor ingest will feel. A `record.AssetClass`-keyed lookup plus a
targeted cleanup on observed class *change* would keep the registry and drop the amplification.

### Smaller notes, not filed as findings

- **Derivative families are imprecise.** `Swap`, `Cfd`, and `Warrant` all carry
  `AssetFamily.ListedDerivative` (`SecurityMaster.fs:671, 689, 691`); OTC swaps and CFDs are not
  listed. `AssetFamily` is the grouping key for certified report packs
  (`ReportGenerationService.cs:243`, `CertifiedReportingSnapshotBuilder.cs:141`), so the label is a
  reporting rollup, not a cosmetic tag. `FxSpot` carries `AssetClass.Other` with no family at all
  (`:653`), which lands FX under "Other" in the same rollups.
- **Option projections are identifier-keyed while every other projection is security-keyed.**
  `option_contract_projection` has `contract_symbol` as primary key with a nullable `security_id`
  (migration 006); the child tables cascade from it correctly, so this is a shape inconsistency
  rather than a leak.
- **`AssetSpecificTermsSchema` skips version 2** (`Legacy = 1`, `CustomAssetProfile = 3`) because
  version 2 belongs to the economic-terms family. The facade documents the split well; the numbering
  gap is worth a one-line comment where the constants are declared so nobody reads it as a missing
  migration.

### Priorities from this pass

Ordered by institutional risk per unit of work, and read as a delta on the standing Top 5 above:

1. **Split the `StructuredCash` family (N1).** A correctness defect with a close-readiness blast
   radius, fixed at the source in the one table that governs classification. Pair it with a test that
   asserts a `CashSweep` record resolves to a cash-equivalent accounting class.
2. **Add the two missing parity guards (V1, N3).** Validator-vs-catalog and packs-vs-catalog, both
   mirroring four guards that already exist. Still the cheapest durable items in this document.
3. **Fix or retire CSV import (V2).** It is wired into two operator surfaces and works in neither.
   Whichever way it goes, it should stop being reachable in its current state.
4. **Retire classification-by-prose (N1, N2).** `ResolveAccountingAssetClass` and the
   `MultiAssetCoverage` routers should read a declared capability off `SecurityAssetClassCatalog`
   rather than substring-matching four fields and five concatenated free-text values. This is the
   generalization that makes the next asset class safe by default instead of safe by coincidence.
5. **Relational projections — or one generic indexed seam — for the private/alternative classes.**
   Unchanged from the standing list, and unchanged in importance: `DirectLoan`, `StructuredCredit`,
   `PrivateFundInterest`, `RealEstateHolding`, and `CommitmentGuarantee` are precisely the classes
   fund operations queries by issuer, maturity, and commitment, and precisely the ones with no
   indexed path.

---

## Resolution pass — 2026-08-26

An implementation pass on the four items prioritised out of the pass above.

**Validation.** The authoring environment had no .NET SDK preinstalled; one was installed and the
work was built and tested locally. `dotnet build Meridian.sln -c Release` succeeds with 0 errors, the
full `Meridian.Tests` suite runs 13,806 passed / 226 skipped / 2 failed, and `Meridian.FSharp.Tests`
passes 447/447. Both remaining failures are pre-existing and unrelated to this pass, each verified
rather than assumed:

- `StrategyDesignServiceTests.Scenario_MultiSymbolRebalance_…` reproduces identically on the base
  commit `2917848a` in a clean worktree. The QuantScript worker is a separate process launched via
  `dotnet exec`, and the locally installed SDK does not resolve its native dependencies — an
  artefact of the authoring container, not of the tree. CI's own QuantScript project passes 166/166.
- `LeanEndpointTests.StopBacktest_UnknownId_Returns404` expects 404 and gets 501, because
  `f5f4b192` changed the Lean stop route to answer 501 without updating the test. It sits under
  `Meridian.Tests.Integration`, which every CI `dotnet test` invocation excludes twice over, so CI
  never runs it. Worth someone fixing — a test outside CI's reach rots silently — but it belongs to
  the Lean surface, not here.

### Closed this pass

| # | Item | What landed |
| --- | --- | --- |
| N1 | `CashSweep` accounts as an asset-backed security | `AssetFamily.SecuritizedCredit` splits the securitized family out of `StructuredCash`; `StructuredCredit` moves to it and `CashSweep` keeps `StructuredCash`. The adapter no longer reads the family at all (see N2/N4 below), so the family is a reporting rollup again rather than an instrument identity. `SecurityAccountingInstrumentClassTests` locks the regression: a cash sweep resolves to no accounting class, which the event service reports at Info instead of raising a High-severity `SECURITY_ACCOUNTING_RULE_MISSING`. |
| V1 | No catalog-to-validator parity guard | `SecurityAssetClassParityGuardTests.ValidatorRegistry_CoversExactlyTheCatalogAssetClasses` — the fifth guard mirroring the four that existed. |
| N3 | Packs declare 18 classes the domain cannot represent | `SecurityAssetPackDescriptor.PlannedAssetClasses` carries anticipated coverage; `AssetClasses` now names only catalog classes. `ValidateDescriptor` enforces both directions at runtime (`asset-pack.asset-class-not-in-catalog`, `asset-pack.planned-asset-class-already-modeled`), and the parity tests guard them. The readiness DTO reports the planned set separately so a reader cannot mistake it for present coverage. |
| V2 | CSV import broken for every asset class | The parser builds the `displayName`/`currency`/`exchange` common-terms payload it already parsed and stamps the asset-specific-terms schema version, so rows reach the create path intact. The accepted set derives from a new catalog capability, `SupportsIdentifierOnlyImport`, instead of a private table. |
| — | Prose-sniffing classification | `ResolveAccountingAssetClass` is a lookup over `SecurityAssetClassCatalog.ResolveAccountingInstrumentClass`, not substring matching across four fields. `MultiAssetCoverageReadService` reads the class a referenced security DECLARES (via `ISecurityMasterQueryService`) and only falls back to inference for evidence naming no security — and that fallback now reads feed SHAPE fields only, never the security's symbol, display name, or reason text. |
| N2 | Coverage read model contradicts ADR-022 | Structured evidence resolves to `StructuredCredit`; NAV/capital-call/distribution evidence resolves to `PrivateFundInterest` instead of being swept into `CustomAsset` alongside it. |

### Behaviour deltas worth knowing

These are intended and stated rather than incidental:

- **Securitized vendor spellings collapse to one accounting class.** `MortgageBacked`,
  `MortgageBackedSecurity` and `Mbs` previously resolved to a distinct `MortgageBackedSecurity`
  accounting class; they now resolve to `AssetBackedSecurity` like every other securitized spelling.
  Both pass the fixed-income gate identically and nothing downstream discriminates between them, so
  the only difference is the string in an issue message. This follows ADR-022: the MBS-vs-ABS
  distinction is a collateral fact, not a class name.
- **Records admitted only because their class string read "Loan" no longer are.** Canonical
  `DirectLoan` records were never admitted to this accounting slice; records whose class string
  happened to be `Loan` or `AmortizingLoan` were. The canonical class now decides uniformly. This
  removes an inconsistency rather than a capability — admitting direct loans to the slice is a
  product decision, not a refactor, and is left unmade.
- **CSV import accepts two asset classes, not nine.** `Equity` and `InvestmentFund` are the only
  classes whose asset-specific terms are entirely optional, so they are the only ones a
  ticker/name/currency row can create. The other seven the parser used to name were rejected at
  create time anyway; they are now refused at parse time with a message that says why. A parity test
  ties the capability to `SecurityAssetTermsSchema`, so the flag cannot drift from the contract it
  describes.

### Still open from the pass above

- **N4 — `ValidateAll()` cannot fire its own overlap rule.** Untouched. `DirectLoan` remains claimed
  by both `private-loan-credit` and `mortgage-facility-intercompany` (the latter's other three
  claims are now planned coverage), and the candidate-only filter still means a built-in overlap
  cannot be reported.
- **N5 — the per-pack contract schema is one shared prose object.** Untouched.
- **N6 — projection fan-out writes to every asset class on every upsert.** Untouched.
- The three long-standing deferred items — relational projections for the private/alternative
  classes, valid-time term history, and codec generation from `SecurityAssetTermsSchema` — are
  unchanged.

---

## Scheduled institutional-requirements pass — 2026-08-28

Re-read against `d3793290` (58 commits after `2917848a`, of which two touch Security Master — both
the 2026-08-26 resolution). No code was changed by this pass and no tests were run; every claim below
is a source read.

> **Base refreshed.** This pass's branch was later merged with `0a5ef91a`, which landed durable
> corporate-action processing and relocated the endpoints file. Line citations therefore resolve
> against the merged head, not against `d3793290`.
>
> An earlier version of this note claimed that merge touched no surface this pass reports on. That
> was wrong, and is retracted: the merge added `SecurityMasterTickerChangeService`, whose
> `RecordAsync` forwards caller-controlled attribution into `AmendTermsAsync` — so it is one of the
> callers P1 reports, and it appears in P1's table for that reason. The merge also moved P1's
> endpoint line range when the corporate-action operations split into their own partial.

Because the 2026-08-26 pass filed and closed its own findings in the same round, this pass re-verified
each claimed closure against source independently rather than accepting the resolution's account.
**All of them hold** — see below. The pass then read the bulk-import path end to end, which no prior
pass had followed past `SecurityMasterCsvParser`; that is where the new findings are.

### Claimed closures, independently re-verified

| # | Item | Evidence at `d3793290` |
| --- | --- | --- |
| N1 | `CashSweep` accounted as an asset-backed security | `AssetFamily.SecuritizedCredit` exists (`SecurityClassification.fs:30`, serialized at `:112`); `StructuredCredit` carries it (`SecurityMaster.fs:675`) and `CashSweep` keeps `StructuredCash` (`:667`). The adapter no longer reads the family: `ResolveAccountingAssetClass` is a single delegation to `SecurityAssetClassCatalog.ResolveAccountingInstrumentClass` (`SecurityMasterAccountingEventSourceAdapter.cs:663-664`). |
| V1 | No catalog-to-validator parity guard | `SecurityAssetClassParityGuardTests.ValidatorRegistry_CoversExactlyTheCatalogAssetClasses` (`:22`). |
| N3 | Packs declare classes the domain cannot represent | Guarded in both directions (`:32`, `:43`) plus `ValidateDescriptor` rejection tests (`:63`, `:83`). `PlannedAssetClasses` is reported separately from present coverage. |
| V2 | CSV import broken for every asset class | `BuildCommonTerms` emits `displayName`/`currency`/`exchange` (`SecurityMasterCsvParser.cs:165-179`); the accepted set derives from `SecurityAssetClassCatalog.IdentifierOnlyImportableAssetClasses` (`:110`), which is exactly `Equity` and `InvestmentFund` and is parity-tested against the terms schema (`SecurityAssetClassParityGuardTests.cs:104`). |
| N2 | Coverage read model contradicted ADR-022 | Confirmed; the read model resolves the class a referenced security declares. |

### Re-verified as still open, unchanged

| # | Item | Evidence at `d3793290` |
| --- | --- | --- |
| N4 | `ValidateAll()` cannot fire its own overlap rule | `SecurityAssetPackRegistry.cs:289` still filters overlap groups to those containing a *candidate* pack, and `ValidateAll()` still passes an empty candidate list (`:258-261`). `DirectLoan` is still claimed by both `private-loan-credit` (`:198`) and `mortgage-facility-intercompany` (`:222`). |
| N5 | Per-pack contract schema is one shared prose object | `ContractSchema` (`:37`), `StandardValidationRules` (`:117`) and `StandardReportingTaxonomy` (`:153`) remain single static instances of English phrases shared by all ten packs. |
| N6 | Projection fan-out writes every class on every upsert | `PostgresSecurityMasterStore.cs:386-389` still runs all 11 writers per record; non-matching writers still delete (`:398-402`). |

The three long-standing deferred items are unchanged and still governed rather than drifting: 11
projection writers against 15 declared gaps (`ProjectionWriters`, `:43-56`;
`IntentionallyUnprojectedAssetClasses`, `SecurityAssetTermsSchemaTests.cs:21-38`), terms still have no
valid-time history, and both codec arms remain hand-written behind the round-trip guard.

### P1 — The Security Master write surface accepts self-asserted provenance and an arbitrary caller-selected valid-time date

> **Scope corrected twice under review.** This item was first filed against bulk import, then widened
> to the shared create boundary, then widened again to amendments and unattended ingests. The scope
> below comes from a full sweep of `ISecurityMasterService` create/amend callers rather than from the
> path that happened to be read first, and the enumeration is the finding's real content.

The highest-severity item **from the original sweep**, and the one the subsystem's own standards
already contradict. It no longer leads the pass: P5, which review surfaced later, outranks it and must
be fixed first — attribution derived onto writes that authorization should have refused is worse than
no attribution, because it puts a named operator on a change they had no right to make. The heading
above also once said "as-of date"; the analysis below retracts that, since `EffectiveFrom` selects
nothing — it asserts economic valid-time metadata.

`SecurityMasterImportService.ImportAsync` takes `fileContent`, `fileExtension`, a progress reporter
and a cancellation token (`:42-46`) — **no actor parameter**. The JSON branch deserializes the
uploaded file straight into `List<CreateSecurityRequest>` (`:108-115`) and hands each element to
`CreateAsync` unmodified (`:164`). `CreateSecurityRequest` carries `SecurityId`, `SourceSystem`,
`UpdatedBy`, `SourceRecordId` and `EffectiveFrom` (`SecurityCommands.cs:5-15`), so on this path all
five are asserted by the file.

The HTTP surface makes the gap explicit rather than incidental. `SecurityMasterEndpoints.cs:818-840`
binds `HttpContext context`, reads the caller's permissions out of it to authorize the request, and
then calls `ImportAsync` **without passing that identity on**. The principal is in scope, is trusted
enough to gate the write, and is discarded before the write happens.

The two operator lanes reach the import *service* by different routes, which matters for the fix.
The browser workstation goes through the HTTP endpoint above. The WPF workstation does not:
`SecurityMasterViewModel` takes an injected `ISecurityMasterImportService` (`:37, 1529, 1542`) and
calls `ImportAsync` on it in-process (`:4358`), behind a `CSV/JSON` file dialog (`:4324-4328`).
There is no `HttpContext` on that path, so threading the endpoint's principal into `ImportAsync`
secures the browser lane only.

Two distinct institutional consequences:

- **Attribution.** This review already records, under What's Solid, that override approvals carry
  "reviewer identity derived from the authenticated principal, not the request body". Bulk create is
  the same governed surface reaching the opposite conclusion, and it is the higher-volume one. A
  golden record cannot defend a value in an audit if the only record of who asserted it is a string
  the asserting file chose.
- **Falsified stored provenance — narrower than it first looks, and corrected twice.** `EffectiveFrom`
  is caller-supplied and unbounded, so a create can date a definition to any point. Two drafts of
  this bullet overstated what that reaches, so state the boundary precisely. *Recorded* time is safe:
  `SecurityMasterMapping.ToEventEnvelope` stamps `EventTimestamp` server-side with `UtcNow` (`:111`),
  and both `RebuildRecordedAsOfAsync` (`SecurityMasterAggregateRebuilder.cs:99`) and
  `RebuildAsOfAsync` (`:67-81`) filter on *that* timestamp, never on `EffectiveFrom`. Nor is there an
  effective-dated term query to corrupt: `GetByIdAsOfAsync`, identifier-as-of lookup and
  reporting-as-of all delegate to `RebuildAsOfAsync`, and current term reads return the latest
  projection. So an arbitrary `EffectiveFrom` does not alter historical query selection at all.
  What it does is write a false economic start date into persisted provenance, where downstream
  consumers and auditors read it as fact. That is the real exposure, and it is worth governing on its
  own terms — but it is a stored-metadata integrity problem, not a query-correctness one, and it is
  distinct from the separately deferred valid-time term history in the standing list.

**The defect is the write surface, not bulk import.** State it as a property rather than a list,
because five review rounds each turned up another caller and a list is the wrong shape for this:
**on `ISecurityMasterService`, caller-supplied attribution is the default and server-derived
attribution is the exception.** Every mutation request type carries an **actor role** and one or more
**valid-time roles** as ordinary payload fields, and most carry a **source role** too, so any new
caller inherits the gap unless its author knows to do otherwise. State those as roles, not field
names, here as well as in the constraints below: the request types do not share a shape, and
`UpsertSecurityAliasRequest` has neither `UpdatedBy` nor `SourceSystem` — its actor role is
`CreatedBy` and its temporal roles are `ValidFrom`/`ValidTo` (`SecurityCommands.cs:59-69`). An
implementer who built the shared boundary from the create request's field names would omit alias
attribution and its validity controls entirely.

**The alias request has no source role at all, and the execution context cannot invent one.** Say
this explicitly rather than leaving "most carry a source role" to be discovered: `Provider` is
identifier content, not mutation provenance (below), so an implementer applying a uniform
actor/source/valid-time context to alias upserts has only two honest options — press `Provider` into
service as a source field, which corrupts identifier resolution, or record no mutation source for
aliases at all. Neither should be chosen silently. The decision this pass leaves open, and which the
implementation must make deliberately, is whether `SecurityAliasDto` and the `security_aliases` row
gain a trusted mutation-source column alongside `CreatedBy`, or whether alias mutations are accepted
as carrying actor provenance only. The rest of the surface is unaffected either way.

**One path already does it correctly, and it is the model for the fix.** The governed workbench
publish endpoint calls `EndpointAuthorization.TryResolveActor(context, out var actor)` and rebinds
the request with `request with { … Actor = actor }`
(`WorkstationEndpoints.SecurityMasterWorkbench.cs:292-299`), so the body's value cannot decide the
actor. `SecurityMasterWorkbenchCommandService` carries that actor into the published event
(`:761-770`), and `ApprovedFieldEditCanonicalMergeHandler` copies it into `UpdatedBy` on the
canonical amendment (`:170-185`). An earlier draft of this item claimed no path derives attribution
from an authenticated identity; that was wrong, and the correction improves the remediation — the fix
extends an existing server-derived provenance chain rather than inventing one, and that chain must be
preserved rather than reworked by any actor-model migration.

The table below is **illustrative, not exhaustive** — it is what successive sweeps have turned up.
Enumerating it definitively means walking all six public mutations of `SecurityMasterService`
(`CreateAsync :68`, `AmendTermsAsync :71`, `AmendPreferredEquityTermsAsync :208`,
`AmendConvertibleEquityTermsAsync :229`, `DeactivateAsync :250`, `UpsertAliasAsync :284`) and their
callers, including registered workflow services, not grepping method names.

| Caller | Attribution today |
| --- | --- |
| Governed workbench publish (`WorkstationEndpoints.SecurityMasterWorkbench.cs:292-299`) | **server-derived from the authenticated actor** — the reference implementation |
| `SecurityMasterTickerChangeService:72-85` | forwards `UpdatedBy` / `SourceSystem` / `EffectiveAtUtc` from `RecordTickerChangeRequest` |
| `SecurityMasterImportService:164` (both UI lanes) | whatever the uploaded file asserts, or the CSV parser's constant |
| `POST /api/security-master` (`SecurityMasterEndpoints.cs:351-362`) | request body, after `RequireSecurityMasterMutationPermission` |
| `POST` amend (`:379-396`) | request body; the `RequireGovernedTermAmendments` gate **defaults to false** (`SecurityMasterWorkbenchOptions.cs:38`), so the direct route is live in the default configuration |
| `SecurityMasterEditViewModel:216, 234` (WPF, in-process) | hardcoded `UpdatedBy: "User"` (`:212, 230`) |
| `EdgarIngestOrchestrator:315, 330` | `UpdatedBy: nameof(EdgarIngestOrchestrator)` — deliberate workload identity |
| `SecurityMasterCommands:276` (Polygon CLI) | workload identity |
| `TradingParametersBackfillService:213` | workload identity |
| `PATCH …/preferred-terms` (`SecurityMasterEndpoints.cs:1043-1058`) | request body, and **ungated** — see below |

**The mutation surface is six members, not two.** An earlier draft of this table came from grepping
`.CreateAsync(` and `.AmendTermsAsync(`, which is not the same thing as enumerating the service.
`SecurityMasterService` exposes six public mutations — `CreateAsync` (`:68`), `AmendTermsAsync`
(`:71`), `AmendPreferredEquityTermsAsync` (`:208`), `AmendConvertibleEquityTermsAsync` (`:229`),
`DeactivateAsync` (`:250`) and `UpsertAliasAsync` (`:284`) — and the two the grep missed carry the
same self-asserted fields: `DeactivateSecurityRequest` has `SourceSystem` / `UpdatedBy` /
`SourceRecordId` / `EffectiveTo` (`SecurityCommands.cs:43-50`), and `UpsertSecurityAliasRequest` has
`CreatedBy` (`:59-69`). The gap is the whole mutation surface.

**A governed control has a live bypass.** There are *two* preferred-terms amendment routes. The one
at `SecurityMasterEndpoints.cs:512-530` calls `RequireGovernedTermAmendmentRoute` before
`AmendPreferredEquityTermsAsync`; the legacy `PATCH /api/security-master/equities/{id}/preferred-terms`
at `:1043-1058` calls the same service method with **no gate at all**. The gate appears at exactly
three sites (`:390`, `:520`, `:581`), and the legacy PATCH is not among them. So the
[2026-08-24 resolution's](#resolution-pass--2026-08-24) claim that `RequireGovernedTermAmendments`
"gates all three direct term-amendment routes uniformly" is incomplete: a fourth route reaches the
same method, and it stays live even when a deployment enables the option specifically to force
maker-checker. There is no equivalent legacy duplicate for convertible terms. This is the one item in
this pass that is a defect in a shipped control rather than in attribution plumbing, and it should be
closed on its own regardless of what happens to the rest of P1.

Two corrections this table forces on the earlier framing. First, amendments are **not** covered by
the governed path in the default configuration, so this is not a create-only gap. Second, the
unattended ingests are not defects — `nameof(EdgarIngestOrchestrator)` is *better* attribution than
a username would be, and a remediation that simply required a principal would either reject those
ingests or destroy useful information.

So the fix is an actor model, not a parameter. It has to distinguish an operator principal (browser
via `HttpContext`, desktop via some desktop-side source) from a trusted workload identity (Edgar,
the Polygon CLI, backfill) from internal system paths like
`ApprovedFieldEditCanonicalMergeHandler:185`, and apply across the mutation surface rather than to
creates alone. Generalising `TryResolveActor` — already proven on the workbench path — is the
obvious starting point, and registered workflow services such as
`SecurityMasterTickerChangeService` need a defined identity source in that model rather than being
left to forward whatever their request carried.

Five constraints the fix has to respect:

- **The general rule, and the one field it must not swallow.** Successive review rounds each found
  another field an enumerated version missed — `CreatedBy`, `EffectiveTo`, `ValidFrom`/`ValidTo`,
  `SourceRecordId` — which is what an enumeration invites. So state it once, generally: **every
  provenance-bearing field on a mutation request is caller-asserted today, and each must end up
  either server-derived or validated against trusted workflow metadata.** That covers actor identity,
  source identity, upstream record identity and every valid-time bound, whatever a given request type
  calls them. `SourceRecordId` is the easiest to overlook and makes the point: `SecurityMasterMapping`
  persists it into provenance on create, amend and deactivate (`:21, 34, 41`), so a caller can attach
  an arbitrary upstream evidence identifier to a governed record.

  **Read those as semantic roles, not field names — the request types do not share a shape.**
  `UpsertSecurityAliasRequest` has neither `UpdatedBy` nor `SourceSystem`: its actor role is
  `CreatedBy` and its temporal role is `ValidFrom`/`ValidTo` (`SecurityCommands.cs:59-69`). An actor
  model built from the create request's field names would silently omit or mis-map alias attribution.

  **`Provider` on an alias is not a source role, and must stay caller-authored.** An earlier draft of
  this bullet mapped it to `SourceSystem`'s role by name similarity. It does not hold: `Provider`
  namespaces the identifier *value*, and lookup compares it against the provider the *query* asks for
  (`ProviderMatches`, `SecurityMasterQueryService.cs:443-456`) — a ticker in Bloomberg's namespace is
  a different identifier from the same string in Reuters'. Deriving it from the executing identity
  would rewrite what the record asserts about the world, and would break resolution for every alias
  whose provider is not the mutating system. `Provider` is content; `SourceSystem` is provenance about
  the mutation, which is why only the latter is in scope.

  That is the same boundary `Reason` sits on, so state it once as the test rather than accumulating
  exceptions: **the rule governs provenance about the mutation — who performed it, on whose authority,
  from what upstream evidence, effective when — and never data that is the record's own content.**
  `Reason` and `Provider` are both on the content side. A field's name resembling a provenance field's
  is not evidence; what the consuming code reads it *for* is.

  **`Reason` is the exception and must stay caller-authored.** It is persisted through the same
  `ToProvenance` call, so the rule as stated would sweep it in — wrongly. An operator's rationale is
  the one provenance field whose *content* should come from the caller; what must be trustworthy is
  the identity it hangs off, not the prose. The reference implementation does exactly this:
  `ApprovedFieldEditCanonicalMergeHandler` carries `revision.FieldJustification` through as the
  amendment `Reason` while deriving `UpdatedBy` from the authenticated actor (`:178-183`). Deriving
  or validating `Reason` against workflow metadata would discard legitimate explanations.

  Treat the notes that follow as worked examples of the rule, not as its extent — an implementer who
  satisfies only the named fields has not satisfied the finding.
- **Every actor-identifying field must be server-derived, whatever it is called.** That is `UpdatedBy`
  on create, amend and deactivate, and **`CreatedBy` on alias upsert** — `UpsertAliasAsync` copies
  `request.CreatedBy` straight into the stored `SecurityAliasDto` (`SecurityMasterService.cs:284-300`)
  and the endpoint forwards the request unchanged (`SecurityMasterEndpoints.cs:449`), so an alias's
  audit trail is exactly as spoofable as a create's.
- **`UpdatedBy` is the actor field; `SourceSystem` is not.** `SecurityMasterConflictDetection` reads
  `SourceSystem` off both sides' provenance and short-circuits when they match
  (`:446-447, 454-458`), and provider ingests set it to values like `"edgar"`
  (`EdgarSecurityMasterIngestProvider.cs:270`) and `"polygon"`
  (`PolygonSecurityMasterIngestProvider.cs:191`). Stamping it from the principal would make two
  operators loading the same vendor look like distinct sources, and one operator loading two vendors
  look like a single source — manufacturing conflicts in the first case and suppressing them in the
  second. Derive `UpdatedBy` from the principal; derive `SourceSystem` from trusted ingest metadata
  or a fixed workflow identifier, never from the actor.
- **The desktop lane already has an actor source — use it rather than inventing one.** WPF holds no
  `HttpContext`, but it is not identity-less: `DesktopAuthenticationSession.CurrentActor` resolves the
  operator from the validated login-session profile (`:24-37`), and that same session is already
  injected into `SecurityMasterViewModel` and read there (`:1537, 1550-1552`). So the desktop input to
  a shared execution context exists and is authenticated; what is missing is the wiring from it to the
  mutation requests, which today carry a hardcoded `"User"`. Naming it matters: an implementer told
  only that "the desktop needs an actor source" may build a second identity abstraction beside the one
  Meridian already maintains.

  **Deriving the actor does not secure this lane — see P5. Attribution and authorization are separate
  defects, and fixing the first without the second yields an accurate audit trail of writes that
  should never have been permitted.**

  **The wiring is not uniformly a one-line change, because not every desktop mutation site can see
  that session.** `SecurityMasterDeactivateViewModel.ConfirmAsync` builds its
  `DeactivateSecurityRequest` with a hardcoded `UpdatedBy: "User"` (`:59-68`), and its constructor
  takes only logging, notification and `ISecurityMasterService` (`:38-48`) — `SecurityMasterViewModel`
  constructs it without passing the authentication session it holds (`:1683`). So on this path the
  actor source has to be threaded through a constructor that does not currently accept it, not merely
  read from an already-injected dependency. Worth separating the two fields here: `SourceSystem:
  "WPF-UI"` on that same request is a fixed workflow identifier, which is exactly what the
  `SourceSystem` rule prescribes — only `UpdatedBy` is defective. Treat child view models that
  construct mutation requests as their own wiring sites when scoping this work.

  **But `CurrentActor` is not unconditionally an authenticated operator, and the wiring must not treat
  it as one.** It falls back to `"local-development"` when `IsAnonymousDevelopmentSession` holds — the
  unconfigured-environment posture allowed by `CanContinueWithoutCredentials` — and returns empty once
  a session has expired (`:14-37`). Gate the operator path on `IsAuthenticated`, and either model the
  intentional anonymous-development posture as its own non-principal identity or refuse governed
  mutations under it. Piping the property straight through would admit non-principal attribution into
  the very execution context this finding exists to make trustworthy.
- **Unattended callers need a trusted workload identity, not a principal.** Edgar, the Polygon CLI
  and the backfill service legitimately have no operator behind them. The execution context needs a
  service/workload identity path so those ingests keep their current, more informative attribution
  instead of being rejected or overwritten.
- **Every caller-controlled valid-time field needs a gate, not a clamp.** Clamping to ingest time
  would be wrong for the same reason the exposure is narrow: a security loaded today can legitimately
  have an economic start date months back, and clamping would overwrite that true fact with a false
  one — replacing a caller-asserted date with a caller-*independent* wrong date, which is worse
  stored provenance, not better. Gate caller-selected dates behind an explicit permission or a
  trusted ingest workflow instead, so the assertion is authorized rather than forbidden.

  **Gate both directions, not just backdating.** A *future* bound is the same arbitrary assertion and
  has a concrete effect: current identifier lookup requires `ValidFrom <= asOf`
  (`SecurityMasterQueryService.cs:382-387, 392-398`), so a caller can hide an identifier from lookup by
  dating its validity forward. Forward-dated economic terms likewise persist as asserted metadata. The
  gate belongs on every caller-selected valid-time override in either direction.

  **The live query effect is not alias-only.** `MatchesIdentifier` applies the same
  `ValidFrom <= asOf && (ValidTo is null || ValidTo > asOf)` predicate in two arms: to the projection's
  *canonical* identifiers first (`:382-387`), then to its aliases (`:392-398`). Canonical identifier
  windows are caller-supplied too, and by a route that is easy to miss because it is nested rather than
  top-level: `SecurityIdentifierDto` carries its own `ValidFrom`/`ValidTo` (`SecurityIdentifiers.cs:53-61`),
  and requests carry collections of it on both mutations — `CreateSecurityRequest.Identifiers`
  (`SecurityCommands.cs:10`) and `AmendSecurityTermsRequest.IdentifiersToAdd` / `IdentifiersToExpire`
  (`:22-23`). A gate written against the requests' own scalar date fields would leave a caller able to
  post a security whose primary ticker is dated out of the current-lookup window at creation, or to add
  one so dated by amendment — the same result as the alias case, one nesting level down. Whatever
  enforces this must walk into the identifier collections, not just the requests' surface fields.

  **Gate the two collections a caller's window actually reaches — not `IdentifiersToExpire`.** An
  earlier draft of this bullet said "every `SecurityIdentifierDto` a create or amend request carries",
  which over-corrects in the opposite direction and would reject legitimate expiries on dates the
  domain never reads. On the expiry path the incoming DTO is matched by identity alone —
  `SecurityIdentifier.sameIdentity` compares kind, normalized value and normalized provider, never the
  window (`SecurityIdentifiers.fs:91-97`) — and `collectExpiredIdentifiers` then sets the *stored*
  identifier's `ValidTo` to the amendment's `EffectiveFrom`
  (`SecurityMasterCommands.fs:457-463`). `validateAmend` likewise runs `validateIdentifier` over
  `IdentifiersToAdd` only (`:442`). So an expiry DTO's `ValidFrom`/`ValidTo` control nothing persisted
  or query-visible; they are placeholders, and the trusted temporal input for an expiry is the
  amendment's `EffectiveFrom`, which the scalar gate already covers. Gate create's `Identifiers` and
  amend's `IdentifiersToAdd`; gating the expiry collection would obscure the field that does matter
  while rejecting valid requests.

  **The exposure differs by field, and the identifier case needs stating precisely.** For economic
  *term* dates it is stored-provenance truthfulness only — nothing selects terms by `EffectiveFrom`,
  per the bullet above. Identifier and alias windows do have live query effect, but not uniformly:
  `RebuildRecordedAsOfAsync` filters the returned alias collection by `CreatedAt`, `ValidFrom` and
  `ValidTo` (`SecurityMasterAggregateRebuilder.cs:104-107`), and current lookup applies the window as
  above. Historical *resolution* is more forgiving than an earlier draft of this bullet claimed, and
  forgiving symmetrically across both arms: `TryGetProjectionByIdentifierAsync` falls back to
  `MatchesIdentifierIgnoringWindow` when nothing is active at the as-of
  (`SecurityMasterQueryService.cs:332-341`, with a comment explaining why), so a unique identifier or
  alias outside its window still resolves. Note *which* lookups get that mercy: the fallback is enabled
  by `allowIdentityFallback: asOfUtc is not null` (`:55`), so historical lookup is forgiving and
  **current** lookup — the caller passing no as-of — is strictly window-filtered with no fallback at
  all. Name the two real exposures — current lookup, and the alias collection returned by
  `GetRecordedByIdAsOfAsync` — rather than attributing the effect to as-of identifier lookup generally.
  The gate has to cover the whole surface, not just create: `EffectiveFrom` on create and amend,
  `EffectiveTo` on `DeactivateSecurityRequest` (`SecurityCommands.cs:46`), `ValidFrom` / `ValidTo` on
  `UpsertSecurityAliasRequest` (`:67-68`), and the nested `ValidFrom` / `ValidTo` on each
  `SecurityIdentifierDto` in a create request's `Identifiers` or an amendment's `IdentifiersToAdd`
  (but not `IdentifiersToExpire`, per the bullet above). Otherwise a caller who cannot backdate a
  definition can still backdate its deactivation, an alias's validity window, or a canonical
  identifier's — each reaching the same historical-integrity problem by another route.
- **The workbench chain must be preserved, not reworked.** Publish already resolves the actor
  server-side and carries it through the command service into the canonical amendment. That path is
  the target state, not a migration candidate: an actor-model change that re-plumbs it risks
  breaking the one provenance chain in this subsystem that is already correct.

One earlier claim in this pass was wrong and is worth retracting explicitly: that
`RequireGovernedTermAmendments` gates amendments, making this a create-only gap. That option defaults
to **false** — its own docstring says the default "preserves the direct write surface for deployments
whose provider-ingest pipelines call these routes" — so on a default deployment the direct amend
route is live and carries caller-asserted attribution exactly like create. Deployments that enable
the option do close the direct HTTP amend route, but not the in-process WPF amend path, which never
touches the endpoint.

### P2 — CSV import hardcodes its actor as `WpfImport`

The same root cause, visible without the JSON path. `SecurityMasterCsvParser.ParseRow` constructs
every request with `SourceSystem: "SecurityMasterImport"` and `UpdatedBy: "WpfImport"`
(`:153-154`). Every security a CSV import ever creates carries that same attribution, so the field
identifies neither the operator nor — since the HTTP endpoint shares the parser — the surface. It is
a constant occupying an audit field. Closing P1 closes this with it.

### P3 — The pack registry's new planned-coverage dimension is unguarded, widening N4

`PlannedAssetClasses` (added 2026-08-26 to close N3) is checked for catalog membership in both
directions, but the overlap rule reads `pack.AssetClasses` only
(`SecurityAssetPackRegistry.cs:285-286`) and never inspects the planned set. Three packs plan
`CreditFacility` today — `private-loan-credit` (`:202`), `mortgage-facility-intercompany` (`:226`)
and `commitment-guarantee` (`:234`) — so the class arrives with three claimants and no owner, and
nothing will say so until it becomes a catalog class, at which point all three packs fail
`asset-pack.planned-asset-class-already-modeled` at once and the ownership question has to be settled
under a red build instead of before one.

This is N4's defect reproduced on the new axis, and it argues for fixing N4 by making the overlap rule
symmetric — evaluate both claimed and planned sets, for incumbents and candidates alike — rather than
by patching the candidate filter alone.

Mitigating context for N4 as a whole: `FindByAssetClass` (`:253`) has no production consumer, only
tests. The registry reaches production through `SecurityMasterOperationalReadinessService:295` (the
readiness report) and `:873` (descriptor validation). So today's `DirectLoan` ambiguity misleads a
reader rather than mis-routing a record — which is why N4 stays a governance item rather than
escalating.

### P5 — The desktop lane mutates the golden record with no authorization check at all

Every HTTP mutation route on the Security Master requires the `ModifySecurityMaster` permission:
create (`SecurityMasterEndpoints.cs:364`), amend (`:396`), deactivate (`:424`), alias upsert (`:452`),
both equity-terms routes (`:535, 596`), corporate-action append (`:665`) and conflict resolution
(`:807`) each carry `RequirePermission(UserPermission.ModifySecurityMaster)`.

The WPF lane reaches the same `ISecurityMasterService` in process and checks nothing.
`SecurityMasterEditViewModel` calls `CreateAsync` (`:216`) and `AmendTermsAsync` (`:234`) directly,
`SecurityMasterDeactivateViewModel` calls `DeactivateAsync` (`:59-68`), and `SecurityMasterViewModel`
calls `ImportAsync` (`:4358`). None of them — nor their parent — calls
`DesktopAuthenticationSession.HasPermission`; the only desktop callers of that method are
`MainWindowViewModel` for `ManageProviders` (`:255`) and `AccountingCloseViewModel` (`:1069-1070`).

**A fifth path makes this worse, and it is a bulk one.** When Polygon is configured,
`BackfillTradingParamsCommand` is constructed as a plain `AsyncRelayCommand` with no `canExecute`
predicate (`SecurityMasterViewModel.cs:1565`); `OnBackfillTradingParams` calls
`_backfillService.BackfillAllAsync()` (`:2186-2193`), and `TradingParametersBackfillService`
walks every active security calling `AmendTermsAsync` for each
(`TradingParametersBackfillService.cs:213`). So the same unauthorized desktop operator can amend the
entire master in one command, not merely one record at a time. Any gate that covers only the edit,
deactivate and import commands leaves the largest-blast-radius mutation on the lane open — enumerate
the desktop mutation *commands*, not the dialogs. (Note it also invokes `BackfillAllAsync()` with no
cancellation token from the view model, which is why it recurs in P4 below.)

**The check exists and works; it is simply never invoked here.** `HasPermission` fails closed on a
configured host — it returns true only when the resolved operator profile actually grants the
permission — and fails open only under the unconfigured local-development posture
(`DesktopAuthenticationSession.cs:49-60`). So on a configured desktop, an authenticated operator
holding only `ViewSecurityMaster` is refused every mutation over HTTP and permitted every one of them
through the workstation. Its own documentation says "server-side authorization remains authoritative
in all cases" — true for the browser lane, but this path never reaches a server, so there is no
authoritative check behind it.

**This is why it is filed separately from P1 rather than folded into it.** P1 is an attribution
defect: the record does not truthfully say who wrote it. This is an authorization defect: the write
should not have been accepted. They have opposite fix orders, too — wiring the actor through first,
as P1 describes, would produce a faithful audit trail of mutations that were never permitted, which
is worse than the status quo in one respect, because the record would then carry a named operator's
identity on a change the operator had no right to make. The desktop mutation commands need the same
permission gate the endpoints apply, enforced before the service call, and their enablement should
reflect it so the UI does not offer actions that will be refused.

### P3b — Editing an alias rewrites its recorded history

Surfaced by review while checking P1's alias attribution rule, and it is a defect in the subsystem
rather than in this pass's prose — which is why it is filed separately rather than folded into P1.

`SecurityMasterService.UpsertAliasAsync` builds its `SecurityAliasDto` with `DateTimeOffset.UtcNow`
as `CreatedAt` (`:284-300`), and the store's upsert overwrites **both** creation columns on conflict —
`on conflict (alias_id) do update set … created_by = excluded.created_by, created_at =
excluded.created_at` (`PostgresSecurityMasterStore.cs:112-124`). So an edit to an existing alias
re-stamps who created it and when.

The consequence reaches history, not just attribution. `RebuildRecordedAsOfAsync` filters aliases by
`CreatedAt <= asOfUtc` (`SecurityMasterAggregateRebuilder.cs:104-107`), so re-stamping `CreatedAt` to
now **removes the alias from every as-of view earlier than the edit**. An identifier that was recorded
in January and corrected in June disappears from the January view — in a subsystem whose recorded-time
reconstruction is one of its strongest properties, and which this review elsewhere credits for
distinguishing "what did we believe then" from "what is true now".

This also constrains P1's actor rule, which is how it came to light: deriving `CreatedBy` from the
authenticated actor is right for a genuine create and wrong for an update, where it would relabel the
original creator.

**Preserving the creation fields is necessary but not sufficient, and an earlier draft of this item
proposed it as though it were the fix.** The on-conflict clause overwrites the whole row —
`alias_value`, `provider`, `scope`, `valid_from`, `valid_to` and the rest, not just the creation
columns (`PostgresSecurityMasterStore.cs:112-124`) — and `RebuildRecordedAsOfAsync` receives that
single current row and only filters it. So freezing `created_at` would stop the alias vanishing from
a January view and instead show January **June's corrected value**, retroactively. Both outcomes are
historically wrong; they differ only in which direction they lie.

The real remedy is therefore larger than an on-conflict tweak: alias state has to be versioned or
event-backed, so a recorded-as-of rebuild can return the row as it stood at that time rather than the
current row filtered by date. If that is out of scope for now, the honest alternative is to narrow
explicitly what recorded-as-of promises for aliases, rather than leave a guarantee the storage shape
cannot deliver. What must not happen is shipping the creation-field fix and considering the history
problem closed.

### P4 — Three ingest paths classify duplicates by exception-message substring, and the same catch swallows cancellation

`SecurityMasterImportService:171-172` decides whether a failed create was a duplicate — and therefore
whether the row is reported as `Skipped` or `Failed` — by testing `ex.Message` for the substrings
`"already exists"` and `"duplicate"`. This is the classify-from-prose antipattern the 2026-08-26 pass
retired from `ResolveAccountingAssetClass` and `MultiAssetCoverageReadService`.

**It survives at three ingest sites, not one.** A sweep for that substring pair returns
`SecurityMasterImportService:171-172`, `EdgarIngestOrchestrator:645-646` (the `IsDuplicateException`
helper) and the Polygon CLI path in `SecurityMasterCommands:281-282` — each classifying create
failures the same way. Fixing only the import service would leave the same defect in both provider
ingests, so the remediation belongs on the create outcome the three share rather than in any one
caller.

**But what the misclassification costs differs by site, and EDGAR must not be described as a count
defect.** Only import and Polygon reclassify a row between counters: the Polygon CLI increments
`skipped` on a substring hit and `failed` otherwise (`SecurityMasterCommands.cs:281-288`), and that
`failed` total decides the command's exit code (`:302`). EDGAR has no failed-security counter at all —
both the duplicate-filtered catch and the generic catch increment `securitiesSkipped`
(`EdgarIngestOrchestrator.cs:120-137`), so its skipped count is the same either way. State EDGAR's
exposure as what it is rather than borrowing the others': the classification decides whether the row
appends to `errors` and whether it logs at Debug or Warning, and `errors` is what the EDGAR CLI turns
into a non-zero exit (`SecurityMasterCommands.cs:227`). Because a genuine duplicate raises the stream
conflict message below and so misses the substring test, it lands in the generic catch — recording an
error and failing the whole ingest for a condition the code means to treat as benign. Real, and worth
fixing, but a different defect from the miscount; a regression test asserting an EDGAR count change
would assert something that cannot happen.

**The substring test does not match the error the system actually raises, so the classification is
already wrong at every site.** An earlier draft of this item called it fragile — something a reworded
message *would* break. It is worse than that: when a create reuses an existing `SecurityId`,
`PostgresSecurityMasterEventStore.AppendAsync` throws `"Security stream version conflict for {id}.
Expected {x}, actual {y}."` (`:40`), which contains neither `"already exists"` nor `"duplicate"`. So
the skip branch never fires for a re-used stream today. **Note what that case is and is not**: the
conflict establishes that a stream already exists, nothing more — it does not establish that the
incoming row is a replay of the stored one, since the append compares versions and never payloads
(detailed below). Calling it a "duplicate" here would prejudge exactly the question the remedy has to
answer, so this item says "re-used stream" and reserves "duplicate" for a row whose equivalence has
actually been established. On the two counting sites that means the rows
are counted `Failed` — in import, the operator-facing summary built from those counts
(`SecurityMasterViewModel.cs:4366-4374`) already misreports them; in the Polygon CLI, `failed` is also
the exit code. On EDGAR it means the error list and the exit code, per the bullet above. Nothing
has to change for the defect to bite — it is biting.

That also fixes where the remedy has to aim. A typed mutation outcome and its regression test must be
grounded in the real stream-exists/concurrency path, not in a hypothetical `"already exists"` message
that no component emits; a fix written against the latter would leave duplicate imports still
reported as failures.

**It is wrong in the other direction too: valid failures land in the skip bucket.** The substring test
matches domain *validation* errors that merely contain the word. `SecurityMasterCommandFacade` surfaces
codes and messages such as `duplicate_identifier_active` — "Active security identifiers must not
contain duplicate kind/value/provider combinations." (`SecurityMasterCommands.fs:403`) — and
`bond_step_dates_duplicate` (`:121`), and `CreateProjectionFromResult` puts that text into the thrown
exception. So a JSON import row carrying duplicate active identifiers or duplicate schedule dates is
reported `Skipped` rather than `Failed` and is omitted from the error list entirely: the operator is
told the row was a harmless replay when in fact it was rejected as invalid and never persisted. That
is a false positive, the mirror of the stream-conflict false negative below, and it is the more
damaging of the two — a silently dropped invalid row leaves the operator believing the security is in
the master. The typed outcome must keep domain validation failures as failures; its regression tests
need a case in each direction.

**And do not let the typed outcome equate "stream exists" with "idempotent duplicate".** The
conflict carries no evidence about the payload: `ExecuteCreateAsync` appends with
`expectedVersion: 0` (`SecurityMasterService.cs:320`), and `AppendAsync` throws purely on
`currentVersion != expectedVersion` (`PostgresSecurityMasterEventStore.cs:36-41`) without comparing
the incoming record to the stored one. So a second create reusing a `SecurityId` with *different*
terms or provenance raises exactly the same exception as a byte-identical replay. An outcome that
maps the conflict straight to `Skipped` would silently discard a competing source assertion — the
same failure mode as the identifier pre-check this document already retracted, arrived at from the
other direction. The outcome therefore needs a content-equivalence or idempotency-key check to earn
the `Skipped` classification, and must preserve `Failed` for a non-equivalent row; without that check
the honest classification of a stream conflict is a conflict, not a duplicate.

**The same `catch` swallows cancellation.** `catch (Exception ex)` (`:168`) also catches the
`OperationCanceledException` that `CreateAsync(request, ct)` throws when the token trips mid-row.
The substring test does not match it, so a cancelled row is counted as `Failed` and logged as an
import error rather than propagating. The `ct.ThrowIfCancellationRequested()` at the top of the loop
(`:160`) only covers cancellation *between* rows. Worse, on the final row with no conflict service
configured, nothing after the loop observes the token, so a cancelled import returns a normal
result. This breaks the repository's standing guardrail that cancellation flow stays intact, and a
typed duplicate outcome would not fix it: the remediation has to rethrow cancellation before
classifying a create failure at all.

The Polygon CLI path shares the shape exactly — `catch (Exception ex)` around
`CreateAsync(request, ct)` at `SecurityMasterCommands:279-282` — so it swallows cancellation the same
way.

**Edgar is the exception, and an earlier draft of this item got it wrong.** Its create loop already
handles this correctly: the duplicate filter is a narrow `catch (Exception ex) when
(IsDuplicateException(ex))`, followed by `catch (OperationCanceledException) when
(ct.IsCancellationRequested) { throw; }` (`EdgarIngestOrchestrator:120-127`). So Edgar carries the
prose-classification defect but **not** the create-loop cancellation defect. Its swallowed
cancellation lives in three *other* broad catches — around `SaveFactsAsync` (`:250-254`), around the
provider fetch/store (`:286-290`), and in `CountOpenConflictsAsync` (`:627-641`), which wraps
`GetOpenConflictsAsync(ct)` in a bare `catch (Exception)` returning `0`. Any of the three converts a
cancellation into an ordinary error or a plausible-looking count.
`EdgarIngestOrchestrator` has five broad catches in total; only the create loop rethrows.

**Say precisely when that produces a normal return, because it is not unconditional.** Each loop
re-observes the token at the top of the next iteration — `ct.ThrowIfCancellationRequested()` at `:229`
for fact groups and `:269` for filers — so a cancellation swallowed partway through a run surfaces on
the following pass, late and at the wrong site but not silently. The normal-completion case needs the
swallow to happen with no token-observing operation after it: the **final** fact group or filer, or
the initial `CountOpenConflictsAsync` when nothing downstream awaits on the token. Those are the
scenarios a regression test has to construct; asserting that any swallowed cancellation yields a
normal result would assert something the loop structure prevents. The defect is still real — a
cancelled ingest is reported as an ordinary error, and a cancelled conflict count silently becomes
zero, which feeds `conflictsDetected` — but its blast radius is the tail of a run, not the whole of it.

**Two more swallow sites sit outside the three ingests this item enumerates, which is the enumeration
failing again rather than two new facts.** The rule stated earlier — *every broad catch wrapping a
cancellable await on these paths swallows cancellation* — already covers them; they are named because
both are reachable from surfaces the pass discusses elsewhere and neither is in the tables:

- **The trading-parameter backfill.** `BackfillTickerAsync` rethrows correctly, but `BackfillAllAsync`
  wraps the call in `catch (Exception ex)` and counts a failure
  (`TradingParametersBackfillService.cs:101-108`). The loop tests `ct.IsCancellationRequested` at the
  top of the next iteration and breaks, so — exactly as with Edgar — the silent case is cancellation on
  the **final** security, which returns a normal success/failure summary. The WPF command compounds it
  by calling `BackfillAllAsync()` with no token at all (`SecurityMasterViewModel.cs:2186-2193`), so
  desktop-initiated backfills have nothing to cancel with in the first place.
- **The Polygon page fetch, before the create loop is ever reached.**
  `PolygonSecurityMasterIngestProvider.FetchPageAsync` wraps `GetAsync(url, ct)` and
  `ReadAsStringAsync(ct)` in `catch (Exception)` and returns `null` (`:129-148`); `FetchAllAsync` reads
  that as end-of-pagination and returns the pages gathered so far, after which the ingest imports that
  partial set and reports success. Rethrowing cancellation around `CreateAsync` alone therefore leaves
  the command completing normally after cancellation — the truncation happens upstream of the loop the
  remediation was aimed at.

Edgar also carries the prose defect on **both** mutations, not just create: `CreateOrAmendSecurityAsync`
calls `CreateAsync` when no security exists and `AmendTermsAsync` when one does (`:303-344`), with
both under the same outer substring filter.

The two defects therefore do not have one shared home:

| Site | Prose duplicate classification | Cancellation swallowed |
| --- | --- | --- |
| `SecurityMasterImportService:171-172` | yes | yes, same catch |
| `SecurityMasterCommands:279-282` | yes | yes, same catch |
| `EdgarIngestOrchestrator:120-127` | yes — create **and** amend | no — rethrows correctly |
| `EdgarIngestOrchestrator:250-254` | — | yes, around `SaveFactsAsync` |
| `EdgarIngestOrchestrator:286-290` | — | yes, around provider fetch/store |
| `EdgarIngestOrchestrator:627-641` | — | yes, in the conflict count |

**The two columns need separate fixes — an earlier draft of this item said otherwise and was wrong.**
A typed outcome — covering create **and** amend, per Edgar above — fixes the first column only. It
changes how a duplicate is *signalled*, but
`CreateAsync(…, ct)` still throws `OperationCanceledException`, and a broad `catch (Exception)` will
keep swallowing it whatever the duplicate signal looks like. Reading the typed outcome as covering
cancellation would leave both operator paths returning normally after a cancelled import, which is
the defect this item is reporting.

**The cancellation rule, stated so it does not depend on the table.** Every broad `catch (Exception)`
wrapping a cancellable await on these ingest paths swallows cancellation and needs the same remedy —
rethrow, or narrow the catch to what it means to handle. `EdgarIngestOrchestrator` alone has five
such catches: the create loop at `:120-127` gets it right (it rethrows), while `:250-254`,
`:286-290` and the conflict count at `:627-641` do not, and successive sweeps kept finding more.
The table below is illustrative of the shape, not an inventory to work through; the fix is the rule
applied to every such catch, with Edgar's create loop as the reference for what right looks like.

**A typed *create* outcome is not enough for Edgar.** `CreateOrAmendSecurityAsync` calls `CreateAsync`
only when no security exists and otherwise calls `AmendTermsAsync` (`:303-344`), with both under the
same outer substring filter. So a create-only outcome would leave Edgar still classifying amendment
failures by exception message. The typed outcome has to cover both mutations, or create and amendment
handling has to be separated there first.

The duplicate fix itself is a typed mutation outcome, **not** a pre-check against the identifier index. A shared
identifier is not a duplicate here by design: `SecurityMasterImportServiceTests.ImportAsync_WhenRecordsAreCreated_TriggersAutomaticConflictRecordingPerSecurity`
imports two records with distinct security ids and the same ISIN from different providers, and
asserts `Imported == 2` with one conflict detected. Pre-skipping the second row would throw away the
competing source assertion the conflict exists to adjudicate, and would stay race-prone besides. The
distinction worth drawing is a genuinely duplicate stream or security id — while identifier ambiguity
keeps flowing to conflict processing untouched. Note what "genuinely duplicate" costs to establish,
though: the stream-conflict exception alone does not prove it, per the paragraph above, so a typed
result can report it only once the outcome carries a content-equivalence or idempotency check.
Without that, reporting the conflict as a duplicate discards a competing assertion by a second
route — the same mistake the pre-check would have made.

### Smaller notes, not filed as findings

- **CSV import defaults a missing currency to `USD`** (`SecurityMasterCsvParser.cs:122-124`). This is
  deliberate and test-locked (`SecurityMasterCsvParserTests.ParsedRow_DefaultsCurrencyAndOmitsAbsentExchange`),
  so it is a decision rather than an oversight — but it sits twenty lines above a docstring stating
  the opposite principle for the sibling payload: "no term is invented for a column the file never
  had" (`:182-184`). Currency drives FX translation, reporting rollups and valuation, so a fabricated
  one is worth more than a defaulted one is worth saving. Worth revisiting deliberately, in either
  direction, so the two payloads state the same contract.
- **N6's fix is cheaper than the finding implies.** `AssetProjectionWriter` already carries its own
  asset-class name as its first field (`PostgresSecurityMasterStore.cs:43-56`), so the amplification
  closes with a dictionary lookup on `record.AssetClass` plus a targeted cleanup on observed class
  *change* — no restructuring of the registry the design deliberately made additive.
- **`LedgerExtensionPolicy` is validated by substring** (`SecurityAssetPackRegistry.cs:338-339`,
  `Contains("core ledger")`). Harmless in isolation and consistent with N5's characterization of the
  registry as prose checked for non-emptiness; noted so N5's eventual resolution covers it.
- **The compensating override layer has hardened well.** `IsProfileBackedCustomAsset` and
  `AssetClassMetadataKeywords` (`SecurityMasterService.cs:961-1029`) are still the hard-coded tables
  finding 4 counts, but `TryResolveProfileBackedAlternativeAssetClass` now documents and enforces the
  right rule — the registered profile id alone decides the class, and contradicting envelope metadata
  is refused rather than silently overridden. The remaining cost is shape, not correctness, exactly
  as the 2026-08-24 independent pass concluded.

### Priorities from this pass

Read as a delta on the standing lists above.

1. **Enforce mutation permissions on the desktop lane (P5).** The one item here that is an
   authorization failure rather than a governance or attribution one, and the only one that lets a
   user perform a write the system is configured to refuse. Every HTTP mutation route requires
   `ModifySecurityMaster`; the WPF edit, deactivate, import **and trading-parameter backfill**
   commands reach the same service in-process and check nothing, on a shell whose `HasPermission`
   would fail closed if asked. The backfill is the one to size the work by: it amends *every* active
   security in a single command, so a gate covering only the per-record dialogs leaves the largest
   mutation open. Enumerate the desktop mutation commands rather than the dialogs. Gate each before
   the service call, and reflect the result in command enablement. Sequence it
   **before** P1's actor wiring: deriving the operator's identity first would attach a real name to
   writes that should not have been accepted.
2. **Gate the legacy preferred-terms PATCH route (P1).** One of three items in this pass that are
   defects in shipped behaviour rather than in plumbing: a deployment that enables
   `RequireGovernedTermAmendments` to force maker-checker still has
   `PATCH …/preferred-terms` (`SecurityMasterEndpoints.cs:1043-1058`) reaching
   `AmendPreferredEquityTermsAsync` ungated. One `RequireGovernedTermAmendmentRoute` call closes it,
   and a route-level test asserting every amendment path refuses under the option keeps it closed.
   Smallest fix in this document with the largest governance consequence.
3. **Stop alias edits rewriting recorded history (P3b).** The third shipped-behaviour defect, and the
   one that touches a property this subsystem is otherwise careful about: an alias upsert re-stamps
   `created_at`, and recorded-as-of rebuilding filters on it, so correcting an identifier erases it
   from every earlier historical view. Note the scope honestly — the upsert overwrites the whole row,
   so preserving the creation fields alone converts a disappearing alias into a retroactively-changed
   one, which is no more truthful. Closing this properly means versioned or event-backed alias state;
   the interim alternative is to narrow explicitly what recorded-as-of promises for aliases. Ranked
   here rather than lower because it loses data today, but it is not the small fix item 2 is.
4. **Derive actor attribution across the whole mutation surface, and gate caller-set dates in both
   directions (P1, P2).** Sequence this after item 1 — attribution without authorization records who
   made a write that should have been refused.
   An auditability defect on governed write paths that both operator lanes expose, on a subsystem
   that already holds itself to the opposite standard elsewhere. Take it where the mutations
   converge, not at `ImportAsync`: all six public members of `SecurityMasterService` carry
   caller-asserted attribution, and amendments are not covered by the governed path in the default
   configuration. Derive every actor field — `UpdatedBy`, and `CreatedBy` on alias upsert — and gate
   every caller-controlled valid-time field, not just `EffectiveFrom`; that includes the `ValidFrom` /
   `ValidTo` nested inside each `SecurityIdentifierDto` in a create request's `Identifiers` or an
   amendment's `IdentifiersToAdd`, which a gate written against the requests' own scalar fields will not
   reach — but not `IdentifiersToExpire`, whose windows the domain never reads. Keep the record's
   *content* out of it — `Provider` namespaces an identifier value and `Reason` is the operator's own
   rationale, so deriving either would corrupt what the record asserts. **`SourceSystem` is not in that
   exempt set**: it is provenance, and it stays in scope. What is forbidden is deriving it from the
   *actor* — it carries source identity for conflict detection, not actor identity — while leaving it
   caller-selected still permits a forged source that manufactures or suppresses conflicts. Derive it
   from trusted ingest metadata or a fixed workflow identifier, per the constraints above. Preserve
   workload identities for unattended ingests rather than replacing them with a principal, and preserve
   the workbench chain that already does this correctly.
5. **Make the pack-overlap rule symmetric across incumbents, candidates, and planned classes
   (N4, P3).** Still among the cheapest durable items in this document, and the planned-coverage axis
   means deferring it now schedules a three-way ownership dispute for the day `CreditFacility` lands.
6. **Key the projection fan-out by asset class (N6).** Unchanged in importance, and cheaper than
   previously filed: the writers already carry the key.
7. **Retire the remaining classify-from-prose sites and the swallowed cancellations (P4).** Three
   ingests still classify mutation failures by exception message — Edgar on both create and amend.
   Two of them swallow cancellation in that same catch; Edgar instead swallows it in three separate
   broad catches (`:250-254`, `:286-290`, `:627-641`). Edgar's create loop is the reference
   implementation for the rethrow, and the typed outcome must cover both mutations — and must not
   report a stream-version conflict as a duplicate without a content-equivalence check, since the
   conflict proves only that the stream exists. Write the
   regression criteria per site, not once: import and the Polygon CLI move a row between `skipped` and
   `failed`, while Edgar has no failed counter and instead gains an error entry and a non-zero exit —
   a test asserting an Edgar count change would assert something that cannot happen. The cancellation
   half reaches beyond those three ingests: the trading-parameter backfill swallows it in
   `BackfillAllAsync` (`TradingParametersBackfillService.cs:101-108`, silent on the final security, and
   invoked with no token at all from WPF), and Polygon's `FetchPageAsync` (`:129-148`) swallows it
   *before* the create loop, so the ingest imports a truncated page set and reports success. Fixing
   only the create call sites leaves both commands completing normally after cancellation.
8. **Relational projections — or one generic indexed seam — for the private/alternative classes.**
   Unchanged from every prior pass, and unchanged in importance: `DirectLoan`, `StructuredCredit`,
   `PrivateFundInterest`, `RealEstateHolding` and `CommitmentGuarantee` remain the classes fund
   operations queries most and the ones with no indexed path.

N5 stays open and stays low-urgency: the honest resolutions are still either promoting the contract
fields to structured per-pack values or restating the type as descriptive metadata, and either is
worth more than leaving it to read as a gate that cannot fail.

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

No code was changed. No tests were run — this review makes no behavioral claims requiring execution.

The 2026-08-28 pass re-read the F# domain classification tables, `SecurityAssetClassCatalog`,
`SecurityAssetPackRegistry`, the accounting event source adapter, `PostgresSecurityMasterStore`
projection fan-out, the parity-guard and terms-schema test suites, and — new to this pass — the bulk
import path end to end: `SecurityMasterCsvParser`, `SecurityMasterImportService`, the
`SecurityMasterImport` endpoint in `Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs`, and the
WPF `SecurityMasterViewModel` import command.
