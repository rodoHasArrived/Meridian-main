using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Adapts retained typed observations within the server's authenticated tenant scope.</summary>
public sealed class BondMarketWorkflowObservationSource(IBondMarketDataService marketData, Func<string> tenantId)
    : IBondWorkflowTypedObservationSource, IBondWorkflowQuoteSource
{
    public Task<BondMarketObservationDto?> GetAsync(Guid observationId, CancellationToken ct = default)
        => marketData.GetAsync(tenantId(), observationId, ct);

    public Task<IReadOnlyList<BondWorkflowRateObservation>> GetAsync(string indexName, DateOnly from, DateOnly to,
        DateTimeOffset knownAt, CancellationToken ct = default)
        => GetAsync(indexName, BondMarketObservationKind.Fixing, from, to, knownAt, ct);

    public async Task<IReadOnlyList<BondWorkflowRateObservation>> GetAsync(string indexName, BondMarketObservationKind kind,
        DateOnly from, DateOnly to, DateTimeOffset knownAt, CancellationToken ct = default)
    {
        if (kind is not (BondMarketObservationKind.Fixing or BondMarketObservationKind.InflationIndex))
            throw new ArgumentException("A workflow observation request must explicitly select rate fixings or inflation index levels.", nameof(kind));
        var observations = await marketData.QueryAsync(tenantId(), new BondMarketDataQuery(
            Kind: kind, SeriesKey: indexName, FromDate: from, ThroughDate: to, KnownAtUtc: knownAt), ct).ConfigureAwait(false);
        var selected = observations.Where(row => row.Input.Kind == kind).ToArray();
        // Source selection is explicit upstream. A different vendor value cannot win by file order.
        var output = new List<BondWorkflowRateObservation>();
        foreach (var date in selected.GroupBy(row => row.Input.EconomicDate).OrderBy(group => group.Key))
        {
            var candidates = date.ToArray();
            var values = candidates.Select(row => row.Input.Kind == BondMarketObservationKind.Fixing
                ? BondMarketDataRules.AnnualFraction(row) : row.Input.Value!.Value).Distinct().ToArray();
            if (values.Length != 1 || candidates.Select(row => row.Input.Kind).Distinct().Count() != 1)
                throw new InvalidOperationException($"Competing retained observations for {indexName} on {date.Key:yyyy-MM-dd} require source selection.");
            // Equal source values share one economic fixing; all original identities and hashes
            // remain in the aggregate dependency rather than being discarded by first-row selection.
            var ordered = candidates.OrderBy(row => row.ObservationId).ToArray();
            var source = string.Join("|", ordered.Select(row => $"{row.Input.ProviderId}:{row.Input.SourceName}:{row.ObservationId:D}"));
            var hash = ordered.Length == 1 ? ordered[0].ContentSha256
                : Sha256Digest.ComputeUtf8(string.Join("|", ordered.Select(row => row.ContentSha256)));
            output.Add(new BondWorkflowRateObservation(indexName, date.Key, values[0], source,
                ordered.Max(row => row.Input.PublishedAtUtc ?? row.RecordedAtUtc), ordered.Max(row => row.RecordedAtUtc), hash));
        }
        return output;
    }
}
