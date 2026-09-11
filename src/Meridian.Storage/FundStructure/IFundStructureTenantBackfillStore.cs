using System.Text.Json;

namespace Meridian.Storage.FundStructure;

/// <summary>Maintenance-only snapshot; this is not an operator-session read API.</summary>
public sealed record FundStructureTenantBackfillRow(
    string Table, Guid Id, string Kind, bool IsNode, string? TenantId,
    IReadOnlyList<Guid> Parents, IReadOnlyList<Guid> Children, JsonElement RetainedRow);

public sealed record FundStructureTenantBackfillEvidence(
    Guid BookId, string FundProfileId, Guid NodeId, string? TenantId, string? CompanyId,
    JsonElement RetainedBook, JsonElement? RetainedRegistryEntry);

public sealed record FundStructureTenantBackfillSnapshot(
    string SourceIdentity, string SchemaIdentity,
    IReadOnlyList<FundStructureTenantBackfillRow> Rows,
    IReadOnlyList<FundStructureTenantBackfillEvidence> Evidence,
    IReadOnlyList<JsonElement> RetainedQuarantine, bool SupportsAtomicApply = true);

public sealed record FundStructureTenantBackfillStamp(string Table, Guid Id, string TenantId);

public sealed record FundStructureTenantBackfillException(
    Guid NodeId, string NodeKind, string Reason, IReadOnlyList<string> CandidateTenantIds);

public sealed record FundStructureTenantBackfillReceipt(
    Guid RunId, string PlanHash, string OperatorId, string ReviewReference,
    DateTimeOffset AppliedAt, int StampedRows, int QuarantinedRows, JsonElement Plan);

/// <summary>Holds both evidence and graph locks until disposal or atomic fund commit.</summary>
public interface IFundStructureTenantBackfillSession : IAsyncDisposable
{
    FundStructureTenantBackfillSnapshot Snapshot { get; }
    Task<FundStructureTenantBackfillReceipt?> FindReceiptAsync(Guid runId, CancellationToken ct);
    Task<FundStructureTenantBackfillReceipt> CommitAsync(
        Guid runId, string planHash, string operatorId, string reviewReference, JsonElement plan,
        IReadOnlyList<FundStructureTenantBackfillStamp> stamps,
        IReadOnlyList<FundStructureTenantBackfillException> exceptions, CancellationToken ct);
}

public interface IFundStructureTenantBackfillStore
{
    /// <summary>Acquires bounded, ordered locks and reads authoritative retained rows.</summary>
    Task<IFundStructureTenantBackfillSession> OpenSessionAsync(CancellationToken ct = default);
}
