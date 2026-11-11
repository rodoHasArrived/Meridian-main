# ADR-027: Single Economic Model Consolidation

**Status:** Accepted
**Date:** 2026-11-09 (proposed and accepted)
**Owner:** core-team
**Reviewed:** 2026-11-09
**Deciders:** core-team (domain owner accepted 2026-11-09)
**Supersedes:** —
**Superseded by:** —

## Context

The Security Master describes the same security's economics twice:

- **The per-class model.** `SecurityKind` (`src/Meridian.FSharp/Domain/SecurityMaster.fs`), a
  32-case union whose per-class payload records (`BondTerms`, `OptionTerms`, …) mirror the flat
  per-class JSON family that ADR-024 governs as the write format. Commands, write validation and
  the projection row (`securities.asset_specific_terms`) live in this world. 14 source files
  consume it, concentrated in the codec, schema and mapping layer.
- **The module model.** `SecurityTermModules` (`src/Meridian.FSharp/Domain/SecurityTermModules.fs`),
  25 optional modules carried by `SecurityEconomicDefinition`, which 27 source files consume —
  reporting, accounting event sourcing, workbench reads. Call schedules, covenants, coupon
  structures and dividend terms are each defined in both models.

They are joined by roughly 2,800 lines of bridge: `SecurityMasterLegacyUpgrade.fs` (1,120 lines,
flat record → economic definition), `SecurityMasterMapping.cs` (1,692 lines, DTO/command/record
mapping) and `SecurityEconomicDefinitionAdapter.cs` (360 lines). The write path round-trips on
every amendment: a flat patch becomes a typed command, domain logic runs on the flat record, and
`SecurityEconomicDefinitionAdapter.ToEconomicRecord` re-derives the economic definition for the
event envelope (`SecurityMasterService.cs`, amend seam).

Three facts bound the decision space; each was verified against source at the date above:

1. **Events are already economic-canonical.** `SecurityMasterEventPayloads.EconomicDefinition` is
   "written by every current command path"; the flat `SecurityProjection` family is legacy, "still
   replayed by converting it into an economic definition". Consolidation therefore does not need a
   new event family — the event store already holds the target model.
2. **The economic→flat direction is declared lossy.** The v2 economic serializer covers 14 of the
   25 modules (`SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster`: 5 `BridgedModules`, 9
   `DroppedModules`, both pinned by coverage tests). The 11 newer modules (covenants, dividend
   schedule, credit rating, ESG, inflation-linked, sinking fund, …) have no economic-payload home:
   they are derived from flat keys on every read. For those modules, **flat is the system of
   record today**, whatever the model diagram suggests.
3. **`retainedEconomicTerms` is the patch over the loss.** When a projection descends from an
   economic event whose modules the flat codec cannot express, the original economic document
   rides inside the flat payload under a reserved key
   (`SecurityTermsEnvelope.RetainedEconomicTermsKey`), is carried across amendments
   (`CarryRetainedEconomicTerms`) and folded back on read (`FoldRetainedEconomicTerms`). D1 of the
   extensibility review pins that any codec work must preserve these retained fields and their
   refusal paths; review slice 3 pins historical reads that never rewrite retained terms.

The cost of the duality is recorded in the
[2026-11-09 dual-model audit reconciliation](../architecture/security-master-extensibility-review.md#verification-pass--2026-11-09--dual-model-audit-reconciliation):
every new asset class pays both models plus bridge arms, four term concepts are defined twice, and
the retained-terms workaround is permanent until something changes. The
[remediation working plan](../engineering/plans/security-master-review-remediation-plan.md)
records consolidation as a domain-owner decision; this ADR is that decision's writeup.

## Decision

Accepted 2026-11-09: **consolidate on the economic model as the single domain model, in
three gated stages, while keeping the flat family as a generated storage and wire codec.** The
stages are ordered so each is independently valuable and the decision can stop after any of them.

1. **Stage 1 — make the flat codec lossless (ends `retainedEconomicTerms` for new writes).**
   Give the 9 dropped and 11 unserialized modules declared flat homes through ADR-024 decision-3
   expand/contract steps (new per-class terms versions), so every module a record holds survives
   the economic→flat→economic round trip. New writes then stop carrying
   `retainedEconomicTerms`; the fold stays as a read-only path for retained history, exactly as
   review slice 3 requires. No stored payload is rewritten.
2. **Stage 2 — move commands and validation onto `SecurityEconomicDefinition`.** Amend, create,
   term-block and approved-merge paths construct and validate the economic definition directly;
   the flat record and `SecurityKind` stop being the domain representation and become the codec
   boundary. The amend-seam round trip (`ToRecord` → `toEconomicDefinition`) disappears;
   `SecurityMasterLegacyUpgrade` is confined to replaying legacy `SecurityProjection` events —
   the read-only legacy upcaster ADR-024 item 5 already requires.
3. **Stage 3 — generate the flat codec from the schema.** With the codec the only remaining
   consumer of per-class term records, generate (or fully assert) both codec arms from
   `SecurityAssetTermsSchema`, closing the extensibility review's oldest open priority (D1).
   `SecurityKind`'s hand-written per-class payload records then shrink to whatever the generator
   needs; per-class behaviour already lives in the descriptor catalog (review slices 6 and 8).

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Per-class model | `src/Meridian.FSharp/Domain/SecurityMaster.fs` | `SecurityKind` union and per-class term records |
| Module model | `src/Meridian.FSharp/Domain/SecurityTermModules.fs` | The term modules (25 at acceptance; 29 since the stage-3 S3-E module homes) |
| Economic definition | `src/Meridian.FSharp/Domain/SecurityEconomicDefinition.fs` | Classification + modules record events carry |
| Bridge (upgrade) | `src/Meridian.FSharp/Domain/SecurityMasterLegacyUpgrade.fs` | Flat record → economic definition |
| Bridge (mapping) | `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs` | Commands, DTOs, event envelopes |
| Bridge (adapter) | `src/Meridian.Application/SecurityMaster/SecurityEconomicDefinitionAdapter.cs` | Write-path derivation; retained-terms carry |
| Retained terms | `src/Meridian.Contracts/SecurityMaster/SecurityTermsEnvelope.cs` | `retainedEconomicTerms` carry/fold |
| Economic↔flat loss ledger | `src/Meridian.Contracts/SecurityMaster/SecurityAssetSpecificTermsUpcasterChain.cs` | `BridgedModules` / `DroppedModules` |
| Event families | `src/Meridian.Contracts/SecurityMaster/SecurityEvents.cs` | `EconomicDefinition` vs legacy `SecurityProjection` |

Every component above is existing evidence. Stage 1 is in progress as remediation-plan slice 10
(per-module increments with their own proof obligations); stages 2 and 3 are accepted but not
started.

## Preconditions the decision item names

- **Payload-upgrade design.** Stage 1 is an ADR-024 decision-3 expand/contract rollout per class
  (readable-before-writable terms versions), not a stored-payload rewrite — ADR-024's rejected
  Alternative 1 stays rejected. The remaining untagged-payload population is independently
  measured by the quality report's `UntaggedTermsPayloadCount` (remediation slice 9) and is a
  separate drain.
- **Replay-compatibility proof.** Events already carry the economic definition, so stages 1–2
  must prove, with a committed replay corpus (extending `SecurityTermsVersioningTests` and the
  schedule-replay refusal suites), that rebuilt state is byte-identical before and after the
  command-path move, retained-terms fixtures included.
- **Event-versioning ruling.** No new event family. Module additions keep versioning through
  `EconomicTermsSchema` as today; the legacy `SecurityProjection` converter is declared the
  permanent read-only replay upcaster. If stage 2 changes the economic payload's serialized
  shape in any way, that is a new `EconomicDefinitionVersion` with both versions replayable —
  never an edit of a shipped version (consistent with ADR-024's append-only version rule).

## Rationale

The event store already chose the economic model; the duality persists only in the write path and
the projection codec. Making the codec lossless first (stage 1) removes the standing data-safety
risk (`retainedEconomicTerms` and the enumerated module loss) while every later stage remains
optional. Moving commands second (stage 2) deletes the per-write round trip where bugs
concentrate, without touching storage formats. Generating the codec last (stage 3) is the step
that actually cuts the per-class cost the audit measured, and it only becomes safe once the codec
is lossless and single-purpose. Staging also respects the pinned guarantees: D1's retained-field
preservation holds through stage 1's fold-on-read, and slice 3's no-rewrite rule holds because no
stage rewrites stored payloads or events.

## Alternatives Considered

### Alternative 1: Keep both models indefinitely (status quo)

**Pros:** No migration risk; the bridge is coverage-tested; slices 6–8 already moved per-class
behaviour onto descriptors, so the worst drift class is contained.
**Cons:** Every new asset class pays two models plus bridge arms; four term concepts stay defined
twice; `retainedEconomicTerms` remains a permanent workaround; the 11 unserialized modules keep
flat as their silent system of record.
**Why not preferred:** The audit's cost finding stands; the duality is paid on every class and
every write, and the containment is test effort, not simplification.

### Alternative 2: Store the economic document as the projection payload for every class

**Pros:** One stored family; no flat codec to keep lossless.
**Cons:** Contradicts ADR-024's accepted write-format machinery (per-class terms versions,
workbench field edits anchored to flat schema paths, SQL projections and quality rules reading
flat keys); requires either rewriting every stored row (ADR-024's rejected alternative) or
dual-reading both families forever; large blast radius through storage, workbench and reporting
at once.
**Why rejected:** Highest-risk path to the same end state; stage 1 + stage 3 obtain the benefits
without changing what is stored.

### Alternative 3: Consolidate on the per-class model and retire the modules

**Pros:** The flat family is already the storage and write format.
**Cons:** The event store writes economic definitions on every command path — reversing means a
new event family or a permanent inverse conversion; 27 consuming files lose the structured model;
the 9 dropped modules have no flat home to lose gracefully; the WPF/browser read models consume
economic shapes.
**Why rejected:** It consolidates onto the narrower model and reverses the direction the event
store already committed to.

## Consequences

Accepted, this ADR retires `retainedEconomicTerms` for new writes (stage 1), the write-path
round trip (stage 2) and the hand-written codec arms (stage 3), in that order, each behind its
own replay proof. Rejected or deferred, the bridge remains the test-guarded status quo and the
remediation plan's decision item records the standing cost. Either outcome should be recorded by
updating this ADR's status and the
[remediation working plan](../engineering/plans/security-master-review-remediation-plan.md)
decision item.
