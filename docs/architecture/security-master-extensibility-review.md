# Security Master Architecture Review — Institutional Extensibility

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-19 (verification pass; prior passes 2026-08-14, 2026-08-12)
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
> **Verification pass, 2026-08-19.** Re-read against current source at `7ed160dc`. One improvement
> landed since 2026-08-14 — permission declarations added to 36 Security Master endpoints, 19 of
> them reads, hardening the *declaration* rather than the enforcement — and one
> new observation was added (a tested calculation library with no production caller). This pass also
> attempted repairs: one finding was fixed, and **two were refuted and retracted** (the
> `IOperatorOverridesStore` "dead dependency" in item 5, and the factor-schedule collapse
> recommended by item 8). Every other structural finding stands as written. See
> [Verification pass — 2026-08-19](#verification-pass--2026-08-19).

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

**The read surface is authorization-gated at the group, and partly per route.** Every Security
Master route has been behind `group.AddEndpointFilter(RequireViewSecurityMasterPermission)` since
well before this review — one filter on the whole group, rejecting a caller without
`ViewSecurityMaster` or `ModifySecurityMaster` and returning `Unauthorized` when no permissions
resolve at all. That filter, not the per-route metadata, is what actually enforces read
authorization today.

*(Added 2026-08-19; counts corrected after review.)* Of 50 mapped endpoints, 36 now carry a fluent
permission declaration: **19** reads declare
`RequireAnyPermission(ViewSecurityMaster, ModifySecurityMaster)` and **17** mutations declare
`RequirePermission(...)`. The record-mutating routes — the generic field edits and the equity
amendments — also carry `RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)`, though that
pairing is not universal; see item 11.

> **Corrected 2026-08-19.** An earlier revision said "roughly 36 read endpoints" and claimed a
> refactor dropping the group filter "cannot silently open 36 routes". Both halves were wrong. The
> 36 is the count of *changed lines* in those three commits (36 insertions / 36 deletions), which is
> every endpoint whose fluent metadata changed — reads and mutations together — not the read count.
> The read count is 19. And **14 of the 50 endpoints carry no fluent permission declaration**, among
> them the pricing hierarchy, price golden-copy and comparison, cash-flow source and projection,
> vendor-entitlement, and latest-quality-report reads. The per-route metadata is therefore a partial
> belt-and-braces over the group filter, not a replacement for it, and the review should not have
> implied otherwise.
>
> *Scope of what was checked:* the counts above come from the declarations in
> `SecurityMasterEndpoints.cs`. This pass did **not** establish how the declaration ratchet treats
> those 14 — none carries `EndpointOpenReadMetadata` and none appears in the read-declaration frozen
> baseline, yet the ratchet passes, and that is unexplained here. An earlier revision asserted that
> dropping the group filter "would silently open exactly those 14"; that consequence does not follow
> from what was verified and has been withdrawn rather than restated a third time.

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
> ~~Related: `ProviderLedgerReconciliationService` injects `IOperatorOverridesStore` and never reads
> it (`ProviderLedgerReconciliationService.cs:48,63,76`) — a dead dependency that reads as an
> override-aware reconciliation path but is not one.~~
>
> **Retracted 2026-08-19.** This was wrong. The dependency is read — in a sibling partial, by
> `GetSecurityMasterOverrideHistoryAsync`
> (`ProviderLedgerReconciliationService.SecurityCoverage.cs:448-469`), which surfaces the ten most
> recent override audit-trail entries (event type, approval status, actor, reviewer, reason,
> comment) into reconciliation break context. `ProviderLedgerReconciliationService` is a `partial`
> class across four files; the 2026-08-14 and 2026-08-19 passes both grepped only the file carrying
> the constructor. Override *governance history* does reach reconciliation. What still does not
> reach anything is an approved override's *value*, which is the substance of item 5 and stands.

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
`new_security_id`, `distribution_ratio`, `acquirer_security_id`, `exchange_ratio`,
`subscription_price_per_share`, `rights_per_share`, `redemption_price_percent_of_par` — **nine**
typed payload columns for eighteen declared event types (`CorporateActionEventTypes.cs`).
*(Corrected 2026-08-19: earlier revisions said eight and omitted `new_security_id` (`003:14`), which
carries the resulting security for spin-offs and mergers, is required by
`CorporateActionTypeDescriptorCatalog`, and is read and written by `PostgresSecurityMasterEventStore`.
Undercounting it made the wide-table problem look marginally smaller than it is.)* `TenderOffer`, `CryptoFork`,
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

> **Status (2026-08-19): closed; the recommendation below was withdrawn.** Attempting the
> recommended collapse showed the three "shapes" are not three shapes of one concept. The F#
> `FactorScheduleEntry { AsOfDate; Factor }` and the C# `StructuredFactorScheduleEntry(AsOfDate,
> Factor)` are a dated factor *level*, mirrored across the interop boundary exactly as every other
> terms type in this codebase is — the normal domain/DTO pattern, not drift.
> `SecurityFactorScheduleEntry(SecurityId, AsOfDate, PriorFactor, CurrentFactor, Source,
> EvidenceLink, SourceContentHash)` is a different thing: a factor *transition* carrying the
> evidence lineage an accounting paydown event has to post
> (`SecurityMasterAccountingEventSourceAdapter.cs:493-501`). Collapsing it onto the two-field type
> would discard the prior/current pairing, the asserting source, and the content hash. Only the
> legacy free-text `StructuredCreditTerms.FactorSchedule: string option` remains, and its own
> docstring already scopes it to legacy rows.
>
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
  `Snapshot()` that materializes every record. Publishing on node A does not invalidate node B.
  ~~`ReplaceAll` clears before repopulating (a reader between the two sees an empty master).~~
  *(Fixed 2026-08-19: `ReplaceAll` now builds the replacement and installs it under a write gate, so
  no reader observes an empty master. The per-process and eviction halves stand.)* Migration
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

### 11. Four governance mutations sit outside the mutation controls

*(Added 2026-08-19, from review of this document's own over-broad claim.)* The endpoint file applies
`RequirePermission(ModifySecurityMaster)` plus `RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)`
to the record-mutating routes, but four mutations do not carry that pairing:

| Route | Permission | Rate-limited |
| --- | --- | --- |
| `DraftSecurityMasterAssetProfile` (`:157`) | `AdminMaintenance` | no |
| `ApproveSecurityMasterAssetProfile` (`:198`) | `AdminMaintenance` | no |
| `RollbackSecurityMasterAssetProfile` (`:239`) | `AdminMaintenance` | no |
| `RunSecurityMasterQualityReport` (`:1558`) | `AdminMaintenance` (handler check only, no fluent declaration) | no |

`AdminMaintenance` is a defensible — arguably stronger — permission for profile governance, so the
gap is not that these are open. It is that they are the routes defining and approving the custom-asset
profiles the extension point depends on (item 3), plus a report run that does real work, and they sit
outside both the rate limit and the declarative permission convention every neighbouring mutation
follows. The risk is the one a reviewer would hit rather than an attacker: a blanket reading of "all
mutations are permission-gated and rate-limited" treats these as covered when they are not.

Two separate remedies, which an earlier draft of this item wrongly merged into one. **All four**
lack `RequireRateLimiting` — that is the four-route question. **Only `RunSecurityMasterQualityReport`**
lacks a fluent `RequirePermission`; the three profile routes already declare
`RequirePermission(UserPermission.AdminMaintenance)` at `:157`, `:198`, and `:239`, so telling an
implementer to add one there would have them write a redundant declaration. Either apply the missing
controls on that basis, or record the exemption where a reader will find it.

## Missing or Incomplete Subsystems Blocking New Asset Classes

*State column refreshed 2026-08-19. Three rows had closed since this table was written and were
still listed as absent; because the table reads as the live blocker list rather than as dated
evidence, that would have sent roadmap work at implementation already done. Closed rows are kept
with their outcome rather than deleted, so the table stays a record of what was raised.*

| Subsystem | State | Blocks |
| --- | --- | --- |
| Codec generation from `SecurityAssetTermsSchema` | Open — table exists; both codec sides hand-written | Every new class needs two hand-edited codec arms that only tests can catch drifting |
| `SecurityKind.CustomAsset` domain case | **Closed** — first-class DU case (`SecurityMaster.fs:566`); profile envelope round-trips | — |
| Per-field provenance persistence | **Closed** — migration 027 creates `security_field_provenance`; 028 adds versioned attribution | — |
| Typed amendment path from the workbench | Open — overlay only, by documented design | Operator corrections reaching pricing/ledger/NAV |
| Generic corporate-action payload envelope | Open — wide table, 9 payload columns / 18 types | Tender offers, forks, returns of capital, paydowns carrying their own economics |
| Effective-interest amortization | Open — enum only; the constant-yield primitives exist unwired in `SecurityCalculations.fs` | GAAP-compliant premium amortization for material portfolios |
| Bond principal schedule | **Closed** — `BondTerms.PrincipalSchedule` (`SecurityMaster.fs:261`), read by `StructuredCashFlowTermsResolver` | — |
| Bond step / inflation coupon structures | Open — `BondCouponStructure` is still `Fixed` / `Floating` / `ZeroCoupon` | Step-rate and TIPS — already classifiable, not computable |
| Asset-class-scoped projection replay | Open — argument ignored | Bounded rebuild cost as class count grows |
| Distributed projection cache invalidation | Open — per-process only | Multi-node deployment coherence |
| Relational projections for 15 of 26 classes | Open — declared gap, test-guarded | Any query path that needs typed columns for private/alternative assets |

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
`StructuredFactorScheduleEntry list`, ~~retiring the duplicate `SecurityFactorScheduleEntry`~~
(**that half withdrawn 2026-08-19 — do not implement it**; `SecurityFactorScheduleEntry` is a factor
*transition* carrying prior/current factors and evidence lineage that the two-field *level* type
cannot hold. See the **Status (2026-08-19)** note on risk item 8). Then
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
| 8 | Factor schedules exist in incompatible shapes | Typed `FactorScheduleEntries` landed and is consumed; ~~the third shape (`SecurityFactorScheduleEntry` in `Meridian.Strategies`) is still duplicated~~ — **retracted 2026-08-19: that type is not a duplicate.** It models a factor *transition with provenance*, not the *level* the other two carry. Item 8 is closed; only the legacy free-text `StructuredCreditTerms.FactorSchedule` remains, already scoped to legacy rows. See the **Status (2026-08-19)** note on risk item 8 |

### Unchanged and open

| # | Item | Current state |
| --- | --- | --- |
| 2 | Three modeling routes for MBS/ABS/CLO | No canonical-home ruling; `Bond` subclasses, `StructuredCredit`, and `CustomAsset` all remain legitimate |
| 7 | Corporate actions: wide table, per-event-type columns | Migration 021 added four more nullable columns; 9 typed payload columns for 18 declared event types, no JSONB envelope |
| 9 | Equity has bespoke amendment endpoints | `PATCH` on preferred/convertible equity terms routes straight to `ISecurityMasterService.Amend…`, bypassing the workbench Draft→Submitted→Approved→Published gate that every generic field edit goes through. Permission-gated and rate-limited, but no maker-checker. No equivalent exists for a bond call schedule or swap leg |
| 10 | Straight-line amortization only | `BondAmortizationMethod.ConstantYield` remains an enum member with no implementation |
| 10 | Per-process projection cache | `SecurityMasterProjectionCache` is still a `ConcurrentDictionary` with no eviction. *(The clear-then-refill half was fixed 2026-08-19; the per-process and eviction halves stand.)* |
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

**4. ~~Retire the third factor-schedule shape and~~ generalize the corporate-action envelope.**
~~Collapse `SecurityFactorScheduleEntry` onto the domain `FactorScheduleEntry`.~~ **The collapse half
of this priority was withdrawn on 2026-08-19 — do not implement it.** The two types model a factor
*level* and a factor *transition with provenance*; collapsing them discards the prior/current
pairing, the asserting source, the evidence link, and the content hash. See the
**Status (2026-08-19)** note on risk item 8. What remains of this priority is the corporate-action
envelope: move corporate-action economics to a JSONB payload keyed by event type — nine columns for
eighteen types means every new type is another nullable column, and six declared types already have
none.

**5. Make the projection cache multi-node-safe.**
~~Per-process with no invalidation, and a clear-then-fill `ReplaceAll` that exposes an empty master
to concurrent readers.~~ *(Updated 2026-08-19: the clear-then-fill half was fixed on this branch —
`ReplaceAll` now installs a whole map under a write gate. What remains of this priority is the
multi-node half.)* The cache is still per-process, with no cross-node invalidation and no eviction.
Migration 025 moved the conflict and revision stores off process-local memory specifically for
scale-out; this cache did not follow.

*Deferred but worth tracking:* effective-interest amortization (GAAP materiality question, not an
architecture question); relational projections for the private/alternative classes; valid-time term
history; asset-class-scoped projection replay.

---

## Verification pass — 2026-08-19

Re-read against current source at `7ed160dc`, five days after the previous pass. Unlike the two
prior passes this one also attempted repairs; see
[Remediation attempted](#remediation-attempted--2026-08-19) for what was changed and what was
refuted. No tests were run — this checkout has no .NET SDK.

### Summary: one real change, otherwise cosmetic

493 commits landed repo-wide since `4b39e9da8`. Across the Security Master surface — the F# domain,
interop, and calculations; `src/Meridian.Contracts/SecurityMaster/`;
`src/Meridian.Application/SecurityMaster/`; `src/Meridian.Storage/SecurityMaster/`;
`src/Meridian.ReferenceData/`; `src/Meridian.Instruments/`; the Security Master services in
`src/Meridian.Strategies/`; the execution gate and reporting lookup; and the workstation endpoint
surface — the diff is **86 insertions and 86 deletions across 21 files**.

**One of those changes is more than cosmetic, though less than it first appears.**
`SecurityMasterEndpoints.cs` accounts for 72 of the 144 changed lines across three commits
(`089aabee`, `95166888`, `862dc32a`) — 36 insertions and 36 deletions, each rewriting one endpoint's
terminating line to append a fluent permission call. Those 36 split into **19** reads declaring
`RequireAnyPermission(ViewSecurityMaster, ModifySecurityMaster)` and **17** mutations declaring
`RequirePermission(...)`; 14 of the file's 50 endpoints were left without a fluent declaration.

> **Corrected 2026-08-19, after review.** This pass first recorded that as authorization *hardening*
> on endpoints "that previously carried no explicit permission" — which implied the reads had been
> open. They had not. At the `4b39e9da8` baseline the file already carried
> `group.AddEndpointFilter(RequireViewSecurityMasterPermission)` on the whole route group
> (`:34`), and that filter runs the same `HasAnyPermission(ViewSecurityMaster,
> ModifySecurityMaster)` check the fluent calls declare. Runtime authorization did not change. What
> changed is that the requirement is now declared per endpoint rather than inherited from one
> group-level line — worth having, because it feeds the authorization declaration ratchet
> (`EndpointAuthorizationDeclarationTests`, `EndpointReadDeclarationTests`), which fails the build
> when a mapped route carries neither a declared permission nor a documented open-read decision, and
> because it removes a single point
> whose deletion would quietly open every route in the group, but it is a hardening of the
> *declaration*, not of the enforcement.

The remaining 18 files are genuinely cosmetic — routing inline SHA-256 sites onto `Sha256Digest`,
and replacing the last literal schema-version writes with named constants.

No change closes, narrows, or reopens any structural finding.

**Every open item from the 2026-08-14 pass was re-verified against source and stands verbatim:**

| # | Item | Re-verified evidence (2026-08-19) |
| --- | --- | --- |
| 5 | Governed edits do not reach the golden record | The only two `ISecurityMasterRevisionPublishedHandler` registrations are still `SecurityProjectionRebuildHandler` (Order=10) and `CoverageInvalidationHandler` (Order=20) (`WorkstationServiceCollectionExtensions.cs:525-530`). `OperatorOverridesDto.Values` is still `IReadOnlyDictionary<string, string>` and its docstring still reads "*without amending the canonical security terms*" (`OperatorOverrides.cs:27-33`). The item's "dead dependency" rider is **retracted** — see the correction under risk item 5 |
| 2 | Three modeling routes for MBS/ABS/CLO | No ruling landed. `SecurityMasterOperationalReadinessService.cs:157` still labels `CustomAsset` "MBS / ABS / CLO / CMBS / private assets"; no ADR exists |
| 1 | Taxonomy outruns term model | `BondCouponStructure` is still exactly `Fixed` / `Floating` / `ZeroCoupon` (`SecurityMaster.fs:222-225`) |
| 8 | Third factor-schedule shape | **Closed, and the recommendation withdrawn.** The types model a factor *level* and a factor *transition with provenance*, not one concept in three shapes — see the correction under risk item 8 |
| 7 | Corporate actions: wide table | `corporate_actions` still carries per-event-type nullable columns and no JSONB payload. Migration `003` declares 8 typed payload columns (`:5-21`); `021` adds `record_date`, `lifecycle_state`, `supersedes_corp_act_id`, and one more payload column, `redemption_price_percent_of_par` (`:7-17`). Migrations still end at 028 |
| 10 | Straight-line amortization only | `FaceValueLot.AmortizedBasisAsOf` is still day-count-weighted straight-line (`FaceValueLot.cs:94-113`); `BondAmortizationMethod.ConstantYield` still has no consumer |
| 10 | Per-process projection cache | Still per-process with no eviction and no cross-node invalidation. The clear-then-refill half was **fixed on 2026-08-19** — `ReplaceAll` now builds the replacement map and installs it with a single `Volatile.Write`, so a concurrent reader never sees an empty or partly filled master |
| 4 | ~7 registries per asset class | Class counts still agree at 26, name for name: the `SecurityKind` DU has 26 cases; `SecurityAssetClassCatalog.Descriptors` has 26 entries (27 `AssetClass:` literals, of which `Unknown` is the separate `DefaultDescriptor` excluded from `Descriptors`); `SecurityAssetTermsSchema.FieldsByAssetClass` has 26 keys. Both codec arms are still hand-written |
| 9 | Equity has bespoke amendment endpoints | Still present, and there are **two** routes to `AmendPreferredEquityTermsAsync`, not one: `AmendSecurityMasterPreferredEquityTerms` (`SecurityMasterEndpoints.cs:524`) and `PatchSecurityPreferredTerms` (`:1125`), with the convertible pair alongside. Both are permission-gated and rate-limited; both bypass the workbench Draft→Submitted→Approved→Published gate |
| — | Relational projections | Still 11 projection stores for 26 classes (11 + the 15 declared gaps = 26); `IntentionallyUnprojectedAssetClasses` still lists the same 15 private/alternative classes (`SecurityAssetTermsSchemaTests.cs:21-38`) |

### New observation: the missing calculation math already exists, unwired

`src/Meridian.FSharp/Calculations/SecurityCalculations.fs` (295 lines) is a documented,
unit-tested formula library carrying exactly the math two open findings report as absent:

- `constantYieldIncome` and `amortizationAccretion` — the effective-interest pair that item 10
  records as "an enum member with no implementation" — plus `pciDailyAmortization` for
  purchased-credit-impaired instruments.
- `inflationAdjustedPrincipal` and `inflationLinkedMarketValue` — the TIPS math item 1 needs for an
  inflation-linked `BondCouponStructure` case.
- `weightedAverageLife` — WAL for prepaying structured securities.
- Newton-Raphson `purchaseYield` / `bookYield` solvers, `repoInterest`, `shortTermDailyAccretion`,
  `fxRemeasurement`, `accruedInterest`, dirty/clean price conversion, and call/put
  in-the-money predicates.

**The module has no production caller.** It carries `[<RequireQualifiedAccess>]`
(`SecurityCalculations.fs:51`), so no consumer can reach these functions unqualified through
`open Meridian.FSharp.Calculations` — every call site must spell `SecurityCalculations.`. Grepping
that token across `src/` returns only its own declaration and its `.fsproj` compile entry; the sole
consumers are assertions in `tests/Meridian.FSharp.Tests/SecurityCalculationsTests.fs`, and the one
other file that opens the namespace (`tests/.../CalculationTests.fs`) opens `Aggregations`. The
attribute is what makes the grep conclusive rather than suggestive. The module predates both prior
passes (added 2026-08-10) and neither caught it.

This is the `SecurityAssetPackRegistry` pattern from item 10 recurring in a second place —
correct-looking capability that no production path reaches — with one difference that matters: the
`DayCount` module in the same file *does* delegate to the canonical
`Meridian.Contracts.SecurityMaster.DayCountConventions` engine so the C# and F# lanes cannot
diverge. The calculation module below it has no such tie to a consumer.

The practical consequence is favorable: it **lowers the cost** of two deferred priorities.
Effective-interest amortization and inflation-linked bond support are not greenfield — the
primitives are written and tested, and the remaining work is a term model to feed them and a
wiring path from `FaceValueLot` / the ledger amortization engine. It also raises a risk the prior
passes did not name: a tested-but-uncalled financial formula library invites a future contributor
to assume the platform computes constant-yield amortization when it does not.

### Remediation attempted — 2026-08-19

Three findings were picked up for repair because they looked small, local, and safe. Attempting
them refuted two of the three. That is recorded here rather than quietly dropped, because a review
that cannot be wrong is not evidence.

| Finding | Outcome |
| --- | --- |
| Clear-then-refill in `SecurityMasterProjectionCache.ReplaceAll` | **Fixed for readers; partly fixed for writers.** The replacement map is built and installed with one `Volatile.Write` under a write gate; reads stay lock-free through `Volatile.Read`, so a reader concurrent with a warm or rebuild sees either the whole previous master or the whole new one. An `Upsert` arriving **after the replacement takes the gate** waits and lands in the installed map. One arriving **while the argument is still being copied** does not wait — the gate is not held yet — so it writes into the outgoing map and the swap discards it. That remaining window is real at production scale and is left open deliberately; see below |
| `IOperatorOverridesStore` "dead dependency" | **Refuted, finding retracted.** It is read from a sibling partial file. See risk item 5 |
| Third factor-schedule shape | **Refuted, recommendation withdrawn.** Two distinct concepts, not one in three shapes. See risk item 8 |

**Correction to the first cut of the cache fix.** The initial swap-only version traded one race for
another, and automated review caught it. Under the old `Clear()`-then-refill, an `Upsert` that
landed *after* the refill loop had already copied its key survived; a plain reference swap discards
every upsert issued during the replacement, because they all write into the outgoing map. The first
version's own `<remarks>` claimed the outcome was "the same a concurrent `Clear()` produced" — it
was not, and the window was strictly wider. `Upsert` and `ReplaceAll` now serialize on a write gate,
with `Upsert` resolving the live map inside it, so a record persisted by create, amend, or a
published rebuild cannot be dropped by a replacement already in flight. Reads remain lock-free.

The gate then created a third problem, also caught in review: because a caller can produce its
projection and only reach `Upsert` after several awaits, a record that waits on the gate may be
*older* than one a rebuild installed meanwhile, and an unconditional assignment downgraded that key.
`Upsert` now compares `Version` and keeps the installed record when the incoming one is older.
`ReplaceAll` deliberately does not do the same: a rebuild replays the whole master, so its set is
authoritative as of its snapshot — including about which securities are absent — and merging
stragglers in by version would resurrect every record it legitimately dropped.

The regression tests were rewritten twice and then partly withdrawn, for one underlying reason. The
first versions spun a reader thread and hoped it interleaved; the second forced the interleaving for
the reader but not for the writer, which signalled *before* calling `Upsert` and so still passed
when it was descheduled at that point. Both could pass against the implementations they existed to
reject — the recurring error being to check that a test passes under the fix without checking that
it fails under the bug.

The concurrent-upsert test was then **removed rather than rewritten a third time**, because a later
finding made it unwritable: `ReplaceAll` must copy its argument *before* taking the gate (a
lazily-enumerating source run under the gate would deadlock against a writer waiting for it), which
leaves an in-memory fill over an array as the only in-gate work — no seam a caller can pause. The
interleaving is no longer externally reachable, so no synchronization scheme would let a test prove
it. **The write gate therefore has no direct regression coverage; it is justified by the lost-update
finding, not by a test.** What remains covered is the reader never seeing a partial master, and a
stale upsert not downgrading an installed record, in both directions.

**Left open: the pre-gate materialization window.** Review then pointed out that moving
materialization outside the gate reopens a narrower version of the lost-update problem — an `Upsert`
that completes while `ReplaceAll` is still copying its argument writes into the outgoing map and is
discarded by the swap, so a security created during that copy disappears from the cache until the
next refresh. That is correct, and it is not fixed here, because the two findings pull against each
other: enumerate inside the gate and a lazy source deadlocks against the writer; enumerate outside
and writes made during the copy are lost. Neither is a bug in the other's fix — they are the two
horns of the same design choice.

> **Corrected 2026-08-19, after review.** This section first claimed the window was "near-zero in
> practice: every production caller passes an already-materialized list, which the array fast path
> takes without enumerating at all". That was wrong, and it understated the risk in the very note
> written to hand the decision to a human. `BuildWarmSetAsync` returns a
> `List<SecurityProjectionRecord>` (`SecurityMasterProjectionService.cs:29,43`), and the fast path
> tests `records as SecurityProjectionRecord[]` — a `List<T>` is not an array, so the cast fails and
> `ToArray()` runs. **Both production callers enumerate outside the gate**, so the window spans the
> copy of the entire warm set rather than being near-zero. Being already materialized is not the
> same as taking the fast path, and the claim conflated them.

The window is therefore real at production scale: an `Upsert` completing while the warm set is copied
lands in the outgoing map and is discarded by the swap. Closing it means a decision rather than a
patch, and there are now three candidates. Widen the fast path to the concrete non-lazy types
(`List<T>` alongside arrays), which restores the property the wrong claim assumed and is the smallest
change. Constrain the parameter so a lazy source cannot be passed at all, which removes the deadlock
horn and lets materialization move back inside the gate. Or give the swap a version boundary that
reconciles writes made during the copy. The first is cheap but leaves the general hazard for any
other `IEnumerable`; the second changes the public shape; the third changes the concurrency model.
That decision is left to a human reviewer rather than taken here.

The remaining open findings were deliberately not attempted. Items 1, 2, 7, and the top priority
need a decision or a schema change rather than a repair: the canonical home for MBS/ABS/CLO is a
product ruling, the corporate-action envelope is a migration, `BondCouponStructure` is a codec
change across both hand-written arms, and the workbench merge path is a feature with replay
semantics to design. None of them is a cleanup, and none should be slipped in as one.

The fix above was not compiled or tested locally — this checkout has no .NET SDK. `quality-gate`
is the gate.

### Priorities — unchanged

The 2026-08-14 re-ranked list stands except for the factor-schedule collapse in its priority 4,
which this pass refuted and which is struck there — a priority list is where a later implementer
looks for work, so a recommendation this review has since concluded is destructive cannot be left
standing in it. One further adjustment: effective-interest amortization
moves from *deferred* to a credible near-term slice, on the strength of the primitives above. It
remains below the top five, because the decision it waits on (GAAP materiality) is still a policy
question and not an architecture one.

The top priority is unchanged and now five days older: **the merge path from the governed
workbench to the golden record.** Every part of that workflow is built except the publish handler
that writes an approved correction into canonical terms. Until it lands, the durable revision
lifecycle, the independent-reviewer gate, the schema-validated field paths, and the provenance
lineage all govern a side table, and an approved coupon correction still cannot reach cash-flow
projection, amortization, pricing, or NAV.

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

The 2026-08-19 verification pass diffed the Security Master surface against `4b39e9da8` across the
F# domain/interop/calculations, `Meridian.Contracts/SecurityMaster/`,
`Meridian.Application/SecurityMaster/`, `Meridian.Storage/SecurityMaster/`,
`Meridian.ReferenceData/`, `Meridian.Instruments/`, the Security Master services in
`Meridian.Strategies/`, the execution gate, the reporting lookup, and
`Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs`; re-verified each open finding against
its cited source location; re-counted the asset-class surfaces and projection stores from their
actual declarations; and swept `src/Meridian.FSharp/Calculations/` for calculation capability not
reachable from a production path. The browser workstation screens and the
`tests/Meridian.Tests/SecurityMaster/` suite were not re-diffed by this pass.

The 2026-08-12 and 2026-08-14 passes changed no code. The 2026-08-19 pass changed one file
(`SecurityMasterProjectionCache.ReplaceAll`) and added one test file; no tests were run locally,
because this checkout has no .NET SDK. Every other claim in this review is a reading of source and
makes no behavioral assertion requiring execution.
