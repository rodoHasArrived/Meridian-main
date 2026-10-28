# Security Master Review Remediation Working Plan

**Status:** draft
**Owner:** core-team
**Reviewed:** 2026-10-28
**Scope:** Engineering
**Source evidence:** `6bcc7a0262ab0870cee32cb2cb344851e9da7e89`

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

## Slice 6 — Remaining catalog, identifier and consumer convergence

**Evidence:** D1, D3, D4, D8, D9 and D11. Separate small changes by owner:

- Replace the WPF multiplier substring rule with the same declared fact the workbench uses.
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
