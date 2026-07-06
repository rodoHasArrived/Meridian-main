using Meridian.Contracts.Domain.Models;
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

    public CrossProviderValidator(
        IReadOnlyList<IHistoricalDataProvider> providers,
        Func<string, string, CancellationToken, Task<string>> resolveSymbolForProvider,
        ILogger log)
    {
        _providers = providers;
        _resolveSymbolForProvider = resolveSymbolForProvider;
        _log = log;
    }

    /// <summary>
    /// Validate <paramref name="bars"/> (from <paramref name="sourceProvider"/>) against the first
    /// other available provider, logging close-price discrepancies over 1% on the first few bars.
    /// </summary>
    public async Task ValidateAsync(
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
            return;

        try
        {
            // Resolve the symbol for the validation provider too — providers can use
            // different symbol formats (e.g. "AAPL" vs "aapl.us"), and validating with
            // the unresolved symbol silently compares against the wrong (or no) data.
            var resolvedSymbol = await _resolveSymbolForProvider(symbol, validationProvider.Name, ct).ConfigureAwait(false);
            var validationBars = await validationProvider.GetDailyBarsAsync(resolvedSymbol, from, to, ct).ConfigureAwait(false);

            if (validationBars.Count > 0)
            {
                var discrepancies = 0;
                foreach (var bar in bars.Take(5)) // Check first 5 bars
                {
                    var matchingBar = validationBars.FirstOrDefault(b => b.SessionDate == bar.SessionDate);
                    if (matchingBar is not null)
                    {
                        var closeDiff = Math.Abs(bar.Close - matchingBar.Close) / bar.Close;
                        if (closeDiff > 0.01m) // More than 1% difference
                        {
                            discrepancies++;
                            _log.Debug("Price discrepancy on {Date}: {Provider1}={Price1}, {Provider2}={Price2}",
                                bar.SessionDate, sourceProvider, bar.Close, validationProvider.Name, matchingBar.Close);
                        }
                    }
                }

                if (discrepancies > 0)
                {
                    _log.Warning("Found {Count} price discrepancies between {Provider1} and {Provider2} for {Symbol}",
                        discrepancies, sourceProvider, validationProvider.Name, symbol);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Cross-validation failed for {Symbol}", symbol);
        }
    }
}
