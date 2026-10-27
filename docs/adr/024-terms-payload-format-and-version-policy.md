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
   `SecurityAssetClassDescriptor.TermsVersion`, raised to a readable version above it only when the
   record holds a term that only that version declares (decision 3). The economic
   definition adapter stamps `termsFormat = "economic"`; the custom-asset profile document is the
   profile family and is emitted verbatim. No code path persists an untagged payload.
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
     versioned class terms; only its stamp is checked. Its fields belong to the approved profile.
     The contract step keeps this rule.
   - **The written form is frozen when the term is declared.** For the `scheduleConventions` block,
     the key is present whenever the writer's effective version is at or above its `Since`: `null`
     when nothing is declared, otherwise all twelve members in alphabetical order, with `null` or
     `[]` for an unset member. Swap legs follow the same rule, with the key on every leg. Inner JSON
     kinds are frozen too: conventions are strings, dates are ISO `yyyy-MM-dd` strings,
     `paymentLagBusinessDays` is an `Int32` and `paymentCalendars` is an array of strings. An inner
     member cannot be added without a new readable version.
   - **Round trip.** A node refuses to re-serialize a row stamped above its write version when it
     cannot reproduce that row's versioned content (a deactivation or a patch-less amend), rather than
     drop what it cannot decode.
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
and its expand step is implemented for the `scheduleConventions` block (2026-10-25).
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
// Read ceiling, and the fields an authoring surface may offer.
SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
SecurityAssetTermsSchema.WritableFields(assetClass);
// Populated terms above a version, and their normalized form for the round-trip guard.
SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, termsVersion);
SecurityAssetTermsSchema.NormalizedTermsAbove(assetClass, payload, termsVersion, out var conflictingPaths);
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
  manifest diff it spans. Four guards hold the expand step: steps at or above the write version are
  identity steps; `ReadableTermsVersion` is null or above the write version; each readable-only
  version introduces a top-level or element field; and every element field's `Since` is within what
  its class reads, optional above version 1.
- `SecurityTermsWriteGoldenTests` — the written terms, economic terms, registry DTOs, term blocks and
  validation reports equal their pre-expand bytes.
- `SecurityAssetClassTermsUpgraderTests` and `SecurityAssetClassTermsUpgradeWiringTests` — an
  identity step returns the stored element itself, a transform step returns a new payload stamped at
  the current version, everything outside the flat codec passes through, and a version-1
  StructuredCredit payload reads exactly as its version-2 twin on every wired read path.
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

*Last Updated: 2026-10-25*

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
    integer `schemaVersion` stamped, or unstamped and so read as, below its class's terms version.
    It never blocks. `SecurityValidationGateService` drops informational issues before a workflow
    gate decides, snapshots or hands the report on, so validation snapshots omit them by design.
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
    the write version on its next re-serialization: the data is equal, and a contract node shows only
    the informational BEHIND notice.
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
