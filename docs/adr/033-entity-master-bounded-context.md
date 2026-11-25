# ADR-033: Entity Master as a Separate Bounded Context

**Status:** Proposed
**Date:** 2026-11-25
**Owner:** core-team
**Reviewed:** 2026-11-25
**Deciders:** repository owner (chose a separate entity master on 2026-11-25; acceptance of this ADR pending)
**Supersedes:** —
**Superseded by:** —

## Context

A Security Master issuer is a free-text `IssuerTerms.IssuerName`, with CIK and LEI stored as identifiers on each
security. Without a first-class issuer:

- Issuer-level concentration is impossible.
- Credit events cannot propagate to every security of the same entity.
- The EDGAR ingest matches a CIK with `limit 1` and can attach a share-class ticker to the wrong security.

The 2026-11-24 architecture review (item C2) asked whether issuers belong inside the Security Master or in a
separate entity master shared with counterparties and fund structure. The repository owner chose a separate
entity master.

## Decision

1. **A separate bounded context.** The entity master is its own context with its own contracts
   (`Meridian.Contracts.EntityMaster`), store, PostgreSQL schema (`entity_master`) and migration set
   (`entity-master`). It runs on the Security Master database connection.
2. **Legal entities.**
   - Each entity is keyed by `EntityId`.
   - LEI (ISO 17442) and CIK (10 digits, zero-padded) are optional. Each is unique when present.
3. **Hierarchy.**
   - Direct-parent links carry valid-time windows `[ValidFrom, ValidTo)`.
   - A new link closes the open one at its own valid-from, so the hierarchy is reconstructable for any date.
   - The ultimate parent is derived by following parent links as of a date, with bounded depth.
4. **Issuer links.**
   - A security's issuer is a valid-dated link from its `SecurityId` to an `EntityId`.
   - The Security Master's `IssuerName` stays as a display attribute. Securities are linked lazily rather than
     migrated in bulk.
5. **Next consumers.** Counterparties and fund-structure legal entities adopt `EntityId` in later changes.
   Each later change states how it reconciles with the entity lifecycle in `Meridian.Storage/FundStructure`.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Contracts | `src/Meridian.Contracts/EntityMaster/EntityMasterDtos.cs` | Entities, valid-dated links, store contract, identifier rules |
| Stores and migrations | `src/Meridian.Storage/EntityMaster/EntityMasterStores.cs`, `src/Meridian.Storage/EntityMaster/Migrations/001_entity_master.sql` | In-memory and PostgreSQL stores; the `entity-master` migration set |
| Registration | `database/schema-control.json`, `database/migration-reservations.json`, `src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs` | Schema control and DI |
| Routes | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.EntityMaster.cs` | `/api/entity-master/...` |
| Tests | `tests/Meridian.Tests/SecurityMaster/EntityMasterPostgresTests.cs` | Identity rules, valid-dated hierarchy and issuer links on both stores |

**Implemented on 2026-11-25:** the context, the stores, the routes, and valid-dated hierarchy and issuer links.

**Still proposed:**

- EDGAR ingest linking issuers by CIK.
- Issuer-level concentration in risk and reporting.
- Counterparty and fund-structure adoption.
- Workstation surfaces.

## Rationale

The same legal entity appears as an issuer, a counterparty and a fund-structure node. A context owned by none
of those three can be shared without either one bending the others to its model.

## Alternatives Considered

### Alternative 1: An issuers aggregate inside the Security Master

**Pros:**
- Smaller, and needs no new context.

**Cons:**
- Counterparties and fund structure would depend on the Security Master for legal-entity identity.

**Why rejected:** the owner chose a shared entity master.

## Consequences

### Positive

- Issuer identity has one home.
- The hierarchy is reconstructable as of any date.
- LEI and CIK are each unique.

### Negative

- Adds a context and a migration set to operate.
- Securities need linking. Until then, `IssuerName` remains the only issuer information.

### Neutral

- The Security Master is unchanged until its consumers adopt issuer links.

## Compliance

### Runtime Verification

- `EntityMasterPostgresTests` covers LEI and CIK uniqueness, valid-dated parents, ultimate-parent derivation and
  issuer windows.

```bash
dotnet run --project build/dotnet/DocGenerator/DocGenerator.csproj -- verify-adrs --adr-dir docs/adr --src-dir src
```

## References

- Security Master architecture review 2026-11-24, item C2
- [ADR-034: Bitemporal Security Master Terms](034-bitemporal-security-master-terms.md)

---

*Last Updated: 2026-11-25*
