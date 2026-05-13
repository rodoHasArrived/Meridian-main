using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Adapts an eagerly-materialised set of option chain snapshots into the synchronous
/// <see cref="IOptionChainProvider"/> contract that <see cref="CoveredCallOverwriteStrategy"/>
/// expects. One instance per backtest run.
/// </summary>
public sealed class CoveredCallChainProviderAdapter : IOptionChainProvider
{
    private readonly IReadOnlyDictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>> _snapshotsByDate;
    private readonly ILogger<CoveredCallChainProviderAdapter> _logger;

    /// <summary>Total candidates across all snapshot dates (for diagnostics).</summary>
    public int TotalCandidates { get; }

    /// <summary>Snapshot dates the adapter knows about.</summary>
    public IReadOnlyCollection<DateOnly> KnownDates => _snapshotsByDate.Keys.ToArray();

    /// <summary>Creates a new adapter from a pre-built map of scan-date → call candidates.</summary>
    public CoveredCallChainProviderAdapter(
        IReadOnlyDictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>> snapshotsByDate,
        ILogger<CoveredCallChainProviderAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(snapshotsByDate);
        _snapshotsByDate = snapshotsByDate;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TotalCandidates = snapshotsByDate.Sum(static kvp => kvp.Value.Count);
    }

    /// <inheritdoc/>
    public IReadOnlyList<OptionCandidateInfo> GetCalls(
        string underlyingSymbol,
        DateOnly asOf,
        decimal underlyingPrice)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol))
        {
            return [];
        }

        if (!_snapshotsByDate.TryGetValue(asOf, out var calls))
        {
            _logger.LogDebug("No snapshot for {Symbol} on {AsOf}", underlyingSymbol.ToUpperInvariant(), asOf);
            return [];
        }

        var normalisedSymbol = underlyingSymbol.Trim().ToUpperInvariant();
        if (calls.Count > 0 && !calls[0].UnderlyingSymbol.Equals(normalisedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Symbol mismatch in chain snapshot: requested {Requested}, snapshot {Snapshot}",
                normalisedSymbol, calls[0].UnderlyingSymbol);
            return [];
        }

        return calls;
    }
}
