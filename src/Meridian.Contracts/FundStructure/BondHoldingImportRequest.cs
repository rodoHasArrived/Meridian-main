namespace Meridian.Contracts.FundStructure;

/// <summary>Publish the account's complete reviewed staging population as observed holdings.</summary>
public sealed record BondHoldingImportRequest(string ConnectionId, string SyncRunId, Guid AccountId,
    Guid BatchId, DateOnly AsOfDate, int Revision = 1, Guid? SupersedesBatchId = null);
