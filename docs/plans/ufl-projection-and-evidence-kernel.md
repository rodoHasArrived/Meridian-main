# UFL Projection And Evidence Kernel

**Owner:** Core Team
**Audience:** Architecture, storage, application, domain, and workstation contributors
**Last Updated:** 2026-05-28
**Status:** target-state shared kernel

## Summary

The UFL projection and evidence kernel is the shared Lane B foundation between canonical Security Master terms and asset-specific operational workflows. It should make projection rebuilds deterministic, asset-class scoped, evidence-backed, and provider-payload isolated before many asset-specific projection stores are added.

The current `UflProjectionRebuilder` is a Phase 0 bridge: it validates the asset class, then runs the shared Security Master rebuild pipeline. The next code slice should keep that behavior stable while introducing asset-class-aware projection metadata and projector registration.

## Kernel Responsibilities

- asset-class-aware rebuild orchestration;
- projection metadata with source event, command, correlation, source system, and rebuild sequence;
- deterministic checkpoint persistence;
- provider payload evidence retention without downstream payload dependency;
- replay-safe projection writes and deletes scoped by asset class;
- status and evidence artifacts that operator workflows can cite.

## Shared Interfaces

The first code slice should introduce a common storage and projector shape rather than one-off rebuild code per asset package:

```csharp
public interface IUflProjectionStore<TSnapshot>
{
    Task UpsertAsync(TSnapshot snapshot, UflProjectionMetadata metadata, CancellationToken ct);
    Task<IReadOnlyList<TSnapshot>> QueryAsync(UflProjectionQuery query, CancellationToken ct);
    Task DeleteByAssetClassAsync(string assetClass, CancellationToken ct);
}

public sealed record UflProjectionMetadata(
    string AssetClass,
    Guid SourceEventId,
    Guid? CommandId,
    Guid? CorrelationId,
    DateTimeOffset AsOf,
    string SourceSystem,
    long RebuildSequence);

public interface IUflAssetProjector
{
    string AssetClass { get; }

    Task ProjectAsync(SecurityMasterRecord record, UflProjectionContext context, CancellationToken ct);
}
```

These types are target-state contracts, not current implementation claims.

## Rebuild Semantics

- Rebuild requests normalize asset class before any store mutation.
- A full shared Security Master rebuild remains valid for Phase 0 compatibility.
- Asset-class-scoped rebuild deletes and rewrites only projections owned by the requested asset class.
- Rebuild mode must not emit external side effects unless an explicit backfill/recompute mode enables them.
- Projection rows must carry enough metadata to prove source event, source system, rebuild sequence, and as-of time.
- Checkpoints must advance only after durable projection writes complete.

## Evidence Boundary

### Implemented

- Shared Security Master projection cache and rebuild orchestration exist.
- Security Master storage includes projection checkpoint support.
- `IUflProjectionRebuilder` exists as the shared UFL rebuild entrypoint.

### Partially Implemented

- UFL rebuild accepts an asset class but currently replays the shared projection cache rather than asset-class-scoped projectors.
- Some verticals, especially direct lending and money-market support, have their own projection/checkpoint concepts that are not yet a common UFL kernel.

### Target-State Only

- Generic `IUflProjectionStore<TSnapshot>`.
- Generic `IUflAssetProjector`.
- Common `UflProjectionMetadata`.
- Shared conformance tests across asset profiles.

### Explicitly Out of Scope

- Pricing engines.
- Risk models.
- Provider-specific downstream workflows.
- Replacing asset-specific vertical stores that already serve deeper operational needs.

## Acceptance Evidence For First Code Slice

- unit tests proving asset-class normalization and unsupported asset handling;
- projection metadata tests for source event, source system, correlation, and rebuild sequence;
- rebuild tests proving scoped delete/write behavior;
- provider-payload isolation tests proving downstream projections read canonical terms and aliases, not raw provider payloads.

## Related Documents

- [UFL Capability Model](ufl-capability-model.md)
- [UFL Conformance Matrix](ufl-conformance-matrix.md)
- [UFL Asset Profile Template](ufl-asset-profile-template.md)
