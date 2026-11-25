namespace Meridian.Contracts.FundStructure;

/// <summary>Full-statement selection never unions revisions or invents acceptance for legacy data.</summary>
public static class CustodianHoldingSnapshotSelector
{
    public static CustodianHoldingSnapshotDto? Select(Guid accountId, DateOnly asOf, DateTimeOffset? knownAt,
        IEnumerable<CustodianStatementBatchDto> batches, IEnumerable<CustodianPositionLineDto> lines)
    {
        if (accountId == Guid.Empty || asOf == default || knownAt is { Offset: var offset } && offset != TimeSpan.Zero)
            throw new ArgumentException("Snapshot selection requires account, date and an optional UTC knowledge cutoff.");
        var eligible = batches.Where(b => b.AccountId == accountId && b.AsOfDate <= asOf
            && (knownAt is null || b.IngestedAt <= knownAt)
            && b.Authority is { IsAccepted: true, IsFullSnapshot: true }).ToArray();
        if (eligible.Length == 0)
            return null;
        var date = eligible.Max(b => b.AsOfDate);
        var revision = eligible.Where(b => b.AsOfDate == date).Max(b => b.Authority!.Revision);
        var winners = eligible.Where(b => b.AsOfDate == date && b.Authority!.Revision == revision).ToArray();
        if (winners.Length != 1)
            throw new InvalidOperationException("Competing accepted full statements require review; holdings are unavailable.");
        var batch = winners[0];
        var positions = lines.Where(l => l.AccountId == accountId && l.BatchId == batch.BatchId).OrderBy(l => l.LineId).ToArray();
        if (positions.Length != batch.LineCount || positions.Any(l => l.AsOfDate != batch.AsOfDate)
            || positions.Select(l => l.LineId).Distinct().Count() != positions.Length
            || string.IsNullOrWhiteSpace(batch.ContentSha256))
            throw new InvalidOperationException("Retained full-statement membership or content evidence is incomplete.");
        var hash = BondHoldingNormalization.ContentHash(new(batch.BatchId, accountId, batch.AsOfDate,
            batch.CustodianName, batch.SourceFormat, null, positions, batch.LoadedBy, Authority: batch.Authority));
        if (hash != batch.ContentSha256)
            throw new InvalidOperationException("Retained statement content no longer matches its immutable identity.");
        return new(batch, positions);
    }
}
