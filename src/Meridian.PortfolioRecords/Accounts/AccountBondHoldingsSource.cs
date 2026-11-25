using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.Accounts;

/// <summary>Read-only accepted account observations; no lots, events or journals are manufactured.</summary>
public sealed class AccountBondHoldingsSource(IAccountQueryService accounts)
    : IAccountBondHoldingsSource, IPortfolioHoldingsSource
{
    public async Task<AccountBondHoldingsSnapshotDto?> GetHoldingsAsync(Guid accountId, DateOnly asOf,
        DateTimeOffset? knownAt = null, CancellationToken ct = default)
    {
        var retained = await accounts.GetCustodianSnapshotAsync(accountId, asOf, knownAt, ct).ConfigureAwait(false);
        if (retained is null)
            return null;
        return new(accountId, retained.Batch.AsOfDate, retained.Batch.BatchId, retained.Batch.ContentSha256!,
            retained.Batch.Authority!.SourceEvidenceId, retained.Lines);
    }

    public Task<IReadOnlyList<PortfolioHoldingDto>> GetHoldingsAsync(DateOnly asOf, CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<PortfolioHoldingDto>>(new NotSupportedException(
            "Bond holdings require an explicit account scope; use IAccountBondHoldingsSource."));
}
