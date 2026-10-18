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
   "flat"` and `assetClassTermsVersion` (the asset class's `SecurityAssetClassDescriptor.TermsVersion`),
   written through `SecurityTermsEnvelope.StampFlat` or by emitting the same three keys. The economic
   definition adapter stamps `termsFormat = "economic"`; the custom-asset profile document is the
   profile family and is emitted verbatim. No code path persists an untagged payload.
2. **The stored family is a column.** Migration `042_security_master_terms_format_column.sql` adds
   `securities.terms_format`, backfilled non-destructively: a declared tag wins; an untagged row is
   classified by the same shape rule `ResolveFormat` applies (profile envelope → `customProfile`;
   version-2 document carrying an economic module key → `economic`; otherwise `flat`). The projection
   store writes the column from `ResolveFormat` on every upsert and refuses an unknown declared format
   with a precise reason. The stored payload is never rewritten by the store or the migration.
3. **Per-class evolution does not touch the codec number.** New flat-term fields bump the class's
   `TermsVersion` and ship an upcaster; `schemaVersion` stays the codec version of the family and the
   reserved value `2` is never declared as a flat version.
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
| Envelope | `src/Meridian.Contracts/SecurityMaster/SecurityTermsEnvelope.cs` | Keys, `StampFlat`, `ResolveFormat`, read guard |
| Versions | `src/Meridian.Contracts/SecurityMaster/SecurityMasterSchemaVersions.cs` | Codec versions and the reserved value |
| Upcaster chain | `src/Meridian.Contracts/SecurityMaster/SecurityAssetSpecificTermsUpcasterChain.cs` | Tag-first dispatch; shape rule for untagged payloads |
| Migration | `src/Meridian.Storage/SecurityMaster/Migrations/042_security_master_terms_format_column.sql` | `terms_format` column, backfill, check constraint |
| Store | `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore.cs` | Writes `terms_format`; refuses unknown formats |
| Writers | `src/Meridian.Application/SecurityMaster/SecurityMasterCsvParser.cs`, `src/Meridian.Application/SecurityMaster/EdgarIngestOrchestrator.cs`, `src/Meridian.FSharp/Interop.SecurityMaster.fs`, `src/Meridian.Wpf/ViewModels/SecurityMasterEditViewModel.cs` | Flat writers that stamp the envelope |
| Validation | `src/Meridian.Application/SecurityMaster/Validation/SecurityValidationService.cs` | Format rules |
| Tests | `tests/Meridian.Tests/SecurityMaster/SecurityTermsVersioningTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityValidationServiceTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityMasterPostgresRoundTripTests.cs` | Stamping, SQL/code parity, column round trip |

Items 1–4 are implemented. Item 5 (retiring the shape rule and the reserved value) is proposed and
waits on the backfill evidence described above.

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
// Readers: never classify by shape when a tag is present.
SecurityTermsEnvelope.ResolveFormat(payload);
// Store: the column is derived from the payload, never from the post-upcast result.
PostgresSecurityMasterStore.ResolveStorableTermsFormat(record);
```

### Runtime Verification

- `SecurityTermsVersioningTests` — stamping idempotence, tag-first dispatch, SQL/code parity for
  migration 042, store refusal of unknown formats.
- `SecurityValidationServiceTests` — `SM_ASSET_SPECIFIC_TERMS_FORMAT_MISSING` and `_UNKNOWN`.
- `SecurityMasterPostgresRoundTripTests.UpsertProjection_PersistsTheTermsFormatColumn_FromTheTagOrTheShapeRule`
  (PostgreSQL integration lane).
- `SecurityAssetSpecificTermsUpcasterChainTests` — no accepted flat version equals
  `EconomicTermsSchema.Current`.
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

*Last Updated: 2026-10-17*

## Applications

- **StructuredCredit terms version 2 (2026-10-18).** Added `structuredCreditKind`, `seniority`,
  `attachmentPoint`, `detachmentPoint`, `weightedAverageCoupon`, `weightedAverageMaturityMonths`,
  `weightedAverageLoanAgeMonths`, `reinvestmentPeriodEnd` and `nonCallPeriodEnd` as optional flat
  terms and bumped `SecurityAssetClassDescriptor.TermsVersion` to 2. Every addition is optional and
  carries no default that changes the reading of a version-1 payload, so the version-1 → 2 upcast is
  the identity: readers accept both versions through `SecurityTermsEnvelope` and the absent terms
  read as unset. A field transformation step is only owed when a later version renames, retypes or
  gives a default to a term.

