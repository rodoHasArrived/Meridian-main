using Meridian.Contracts.Domain.Models;

namespace Meridian.ProviderSdk;

/// <summary>
/// Abstraction for writing historical bar data to storage.
/// Defined in ProviderSdk to break the circular dependency between
/// Infrastructure and Storage projects. Storage implementations provide
/// concrete writers; Infrastructure consumers inject this interface.
/// </summary>
/// <remarks>
/// Resolves Phase 0.1: DataGapRepair.StoreBarsAsync was a no-op because
/// Infrastructure could not reference Storage directly.
/// </remarks>
public interface IHistoricalBarWriter
{
    /// <summary>
    /// Persists a batch of historical bars to the configured storage backend.
    /// </summary>
    /// <param name="bars">The bars to write.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteBarsAsync(IReadOnlyList<HistoricalBar> bars, CancellationToken ct = default);
}
