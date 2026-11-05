using Meridian.Contracts.DirectLending;

namespace Meridian.Storage.DirectLending;

/// <summary>
/// Observed benchmark fixings used by scheduled floating-rate resets. Insert-only: recording a
/// fixing for an index and observation date that already has one adds a new revision, and the
/// highest revision is the effective fixing. Index names compare case-insensitively.
/// </summary>
public interface IDirectLendingIndexFixingStore
{
    /// <summary>Records a fixing whose rate was already normalized from its declared unit.</summary>
    Task<DirectLendingIndexFixingDto> RecordAsync(RecordIndexFixingRequest request, decimal rateFraction, CancellationToken ct = default);

    /// <summary>The effective (highest-revision) fixing for an index on a date, or <see langword="null"/>.</summary>
    Task<DirectLendingIndexFixingDto?> GetEffectiveAsync(string indexName, DateOnly observationDate, CancellationToken ct = default);

    /// <summary>Effective fixings, optionally filtered by index and an inclusive observation-date range.</summary>
    Task<IReadOnlyList<DirectLendingIndexFixingDto>> ListAsync(string? indexName = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
}

/// <summary>Process-local fixing store for the in-memory direct-lending service and tests.</summary>
public sealed class InMemoryDirectLendingIndexFixingStore : IDirectLendingIndexFixingStore
{
    private readonly object _gate = new();
    private readonly List<DirectLendingIndexFixingDto> _fixings = [];

    public Task<DirectLendingIndexFixingDto> RecordAsync(RecordIndexFixingRequest request, decimal rateFraction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var indexName = NormalizeIndexName(request.IndexName);
        lock (_gate)
        {
            var revision = _fixings
                .Where(item => item.IndexName == indexName && item.ObservationDate == request.ObservationDate)
                .Select(static item => item.Revision)
                .DefaultIfEmpty(0)
                .Max() + 1;
            var fixing = new DirectLendingIndexFixingDto(
                Guid.NewGuid(),
                indexName,
                request.ObservationDate,
                revision,
                request.Rate,
                rateFraction,
                request.SourceRef,
                DateTimeOffset.UtcNow,
                request.RecordedBy);
            _fixings.Add(fixing);
            return Task.FromResult(fixing);
        }
    }

    public Task<DirectLendingIndexFixingDto?> GetEffectiveAsync(string indexName, DateOnly observationDate, CancellationToken ct = default)
    {
        var normalized = NormalizeIndexName(indexName);
        lock (_gate)
        {
            return Task.FromResult(_fixings
                .Where(item => item.IndexName == normalized && item.ObservationDate == observationDate)
                .OrderByDescending(static item => item.Revision)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<DirectLendingIndexFixingDto>> ListAsync(string? indexName = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(indexName) ? null : NormalizeIndexName(indexName);
        lock (_gate)
        {
            IReadOnlyList<DirectLendingIndexFixingDto> result = _fixings
                .Where(item => normalized is null || item.IndexName == normalized)
                .Where(item => from is null || item.ObservationDate >= from)
                .Where(item => to is null || item.ObservationDate <= to)
                .GroupBy(static item => (item.IndexName, item.ObservationDate))
                .Select(static group => group.OrderByDescending(static item => item.Revision).First())
                .OrderBy(static item => item.IndexName, StringComparer.Ordinal)
                .ThenBy(static item => item.ObservationDate)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    /// <summary>Index names are stored trimmed and upper-cased so "sofr" and "SOFR" are one index.</summary>
    public static string NormalizeIndexName(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new ArgumentException("An index name is required.", nameof(indexName));
        }

        return indexName.Trim().ToUpperInvariant();
    }
}
