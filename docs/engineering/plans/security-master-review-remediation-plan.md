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

## Slice 7 — Terms schema and form model served to the browser workstation (proposed 2026-11-09)

**Evidence:** D9; the [2026-11-09 verification pass](../../architecture/security-master-extensibility-review.md#verification-pass--2026-11-09--dual-model-audit-reconciliation).
`SecurityAssetTermsSchema` and `SecurityAssetTermFormModel` drive the WPF passport editor, but no
`Meridian.Ui.Services` endpoint serves either, so the dashboard's term editing is limited to
hand-built bond panels and new asset classes get desktop editors only. Serve the schema and the
shared form model through the `Meridian.Ui.Services`/`Meridian.Ui.Shared` read-model seam and
derive the browser editor from the same declared facts the WPF editor consumes. Define the D9
parity contract between the browser form model and the shared term-form model at that seam, not
as mirrored field lists.

**Proof:** the browser editor renders every catalog class from the served schema; a schema field
added to one class appears in both clients without a client-side code change; parity is asserted
against the declared schema rather than a copied list; existing bond panels keep their behavior or
are explicitly retired.

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

## Slice 9 — Tag stored legacy payloads, then retire shape detection (proposed 2026-11-09)

**Evidence:** D1, ADR-024; the 2026-11-09 verification pass. Three payload families share one
`schemaVersion` key, and untagged stored rows are classified by
`SecurityTermsEnvelope.ResolveFormat`'s shape heuristic, which ADR-024 documents and guards.
Backfill `termsFormat` onto stored rows using the same classification the readers already apply,
verify that no untagged payloads remain, and only then narrow the readers to declared formats.
The heuristic stays until the verification query returns zero.

**Proof:** the backfill is idempotent and classifies exactly as the runtime heuristic; a
verification query shows zero untagged rows; retained fixtures read identically before and after;
after removal, a new untagged payload fails loudly rather than being guessed. The migration needs
the repository's independent schema/storage review.

## Decision — single economic model consolidation (recorded 2026-11-09, not an accepted slice)

The 2026-11-09 pass records the proposal to make `SecurityTermModules` canonical, retire the
`SecurityKind` bridge (`SecurityMasterLegacyUpgrade.fs`, `SecurityMasterMapping.cs`,
`SecurityEconomicDefinitionAdapter`) and remove `retainedEconomicTerms`. This is a domain-owner
decision, not a slice: it supersedes guarantees the rolling review currently pins (D1's retained
economics and explicit refusals; slice 3's historical reads without rewriting retained terms) and
needs a payload-upgrade design, replay-compatibility proof and an event-versioning ruling before
any implementation starts. Slices 7–9 are deliberately shaped to stay useful under either
outcome.
