using Meridian.Contracts.Domain.Models;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Persists one non-empty session-date batch of historical bars. The returned path identifies
/// the committed batch for progress notifications; naming and durability belong to persistence.
/// </summary>
public interface IBackfillBarWriter
{
    Task<string> WriteAsync(
        DataGranularity granularity,
        IReadOnlyList<HistoricalBar> bars,
        string fallbackSource,
        CancellationToken ct = default);
}
