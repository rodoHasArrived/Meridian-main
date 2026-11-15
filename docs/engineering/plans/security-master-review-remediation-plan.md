# Security Master Review Remediation Working Plan

**Status:** active working plan
**Owner:** core-team
**Reviewed:** 2026-11-14
**Scope:** Engineering
**Source evidence:** `6bcc7a0262ab0870cee32cb2cb344851e9da7e89` (slices 1–6);
`fc848789e637f3e241698c84b5415a1a9e0ed32d` (slices 7–9 and the consolidation decision, 2026-11-09);
`8d6eb1d425` (architecture-first follow-on baseline, 2026-11-14)

This plan consumes the [October review consolidation](../../../archive/docs/architecture/security-master-extensibility-review.md#consolidated-october-review-and-dispositions--2026-10-28),
including the dated observations and corrected prescriptions from #3111, #3188, #3196, #3220,
#3234, #3240, #3247 and #3263. It replaces #3240's unaccepted October 20 design as a current
working input. The immutable original plan is linked in the review's coverage ledger.

Slices 1–10 below retain their dated review sequence and implementation dispositions. The
[architecture-first follow-on](#architecture-first-follow-on--2026-11-14) records the approved
next sequence against the November 14 baseline. Neither sequence assigns a roadmap rank,
owner acceptance, release readiness or target date. Each slice needs a fresh source comparison,
its own focused regressions and required hosted checks before merge. A migration also needs the
repository's independent schema/storage review; this document grants no waiver.

## Slice 1 — Shared governed-write authority and independent profile approval

**Evidence:** D5. Enforce the enabled governed-amendment policy at the shared application seam,
covering HTTP and in-process WPF callers. Give approved publication an explicit authority path
rather than an unrestricted caller-provided bypass. Retain ordinary authorization checks.
Capture profile draft identity and enforce a different approver in every store-backed path.

**Proof:** direct edits refused with the option enabled through both clients; approved publication
still succeeds; direct edits retain documented behavior when disabled; same-actor profile approval
fails; independent approval succeeds. Test retained drafts and retry/concurrent approval behavior.

**Decision:** deployment default and legacy draft identity policy remain open; do not silently
turn governance on or invent a maker for old records.

## Slice 2 — Governed publication detects a stale field proposal

**Evidence:** D5. Capture the draft's canonical base version or field predecessor. At publication,
compare the field with that base, reopen/refuse a conflicting proposal, and preserve unrelated
concurrent changes. Retain the revision ID already stored in canonical `SourceRecordId`.
Expose whether the approved edit changed the golden record or remains overlay-only.

**Proof:** same-field change between draft and publish conflicts; unrelated-field change survives;
unchanged retry converges; maker/checker/publisher can be recovered through retained lineage;
unmergeable shapes have a visible, tested terminal or repair state. Pin transaction boundaries.

## Slice 3 — Stable classification labels and compatible historical reads

**Evidence:** D6. Replace adapter option/union `ToString()` serialization with declared label
helpers. Reconcile the instrument descriptor's intended taxonomy with the domain labels through
explicit mappings and exceptions. Decide how historical strings read without mutating old events.

**Proof:** every catalog class emits expected family/subtype; retained payload fixtures replay
before and after the change; certified-report grouping is pinned for affected classes. A display
repair cannot silently change stored classification or report grouping.

## Slice 4 — Declared rate semantics and explicit accounting disposition

**Evidence:** D10. First inventory each rate field's meaning and the already-readable schedule
conventions. Specify fraction/percentage-point/basis-point behavior per field, valid ranges and
negative-rate policy. Choose a legacy evidence/reporting and governed-repair policy; magnitude
alone cannot determine a legacy unit. Reuse ADR-024's readable-before-writable rollout.

For accounting, pin caller precedence and require a supported or outside-slice disposition for
catalogued classes. Keep any fallback for unmodelled external names explicit.

**Proof:** the same declared rate produces identical monetary results across cash-flow, accrual
and obligation readers, including sub-one values, zero, supported negatives and spreads; ambiguous
legacy data is identified without silent conversion. StructuredNote and other outside-slice cases
cannot enter bond posting through an unintended taxonomy fallback.

**Decisions:** canonical units/ranges, legacy rows and accounting treatment require domain-owner
review. The October 20 blanket `[0,1]` rule and all-fraction design are not accepted defaults.

**Implementation note (2026-11-04):** a local implementation adds `RateUnitNormalizer`,
`SecurityRateFieldUnits` and explicit catalog accounting dispositions. See the
[slice-4 disposition](../../../archive/docs/architecture/security-master-extensibility-review.md#slice-4-disposition--2026-11-04-d10).
D3 decided 2026-11-05: undeclared non-zero rates block. Legacy fixed-income rows stop projecting
until they declare a unit, Security Master engine swaps cannot declare per-leg units yet, and readers
still treat negative rates differently. Canonical units and ranges, the repair path, the negative-rate
policy and StructuredNote accounting remain open. The legacy-fixture tests now assert the decision.

**2026-11-05 partial input (direct lending only):** `RateUnitNormalizer`
(`src/Meridian.Contracts/DirectLending/RateUnitNormalizer.cs`) is an explicit-unit normalizer
(Fraction / Percent / BasisPoints, no unspecified unit, no magnitude inference, field-specific ranges
allowing negative index, spread and floor). Only the new direct-lending
[PIK capitalization and reset-schedule terms](../blueprints/direct-lending-pik-capitalization-and-rate-resets.md)
use it. It is separate from the Security Master `RateUnitNormalizer`
(`src/Meridian.Contracts/SecurityMaster/RateUnitNormalizer.cs`) in the implementation note above; the
two may converge once canonical units are approved.

## Slice 5 — Audit evidence contracts before storage/serialization changes

**Evidence:** D7. Classify each mutable row (cash-flow source, entitlement, field provenance and
conflict outcome) as reconstructible current state or independent history. Cover every host's
provenance composition. Design append-only protection for economic events around the existing
backfill history; define recorded identifier history separately from valid-time identity.
Version governance payload types and keep compatible readers for stored JSON naming/shapes.

**Proof:** mutation/removal/truncate refusals where append-only is required; rollback and
concurrent-write tests; history reconstructed with explicit valid/recorded cutoffs; old payload
fixtures read unchanged; canonical event/projection/provenance commit atomically. Independent
schema/storage review remains a gate for migrations and destructive-operation waivers.

**Progress (2026-11-04):** identifier recorded history (migration 062) and versioned governance
payloads with captured version-1 fixtures are implemented; see the
[D7 slice-5 disposition](../../../archive/docs/architecture/security-master-extensibility-review.md#d7--audit-history-and-serialization-distinguish-current-state-from-retained-evidence).
Row classification, the WPF provenance composition and migration 062's independent review remain open.

## Slice 6 — Remaining catalog, identifier and consumer convergence

**Evidence:** D1, D3, D4, D8, D9 and D11. Separate small changes by owner:

- Replace the WPF multiplier substring rule with the same declared fact the workbench uses.
  (Done before 2026-11-04. On 2026-11-06 the WPF conflict-routing substring rule also moved to
  declared field names; see the D9 disposition in the review.)
- Cover reverse instrument mapping and readiness projector mapping, including explicit ambiguity
  and outside-slice exceptions; retain conditional quality rules beside generic required terms.
- Align identifier normalization/check digits at a dependency-safe seam, retaining provider and
  listing/venue distinctions; compare C#, F# and retained SQL fixtures.
- Declare profile-to-class resolution and cash-flow term roles with precedence/legacy rules;
  give the pack registry an agreed typed purpose or preserve it as informational documentation.
  (Pack registry: informational disposition recorded 2026-11-04. On 2026-11-09 the built-in shared
  coverage was declared and checked, and the candidate overlap rule gained the planned axis; see the
  D9 disposition in the review. Profile-to-class resolution closed in the 2026-11-10 close-out;
  cash-flow term roles and the resolver's spellings closed 2026-11-13, see the review's D9 leftovers entry.)
- Verify actual ledger/NAV/risk/client consumption of income, factor, corporate-action and
  unitized-lot capabilities with retained end-to-end examples. Keep override enrichment fields
  separate from economic-term fields.
- Reassess specialized Option projection and CustomAsset projection separately. (Option reassessed
  2026-11-10: it stays specialized by owner decision; see the D9 disposition in the review.) Codec generation
  must preserve retained economics and precise refusal paths. Existing graph/OTC/curve/schedule
  increments and their acceptance limitations remain owned by their ADRs and resolution sections.

**Proof:** coverage fails when a mapping/consumer is omitted, retained values keep their meaning,
and no unrelated operational override field disappears. Each implementation updates the rolling
review with a dated disposition and exact validation evidence; it does not add another unchanged
scheduled-review PR.

## Slice 7 — Term-form parity contract between the shared model and the browser mirror (2026-11-09)

**Evidence:** D9; the [2026-11-09 verification pass](../../../archive/docs/architecture/security-master-extensibility-review.md#verification-pass--2026-11-09--dual-model-audit-reconciliation).
The registry schema is already served (`/api/security-master/asset-classes[/{assetClass}]`) and
the dashboard passport editor already builds its term fields from it; the original "serve the
schema to the browser" scope was stale. What D9 still named open: the browser mirrors
`SecurityAssetTermFormModel`'s grouping, choice and validation rules by hand
(`security-passport-schema-fields.view-model.ts`), with nothing asserting the two lanes agree.
Pin the parity as a generated contract: a .NET golden test emits a fixture from
`SecurityAssetClassRegistry` + `SecurityAssetTermFormModel` (every class's served fields, pickable
keys in form order, closed value choices, and each editor kind's validation verdict over a fixed
input corpus), and a dashboard test replays the same fixture through the TypeScript mirror.

**Proof:** the fixture regenerates deterministically and the .NET test fails on any schema or
form-rule drift from the committed copy; the dashboard parity suite fails until the mirror
matches the regenerated fixture; the mirror's number, integer and date rules match the shared
model's invariant parsing on the corpus (sign and group separators, `long` range, real calendar
dates). Known out-of-corpus host-parsing esoterica (misplaced group separators, beyond-`decimal`
magnitudes) stay with the authoritative server validator and are documented in the test.

**Scope note:** which screens launch the schema-driven editor, and retiring the pre-schema bond
panels, stay with the web/WPF parity lane (`W8-WPF-PARITY-001`), not this slice.

## Slice 8 — Descriptor-owned consumer rules: sub-type, routing keywords, quote convention (proposed 2026-11-09)

**Evidence:** D3, D9, D10; the 2026-11-09 verification pass. `DeriveSubType` maps 11 of 32 catalog
classes in `Meridian.Ui.Shared`; `MultiAssetCoverageReadService` infers the asset class from
keywords (any evidence shape containing "factor" reads as `Bond`); `LotQuantity.ValueAtPrice`
hard-codes percent-of-par for every `FaceValue` lot. Move each rule onto declared descriptor
facts, following the slice-6 pattern (`LotUnit`/`LotMultiplierTermKey`, conflict-field
categories): derive the sub-type from the catalog, replace keyword inference with declared
evidence routing or an explicit unresolved state, and add a declared quote-convention fact that
the pricing conversion reads.

**Proof:** sub-type coverage fails on an omitted class; no coverage routing depends on substring
matching; the same face-value quote prices identically before and after for every retained
fixture; a non-percent-of-par instrument can declare its convention without touching
`LotQuantity` consumers.

**Decision:** the quote-convention vocabulary (percent-of-par, per-unit, yield-quoted, …) needs
domain-owner review; magnitude or class-name inference is not an accepted fallback.

**Implementation (2026-11-09):** the sub-type derivation reads the F# `AssetClassRegistry`
(three pinned legacy workstation spellings, Equity declared sub-type-ambiguous);
`MultiAssetEvidenceClassRouter` replaces the coverage read model's substring inference with
exact-token routes over the produced evidence vocabulary, unresolved outside it; the catalog
descriptor declares `QuoteConvention` (defaulting from the lot unit so no price changes), and
`LotQuantity` gains the convention-aware overload plus `QuoteConventionFor`. See the
[slice-8 disposition](../../../archive/docs/architecture/security-master-extensibility-review.md#slice-8-disposition--descriptor-owned-consumer-rules--2026-11-09).
Open from this slice: moving class-aware pricing callers onto the convention overload when a
class first declares a non-default convention, and the quote-convention vocabulary decision
above.

## Slice 9 — Measure the ADR-024 retirement gate; drain through stamped writes (2026-11-09)

**Evidence:** D1, ADR-024 decision 5; the 2026-11-09 verification pass. The originally proposed
remedy — a migration backfilling `termsFormat` into stored payloads — is the alternative ADR-024
explicitly rejected (rewriting stored blobs mutates event-sourced history and makes a
misclassification permanent), so this slice was re-scoped to what the accepted design still
lacked: the decision-5 gate count was not measurable anywhere. Every write already stamps the
tag, migration 042 already records the classification in the non-destructive `terms_format`
column, and the population drains organically as records are rewritten by ordinary writes.

**Implementation (2026-11-09):** the data-quality scan reports rule `TX003` for each active
record whose terms declare no `termsFormat` (naming the family the shape rule infers), and
`SecurityMasterQualityReportDto.UntaggedTermsPayloadCount` gauges the whole stored population,
inactive rows included, mirroring the decision-5 SQL through the same
`SecurityTermsEnvelope.GetDeclaredFormat` reader every consumer uses. The quality-report
endpoint already serves both workstations, so each governed deployment's backlog is readable
evidence.

**Proof:** an untagged row raises TX003 naming its inferred family; a stamped row raises none;
the gauge counts inactive rows a scan skips; a fully stamped population reports zero.

**Open (the ADR's separate, later change):** when every governed deployment reports zero, retire
the shape rule and the reserved-2 routing behind a replay-only legacy upcaster, so a new untagged
payload fails with a precise error instead of being inferred. That change re-reads ADR-024
decision 5 and is not implementable from this repository alone — it needs the deployment
evidence this slice makes visible.

## Decision — single economic model consolidation (recorded 2026-11-09, not an accepted slice)

The 2026-11-09 pass records the proposal to make `SecurityTermModules` canonical, retire the
`SecurityKind` bridge (`SecurityMasterLegacyUpgrade.fs`, `SecurityMasterMapping.cs`,
`SecurityEconomicDefinitionAdapter`) and remove `retainedEconomicTerms`. This is a domain-owner
decision, not a slice: it supersedes guarantees the rolling review currently pins (D1's retained
economics and explicit refusals; slice 3's historical reads without rewriting retained terms) and
needs a payload-upgrade design, replay-compatibility proof and an event-versioning ruling before
any implementation starts. Slices 7–9 are deliberately shaped to stay useful under either
outcome.

*2026-11-09:* the decision writeup is
[ADR-027](../../adr/027-single-economic-model-consolidation.md). Its evidence reframes the
question — the event store already writes the economic definition on every command path, the
economic serializer covers 14 of the 25 modules so flat is the silent system of record for the
rest, and the duality lives in the write-path round trip and the projection codec. It defines
three gated stages (lossless flat codec ending `retainedEconomicTerms` for new writes; commands
onto `SecurityEconomicDefinition`; schema-generated codec arms) with the three preconditions
above mapped onto them. **Accepted by the domain owner 2026-11-09**; stage 1 proceeds as slice 10
below, and stages 2–3 start only after it completes.

## Slice 10 — ADR-027 stage 1: a lossless flat codec (in progress 2026-11-09)

**Evidence:** ADR-027 stage 1; the upcaster's own loss ledger
(`SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.DroppedModules`, the
`EnsureScheduleModulesRecoverable` refusals, and `retainedEconomicTerms`). Each increment makes
one lost module class recoverable through declared flat homes, never by rewriting a stored
payload; `retainedEconomicTerms` keeps riding beside the flat keys until the last increment,
then stops being written for new writes while fold-on-read keeps serving retained history.

**Increment 1 — the Call module's schedule lists (implemented 2026-11-09).** For a class that
declares the flat `callSchedule`/`putSchedule` arrays (Bond, CertificateOfDeposit at terms
version 3; StructuredCredit at 4), the class-aware flattening recovers the economic Call
module's lists into those arrays in the frozen ADR-024 entry form, stamped at the arrays'
version, instead of refusing economic-only replay. Declared conversion semantics, pinned by
`SecurityEconomicCallScheduleFlatteningTests`: `callPx` is a percentage of par; a par call
without a price writes 100 at `PercentOfPar`; `callType` "MakeWhole" is a price type carrying
the module-level spread; every other label is an exercise style carried back verbatim — the
flat field preserves undeclared style strings (`WithVerbatimUndeclaredValue`) and the economic
upgrade copies them into `callType` unchanged, so the recovery keeps them (a 2026-11-09 Codex
review finding caught the first cut nulling undeclared labels, silently losing them on the
round-trip); members the
economic family never carried stay null under ADR-025's declared read defaults. Refusals narrow,
never vanish: accrual conventions refuse everywhere; schedule lists still refuse on the
class-independent route and for classes without the declared arrays; malformed lists and
populated-but-dateless entries refuse on every route
(`SecurityEconomicScheduleReplayRefusalTests`).

**Increment 2 — the Accrual module's conventions (implemented 2026-11-09).** For a class that
declares the `scheduleConventions` block (Bond and CertificateOfDeposit at terms version 2;
StructuredCredit at 3 — the Since-2 entry near StructuredNote in the schema belongs to `Swap.legs`), the flattening recovers the economic Accrual module's
business-day convention (verbatim, and only when
`SecurityTermConventions.TryParseBusinessDayConvention` decodes it) and its joint calendar (as
the block's one payment calendar) into the frozen twelve-member block, stamped at the held
terms' versions; at or above the block's Since the key is always present, null when nothing is
declared, per ADR-024's frozen fill. Unrecognized spellings, blank or non-string calendars, and
case-variant modules or members refuse exactly as the old guard did; classes without the
declared block (and the class-independent route) keep refusing.

**Increment 3 — redemption, issuer and fund members (verified complete 2026-11-09, no code).**
Source comparison against `SecurityMasterLegacyUpgrade.termsFromKind` shows the planned work does
not exist: every issuer and fund member a flat key feeds is already inverted by the class-aware
mappings (issuer name/program/institution across fifteen classes; all four fund fields on
MoneyMarketFund; fund family on InvestmentFund and PrivateFundInterest), the Redemption module's
only source is preferred-equity classification on the Equity route that refuses economic replay
entirely, and the remaining module members (`leiCode`, `ultimateParentName`, `issuerSector`,
`issuerCountry`, `factorDate`, `prepaymentAssumption`, `averageLifeYears`, `originator`,
`creditEnhancementPct`) are constant `None` in the fill — no flat key feeds them, so there is
nothing to invert and they correctly persist only through `retainedEconomicTerms`.

**Increment 4 — the structured-product factor schedule (implemented 2026-11-09).** For the one
class that declares the typed `factorScheduleEntries` array (StructuredCredit, Since 1, so the
stamp floor of 2 governs), the class-aware flattening recovers the economic
`structuredProduct.factorScheduleEntries` list into the flat array in the frozen two-member
entry form (`asOfDate`, `factor`), preserving stored order — closing a silent drop: before this
increment the dated schedule survived only inside `retainedEconomicTerms` while the flat row and
every projection built from it lost it, with no refusal recording the loss. Recovery is
fail-closed under the flat side's own invariants
(`SecurityMasterCommands.fs` `structured_credit_factor_schedule_*`): each entry needs the
canonical `asOfDate` (decodable ISO `yyyy-MM-dd`) and `factor` (number in `[0,1]`); dates must
be distinct and, ordered by date, factors non-increasing; null/all-null entries are unset and
skipped; non-array lists, non-object entries, members the serializer never writes, and
case-variant module or key spellings that declare content refuse
(`SecurityEconomicCallScheduleFlatteningTests`, `SecurityEconomicScheduleReplayRefusalTests`).
The cross-module `structured_credit_factor_after_maturity` rule stays with domain validation,
as increments 1–2 left notice/window rules. Non-declaring classes and the class-independent
route keep the pre-existing silent-retain behavior unchanged.

**Increment 5 — the 11 unserialized modules (implemented 2026-11-09).** The economic serializer
(`SecurityEconomicDefinitionAdapter.BuildEconomicTermsJson`) now emits every one of the 25
`SecurityTermModules`: `floatingRate`, `tradingParameters`, `creditRating`, `depositaryReceipt`,
`dividendSchedule`, `covenants`, `esg`, `venue`, `multiCalendar`, `inflationLinked` and
`sinkingFund` joined the original 14 under the same contract (every module key written, null when
the definition has no such module), appended after `structuredProduct` so existing fixtures
change suffix-only. Of the newcomers only `FloatingRate` (StructuredCredit's coupon-or-index,
CapFloor's index/strikes/reset) and `TradingParameters` (Commodity contract size, Cfd leverage,
Warrant multiplier) have producers in `termsFromKind` today — those flat-fed values previously
vanished from the economic family and survived economic-only replay only through the retained
flat document; the other nine are constant-`None` expansion modules that serialize as null until
a producer appears. DU labels ride through new F# `DividendFrequency.Label`/`CovenantKind.Label`
helpers beside the existing label modules. The conversion ledger adds all 11 to
`DroppedModules` (5 bridged / 20 dropped, coverage-pinned): the class-independent bridge does
not read them and they ride verbatim in `retainedEconomicTerms`, exactly as the original dropped
modules do. Recovering their flat homes on economic-only replay (CapFloor
`referenceIndex`/`capStrike`/`floorStrike`/`resetFrequency`, Commodity `contractSize`, Warrant
`multiplier`, Cfd `leverage` — the last needs a bespoke reciprocal transform) belongs to
increment 6's per-module identity work.

**Increment 6 — the identity gate ends the retained carry where it adds nothing (implemented
2026-11-09; stage 1 complete).** The class flattenings gained the inverses for the newly
serialized modules (CapFloor `referenceIndex`/`capStrike`/`floorStrike`/`resetFrequency` from
`floatingRate`; Warrant `multiplier` from `tradingParameters.contractMultiplier`), and
economic-only replay now runs a byte-identity gate: after flattening, the candidate (carry
removed) is rebuilt into its next economic document, and when that reproduces the source
byte-for-byte the carry is dropped — the flat row IS the document. Byte equality is the
strictest possible test, failing closed to keeping the carry on any difference or exception:
image documents written by the current serializer shed it (CertificateOfDeposit and
StructuredCredit twins did at this increment; Bond's floating-coupon twin did not then, and
does since the stage-3 contraction — every class's image now sheds it, pinned per class);
historical shapes and hand-written documents keep it with fold-on-read, and refusals refuse
unchanged. No stored payload is rewritten. Pinned by the derived-verdict gate theory over every
declared class, the class-aware retained pins (now per-class facts), and the regenerated corpus
golden whose diff is exactly the increment: the CD image sheds its carry on the rebuild and
amend-cycle paths, and the CapFloor/Warrant rebuilds recover their flat keys.

### Slice 11 — ADR-027 stage 2: commands and validation onto the economic definition

The ADR-named precondition landed first (2026-11-09): the **replay-compatibility corpus**
(`SecurityMasterReplayCorpusTests` over `tests/fixtures/security-master/replay-corpus/`), a
committed set of stored-event-shaped records whose rebuild, next-event, and amend-cycle bytes —
refusal messages included — are pinned in a committed golden with the received-file workflow.
Every stage-1 codec change and every stage-2 command-path move must leave the corpus green or
carry a reviewed golden diff. Planned increments, each corpus-gated:

- **S2-B1 — the economic validation surface (implemented 2026-11-09, no behavior change).**
  `validateDefinitionTerms` over `SecurityTermModules`, gated by the classification's canonical
  `TypeName` exactly as `validateKindTerms` is gated by the kind case, re-expresses every rule
  whose subject data has a module home — 54 error codes across 23 classes, enumerated in the
  committed `definitionExpressibleCodes` ledger (exposed as
  `SecurityMasterCommandFacade.DefinitionExpressibleCodes` beside
  `ValidateDefinitionInvariants`). `SecurityDefinitionValidationEquivalenceTests` pins the
  equivalence: every declared class's clean twin passes both surfaces; for every ledgered code a
  twin mutation trips both with the same code; the definition surface never reports a code the
  kind surface does not; and the mutation table must cover exactly the ledger. The three
  factor-schedule codes whose invalid shapes the codec gate
  (`SecurityEconomicScheduleValidation`) refuses before any validator runs are proven on both
  surfaces in `SecurityDefinitionValidationTests` (F#), which mutates the records directly —
  both surfaces carry them as defense in depth. Codes with no module home (option strike, swap
  legs, bond subclass/par/principal schedule/step coupons, currency pairs, notionals, CapFloor
  type-conditional strike requirements, CustomAsset envelope) stay codec-side and are exactly
  the ledger's complement, shrinking as modules gain homes. The kind → modules projection is now
  a first-class seam (`SecurityMasterLegacyUpgrade.modulesFromKind`) for S2-B2 to derive the
  definition at mapping time. Nothing on the write path calls the new surface yet.
- **S2-B2 — validation recomposed onto the definition surface (implemented 2026-11-09).**
  `validateKindTerms` is now a composition: `validateDefinitionTerms` over
  `modulesFromKind`, authoritative for all 54 ledgered codes on every write path (create,
  amend, record validation, the facade's standalone checks — the workbench term-block and
  profile-field validation sites inherit it through `ValidateKindInvariants`), plus
  `validateCodecOnlyKindTerms`, which keeps exactly the ledger's complement — the rules whose
  data has no module home. Nothing is checked twice and nothing dropped: the equivalence
  suite's mutation table pins the partition, and the switch changed zero verdicts (596 F# +
  5,593 C# SecurityMaster tests green, corpus and write goldens byte-identical).
- **S2-B3 — events derived from the typed record (implemented 2026-11-09).** The command
  services (create, amend, deactivate) derive the economic record through the new
  `ToEconomicRecord(record, projection)` overload whenever no profile-backed override replaced
  the projection's class or terms — the typed `SecurityMasterRecord` from the command result is
  authoritative, and the per-write encode → re-decode seam (`ToProjection` → `ToRecord`)
  disappears on those paths. A profile-backed override (CustomAsset resolution, envelope
  repinning) replaces the document after the domain command, so the overridden document stays
  authoritative there and keeps the projection-decode overload, as do event replay and
  rebuild — the decode's permanent read-only legacy-upcaster role. Byte-neutrality is pinned by
  `RecordAwareDerivation_IsByteIdenticalToTheProjectionDecode` over every declared class twin,
  plus the corpus and write goldens.
- **S2-B4 — term-block and approved-merge paths (verified complete 2026-11-09, pin only).**
  Source comparison shows the planned move already happened through composition: the workbench
  term-block and profile-field edit validations call
  `SecurityMasterCommandFacade.ValidateKindInvariants`, which since S2-B2 delegates every
  ledgered code to the definition surface; and the approved-merge handler
  (`ApprovedFieldEditCanonicalMergeHandler`) publishes through the ordinary amend route, which
  S2-B2 validates on the definition and S2-B3 derives from the typed record. The remaining
  `ToRecord(...).Kind` at the edit-validation sites is the flat → kind decode of the staged
  document — the codec boundary itself, which stage 3 (S3-D) shrinks, not a command seam. A new
  pin asserts the ledgered code (`structured_credit_current_factor_invalid`) surfaces through
  the workbench route, proving the term-block path shares the canonical amend's authority.

### Slice 12 — ADR-027 stage 3: the schema-generated flat codec (planned 2026-11-09)

Grounding: `SecurityAssetTermsSchemaRoundTripTests` already turned the schema table into a
"drift eliminator" for the encode arm's key sets — every class's serialized key set must equal
the declared schema field set, with byte-stable round trips over fully-populated payloads. What
stage 3 still owes D1 is per-field semantics and the retirement of the two hand-written arms
(the 32-class decode switch in `SecurityMasterMapping.ToSecurityKind` and the per-kind F#
Interop writer). Per the ADR, stage 3 starts only after stage-1 increment 6 (codec lossless)
and S2-B4 (codec single-purpose) land. Planned increments:

- **S3-A — codec-arm gap audit (completed 2026-11-09).** The dated gap table is in the review
  record: the suite already eliminates key-set drift, pins byte-stable round trips, fixture
  completeness and hand-picked vocabulary refusals; unproven are per-alias acceptance, per-field
  missing-Req and type-mismatch refusals, the systematic `OneOf` and `WithConvention` sweeps,
  and encode-side form rules — the S3-B/S3-C backlog, to be generated from the schema itself.
- **S3-B — the decode-arm sweeps (implemented 2026-11-09).** 387 schema-generated cases:
  per-alias write equivalence, missing-Req refusal, type-mismatch refusal, the vocabulary and
  convention sweeps. First catch: seven classes' declared aliases were unimplemented in the
  codec (silent optional drops under advertised spellings) — fixed schema-drivenly with
  `CanonicalizeDeclaredAliases` in write mode, ambiguous double-spellings refusing. The decode
  arm's real contract is now ledgered where it diverges from a naive schema reading: two-sided
  vocabulary enforcement, `ToleratedAbsenceDefaults` (eight Required booleans plus Bond
  subclass; since 2026-11-13 declared on the schema as `AbsentReadDefault`), `ToleratedMistypes` (the legacy factorSchedule pointer). See the review record.
- **S3-C — the encode-arm sweeps (implemented 2026-11-09).** 124 schema-generated cases:
  every written value carries its field's declared JSON kind (dates ISO, guids parseable);
  a minimal required-only submission writes every required key and invents no optional value
  beyond two committed ledgers (`WrittenOptionalDefaults` — ADR-025's Bond coupon pair — and
  `ConditionallyRequiredOptionals` — CapFloor's type-conditional strikes, the S2-B1 codec-only
  rule made visible); the written form is a fixed point of the codec for minimal rows as well
  as the full twins (the property S3-D must preserve per class); and the envelope rules hold on
  minimal rows, not only the fully-populated fixtures.
- **S3-D — schema-bound codec extraction (cohort 1 implemented 2026-11-09; continuing).**
  The extraction's shape, bounded by the typed-record boundary: every scalar read runs through
  the `SchemaBoundTerms` reader, which verifies against the schema table that the key is
  declared and read as its declared type — codec/schema drift throws with a named field instead
  of reading past the table; record construction stays per-class until S3-E. Cohorts 1–4 (FxSpot,
  Deposit, MoneyMarketFund, Commodity, CryptoCurrency, Cfd; CertificateOfDeposit,
  CommercialPaper, TreasuryBill, Repo, CashSweep, OtherSecurity, Warrant; Option, Future,
  FxForward, NonDeliverableForward, CreditDefaultSwap, Swaption, CapFloor, StructuredNote;
  DirectLoan, StructuredCredit, PrivateFundInterest, PrivateCompanyEquity, RealEstateHolding,
  CommitmentGuarantee, Swap — 28 of 32 classes)
  are migrated and ledgered
  (`SchemaBoundDecodeClasses`), each class's fully-populated twin exercising every bound read in
  the sweep suite; remainder is structural by design: Equity and Bond read through their
  bespoke classification/coupon-structure builders, InvestmentFund through its fund-profile
  reads, and CustomAsset stays the profile-envelope exclusion — their scalar portions bind when
  those builders are touched, and the sweep suite already asserts their schema conformance
  externally (sweeps, write
  goldens, corpus — behavior-neutral by the full namespace sweep). Bespoke arms remain for the
  structural decodes (coupon structures, schedules, swap legs, profile envelopes).
  *2026-11-10: S3-D is complete.* Equity, Bond, InvestmentFund and CustomAsset read their scalars
  through the schema-bound reader, including the reads inside the bond, equity-classification and
  profile-envelope decoders, so `SchemaBoundDecodeClasses` names all 32 catalog classes and a test
  holds it equal to the catalog. The reader binds nested objects to the inner shape the schema
  registers (`Equity.convertibleTerms`). Fourteen reads the schema cannot bind are committed in
  `UnboundCompatibilityReads` and enforced at run time: three keys of the envelope-less CustomAsset
  salvage, and the members of `Equity.preferredTerms`, for which the schema registers no inner
  shape. No schema field was added and no fixture, golden or corpus changed.
- **S3-E — shrink `SecurityKind`'s per-class records (scoped by census 2026-11-09).** The
  consumer census shows the precondition nearly holds already: outside the codec and domain
  spine, only four call sites touch any per-class record (`BondTerms` ×2,
  `StructuredCreditTerms` ×2); everything else is codec-and-spine only. The remaining
  dependency is module homes for the codec-only field complement (strikes, currency pairs,
  notionals, envelopes) — field-by-field expand/contract the owner sequences, in a successor
  PR, with the census as its starting evidence.
  - **S3-E1 — strikes (implemented 2026-11-10).** New `exercise` term module
    (`ExerciseTerms`: option right, strike price, strike rate, exercise style, underlying
    security, premium amount) homes the Option, Warrant, Swaption and CapFloor strike fields;
    Option and Future contract multipliers join `tradingParameters`. Fourteen rules moved from
    the codec-only validator to the definition surface (ledger and mutation table extended);
    the class-aware flattening recovers the fields on economic-only replay. The identity gate
    now tolerates exactly one difference — a module key added after the gate shipped
    (`ModulesAddedAfterIdentityGate`) that an image predates and the rebuild emits null — so
    serializer growth does not re-age stored images into carrying their retained document.
  - **S3-E2 — currency pairs (implemented 2026-11-10).** New `currencyPair` term module
    (`CurrencyPairTerms`: base, quote and settlement currencies, forward rate) homes the pair
    fields of FxSpot, CryptoCurrency, FxForward and NonDeliverableForward. Fifteen rules moved
    to the definition surface; FxSpot and CryptoCurrency keep no codec-only rule and gained
    class-aware flattenings, so their economic-only replays recover both required keys.
  - **S3-E3 — notionals (implemented 2026-11-10).** New `notional` term module
    (`NotionalTerms`: notional, denomination, committed, funded and unfunded amounts) homes
    the contract notionals of FxForward, NonDeliverableForward, CreditDefaultSwap, Swaption and
    CapFloor, the StructuredNote denomination, and the commitment amounts of
    PrivateFundInterest and CommitmentGuarantee. Eleven rules moved to the definition surface;
    FxForward, NonDeliverableForward and CapFloor keep no codec-only rule. Swap leg notionals
    stay with the structural leg decode.
  - **S3-E4 — the profile envelope (implemented 2026-11-10).** New `profileEnvelope` term
    module (`ProfileEnvelopeTerms`: profile id, profile version and the governed terms
    document) homes the three `CustomAssetTerms` fields; the economic document embeds the
    document as JSON. All seven CustomAsset envelope rules moved to the definition surface.
    An economic-only CustomAsset replay now returns the stored profile document whole (no
    marker, no retained carry) instead of reading as the OtherSecurity salvage; a document
    written before the module keeps the former route. The profile pin of a profile-backed
    row that resolves to a first-class class is not a record field and stays document-level.
  - **Remaining for S3-E.** The record-field contraction itself (removing per-class record
    fields now that they have homes) and the residual codec-only complement listed in the
    review record (structural decodes — bond coupon structures and principal schedules, swap
    legs, covenant entries — plus the scalar remainder: CDS reference terms, structured-note
    payoff terms, private-asset valuations, listing detail). Both wait on owner sequencing.
    *2026-11-11:* the owner sequenced both as follow-up pull requests; see the close-out pass
    below.
  *2026-11-10 (close-out pass):* the contraction has not started. Two limits apply when it does.
  ADR-027 keeps `SecurityMasterLegacyUpgrade` as the permanent read-only upcaster for legacy
  `SecurityProjection` events, so the bridge is confined, not deleted. And the residual complement
  above still needs module homes before those record fields can go.

## Close-out pass — 2026-11-10

Recorded in the review's
[close-out section](../../../archive/docs/architecture/security-master-extensibility-review.md#close-out-pass--2026-11-10).
It implements the typed blocked outcome and ADR-025 increment 8f, completes S3-D, decides the
governance defaults (D5), closes the code-level parts of D6 and D8, moves conflict and profile
routing onto class-owned keys (D9), and acts on an agent-run review of migration 062 (D7). Open
after it: S3-E above; the owner's authorization of the migration 062 waiver; and the D10 choices
that remain the domain owner's (a repair path for undeclared legacy rates, a signed-interest
model, StructuredNote's accounting treatment).

*2026-11-11 (owner decisions):* the owner authorized the migration 062 waiver, accepted the three
D10 recommendations as the decisions, and sequenced the remaining code as follow-up pull requests
after the close-out pull request merges. Recorded in the review's
[owner decisions](../../../archive/docs/architecture/security-master-extensibility-review.md#owner-decisions--2026-11-11).
At the time of the owner decision, the follow-up items were:

- **S3-E record contraction**, one field group at a time, each with its own replay-compatibility
  proof, including module homes for the residual complement listed above.
- **D10, undeclared legacy rates:** a governed per-record rate-unit field for the classes that
  cannot carry a `scheduleConventions` block, with no field default.
- **D10, negative rates:** readers that do not model signed interest block with a typed reason
  where the field permits negatives, and never floor.
- **D10, StructuredNote:** fair value through profit or loss from valuation marks, with no accrual
  posting, when its accounting template is added.


## Follow-through implementation — 2026-11-11

The follow-through replaces per-class scalar storage across all 32 `SecurityKind` payloads with
shared typed modules. Separate replay cases cover exercise/multipliers, currency pairs, notionals,
profile envelopes, credit protection, structured payoffs, private valuations, listing details,
coupon structures, principal, swap legs, covenants, maturity, scalar rates, issuers, pool detail,
financing, sweep/fund terms, equity capital, tranche contracts, debt/redemption facts, exact raw
rate/cadence terms, underlying swaps, rate conventions, commodity contracts, digital networks,
underlying exposure, settlement, classification labels, reference sources and fund shares.
Complete declared schedules retain every convention, exercise window and notice rule; the older
Call/Accrual modules remain compatible summaries. Conflicting populated summaries and malformed
complete-module documents are refused.

Computed compatibility getters preserve existing codec/read APIs without duplicated scalar
storage. A global reflection check requires every field of every per-class payload to use a
registered canonical module type. The permanent legacy upcaster and historical retained-document
fold remain; source events and historical refusal/rebuild outputs are not rewritten. Current
new-event output gains the explicit module homes. Old economic-only Equity documents without a
complete capital module retain their historical refusal; new complete capital documents recover
exactly, including preferred/convertible terms.

The new `rateUnit` term is an explicit per-record repair for scalar-rate classes without usable
schedule anchors. It has no default, is version-gated, survives economic replay and uses the
existing governed draft/review/approve/publish lifecycle. Raising this field's authoring version
does not authorize the calculation-only companion or swap-leg schedule blocks.

Current readers that cannot model signed interest return `NegativeInterestUnsupported` and no
partial output rather than applying an implicit zero floor. Contractual calculations that support
signed interest and frozen historical replay retain their own semantics.

The browser and WPF schedule books show typed declared-schedule results and blockers, plus the
effective governance profile, its source/latch, enforced requirements and ignored configuration.
Both consume the shared trust snapshot. Browser row labels distinguish puts and notice deadlines.

Pending migration 062 now checks the authorized apply conditions in the runner. Upgrades require
explicit stopped-writer and retired-legacy-writer attestations, acquire parent then child exclusive
locks without waiting, and refuse a backfill above one million identifier rows unless the command
timeout exceeds thirty seconds (or is unlimited). The locks last through commit/rollback; the
attestations describe deployment facts the database cannot infer. An already-applied 062 is not
rechecked. The shipped SQL and checksum are unchanged. See the governed-edit rollout for settings.

### Validation follow-up — 2026-11-11

The follow-through moved the data and left 55 rules reading the per-class record in
`validateCodecOnlyKindTerms`. With every field on a shared module those rules had a home, so
they now sit on the definition surface with unchanged codes and messages, each ledgered and
proven on both surfaces. The codec-only validator is removed: `validateKindTerms` is
`validateDefinitionTerms` over the kind's modules, and the ledger is the complete list of
term-rule codes.

Two defects found in review of an earlier, superseded design of this stage also held here and
are fixed. The retained-document fold no longer refills a value the flat row carries exactly
and an amendment cleared (`FlatAuthoritativeFields`). And an option's exercise style must be
one of the three spellings the flat decode reads back (`option_exercise_style_invalid`).

One limitation was recorded at merge and closed the same day: a directly built per-class record
could set a module member its class's flat row does not persist. `SecurityKindPersistence` now
lists, per class and embedded module, the members the row carries, and `validateKind` refuses
the rest (`term_member_not_persisted`) and a conflicting value in a member the row derives
(`term_member_value_not_persisted`). The list was derived from the flat codec's own round trip
and is held to it by `SecurityKindPersistenceTests`. See the review record.

A second gap in the fold is closed too. The fix above covered the stage-3 homes; a value in a
field the document carried before stage 3 (a call date, an issuer name, a reference index) was
still refilled after an amendment cleared it. The write path now flattens and rebuilds the
retained document's own flat row and treats whatever that rebuild populates as held by the row,
so only values the row never held fold back. Replay output is unchanged.

### Codec generation, cohort 1 — 2026-11-11

Stage 3's codec arms were asserted, not generated. Twenty classes now have both arms generated
from one binding table (`SecurityAssetTermsCodecBindings`): the decode arm in
`SecurityMasterMapping.GeneratedDecode.cs` and the writer arm in
`Interop.SecurityMaster.GeneratedWriter.fs`, both committed and held to the table by
`SecurityFlatCodecGenerationTests`. Behaviour is unchanged.

Cohort 2 (2026-11-12) added a module-level row kind and generated Deposit,
CertificateOfDeposit, CommercialPaper and CashSweep, so 24 of 32 classes are generated. Eight
remained hand-written at that point.

Cohort 3 (2026-11-13) added list, computed and multi-key row kinds plus per-class decode inputs.
It generated the five structural classes (Bond, Swap, DirectLoan, StructuredCredit,
CommitmentGuarantee), so 29 of 32 classes are generated.

Cohort 4 (2026-11-13) generated Equity and InvestmentFund, so 31 of 32 classes are generated.
CustomAsset is not generated by design: it stores the approved profile's document verbatim and has
no class keys to bind. Codec generation is complete. The same pass declared the inner shape of
`Equity.preferredTerms`, retiring eleven unbound-read ledger entries; only the three CustomAsset
legacy-salvage reads remain unbound. See the review record.

## Accrual and accounting follow-through — 2026-11-12

The S3-E contraction above is already complete and retains its per-group replay proofs. Accrual
windows now read shared lot exposure and the declared quote convention; recurring admission
remains Bond only, with unsupported factored lots refused. Empty blocked projections report
their block before checking for an empty schedule. Cash-flow context v4 stores row version 1
and measured accrual dates; frozen v0 serialization preserves v1–v3 receipts.

The unused whole-coupon helper and direct corporate-action poster have been removed from
production. Golden cases for historical corporate-action defects remain in test fixtures.
StructuredNote uses the existing governed daily-valuation workflow with an explicit FVTPL
template, source-mark evidence and approval; it remains outside coupon accrual accounting.

The canonical principal-only call/put mode binds reviewed full long-lot quantities and bases to
a retained disposal instruction. Approval, period checks and atomic disposal govern lot closure
and journal posting together. Interest, make-whole and short redemptions remain blocked on this
path pending their own allocation support.


## Unit-safe amortization and pool accrual — 2026-11-12

The follow-up removes the cost-basis service's assumed quantity unit. Amortization targets
economic face per recorded quantity, with separate factor adjustments and explicit rejection
of incompatible lot units. Decimal face-unit costs and retained exact monetary basis survive
relief without ten-place unit rounding.

Calculated fixed-rate Bond and StructuredCredit consumers accept typed daily holdings and
row-2 interest segments. Dated factors and explicit principal reconcile before projection;
source reference principal and pre-paydown held face scale interest exactly once. A scoped
read retains acquisition/disposal history and the final interest day when a full paydown
closes a lot. Recurring output remains an approval-bound accrual draft, excluding principal cash. The typed
path captures one version-pinned v5 projection; its receipt hash and model persist in the
structured recurring source evidence and survive draft restart without a source reread.

Context v5 retains these segments. Contexts v1–v3 keep row 0 and v4 keeps row 1, including
frozen JSON, hashes and arithmetic. Declared schedules, external/provider schedules, floating
economics, negative interest, stale sources and unsupported synthetic sinkers retain their gates.
Positive legacy Bond coupons still require a resolvable rate unit; this change does not add a
new Bond rate-unit declaration or lift the declared-schedule posting gate. StructuredCredit
uses its existing governed scalar unit declaration. Consumer admission is not proof that
every retained record satisfies these existing conditions.

Validation on the integrated branch: the .NET 10 build of `tests/Meridian.Tests/Meridian.Tests.csproj`
passed with `EnableWindowsTargeting=true`; 569 focused tests passed with zero skips, with Docker
PostgreSQL enabled. The filter covers cost-basis/relief precision, the source projector and bridge,
context receipt versions, historical lot reconstruction, recurring journal calculations/lifecycle,
draft intake, rate-unit parity and catalog coverage. The database regression executes governed
paydowns and a same-day disposal, restarts the store, and verifies final-day entitlement and scope.
An additional 122 lot-adapter, schedule-equality, compatibility and ledger-gate tests passed
with zero skips. A separate read-only agent financial review found no unresolved findings
after corrections.
Changed-file whitespace formatting passed. Maintained documentation generation converged; the
repository inventory and test coverage report were refreshed. Hosted checks remain the merge
authority. No migration or historical journal rewrite is introduced.

## Architecture-first follow-on — 2026-11-14

**Planning status:** approved implementation sequence; the stages below are not delivery evidence.
The source baseline is `8d6eb1d425`. [ADR-027](../../adr/027-single-economic-model-consolidation.md)
is accepted and its three consolidation stages are complete: the economic definition is domain
authority, all 32 classes have schema-bound decode, and the flat codec is generated for the 31
classes that have one. CustomAsset retains its approved profile document. Preserve those results,
the F# `SecurityKind` union and all historical event, receipt and codec compatibility boundaries.

The adverse review narrows three claims from the brainstorm. A descriptor cannot establish that
a particular holding can execute an operation. Vendor/class/issuer grouping cannot establish a
common rate convention. Required terms and an accounting template cannot supply a missing accrual
algorithm. The following order makes each of those claims executable and testable before adding
operator actions that depend on it.

### 1. Complete the layered asset-pack foundation

Give each of the 32 classes one logical module, composed through dependency-safe contributions
owned by the existing layers. A Contracts descriptor must not depend on application, F# validation,
storage or ledger implementations. Compose an explicit built-in catalog with deterministic order;
reject duplicate, missing or contradictory contributions. No runtime class creation or plugin
discovery is introduced.

Migrate every class through these registry slices in order, preserving behavior after each slice:

1. Metadata, writable schema, upgrade steps, cash-flow term roles and rate-field units.
2. Validators, including conditional economic rules and precise typed refusals.
3. C#/F# codec rows and economic bridge contributions, preserving generated output and CustomAsset's
   profile-document exception.
4. Storage projection contributions, retaining Option's specialized behavior and every declared
   unsupported projection.
5. Accounting classification and template bindings, retaining explicit outside-slice dispositions.

Central registries become projections of these contributions. Keep independent characterization
fixtures and union/catalog coverage checks so a shared omission cannot make both implementation
and expected output silently agree. Preserve generated schema versions, raw serialized values,
module persistence coverage and permanent legacy read adapters. Remove each old registration only
after its replacement has parity proof across all 32 classes.

This is the structural increment of `W11-PACK-001`. Compiled composition does not complete its
governed pack activation, record version pinning, approval or rollback lifecycle.

### 2. Bind immutable draft authority and typed edit impact

Before bulk repair, retain each draft's authenticated maker, submitter, base revision and proposed
change. An approver must differ from both maker and submitter. Legacy drafts missing this authority
remain explicitly unreviewable until repaired; do not invent identities or adopt the current
projection as their original base. Preserve the existing approved publication boundary.

Extract a read-only impact evaluator from the existing affected-book and period-aware restatement
resolvers. Evaluate the proposed overlay against its exact base, with no publication, restatement
creation, journal or lot mutation. Return typed complete, partial or unavailable impact: affected
lots/books/periods, locked-period disposition and known retained report dependencies. An unresolved
dependency cannot become an empty affected set or a zero monetary delta.

Bind review evidence to the draft/base and dependency versions, effective dates and calculation
version. Recheck authority at approval and publication; changed operands invalidate the preview
and require fresh review. Preserve same-field conflict refusal, unrelated-field edits, idempotent
retry and explicit canonical-versus-overlay publication outcomes.

### 3. Deliver capability diagnostics and governed rate repair together

Serve one shared capability evaluation for terms, cash flows, accrual, valuation, corporate actions
and redemption. Use the actual execution preflight for the selected security/holding, book, date
and operation. Descriptors describe possible support; they cannot turn a failed or unperformed
preflight into Ready. Return Ready, Review or Blocked with applicability and evaluation freshness
separate, typed reasons, and permitted remediation links. Unknown inputs mean Review; inapplicable
operations are explicitly identified.

The passport renders this projection; Portfolio rolls it up by distinct holdings with a declared
scope and as-of time. Reason counts disclose overlap and unavailable coverage. The rollup is
operational eligibility, separate from the charter's `W11-COVER-001` Verified Coverage metric.
Keep existing routes and browser workspace boundaries; share server contracts with other clients
without introducing WPF changes in this increment.

Build the repair queue as discovery filters by vendor, class and issuer. A repair cohort also
requires the same source field, source schema/version, term role and evidence scope. Freeze the
selected record IDs and base versions, show original scalar and proposed declared unit, and retain
the supporting evidence reference/hash. The repair declares interpretation; it never rescales the
stored scalar or infers a unit from value magnitude.

Stage ordinary governed drafts per record, retaining separate decisions and receipts. One operator
submission may contain many drafts; each item remains reviewable and a different authenticated
person approves it. Publication is explicit. Persist resumable item outcomes for created, already
staged, conflicted, refused and published records; retries cannot duplicate drafts or silently skip
conflicts. Recheck each record's authority instead of extending a batch decision to changed records.
No AI unit suggestions are included in this delivery.

### 4. Add retained review comparisons and report-input-first temporal evidence

Extend the typed impact preview with old/new cash-flow comparisons only when both sides can be
calculated under their recorded inputs. Keep each refusal visible when a side is unavailable;
never substitute zero flows. Retain the reviewed preview with the approval and show stale evidence
before allowing a new decision.

The first passport comparison shows the selected report's retained input beside the current
approved definition, with the report's valuation date, book and calculation context visible. A
report-line deep link starts from its exact retained input revision/snapshot and calculation
version. Show value, raw/normalized rate unit, source revision and maker/approval/publication
lineage. An unresolved input identity remains an explicit gap; current state cannot substitute
for the historical input. A general timestamp-cutoff view may select eligible retained rows using
the existing query contract, but must distinguish effective and recorded timestamps and must not
claim complete effective-time term history or database commit visibility. Capture newly required
history prospectively and label legacy gaps without fabricated backfill. Reuse the existing
subject-addressed proof API and evidence links.

### 5. Admit one declarative accrual profile

Replace the current `SupportsRecurringFixedIncomeAccrual` boolean admission with declarations
that select a registered, tested accrual profile and approved accounting binding, preserving the
current Bond and StructuredCredit boundaries and all lot, terms, source, period and approval
checks. The first new profile is a fixed-rate bullet CertificateOfDeposit with Actual/365,
an explicit rate unit, supported accruing lot unit and compatible approved journal template.
Require complete start/maturity/principal/coupon terms. Exclude fees, discount/OID, premium,
amortization, floating/reset, embedded optionality and unsupported principal schedules.

Preserve stable accrual source identity across descriptor changes and retries. The same receipt,
review, independent approval and posting flow used by the supported Bond lane governs CD drafts.
A declaration cannot bypass the declared-schedule posting gate or any existing refusal. Bond
results and historical receipts remain unchanged. DirectLoan and CommercialPaper need separate
profile proofs; no whole-class admission follows from this CD increment. `W12-CREDIT-001` continues
to own loan servicing, fees, PIK, fixing authority and nonaccrual semantics.

### 6. Converge vanilla interest-rate swaps on reusable legs

Implement `W11-LEGS-001` as fixed/floating leg modules carrying their own schedule conventions,
day count, currency/notional and declared rate unit. Reuse ADR-025's schedule kernel and retained
fixing/source evidence, then converge existing vanilla interest-rate swap cash-flow consumers on
those modules. Preserve instrument-level economics and typed refusal of missing fixings or
unsupported structures. Replay and old payloads retain their existing interpretation.

This increment excludes cross-currency exchanges, CDS credit events, premium/protection/funding
payoffs, caps/floors, swaptions and structured-note optionality. Their event, exercise, valuation and
accounting semantics require separately bounded extensions; common schedule legs do not supply them.

### 7. Use CustomAsset usage to prepare developer-review candidates

Group usage by approved profile identity/version and compatible declared economic semantics.
Expose record counts, approved field definitions and completeness, evidence and unresolved gaps;
popularity alone is not class equivalence. Reuse the existing candidate-pack functions to export
a deterministic draft module/pack review artifact. Do not derive defaults from observed record
values, create classes at runtime or activate a pack from the operator workflow.

### Verification and roadmap ownership

Each registry slice needs independent parity and replay fixtures, omitted/duplicate contribution
failures and preserved typed refusals. Financial workflow tests cover cross-scope access, maker/
submitter self-approval, stale bases and impact operands, locked periods, restart/retry, exact
per-record receipts and partial batch conflicts. Persistence changes include real PostgreSQL
concurrency/rollback proof. CD fixtures independently calculate money and reject every excluded
economic shape; swap fixtures compare signed fixed/floating flows and missing-fixing refusals.
Browser checks exercise keyboard operation, dense desktop layouts, loading/stale/partial/blocked
states, remediation navigation and exact decimal display. Use synthetic evidence.

Keep `W5-MASSET-001`'s delivered baseline while tracking capability follow-ons here; rate repair
extends the governed migration lane under `W10-BOND-001`. Review/temporal evidence extends
`W10-PROV-001` and feeds `W5X-OEG-001`. `W11-PACK-001` owns composition and later candidate exports;
`W11-LEGS-001` owns only the bounded vanilla swap convergence. These links assign no new product
rank, acceptance or release certification. Source/module documentation and generated registries
must describe the actual implemented slice and its validation, leaving later stages explicitly
planned. Apply the repository's Level 2 verification and separate-agent adverse review to financial
and architectural changes; existing owner merge and hosted-check authority remain in force.

### Source disposition — 2026-11-14, local verification complete; hosted checks pending

The approved sequence above now has source implementations for each bounded increment. This note
records source boundaries, not row completion, hosted-check success, deployment or operator acceptance.

- The compiled 32-class foundation composes Contracts descriptors/schema/upgrades/roles/units and
  codec/economic recovery rows, Application validators/generated decode contributions, Storage
  descriptors and Option/CustomAsset specialized writers, and existing exact accounting bindings.
  The F# union stays unchanged. Governed pack activation/version pinning remains planned.
- Passport capabilities are passive, selected-version preflights. Missing execution context stays
  Review. Portfolio counts only loaded authorized brokerage observations with explicit unresolved
  coverage. Repair discovery pages through a bounded live source scan, retaining missing-projection
  issues and refusing changed overlapping observations. Frozen scalar/evidence cohorts create
  per-record revisions and subject-addressed workflows with immutable maker/base, independent
  reviewer, resumable outcomes and explicit publication. Bond uses full schedule migration.
- Impact receipts retain exact base/proposed terms, conservative books, known periods/reports and
  typed old/new flow evidence. `LotImpactUnverified` prevents exact live-lot claims. Stale approved
  evidence needs a fresh draft/workflow. A short broad PostgreSQL dependency guard and ambient
  per-item canonical mutation close the approval/publication race; dispatch follows commit.
- CD version 6 adds explicit start/principal/cadence terms for the tested fixed-bullet Actual/365
  AtMaturity, par-lot/NoAmortization profile. Existing Bond/StructuredCredit version-5 receipts stay
  unchanged; CDs below declaration stamp 6 retain old version-5 analytical capture/replay. Declared
  version-6 CDs must pass the strict profile without legacy fallback. Write-once daily-lot
  economic-obligation claims exclude schedule/template/model versions
  and block overlapping schedules/cadences before intake, including restart/retry.
- Optional `vanilla-irs-v1` converges a bounded fixed/floating two-leg Swap on the same contractual
  engine. Swap remains version 4/read 4/floor 1; omitted request behavior and old hashes remain.
  Dated fixing/forward authority, excluded structures and the nonposting boundary remain enforced.
- CustomAsset usage reads exact approved profile/version references and required-field presence;
  malformed/nonapproved/unknown references remain distinct. Manual authorized developer export
  carries planned classes and review tasks, never record-derived defaults or runtime activation.
- Report comparisons start from prospectively retained actual journal receipt inputs and authorized
  visible report-line bindings. Current event/revision/workflow attribution distinguishes actors
  and actual approval. Legacy missing inputs/attribution remain gaps; no current-state substitute,
  historical approval visibility or complete effective-time query is claimed.

Security Master migration 069 and ledger migration 073 are new normal-runner migrations with scoped
schema-control explanations, not production execution receipts. Conditional node compatibility and
explicit authoring govern CD version-6 use; no fleet floor rises automatically. Follow
[repairs and evidence operations](../../operators/security-master-repairs-and-evidence.md) and
[terms-contract rollout](../../operators/security-master-terms-contract-rollout.md). The increment
adds browser/shared-service behavior without a WPF implementation change. Remaining roadmap activation
and acceptance conditions stay intact.

### Local verification evidence — 2026-11-14

The financial slice below passed **8,826 tests, zero failures and zero skips**, including PostgreSQL
revision-workflow/publication checks and cross-process recurring economic-obligation claims. It is a
filtered `Meridian.Tests` run, not a full-solution result:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --no-restore --filter 'FullyQualifiedName~SecurityMaster|FullyQualifiedName~RecurringJournal|FullyQualifiedName~InstrumentCapabilityReadServiceTests|FullyQualifiedName~ImpactDependencyGuardTests|FullyQualifiedName~SecurityRevisionWorkflow|FullyQualifiedName~OperationsContinuityWorkflow|FullyQualifiedName~LedgerReportingAuthoritativeSourceTests|FullyQualifiedName~ReportLedgerAmountProvenanceTests|FullyQualifiedName~ReportingRetainedLedgerPopulation|FullyQualifiedName~WorkstationServiceCollectionExtensionsTests|FullyQualifiedName~WorkstationCloseTenantContextCompositionTests|FullyQualifiedName~PeriodAwareRestatementResolverTests|FullyQualifiedName~AffectedLedgerBookResolverTests' --nologo -v minimal -m:2
dotnet test tests/Meridian.Contracts.UnitTests/Meridian.Contracts.UnitTests.csproj --no-restore --nologo -v minimal -m:2
dotnet format whitespace Meridian.sln --verify-no-changes --no-restore --verbosity minimal
```

The Contracts run passed **502 tests with zero skips**; solution whitespace verification exited 0.
After a clean lockfile installation, the dashboard's full `npm test` run passed **6,499 tests, zero
failures and zero skips** in 75 batches using Vitest 5.1.0. The final locked production bundle uses
Vite 8.3.6; global strict TypeScript 5.9.3 and lint checks also passed:

```bash
npm --prefix src/Meridian.Ui/dashboard test
npm --prefix src/Meridian.Ui/dashboard run build
npm --prefix src/Meridian.Ui/dashboard run typecheck:strict
npm --prefix src/Meridian.Ui/dashboard run lint
python build/scripts/schema-control.py local --mode snapshot --base-ref 8d6eb1d4259a85db2032600f5e19404e9233bbd8
```

The disposable PostgreSQL 16.14 snapshot applied 183 migrations across 11 modules with zero migration
or policy errors and 303 unchanged baseline policy warnings. The reviewed candidate was promoted
through the schema tool. Maintained generation converged, all 12 reviewed source-module hashes and
source contracts validated, and unchanged-source UML renderer churn was restored.

All five synthetic browser workflows passed with matching pinned Chromium revision 1254
(158.0.8127.18) against locked Vite 8.3.6 at 1440×900 and 1366×768; repair also ran at 1920×1080.
Keyboard, evidence inspection, scoped actions and explicit refusal checks reported no relevant
page/console errors or scoped Axe issues. These local checks establish their stated development
scope. Full aggregate CI was not run. The complete `bash scripts/ci.sh --lane verify-docs` lane passed
on clean checkpoint `ca8bd50454`, including its whole-repository generated-output gate. GitHub-hosted
integration checks remain pending. No live-provider, production deployment or operator
release-acceptance result is claimed.

### Integration checkpoint — 2026-11-15, merged-head verification pending

The November 14 test counts above belong to the named pre-integration checkpoints. Integration with
current main preserves its accounting framework and generated-output delivery policy. Our unpublished
repair migration is renumbered to Security Master 069 and the revision-workflow migration to Ledger
073; main's Security Master 068 backfill and Ledger 069–072 remain unchanged. Reviewed references and
source baselines stay committed, while declared machine inventories, contract mirrors and workstation
builds remain CI artifacts. Reconcile the reviewed module hashes only after reviewing the combined
source, then run current-head financial/persistence and generation checks before claiming merge
verification. No prior test count establishes the newly combined revision's result.
