using Meridian.Contracts.FixedIncome;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Explicit, retained market inputs; importing an observation never selects a valuation price or posts a journal.</summary>
public sealed class BondMarketDataService(IBondMarketDataStore store, TimeProvider? clock = null) : IBondMarketDataService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<BondMarketImportResult> ImportAsync(string tenantId, string actor, BondMarketImportRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Observations is not { Count: > 0 and <= 10000 })
            throw new ArgumentException("Import between 1 and 10,000 typed observations per request.", nameof(request));
        var now = _clock.GetUtcNow();
        var observations = request.Observations.Select(input =>
        {
            var normalized = BondMarketDataRules.Normalize(input);
            if (normalized.PublishedAtUtc > now)
                throw new ArgumentException("An observed input cannot have a publication time after the import's recording time.");
            if (normalized.Kind is BondMarketObservationKind.Fixing or BondMarketObservationKind.InflationIndex or BondMarketObservationKind.PrincipalFactor
                && normalized.EconomicDate > DateOnly.FromDateTime(now.UtcDateTime))
                throw new ArgumentException("Future index/factor values are forecasts and must be retained as curve inputs, not observed fixings.");
            var hash = BondMarketDataRules.ContentHash(normalized);
            return new BondMarketObservationDto(Guid.NewGuid(), normalized, now, actor.Trim(), hash);
        }).ToArray();
        return store.AppendAsync(tenantId.Trim(), observations, ct);
    }

    public async Task<IReadOnlyList<BondMarketObservationDto>> QueryAsync(string tenantId, BondMarketDataQuery query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Kind is { } kind && !Enum.IsDefined(kind) || query.SecurityId == Guid.Empty
            || query.FromDate > query.ThroughDate || query.KnownAtUtc is { } cutoff && (cutoff == default || cutoff.Offset != TimeSpan.Zero))
            throw new ArgumentException("Market input query filters or UTC knowledge cutoff are invalid.", nameof(query));
        var knownAt = query.KnownAtUtc ?? _clock.GetUtcNow();
        var eligible = (await store.ReadAsync(tenantId.Trim(), ct).ConfigureAwait(false))
            .Where(row => row.RecordedAtUtc <= knownAt && (row.Input.PublishedAtUtc is null || row.Input.PublishedAtUtc <= knownAt)
                && (query.Kind is null || row.Input.Kind == query.Kind)
                && (query.SeriesKey is null || string.Equals(row.Input.SeriesKey, query.SeriesKey.Trim(), StringComparison.OrdinalIgnoreCase))
                && (query.ProviderId is null || string.Equals(row.Input.ProviderId, query.ProviderId.Trim(), StringComparison.OrdinalIgnoreCase))
                && (query.SecurityId is null || row.Input.SecurityId == query.SecurityId)
                && (query.EconomicDate is null || row.Input.EconomicDate == query.EconomicDate)
                && (query.FromDate is null || row.Input.EconomicDate >= query.FromDate)
                && (query.ThroughDate is null || row.Input.EconomicDate <= query.ThroughDate));
        if (!query.IncludeSuperseded)
            eligible = eligible.GroupBy(row => BondMarketDataRules.SourceKey(row.Input), StringComparer.Ordinal)
                .Select(group => group.MaxBy(row => row.Input.Revision)!);
        return eligible.OrderBy(row => row.Input.EconomicDate).ThenBy(row => row.Input.ProviderId, StringComparer.Ordinal)
            .ThenBy(row => row.Input.ObservationKey, StringComparer.Ordinal).ThenBy(row => row.Input.Revision).ToArray();
    }

    public async Task<BondMarketObservationDto?> GetAsync(string tenantId, Guid observationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (observationId == Guid.Empty)
            throw new ArgumentException("A retained observation identity is required.", nameof(observationId));
        return (await store.ReadAsync(tenantId.Trim(), ct).ConfigureAwait(false)).SingleOrDefault(row => row.ObservationId == observationId);
    }
}
