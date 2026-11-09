# Security Master Review Remediation Working Plan

**Status:** draft
**Owner:** core-team
**Reviewed:** 2026-11-09
**Scope:** Engineering
**Source evidence:** `6bcc7a0262ab0870cee32cb2cb344851e9da7e89` (slices 1–6);
`fc848789e637f3e241698c84b5415a1a9e0ed32d` (slices 7–9 and the consolidation decision, 2026-11-09)

This plan consumes the [October review consolidation](../../architecture/security-master-extensibility-review.md#consolidated-october-review-and-dispositions--2026-10-28),
including the dated observations and corrected prescriptions from #3111, #3188, #3196, #3220,
#3234, #3240, #3247 and #3263. It replaces #3240's unaccepted October 20 design as a current
working input. The immutable original plan is linked in the review's coverage ledger.

The order below is a proposed review sequence by correctness risk. It assigns no roadmap rank,
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
[slice-4 disposition](../../architecture/security-master-extensibility-review.md#slice-4-disposition--2026-11-04-d10).
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
[D7 slice-5 disposition](../../architecture/security-master-extensibility-review.md#d7--audit-history-and-serialization-distinguish-current-state-from-retained-evidence).
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
- Verify actual ledger/NAV/risk/client consumption of income, factor, corporate-action and
  unitized-lot capabilities with retained end-to-end examples. Keep override enrichment fields
  separate from economic-term fields.
- Reassess specialized Option projection and CustomAsset projection separately. Codec generation
  must preserve retained economics and precise refusal paths. Existing graph/OTC/curve/schedule
  increments and their acceptance limitations remain owned by their ADRs and resolution sections.

**Proof:** coverage fails when a mapping/consumer is omitted, retained values keep their meaning,
and no unrelated operational override field disappears. Each implementation updates the rolling
review with a dated disposition and exact validation evidence; it does not add another unchanged
scheduled-review PR.

## Slice 7 — Term-form parity contract between the shared model and the browser mirror (2026-11-09)

**Evidence:** D9; the [2026-11-09 verification pass](../../architecture/security-master-extensibility-review.md#verification-pass--2026-11-09--dual-model-audit-reconciliation).
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
[slice-8 disposition](../../architecture/security-master-extensibility-review.md#slice-8-disposition--descriptor-owned-consumer-rules--2026-11-09).
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
StructuredCredit twins do today; Bond's floating-coupon twin does not yet and keeps it);
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
  subclass), `ToleratedMistypes` (the legacy factorSchedule pointer). See the review record.
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
  of reading past the table; record construction stays per-class until S3-E. Cohorts 1–3 (FxSpot,
  Deposit, MoneyMarketFund, Commodity, CryptoCurrency, Cfd; CertificateOfDeposit,
  CommercialPaper, TreasuryBill, Repo, CashSweep, OtherSecurity, Warrant; Option, Future,
  FxForward, NonDeliverableForward, CreditDefaultSwap, Swaption, CapFloor, StructuredNote —
  21 of 32 classes)
  are migrated and ledgered
  (`SchemaBoundDecodeClasses`), each class's fully-populated twin exercising every bound read in
  the sweep suite; remaining classes migrate cohort by cohort under the same net (sweeps, write
  goldens, corpus — behavior-neutral by the full namespace sweep). Bespoke arms remain for the
  structural decodes (coupon structures, schedules, swap legs, profile envelopes).
- **S3-E — shrink `SecurityKind`'s per-class records** to what the interpreter needs —
  explicitly decision-gated: it alters domain types, and the domain owner sequences it.
