# Security Master Identifier Conflict Detection

Security Master identifier resolution and ambiguity detection use the same canonical identity:
`SecurityIdentifierKind` plus the value produced by `SecurityIdentifierNormalizer`. Raw
punctuation, whitespace, and case are never separate identities.

An ambiguity exists only when two different `SecurityId` values claim the same canonical
identifier during overlapping half-open validity windows (`[ValidFrom, ValidTo)`). Adjacent
historical assignments therefore do not conflict, while every pair among three or more overlapping
claimants is retained with a deterministic conflict id.

Publish and rebuild paths query candidate claims through the normalized identifier index. A rebuild
submits its persisted projections as one batch, excludes those subjects from the database lookup,
builds the identifier map once, and records only conflicts involving the batch. Cost is proportional
to identifiers plus actual candidate matches; it does not materialize the complete projection table
for every record.

The full open-conflict refresh remains a deliberate universe scan because it audits all claims. The
ingest and rebuild hot paths must use `FindIdentifierCandidatesAsync` and
`RecordConflictsForProjectionsAsync`; substituting `LoadAllAsync` in either path reintroduces the
quadratic rebuild failure mode.

Validation lives in:

- `SecurityMasterConflictServiceTests` for normalization, validity windows, and claimant pairs.
- `SecurityMasterRebuildOrchestratorTests` for one batched conflict call per rebuild batch.
- `PostgresSecurityMasterConflictServiceTests` for normalized indexed candidate lookup.
