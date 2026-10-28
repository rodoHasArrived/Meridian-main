# ADR-024: Terms Payload Format and Version Policy

**Status:** Accepted
**Date:** 2026-10-17
**Owner:** core-team
**Reviewed:** 2026-10-17
**Deciders:** core-team
**Supersedes:** —
**Superseded by:** —

## Context

A security's `AssetSpecificTerms` slot holds three payload families — flat per-asset-class terms,
the structured economic-terms document, and the custom-asset profile envelope — behind one shared
`schemaVersion` integer. Readers historically had to guess the family from the integer plus the
presence of module keys, which is why flat version `2` is permanently reserved
(`AssetSpecificTermsSchema.ReservedForEconomicTerms`): declaring a genuine flat v2 would route every
such payload through the economic → flat bridge and silently empty it. The
[extensibility review](../architecture/security-master-extensibility-review.md) (D1–D3) records the
failure modes, including the `securities.schema_version` column having had two definitions.

`SecurityTermsEnvelope` already defines the fix: every payload declares its family in `termsFormat`
and flat payloads their per-class contract version in `assetClassTermsVersion`, with `StampFlat` as
the single stamping routine and `ResolveFormat` as the single classifier (declared tag wins, shape
rule only for untagged payloads). What remained was that some writers bypassed the envelope, the
stored family was not queryable, and nothing recorded the policy that lets the shape rule retire.

## Decision

1. **Every writer stamps the family.** A flat payload carries `schemaVersion`, `termsFormat =
   "flat"` and `assetClassTermsVersion`, written through `SecurityTermsEnvelope.StampFlat` or by
   emitting the same three keys. The stamp is the writer's effective version: the asset class's
   `SecurityAssetClassDescriptor.TermsVersion`, raised to the highest `Since` among the versioned
   terms the record holds (decision 3). The economic
   definition adapter stamps `termsFormat = "economic"`; the custom-asset profile document is the
   profile family and is emitted verbatim. No code path persists an untagged payload.
   *Amended 2026-10-28 (stamp model S2, in effect from the contract step, increment 5b-2):* the
   stamp describes the document. It is the highest `Since` among the versioned terms the record
   holds, floored at the class's stamp floor (its write version before the contract step, raised by
   any later transform step) rather than at the raised `TermsVersion`. Until a contract step raises
   a write version the floor equals `TermsVersion`, so the rule above describes every write today;
   see the increment 5a application below.
2. **The stored family is a column.** Migration `042_security_master_terms_format_column.sql` adds
   `securities.terms_format`, backfilled non-destructively: a declared tag wins; an untagged row is
   classified by the same shape rule `ResolveFormat` applies (profile envelope → `customProfile`;
   version-2 document carrying an economic module key → `economic`; otherwise `flat`). The projection
   store writes the column from `ResolveFormat` on every upsert and refuses an unknown declared format
   with a precise reason. The stored payload is never rewritten by the store or the migration.
3. **Per-class evolution does not touch the codec number, and runs expand-then-contract.**
   `schemaVersion` stays the codec version of the family and the reserved value `2` is never declared
   as a flat version. A class's terms evolve in two deployable steps:
   - **Expand.** The new term is tagged `.Since(n)` and the class raises
     `SecurityAssetClassDescriptor.ReadableTermsVersion` to `n`, above its write version
     (`TermsVersion`). Versions above the write version are reached only through identity upgrade
     steps. Every node then reads version `n`: the read guard accepts stamps up to
     `ReadsTermsThrough`, and decoding a versioned term is gated on the payload's own stamp.
   - **Write mode refuses what it cannot write.** Every create, amend, import, term-block amend and
     approved field-edit merge refuses a payload stamped above the write version, or one that
     populates a term above it, with a precise reason. Absent, `null`, `[]` and objects whose members
     are all unset mean unset and are accepted.
   - **Contract.** A later change raises `TermsVersion` to `n` once every node in the fleet reads
     `n`. Committed readable versions are append-only: a version is never edited after it ships, so
     a further addition takes the next readable version.
   - **Versioned class terms are flat-codec-only.** A profile envelope of a profile-capable class
     (`customProfileId` or a `profileFields` object) is never walked, decoded or validated for
     versioned class terms, nor convention-checked on write; only its stamp is checked. Its fields
     belong to the approved profile. The contract step keeps this rule.
   - **The written form is frozen when the term is declared.** For the `scheduleConventions` block,
     the key is present whenever the writer's effective version is at or above its `Since`: `null`
     when nothing is declared, otherwise all twelve members in alphabetical order, with `null` or
     `[]` for an unset member. Swap legs follow the same rule, with the key on every leg. Inner JSON
     kinds are frozen too: conventions are strings, dates are ISO `yyyy-MM-dd` strings,
     `paymentLagBusinessDays` is an `Int32` and `paymentCalendars` is an array of strings.
     - The `callSchedule` and `putSchedule` arrays (`Since` 3 on Bond and CertificateOfDeposit, 4 on
       StructuredCredit) are always present at or above that version: `[]` when nothing is
       declared, otherwise one object per entry in stored order. Each entry carries all its members
       (twelve for a call, nine for a put) in alphabetical order, with `null` for an unset one.
       Below the `Since`, the keys are absent.
     - An entry whose members are all `null` is unset: a write accepts it, and a re-serialization
       drops it.
     - An entry with content but no date is populated, so a write refuses it, and the round-trip
       guard refuses to re-serialize a row stamped at or above the arrays' `Since` that carries one.
       It decodes to no term.
     - Inside an entry, dates are ISO `yyyy-MM-dd` strings, prices and spreads are JSON numbers,
       notice counts are `Int32`, `exerciseFrequency` is a convention string (`CouponFrequency`)
       and the vocabularies are verbatim strings.
   - **Container shapes are frozen once readable.** The member list of a versioned container, the
     `scheduleConventions` block and the call and put entries alike, is frozen once readable
     (`tests/fixtures/security-master/terms-element-shapes/versioned-containers.json`). A member
     added later needs a new container key at a new readable version, or a guard and reader change
     that gates members on their own version. A later class-wide term belongs at the top level,
     which the next readable version can add.
   - **Round trip.** A node refuses to re-serialize a row stamped above its write version when it
     cannot reproduce that row's versioned content (a deactivation or a patch-less amend), rather than
     drop what it cannot decode. It compares only the versioned terms the row's own stamp reaches: a
     term above the stamp is an undeclared key of that row, and is dropped as on a node that reads no
     further.
4. **Validation surfaces the backlog.** `SecurityValidationService` reports
   `SM_ASSET_SPECIFIC_TERMS_FORMAT_MISSING` (warning, names the inferred family) for an untagged
   payload and `SM_ASSET_SPECIFIC_TERMS_FORMAT_UNKNOWN` (error) for a tag this node does not know.
5. **Retirement is gated on evidence.** The shape rule (`IsEconomicTermsShape`, the untagged branch of
   `ResolveFormat`) and the reserved-2 routing stay until `select count(*) from securities where
   asset_specific_terms->>'termsFormat' is null` is zero on every governed deployment and the
   event-sourced history has a read-only legacy upcaster for replay. That removal is a separate,
   later change; untagged payloads then fail with a precise error instead of being inferred.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Envelope | `src/Meridian.Contracts/SecurityMaster/SecurityTermsEnvelope.cs` | Keys, `StampFlat`, `ResolveFormat`, read guard, write guard (`TryGetWriteRefusal`) |
| Catalog | `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs` | Per-class write version (`TermsVersion`) and readable version (`ReadableTermsVersion`, `ReadsTermsThrough`) |
| Term schema | `src/Meridian.Contracts/SecurityMaster/SecurityAssetTermsSchema.cs` | `.Since(n)` tags, `WritableFields`, `PopulatedTermsAbove`, `NormalizedTermsAbove` |
| Versioned terms | `src/Meridian.Contracts/SecurityMaster/SecurityVersionedTerms.cs` | Container keys, `IsVersionedContainer`, the one decode predicate (`Decoding`), the shared block reader and the declared reads |
| Round-trip guard | `src/Meridian.Application/SecurityMaster/SecurityMasterService.AheadOfWriteTerms.cs` | Refuses to re-serialize an ahead-of-write row it cannot reproduce |
| Versions | `src/Meridian.Contracts/SecurityMaster/SecurityMasterSchemaVersions.cs` | Codec versions and the reserved value |
| Upcaster chain | `src/Meridian.Contracts/SecurityMaster/SecurityAssetSpecificTermsUpcasterChain.cs` | Tag-first dispatch; shape rule for untagged payloads |
| Migration | `src/Meridian.Storage/SecurityMaster/Migrations/042_security_master_terms_format_column.sql` | `terms_format` column, backfill, check constraint |
| Store | `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore.cs` | Writes `terms_format`; refuses unknown formats |
| Writers | `src/Meridian.Application/SecurityMaster/SecurityMasterCsvParser.cs`, `src/Meridian.Application/SecurityMaster/EdgarIngestOrchestrator.cs`, `src/Meridian.FSharp/Interop.SecurityMaster.fs`, `src/Meridian.Wpf/ViewModels/SecurityMasterEditViewModel.cs` | Flat writers that stamp the envelope |
| Validation | `src/Meridian.Application/SecurityMaster/Validation/SecurityValidationService.cs` | Format rules; the informational terms-version notice |
| Upgrade hook | `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassTermsUpgrader.cs` | Runs a class's declared steps on read; identity steps return the stored payload itself |
| Upgrade steps | `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassTermsUpgrade.cs` | Identity and transform steps declared on `SecurityAssetClassDescriptor.TermsUpgrades` |
| Version manifests | `tests/fixtures/security-master/terms-versions` | Committed per-class key sets for every terms version |
| Tests | `tests/Meridian.Tests/SecurityMaster/SecurityTermsVersioningTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityValidationServiceTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityMasterPostgresRoundTripTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityAssetClassTermsHistoryTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityAssetClassTermsUpgraderTests.cs` | Stamping, SQL/code parity, column round trip, version history, upgrade hook |

Items 1–4 are implemented; decision 3's upcaster is `SecurityAssetClassTermsUpgrader` (2026-10-23),
and its expand step is implemented for the `scheduleConventions` block and the `callSchedule` and
`putSchedule` arrays (2026-10-25), with one decode predicate for them (2026-10-28). Decision 1's
S2 amendment takes effect with the contract step.
Item 5 (retiring the shape rule and the reserved value) is proposed and waits on the backfill
evidence described above.

## Rationale

The tag is already the dispatch key for every payload written since it was introduced; making it
mandatory on write and queryable in storage lets the remaining untagged population be measured and
drained instead of inferred forever. Promoting the family to a column mirrors migration 024's
`schema_version` and gives the column one definition shared by backfill and upsert. Keeping the
shape rule until the backlog is provably empty avoids the silent-loss failure the reserved value was
introduced to prevent.

## Alternatives Considered

### Alternative 1: Rewrite stored payloads to add the tag

**Pros:**
- Readers could drop the shape rule immediately.

**Cons:**
- Mutates every stored blob, including event-sourced history that replay must decode byte-for-byte.
- Any misclassification by the one-time shape pass is permanent.

**Why rejected:** The column records the classification without touching the payload; a wrong
inference stays correctable.

### Alternative 2: Split `schemaVersion` into per-family version keys now

**Pros:**
- Removes the numeric collision at the source.

**Cons:**
- Every reader of archived events and WAL would need both vocabularies at once.
- The collision is already neutralised for tagged payloads by `termsFormat`.

**Why rejected:** Deferred; `assetClassTermsVersion` already gives flat terms an independent
version stream, and the remaining collision only matters for untagged legacy rows.

## Consequences

### Positive

- Every new payload states its family; shape inference becomes a measured legacy backlog.
- `where terms_format = 'economic'` answers audit and compatibility questions directly.
- Operators see untagged rows as validation warnings naming the inferred family.

### Negative

- One more column to keep in step with the payload (the store derives it on every upsert).
- Two implementations of the shape rule (SQL and C#) until the rule retires; a unit test pins the
  SQL to the code's vocabulary.

### Neutral

- Catalog manifests under `database/manifest/` are refreshed by the schema-control snapshot workflow.

## Compliance

### Code Contracts

```csharp
// Writers: a flat payload carries the full envelope.
SecurityTermsEnvelope.StampFlat(json, assetClass);
// Writers holding a term above the write version stamp the version it needs, within ReadsTermsThrough.
SecurityTermsEnvelope.StampFlat(json, assetClass, assetClassTermsVersion);
// Write paths: refuse a payload this node reads but cannot write.
SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, payload);
SecurityTermsEnvelope.TryGetWriteRefusal(assetClass, payload, out var reason);
// Profile envelopes carry no versioned class terms; only their stamp is checked.
SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, payload);
// Which versioned containers a payload decodes: a read by its own stamp, a write by the write
// version, a profile envelope none.
SecurityVersionedTerms.Decoding(assetClass, payload, writeMode).Decodes(field);
// The read guard's refusal, without throwing.
SecurityTermsEnvelope.TryGetReadRefusal(assetClass, payload, out var readRefusal);
// Read ceiling, and the fields an authoring surface may offer.
SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
SecurityAssetTermsSchema.WritableFields(assetClass);
// Populated terms above a version, and their normalized form for the round-trip guard.
SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, termsVersion);
SecurityAssetTermsSchema.NormalizedTermsAbove(assetClass, payload, termsVersion, out var conflictingPaths);
// The round-trip guard bounds the normalized form by the row's own stamp.
SecurityAssetTermsSchema.NormalizedTermsAbove(assetClass, payload, termsVersion, throughVersion, out conflictingPaths);
// Readers: never classify by shape when a tag is present.
SecurityTermsEnvelope.ResolveFormat(payload);
// Store: the column is derived from the payload, never from the post-upcast result.
PostgresSecurityMasterStore.ResolveStorableTermsFormat(record);
// Readers: an older per-class terms version upgrades on read; identity steps return the payload unchanged.
SecurityAssetClassTermsUpgrader.UpgradeForRead(assetClass, payload);
```

### Runtime Verification

- `SecurityTermsVersioningTests` — stamping idempotence, tag-first dispatch, SQL/code parity for
  migration 042, store refusal of unknown formats.
- `SecurityValidationServiceTests` — `SM_ASSET_SPECIFIC_TERMS_FORMAT_MISSING` and `_UNKNOWN`.
- `SecurityMasterPostgresRoundTripTests.UpsertProjection_PersistsTheTermsFormatColumn_FromTheTagOrTheShapeRule`
  (PostgreSQL integration lane).
- `SecurityAssetSpecificTermsUpcasterChainTests` — no accepted flat version equals
  `EconomicTermsSchema.Current`.
- `SecurityAssetClassTermsHistoryTests` — every catalog class has a committed manifest for versions
  1 to its `ReadsTermsThrough`, the latest manifest equals the live schema, each field's `.Since(n)` is
  the first version that lists it, steps are contiguous, and an identity step is legal for the
  manifest diff it spans. Five guards hold the expand step: steps at or above the write version are
  identity steps; `ReadableTermsVersion` is null or above the write version; each readable-only
  version introduces a top-level or element field; every element field's `Since` is within what
  its class reads, optional above version 1; and the members of every container introduced above
  version 1 equal its frozen entry in `versioned-containers.json`
  (`VersionedContainerShapes_AreFrozenOnceReadable`).
- `SecurityTermsWriteGoldenTests` — the written terms, economic terms, registry DTOs, term blocks and
  validation reports equal their pre-expand bytes. Before increment 4b the fixture gained the
  validation reports of stored rows carrying stray `callSchedule` and `putSchedule` keys, and the
  terms a re-serialization writes for the increment 4a twins (with their economic terms) and for
  rows carrying a stray `callSchedule` at stamps 1, 2 and 3; increment 4b leaves every entry
  unchanged.
- `SecurityAssetClassTermsUpgraderTests` and `SecurityAssetClassTermsUpgradeWiringTests` — an
  identity step returns the stored element itself, a transform step returns a new payload stamped at
  the current version, everything outside the flat codec passes through, and a version-1
  StructuredCredit payload reads exactly as its version-2 twin on every wired read path.
- `SecurityAssetTermsSchemaRoundTripTests` (readable twins) — at every readable version above the
  write version (Bond and CertificateOfDeposit 2 and 3, StructuredCredit 3 and 4, Swap 2), a twin
  populating every term up to that version round-trips byte-stably at its own stamp, keeps stored
  entry order, writes each entry's members in ordinal order, and is refused by create and amend; a
  stray block or schedule below its `Since` is not decoded; and foreign rows (partial entries,
  all-null entries, rows that downgrade) persist pinned bytes through deactivate and patch-less amend.
- `SecurityMasterServiceSnapshotTests` (ahead-of-write partial) — the round-trip guard's bound: a
  decodable Bond stamped 3 persists its arrays and stamp byte for byte; a Bond stamped 2 or a
  StructuredCredit stamped 3 carrying a stray `callSchedule` persists the golden bytes; an entry the
  readers cannot type is refused on deactivate and patch-less amend with nothing written; and a
  StructuredCredit profile envelope carrying call and put schedules at its root and in
  `profileFields` is created at stamp 1 and deactivated at stamp 4 byte for byte.
- `SecurityEconomicTermsClassAwareBridgeTests` and the F# `DomainTests` — the writer's effective
  version and frozen entry form, and exact economic `Call` goldens for declared schedules under the
  rule in [ADR-025](025-contractual-schedule-kernel.md#applications).
- `SecurityVersionedTermsTests` (increment 5a) — the predicate against a verbatim copy of the decode
  ceiling it replaced, for every class, versioned container, stamp, envelope shape and mode; Read
  mode by the row's own stamp; the neutrality precondition (every versioned container above its
  class's write version, retargeted by increment 5b-2); the shared reader against a verbatim copy
  of the mapping's under the invariant and de-DE cultures; the declared reads; and
  `TryGetReadRefusal`'s exact texts.
- `ScheduleConventionsDecodeGoldenTests` (increment 5a) — 1,065 decode cases committed before the
  predicate and the shared reader replaced the mapping's, unchanged after.
- `VersionedContainerKeyLiteralRatchetTests` (increment 5a) — the quoted container keys appear in
  C# under `src/` only in the schema and `SecurityVersionedTerms`; the set only shrinks.
- Verify paths in the Implementation Links table from the repository root:

```bash
dotnet run --project build/dotnet/DocGenerator/DocGenerator.csproj -- verify-adrs --adr-dir docs/adr --src-dir src
```

This command verifies referenced files and directories exist; it does not validate behavior.

## References

- [ADR-022: Canonical Asset-Class Homes](022-canonical-asset-class-homes.md)
- [Security Master extensibility review](../architecture/security-master-extensibility-review.md)
- Migration `024_security_master_schema_version_column.sql` (the `schema_version` precedent)

---

*Last Updated: 2026-10-28*

## Applications

- **StructuredCredit terms version 2 (2026-10-18).** Added `structuredCreditKind`, `seniority`,
  `attachmentPoint`, `detachmentPoint`, `weightedAverageCoupon`, `weightedAverageMaturityMonths`,
  `weightedAverageLoanAgeMonths`, `reinvestmentPeriodEnd` and `nonCallPeriodEnd` as optional flat
  terms and bumped `SecurityAssetClassDescriptor.TermsVersion` to 2. Every addition is optional and
  carries no default that changes the reading of a version-1 payload, so the version-1 → 2 upcast is
  the identity: readers accept both versions through `SecurityTermsEnvelope` and the absent terms
  read as unset. A field transformation step is only owed when a later version renames, retypes or
  gives a default to a term. Economic replay without retained flat terms recovers the three pool
  statistics from the `structuredProduct` module and gives an IO or PO strip its kind back from the
  module's `isInterestOnly` / `isPrincipalOnly` flags; any other family is not carried by the
  economic document and stays absent rather than guessed.
- **Per-class terms upgrade hook (2026-10-23).** Decision 3's upcaster now has a home.
  `SecurityAssetClassDescriptor.TermsUpgrades` declares one step per terms version, each an identity
  or a transform (`SecurityAssetClassTermsUpgrade`), and `SecurityAssetClassTermsUpgrader` runs them
  on read. It acts only on flat-codec objects (`schemaVersion` 1 or absent, flat by tag or shape, no
  `customProfileId` or `profileFields`) stamped from version 1 up to, but not including, the class's
  current version, and passes everything else through. It never refuses a payload: refusing a newer
  stamp stays with `EnsureReadableAsAssetSpecificTerms`. It throws only when a class's declared steps
  or one of its transforms are defective. Identity steps return the stored element itself, and a
  transform returns a new in-memory payload stamped at the current version. Stored blobs, the
  `schema_version` and `terms_format` columns, event and snapshot blobs are never rewritten.
  - **Wired read paths.** The hook runs in the mapping decoder (after the read guard), the
    economic-definition adapter's retained-terms branch, the structured cash-flow resolver and the
    Security Master cash-flow factor-date walk, the obligation projection's Security Master terms
    wrapper, the relational projection decoder, and the class validators.
  - **StructuredCredit.** The 2026-10-18 identity claim above is now declared and checked. The nine
    version-2 fields are tagged `.Since(2)`, the step `Identity(1)` is declared, and the committed
    manifests show the version-1 → 2 diff adds only optional keys.
  - **Manifests.** The manifests under `tests/fixtures/security-master/terms-versions` are
    append-only history: a committed version is never edited. Only review enforces that, since the
    guard compares the live schema with the latest version alone. A class evolves by bumping its
    `TermsVersion`, tagging new fields `.Since(n)` and declaring the step; the guard then writes the
    new version for review.
  - **Backlog signal.** `SM_ASSET_CLASS_TERMS_VERSION_BEHIND` (Info) reports a flat row with an
    integer `schemaVersion` stamped, or unstamped and so read as, below its class's terms version
    (redefined from the contract step by the increment 5a entry below). It never blocks.
    `SecurityValidationGateService` drops informational issues before a workflow gate decides,
    snapshots or hands the report on, so validation snapshots omit them by design.
    The Security Master workbench counts only non-informational issues in its trust posture, and the
    WPF Security Master screen counts the same way in its validation status and coverage summary.
    The notice appears in the validation report itself (`GET /api/security-master/{id}/validation`
    and the issue lists in both workstations).
  - **Precondition for the first transform step.** Every reader of stored flat terms must be wired
    to the hook, or shown unaffected, before any class declares a transform. Unwired today:
    `PortfolioCashLadderEngine` and `SwapProjectionService`, `SecurityMasterDataQualityService`,
    `SecurityMasterWorkbenchQueryService`, the F# readers, the Strategies
    `SecurityMasterAccountingEventSourceAdapter` (factor schedule and current factor from retained
    legacy terms), `PostgresSecurityMasterStore.TryBuildOptionProjection`, persisted Asset Operations
    payloads walked by `AssetObligationProjectionService.BuildProjectedObligationEvents`, and the
    raw-payload comparisons in `OpenLotAmortizationContinuation` and the conflict detection.
    `SecurityMasterQueryService.GetByIdAsync` returns the stored detail while the as-of reads go
    through the hooked adapter, so the two would differ. The adapter's output is persisted: the
    rebuild orchestrator's full warm, incremental replay and asset-class rebuild upsert it. Only the
    asset-class rebuild has a production caller today. A transformed payload would therefore reach
    `securities.asset_specific_terms` on a rebuild, and on every boot if a startup warm were wired.
    Before the first transform, either accept that the projection is a derived read model, or move
    the hook below the cache. A transform must leave a payload already in its target shape unchanged,
    because an unstamped create, amend or import request reads as version 1. The hook's domain
    already excludes the payloads the mapping treats as profile-backed (`customProfileId`) or reads
    through `profileFields`, so a transform never rewrites an outer object whose terms the reader
    takes from elsewhere.
- **Readable before writable (priority 5, increment 4a) (2026-10-25).** Decision 3's expand step,
  applied to the `scheduleConventions` block that later schedule generation reads.
  - **Versions.** Bond, CertificateOfDeposit and Swap write terms version 1 and read through 2;
    StructuredCredit writes 2 and reads through 3. Each new readable version adds only the optional
    `scheduleConventions` object (on Swap, on each leg), reached by an identity step. The committed
    manifests record the new versions; Swap's version 2 manifest equals version 1 because manifests
    list top-level keys only, and the element-field guard covers the leg.
  - **No node writes the block yet.** The schema, registry, term blocks and field-edit validator
    offer writable fields only. A create, amend, import, term-block amend, field edit or workbench
    submission that populates the block is refused with a reason naming the term, the class, the
    term's version and the write version; one that carries a stamp above the write version is refused
    with a reason naming the class, the stamp and the versions the node reads and writes. Written
    payloads, economic terms, registry
    DTOs, term-block keys and validation reports are byte-identical to the base commit
    (`SecurityTermsWriteGoldenTests`).
  - **Reading.** A row stamped 2 (3 for StructuredCredit) decodes its block into the F# record. A
    declared block also fills the economic `Accrual` module for Bond, CertificateOfDeposit and
    StructuredCredit under the frozen rules in
    [ADR-025](025-contractual-schedule-kernel.md#applications). A row whose block is empty re-emits at
    the write version on its next re-serialization (generalized by the increment 4b entry below: the
    highest version among the terms it still holds): the data is equal, and a contract node shows
    only the informational BEHIND notice (redefined by the increment 5a entry below: under stamp
    model S2 that stamp is what a contract node writes for the terms the row holds, so it shows
    none).
  - **Deploy.** Roll every node forward to this build (and to the next readable step's build) before
    any contract step raises a write version.
  - **Rollback floor.** Once a contract node has written a version-2 or version-3 stamp, no node may
    roll back below this build, because older nodes refuse those rows.
  - **During the contract rollout.** Full-payload amends of rows stamped above the write version are
    refused on expand-only nodes: the WPF edit screen, `SecurityTermBlocks.BuildPatch` and the
    approved field-edit merge. Deactivations and patch-less amends still work. Route edit traffic to
    contract nodes.
  - **Before deploying.** Drain staged workbench `legs` revisions that carry a populated block; they
    now fail at merge with the not-yet-writable reason.
- **Call and put schedules readable before writable (priority 5, increment 4b) (2026-10-25).**
  Decision 3's expand step again, applied to the declared call and put schedules that later
  redemption and schedule consumers read.
  - **Versions.** Bond and CertificateOfDeposit still write terms version 1 and now read through 3;
    StructuredCredit still writes 2 and reads through 4. Each new readable version adds only the
    optional `callSchedule` and `putSchedule` arrays, reached by an identity step from the version
    below it. The committed manifests record the new versions, and
    `tests/fixtures/security-master/terms-element-shapes/versioned-containers.json` freezes the
    twelve-member call entry and the nine-member put entry under all six paths, beside the four
    `scheduleConventions` shapes. Swap is unchanged: it writes 1 and reads through 2.
  - **No node writes the arrays yet.** The increment 4a write guards refuse them with no logic
    change. A create, amend, import, approved field-edit merge or workbench submission that
    populates either array, under any key casing, is refused with the not-yet-writable reason naming
    version 3 (4 for StructuredCredit). A field edit of either key, under any casing, and a
    term-block amend of `callSchedule` or `putSchedule` (exact spelling) are refused with the same
    reason whatever the value, `[]` included; a term-block amend under another casing gets the
    undeclared-block reason. On 4a, creates, amends and imports dropped these undeclared
    keys silently, and field edits and term-block amends refused them as undeclared. When a payload
    populates the block and an array, the reason names `scheduleConventions`. A Bond or
    CertificateOfDeposit write stamped 3, or a StructuredCredit write stamped 4, gets the
    readable-but-not-writable stamp reason. When such a write also populates a term introduced above
    its own stamp (a Bond stamped 2 with a populated `callSchedule`), that reason names the version
    the term needs ("…from a node that writes version 3") rather than the stamp. The read-guard, write-version and `StampFlat` texts for
    these classes now say "reads versions 1 through 3" (1 through 4 for StructuredCredit). Profile
    envelopes are still checked by stamp only. The Write-mode element-convention walk now covers only
    the array fields the node writes and skips a versioned container of a profile envelope, so an
    entry's `exerciseFrequency` (a `CouponFrequency` convention) is checked only once the arrays
    become writable, and never in a profile envelope. A Bond that declares a schedule cannot be
    reclassified to StructuredCredit: it gets the `structured_credit_call_put_schedules_unwritable`
    blocker and no proposed terms.
  - **Reading.** A Bond or CertificateOfDeposit row stamped 3, or a StructuredCredit row stamped 4,
    now reads; nodes without this build refuse it. The arrays decode into the F# `CallPutSchedules`
    record behind the 4a stamp gate, so only a flat, non-profile payload stamped at or above their
    version reaches them; a stray key on a lower stamp, on an unstamped payload or in a profile
    envelope is never decoded, validated or compared. The readers are tolerant: a value that is not
    an array reads as no entries, and an entry that is not an object or has no parseable date is
    dropped. The round-trip guard compares only the versioned terms above the write version and at
    or below the row's own stamp. It refuses to re-serialize a row stamped 3 (4 for StructuredCredit)
    whose arrays would change, while a stray `callSchedule` on a Bond stamped 2 or a StructuredCredit
    stamped 3 is still dropped as 4a drops it. Its reason now ends "…that settle its versioned
    terms." The 4a downgrade is generalized: a row re-emits at the highest version among the
    versioned terms it still holds, or at the write version when it holds none. A Bond stamped 3
    that holds only conventions re-emits at 2, and one that holds nothing at 1; a StructuredCredit
    stamped 4 re-emits at 3 or 2 in the same way. The data is equal, and a contract node shows only
    the informational BEHIND notice (redefined by the increment 5a entry below: under stamp model
    S2 each of these stamps is what a contract node writes for the terms the row holds, so it shows
    none).
  - **Economic terms.** Declared schedules fill the economic `call` module under one rule, frozen in
    [ADR-025](025-contractual-schedule-kernel.md#applications). Bond and CertificateOfDeposit keep
    `isCallable` and `firstCallDate` from their scalar call terms, and `callPrice` stays null, since
    none of the three classes carries a scalar call price. The declared entries feed only the call
    and put lists, `isPuttable` and `makeWholeSpreadBps`. StructuredCredit has no scalar call terms:
    it gains a `call` module only when it declares an entry, with `isCallable` set when a call is
    declared and `firstCallDate` null. A row that declares nothing produces the same economic terms
    as before.
  - **Stack.** Increment 4b is built on the companion change that makes `OpenLotAmortization` refuse
    a `callSchedule` or `putSchedule` holding anything but null or `[]`, read from `profileFields`,
    the root terms or the common terms. Every build that contains increment 4b therefore contains
    that refusal, and the contract step's precondition that every node runs increments 4a and 4b
    covers it.
  - **Deploy and rollback floor.** Roll every node forward to this build before any contract step
    raises a write version. Once a Bond or CertificateOfDeposit row stamped 3, or a StructuredCredit
    row stamped 4, exists, no node may roll back below this build, because older nodes refuse those
    rows. While this build rolls out, a 4a node still accepts a populated array on write and drops
    it, and a node on this build refuses it; neither writes the arrays.
  - **During the contract rollout.** The full-payload amends listed for 4a are refused on
    expand-only nodes for every row stamped above the write version, now including rows stamped 3
    (4 for StructuredCredit). Deactivations and patch-less amends still work while the row
    re-serializes exactly. Route edit traffic to contract nodes.
  - **Nothing to drain.** No staged workbench revision can carry a populated array: 4a's field-edit
    validator already refused `callSchedule` and `putSchedule` as undeclared keys, and no writer
    emitted them at the top level before this build. Stored rows that carry one are the first check
    below.
  - **Before deploying, check stored rows (expect 0).** A full-payload amend of a row this query
    counts is refused on this build, where 4a dropped the key. It counts Bond, CertificateOfDeposit
    and StructuredCredit rows that populate a top-level `callSchedule` or `putSchedule` under any
    casing, excluding StructuredCredit profile envelopes. Replace `<deployment_schema>` with the
    deployment's Security Master schema.

    ```sql
    select lower(s.asset_class) as asset_class, count(distinct s.security_id)
    from <deployment_schema>.securities s
    cross join lateral jsonb_each(case when jsonb_typeof(s.asset_specific_terms) = 'object'
                                       then s.asset_specific_terms else '{}'::jsonb end) kv
    where lower(s.asset_class) in ('bond', 'certificateofdeposit', 'structuredcredit')
      and lower(kv.key) in ('callschedule', 'putschedule')
      and jsonb_path_exists(kv.value, '$.** ? (@.type() != "null" && @.type() != "array" && @.type() != "object")')
      and not (lower(s.asset_class) = 'structuredcredit'
               and ((jsonb_typeof(s.asset_specific_terms->'customProfileId') = 'string'
                     and s.asset_specific_terms->>'customProfileId' ~ '\S') is true
                    or (jsonb_typeof(s.asset_specific_terms->'profileFields') = 'object') is true))
    group by 1;
    ```

    The profile exclusion applies to StructuredCredit only, the one profile-capable class of the
    three, and follows `CarriesProfileEnvelope`: a non-blank string `customProfileId` or a
    `profileFields` object. The populated test follows `IsPopulatedTermValue`: a value is populated
    when it holds a non-null scalar at any depth.
  - **Before deploying, check inbound traffic.** No in-repo producer emits a top-level
    `callSchedule` or `putSchedule`; the economic definition adapter writes those names only inside
    the economic `call` module. Search API request logs and retained import batches (JSON, CSV,
    EDGAR, WPF) for Bond, CertificateOfDeposit and StructuredCredit payloads that populate either key
    at the top level, and notify the owners of any client that sends them. 4a dropped those keys
    silently; this build refuses them with the not-yet-writable reason. From the 2026-10-27 change
    that answers refused creates and amends with 400 (its resolution pass is in the
    [extensibility review](../architecture/security-master-extensibility-review.md)), the create,
    amend and term-block endpoints answer 400 with that reason as the Problem Details `detail`. On a
    node without that change, including one rolled back below it, only the term-block endpoint's
    pre-check of a `callSchedule` or `putSchedule` block answers 400; a create or amend request
    answers 500, with the reason only in the server log. An import reports the reason among that
    row's errors, and a workbench field edit is refused by the field-edit route (422) before
    anything is staged.
- **Versioned containers decode by their own stamp (priority 5, increment 5a) (2026-10-28).**
  Decision 3's rule that versioned class terms are flat-codec-only now has one implementation,
  shared by every reader that decides what a row declares.
  - **The predicate.** `SecurityVersionedTerms.IsVersionedContainer(field)` holds for an `Object`
    or `Array` term introduced above version 1: the `scheduleConventions` block and the
    `callSchedule` and `putSchedule` arrays of Bond, CertificateOfDeposit and StructuredCredit, and
    the block on each Swap leg. Containers that predate per-class versioning (Swap `legs`, a bond's
    `principalSchedule`, StructuredCredit's `factorScheduleEntries`) and later scalar terms are not
    versioned containers. `SecurityVersionedTerms.Decoding(assetClass, payload, writeMode)` decides
    which versioned containers a payload decodes (`SecurityVersionedTermDecoding.Decodes`): a read
    decodes one at or below the row's own stamp, a write one at or below the class's write version,
    and a profile envelope (`IsProfileGovernedPayload`) none in either mode, whatever the write
    version. The mapping (the block, the call and put arrays, the leg blocks and the Write-mode
    element-convention walk), the schema-terms validator (for versioned containers; later scalar
    terms keep the stamp ceiling) and the Security Master cash-flow engine all decide through it.
    The block is read through one shared reader, a verbatim port of the mapping's: exact-case keys,
    current-culture dates, blank strings kept, and whitespace-only calendars dropped with the rest
    untrimmed.
  - **Neutral today, and why it lands before the raise.** The old Read-mode ceiling was the higher
    of the write version and the stamp. It differs from the stamp alone only for a container whose
    `Since` is above the stamp and at or below the write version, and none exists: every versioned
    container is above its class's write version (Bond and CertificateOfDeposit 2 and 3 over write
    1, StructuredCredit 3 and 4 over write 2, Swap legs 2 over write 1). Profile envelopes and
    writes decoded only the write version's terms, which include none of them. A golden of 1,065
    decode cases, committed before the change, is unchanged, and a test pins the fact that makes the
    predicate neutral; increment 5b-2 retargets it. With the old ceiling, raising the write versions
    would have made StructuredCredit profile envelopes decode their root block and arrays, against
    this decision, and Read mode would have turned stray keys on rows stamped 1 or 2 into declared
    terms. Write mode still decodes through the write version after the raise, so increment 5b-1
    refuses an amend that would promote a stray container of the stored row.
  - **Keys.** `SecurityVersionedTerms.ScheduleConventionsKey`, `CallScheduleKey` and
    `PutScheduleKey`. `VersionedContainerKeyLiteralRatchetTests` allows the quoted keys in C# under
    `src/` only in `SecurityAssetTermsSchema.cs` and `SecurityVersionedTerms.cs`, and the set may
    only shrink; `OpenLotAmortization` and the mapping read the constants.
  - **The stamp refusal, mirrored.** `SecurityTermsEnvelope.TryGetReadRefusal` returns the read
    guard's two refusals without throwing, with the exact texts
    `EnsureReadableAsAssetSpecificTerms` throws, and that method now throws its reason. The shared
    declared reads apply it: a declared non-asset format declares nothing (economic terms are
    projected through their own family), and a stamp outside 1 to `ReadsTermsThrough` is
    unreadable. `ScheduleSpecFactory` therefore refuses a row of a class with a versioned schedule
    container stamped above what the node reads, as every other read path does, instead of letting
    the cash-flow engine project legacy output for it
    ([ADR-025](025-contractual-schedule-kernel.md#applications), increment 5a entry).
  - **Stack.** Increment 5a is built on 5a-0 (PR #3283), which makes `OpenLotAmortization` refuse
    a populated or malformed `scheduleConventions` and read the call, put and conventions terms in
    every source the cash-flow resolver reads and under every casing. Every build that contains 5a
    therefore contains that refusal, and 5b-2's precondition that every node runs 5a covers it.
    5a-0's deploy gate (AQ1 and AQ2, expect 0) is in the
    [extensibility review](../architecture/security-master-extensibility-review.md).
  - **Deploy and rollback floor.** 5a writes nothing differently, so it rolls forward and back
    freely until increment 5b-2's first declaring write. From that write on, no node, desktops
    included, may run below 5a: a 4b node would ignore the block in projections, let them post and
    amortize a block-only declaring Bond on the bullet grid, and a 5a-0 node would refuse the
    amortization but project and post the legacy dates. Each node's startup capability line shows
    whether it runs 5a. The rollout and its inventory gate are in the
    [terms contract rollout](../operators/security-master-terms-contract-rollout.md), and increment
    5b-1's capability floor (decision 11 of the increment-5 plan) is to enforce the floor.
  - **Decision 1 amended: the stamp describes the document (stamp model S2).** Decided by the owner
    on 2026-10-28, before 5a merges, and implemented in increment 5b-2. From the raise on, a writer
    stamps the highest `Since` among the versioned terms the record holds, floored at the class's
    stamp floor: its write version before the contract step (1 for Bond and CertificateOfDeposit, 2
    for StructuredCredit), raised to the target version of any transform step declared later. A
    record that declares nothing keeps the stamp and the bytes it is written with today, and a
    declaring record is stamped at the version its declared terms need. Under decision 1 as first
    written (S1), every write after the raise would stamp at least the new write version and emit
    the frozen unset forms. Every touched row's bytes would change, and with them up to 43 of the
    164 write-golden entries: the 42 that name Bond, CertificateOfDeposit or StructuredCredit, and
    `registry.dtos`; the other classes' entries would not move. Full-payload edits of every such
    row would be refused on 5a nodes, and desktops cannot be routed; every legacy row would show the
    BEHIND notice; and a Reg S/144A continuation whose successor differs from its predecessor only
    by the stamp and unset containers would be refused unless the continuation comparison were
    relaxed. S2 confines all of this to declaring rows, so 5a carries no continuation change and the
    raw-payload comparison in `OpenLotAmortizationContinuation` stays as listed under the upgrade
    hook above. 5b-2 changes the F# `effective` floor, `StampFlat`'s lower bound,
    `WriteFlatEnvelope` and the notice. Before the raise the floor equals `TermsVersion`, so S1 and
    S2 write the same bytes, and 4b and 5a nodes already write exactly the S2 bytes for records that
    declare nothing.
  - **BEHIND, redefined (with S2, in 5b-2).** `SM_ASSET_CLASS_TERMS_VERSION_BEHIND` (Info) is to
    report a flat row whose stamp is below what the node would write for the terms the row holds:
    the highest `Since` among the versioned terms it decodes, floored at the class's stamp floor. A
    record that declares nothing and carries its class's floor stamp is not behind. Until 5b-2 the
    notice keeps the definition under the upgrade hook above, which gives the same answer while the
    floor equals `TermsVersion`.
