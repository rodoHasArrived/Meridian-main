using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.AssetOperations;

/// <summary>Accepted observed positions, retaining the exact full-statement authority.</summary>
public sealed record AccountBondHoldingsSnapshotDto(
    Guid AccountId,
    DateOnly AsOfDate,
    Guid BatchId,
    string SourceContentHash,
    string EvidenceReference,
    IReadOnlyList<CustodianPositionLineDto> Positions);

public interface IAccountBondHoldingsSource
{
    Task<AccountBondHoldingsSnapshotDto?> GetHoldingsAsync(Guid accountId, DateOnly asOf,
        DateTimeOffset? knownAt = null, CancellationToken ct = default);
}
