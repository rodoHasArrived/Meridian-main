using Meridian.Application.Services;
using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Default implementation of <see cref="ICoveredCallChainProviderFactory"/>. Fetches
/// expirations and chain snapshots through the existing <see cref="OptionsChainService"/>
/// (which already aggregates across <c>IOptionsChainProvider</c> implementations) and
/// converts them to <see cref="OptionCandidateInfo"/> instances.
/// </summary>
public sealed class CoveredCallChainProviderFactory : ICoveredCallChainProviderFactory
{
    private readonly OptionsChainService _chainService;
    private readonly IOptionsMonitor<CoveredCallBacktestOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CoveredCallChainProviderFactory> _logger;

    public CoveredCallChainProviderFactory(
        OptionsChainService chainService,
        IOptionsMonitor<CoveredCallBacktestOptions> options,
        ILoggerFactory loggerFactory)
    {
        _chainService = chainService ?? throw new ArgumentNullException(nameof(chainService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<CoveredCallChainProviderFactory>();
    }

    /// <inheritdoc/>
    public async ValueTask<IOptionChainProvider> CreateAsync(
        string underlyingSymbol,
        DateOnly from,
        DateOnly to,
        int maxDte,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        if (to < from)
            throw new ArgumentException("'to' must be on or after 'from'.", nameof(to));

        var normalisedSymbol = underlyingSymbol.Trim().ToUpperInvariant();
        var opts = _options.CurrentValue;

        var expirations = await _chainService.GetExpirationsAsync(normalisedSymbol, ct).ConfigureAwait(false);
        if (expirations.Count == 0)
        {
            _logger.LogWarning(
                "CoveredCallChainProviderFactory: no expirations available for {Symbol}; backtest will see an empty chain.",
                normalisedSymbol);
            throw new ConfigurationException(
                $"No options chain provider returned expirations for {normalisedSymbol}. Configure an IOptionsChainProvider implementation.");
        }

        // We want expirations that could be tradeable within [from, to + maxDte].
        var windowEnd = to.AddDays(maxDte);
        var eligibleExpiries = expirations
            .Where(exp => exp >= from && exp <= windowEnd)
            .OrderBy(static d => d)
            .ToList();

        if (eligibleExpiries.Count == 0)
        {
            _logger.LogWarning(
                "CoveredCallChainProviderFactory: no expirations within window {From}-{End} for {Symbol}.",
                from, windowEnd, normalisedSymbol);
            // Empty adapter is still valid — the strategy will produce zero trades and the UI surfaces that.
            return new CoveredCallChainProviderAdapter(
                new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>(),
                _loggerFactory.CreateLogger<CoveredCallChainProviderAdapter>());
        }

        // Slice 1: production IOptionsChainProvider implementations return a live (current-time)
        // snapshot — they do not accept an `asOf` parameter. To keep the strategy productive across
        // historical scan dates we (1) collect today's available chain across all eligible expiries
        // and (2) replicate the candidate set under every trading-day date in [from, to], adjusting
        // DaysToExpiration per scan date. This is NOT point-in-time accurate; the dashboard surfaces
        // a banner explaining that historical chain data is approximated. A historical chain-store
        // is tracked as a slice 1.5 follow-up.
        var liveCandidatesByExpiry = new Dictionary<DateOnly, List<OptionCandidateInfo>>();

        foreach (var expiry in eligibleExpiries)
        {
            ct.ThrowIfCancellationRequested();

            OptionChainSnapshot? snapshot;
            try
            {
                snapshot = await _chainService
                    .FetchChainSnapshotAsync(normalisedSymbol, expiry, opts.MaxStrikesPerExpiration, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "CoveredCallChainProviderFactory: snapshot fetch failed for {Symbol} {Expiry}; skipping",
                    normalisedSymbol, expiry);
                continue;
            }

            if (snapshot is null || snapshot.Calls.Count == 0)
            {
                continue;
            }

            // Use the snapshot's *expiration* as the bucket key — we only need one bucket per expiry
            // before fanning out per scan date below.
            if (!liveCandidatesByExpiry.TryGetValue(expiry, out var bucket))
            {
                bucket = new List<OptionCandidateInfo>(snapshot.Calls.Count);
                liveCandidatesByExpiry[expiry] = bucket;
            }
            bucket.AddRange(ConvertCalls(snapshot, asOf: from));
        }

        if (liveCandidatesByExpiry.Count == 0)
        {
            _logger.LogWarning(
                "CoveredCallChainProviderFactory: no chain snapshots resolved for {Symbol} in [{From}, {To}]. Backtest will see an empty chain.",
                normalisedSymbol, from, to);
            return new CoveredCallChainProviderAdapter(
                new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>(),
                _loggerFactory.CreateLogger<CoveredCallChainProviderAdapter>());
        }

        // Fan the snapshot out across every trading-day date in [from, to]. Each scan date sees the
        // same option set but with DaysToExpiration recomputed so the strategy's DTE filter behaves
        // as expected as expiries approach.
        var snapshotsByDate = new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var perDate = new List<OptionCandidateInfo>();
            foreach (var (expiry, candidates) in liveCandidatesByExpiry)
            {
                if (expiry < d) continue;
                var dte = expiry.DayNumber - d.DayNumber;
                foreach (var c in candidates)
                {
                    perDate.Add(c with { DaysToExpiration = dte });
                }
            }
            if (perDate.Count > 0)
            {
                snapshotsByDate[d] = perDate;
            }
        }

        return new CoveredCallChainProviderAdapter(
            snapshotsByDate,
            _loggerFactory.CreateLogger<CoveredCallChainProviderAdapter>());
    }

    /// <inheritdoc/>
    public async ValueTask<CoveredCallChainPreviewResult> PreviewAsync(
        string underlyingSymbol,
        DateOnly asOf,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        var normalisedSymbol = underlyingSymbol.Trim().ToUpperInvariant();

        var expirations = await _chainService.GetExpirationsAsync(normalisedSymbol, ct).ConfigureAwait(false);
        if (expirations.Count == 0)
        {
            throw new ConfigurationException(
                $"No options chain provider returned expirations for {normalisedSymbol}.");
        }

        var nextExpiries = expirations.Where(e => e >= asOf).OrderBy(static d => d).Take(4).ToList();
        if (nextExpiries.Count == 0)
        {
            return new CoveredCallChainPreviewResult(0m, []);
        }

        var opts = _options.CurrentValue;
        var allCandidates = new List<OptionCandidateInfo>();
        decimal underlyingPrice = 0m;

        foreach (var expiry in nextExpiries)
        {
            ct.ThrowIfCancellationRequested();
            OptionChainSnapshot? snapshot;
            try
            {
                snapshot = await _chainService
                    .FetchChainSnapshotAsync(normalisedSymbol, expiry, opts.MaxStrikesPerExpiration, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "CoveredCallChainProviderFactory.PreviewAsync: snapshot fetch failed for {Symbol} {Expiry}",
                    normalisedSymbol, expiry);
                continue;
            }

            if (snapshot is null || snapshot.Calls.Count == 0)
            {
                continue;
            }

            if (underlyingPrice == 0m)
            {
                underlyingPrice = snapshot.UnderlyingPrice;
            }

            allCandidates.AddRange(ConvertCalls(snapshot, asOf));
        }

        return new CoveredCallChainPreviewResult(underlyingPrice, allCandidates);
    }

    /// <summary>
    /// Converts the calls in an <see cref="OptionChainSnapshot"/> into strategy-side
    /// <see cref="OptionCandidateInfo"/> records. Surfaces only fields that are well-defined
    /// from a chain quote.
    /// </summary>
    public static List<OptionCandidateInfo> ConvertCalls(OptionChainSnapshot snapshot, DateOnly scanDate)
    {
        var result = new List<OptionCandidateInfo>(snapshot.Calls.Count);
        foreach (var call in snapshot.Calls)
        {
            var contract = call.Contract;
            var dte = Math.Max(0, contract.Expiration.DayNumber - scanDate.DayNumber);

            // The strategy's filters reject zero-bid contracts and require a usable delta.
            // If the provider didn't supply delta, default to 0.0 (which fails the strategy's
            // MaxDelta guard unless the operator allows it).
            var delta = (double)(call.Delta ?? 0m);
            var iv = call.ImpliedVolatility.HasValue ? (double?)call.ImpliedVolatility.Value : null;
            var vega = call.Vega.HasValue ? (double?)call.Vega.Value : null;

            result.Add(new OptionCandidateInfo(
                UnderlyingSymbol: snapshot.UnderlyingSymbol,
                Strike: contract.Strike,
                Expiration: contract.Expiration,
                Style: contract.Style,
                Multiplier: (int)contract.Multiplier,
                Bid: call.BidPrice,
                Ask: call.AskPrice,
                DaysToExpiration: dte,
                OpenInterest: call.OpenInterest ?? 0L,
                Volume: call.Volume ?? 0L,
                Delta: delta,
                ImpliedVolatility: iv,
                Vega: vega,
                IvPercentile: null,
                DaysToNextExDiv: null,
                NextDividendAmount: null));
        }

        return result;
    }
}
