using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Cross-validates bars returned by one provider against a second provider for the
/// <see cref="CompositeHistoricalDataProvider"/>, logging any material price discrepancies.
/// Extracted so the composite provider does not own validation logic directly
/// (single-responsibility collaborator). Validation is best-effort: failures are logged and
/// never propagate to the data path.
/// </summary>
internal sealed class CrossProviderValidator
{
    private readonly IReadOnlyList<IHistoricalDataProvider> _providers;
    private readonly Func<string, string, CancellationToken, Task<string>> _resolveSymbolForProvider;
    private readonly ILogger _log;
    private readonly Func<CrossProviderTolerancePolicy> _policy;

    public CrossProviderValidator(
        IReadOnlyList<IHistoricalDataProvider> providers,
        Func<string, string, CancellationToken, Task<string>> resolveSymbolForProvider,
        ILogger log,
        Func<CrossProviderTolerancePolicy>? policy = null)
    {
        _providers = providers;
        _resolveSymbolForProvider = resolveSymbolForProvider;
        _log = log;
        _policy = policy ?? (static () => CrossProviderTolerancePolicy.Default);
    }

    /// <summary>
    /// Validate <paramref name="bars"/> (from <paramref name="sourceProvider"/>) against the first
    /// other available provider, logging close-price discrepancies above the policy's backfill close
    /// tolerance on the policy's number of bars (default: first 5 bars, 1%).
    /// </summary>
    /// <returns>The number of compared bars whose close differed by more than the tolerance.</returns>
    public async Task<int> ValidateAsync(
        IReadOnlyList<HistoricalBar> bars,
        string symbol,
        DateOnly? from,
        DateOnly? to,
        string sourceProvider,
        CancellationToken ct)
    {
        // Try to validate with a different provider
        var validationProvider = _providers.FirstOrDefault(p => p.Name != sourceProvider);
        if (validationProvider is null)
            return 0;

        try
        {
            // Resolve the symbol for the validation provider too — providers can use
            // different symbol formats (e.g. "AAPL" vs "aapl.us"), and validating with
            // the unresolved symbol silently compares against the wrong (or no) data.
            var resolvedSymbol = await _resolveSymbolForProvider(symbol, validationProvider.Name, ct).ConfigureAwait(false);
            var validationBars = await validationProvider.GetDailyBarsAsync(resolvedSymbol, from, to, ct).ConfigureAwait(false);

            var discrepancies = 0;
            if (validationBars.Count > 0)
            {
                var policy = _policy();
                var tolerance = (decimal)policy.BackfillCloseTolerancePercent / 100m;
                var compared = policy.BackfillBarsToCompare is { } limit ? bars.Take(limit) : bars;
                var bySession = validationBars
                    .GroupBy(b => b.SessionDate)
                    .ToDictionary(g => g.Key, g => g.First());
                foreach (var bar in compared)
                {
                    if (bySession.TryGetValue(bar.SessionDate, out var matchingBar) && bar.Close != 0)
                    {
                        var closeDiff = Math.Abs(bar.Close - matchingBar.Close) / bar.Close;
                        if (closeDiff > tolerance)
                        {
                            discrepancies++;
                            _log.Debug("Price discrepancy on {Date}: {Provider1}={Price1}, {Provider2}={Price2}",
                                bar.SessionDate, sourceProvider, bar.Close, validationProvider.Name, matchingBar.Close);
                        }
                    }
                }

                if (discrepancies > 0)
                {
                    _log.Warning("Found {Count} price discrepancies between {Provider1} and {Provider2} for {Symbol} above {TolerancePercent}%",
                        discrepancies, sourceProvider, validationProvider.Name, symbol, policy.BackfillCloseTolerancePercent);
                }
            }

            return discrepancies;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Cross-validation failed for {Symbol}", symbol);
            return 0;
        }
    }
}
