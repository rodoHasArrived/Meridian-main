using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Builds a snapshot-backed <see cref="IOptionChainProvider"/> for a single covered-call
/// backtest run by eagerly materialising chain data for every trading date in the window.
/// </summary>
public interface ICoveredCallChainProviderFactory
{
    /// <summary>
    /// Materialises a chain provider that supplies call-option candidates for each scan date
    /// in <c>[from, to]</c>. The factory may consult any registered <c>IOptionsChainProvider</c>
    /// implementations to gather chain snapshots.
    /// </summary>
    ValueTask<IOptionChainProvider> CreateAsync(
        string underlyingSymbol,
        DateOnly from,
        DateOnly to,
        int maxDte,
        CancellationToken ct = default);

    /// <summary>
    /// Materialises a single-date chain (for the configure-form preview). Underlying price is
    /// resolved from the latest available snapshot. Returns a tuple of (underlyingPrice, candidates).
    /// </summary>
    ValueTask<CoveredCallChainPreviewResult> PreviewAsync(
        string underlyingSymbol,
        DateOnly asOf,
        CancellationToken ct = default);
}

/// <summary>Result returned by <see cref="ICoveredCallChainProviderFactory.PreviewAsync"/>.</summary>
public sealed record CoveredCallChainPreviewResult(
    decimal UnderlyingPrice,
    IReadOnlyList<OptionCandidateInfo> Candidates);
