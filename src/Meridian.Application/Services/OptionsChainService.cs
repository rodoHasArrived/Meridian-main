using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Services;

/// <summary>
/// Application service that orchestrates option chain discovery, filtering,
/// and periodic snapshot collection based on <see cref="DerivativesConfig"/>.
/// </summary>
[ImplementsAdr("ADR-001", "Options chain service implementing provider abstraction")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken for graceful cancellation")]
public sealed class OptionsChainService
{
    private readonly OptionDataCollector _collector;
    private readonly IOptionsChainProvider? _provider;
    private readonly ILogger<OptionsChainService> _logger;

    public OptionsChainService(
        OptionDataCollector collector,
        ILogger<OptionsChainService> logger,
        IOptionsChainProvider? provider = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _provider = provider;
    }

    /// <summary>
    /// Returns whether an options chain provider is available.
    /// </summary>
    public bool IsProviderAvailable => _provider is not null;

    /// <summary>
    /// Gets available expiration dates for an underlying symbol.
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(
        string underlyingSymbol,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);

        if (_provider is null)
        {
            _logger.LogWarning("No options chain provider configured; returning empty expirations for {Symbol}", underlyingSymbol);
            return Array.Empty<DateOnly>();
        }

        return await _provider.GetExpirationsAsync(underlyingSymbol, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets available strikes for an underlying symbol and expiration.
    /// </summary>
    public async Task<IReadOnlyList<decimal>> GetStrikesAsync(
        string underlyingSymbol,
        DateOnly expiration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);

        if (_provider is null)
        {
            _logger.LogWarning("No options chain provider configured; returning empty strikes for {Symbol}", underlyingSymbol);
            return Array.Empty<decimal>();
        }

        return await _provider.GetStrikesAsync(underlyingSymbol, expiration, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches a chain snapshot from the provider, routes it through the collector,
    /// and returns the snapshot.
    /// </summary>
    public async Task<OptionChainSnapshot?> FetchChainSnapshotAsync(
        string underlyingSymbol,
        DateOnly expiration,
        int? strikeRange = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);

        if (_provider is null)
        {
            _logger.LogWarning("No options chain provider configured; cannot fetch chain for {Symbol}", underlyingSymbol);
            return null;
        }

        var chain = await _provider.GetChainSnapshotAsync(underlyingSymbol, expiration, strikeRange, ct)
            .ConfigureAwait(false);

        if (chain is not null)
        {
            _collector.OnChainSnapshot(chain);
            _logger.LogInformation(
                "Fetched chain snapshot for {Symbol} expiration {Expiration} with {Contracts} contracts",
                underlyingSymbol, expiration, chain.TotalContracts);
        }

        return chain;
    }

    /// <summary>
    /// Fetches a quote for a specific option contract from the provider.
    /// </summary>
    public async Task<OptionQuote?> FetchOptionQuoteAsync(
        OptionContractSpec contract,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (_provider is null)
        {
            _logger.LogWarning("No options chain provider configured; cannot fetch quote for {Contract}", contract);
            return null;
        }

        var quote = await _provider.GetOptionQuoteAsync(contract, ct).ConfigureAwait(false);

        if (quote is not null)
        {
            _collector.OnOptionQuote(quote);
        }

        return quote;
    }

    /// <summary>
    /// Fetches chain snapshots for all underlyings configured in the derivatives config,
    /// filtering expirations according to the configuration settings.
    /// </summary>
    public async Task<IReadOnlyList<OptionChainSnapshot>> FetchConfiguredChainsAsync(
        DerivativesConfig config,
        CancellationToken ct = default)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));

        if (!config.Enabled || _provider is null)
        {
            return Array.Empty<OptionChainSnapshot>();
        }

        var underlyings = config.Underlyings ?? Array.Empty<string>();
        var results = new List<OptionChainSnapshot>();

        foreach (var underlying in underlyings)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var expirations = await _provider.GetExpirationsAsync(underlying, ct).ConfigureAwait(false);

                var filtered = FilterExpirations(expirations, config);

                foreach (var expiry in filtered)
                {
                    ct.ThrowIfCancellationRequested();

                    var chain = await _provider.GetChainSnapshotAsync(
                        underlying, expiry, config.StrikeRange, ct).ConfigureAwait(false);

                    if (chain is not null)
                    {
                        _collector.OnChainSnapshot(chain);
                        results.Add(chain);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch option chains for {Symbol}: {ErrorType}",
                    underlying, ex.GetType().Name);
            }
        }

        _logger.LogInformation(
            "Fetched {ChainCount} chain snapshots for {UnderlyingCount} underlyings",
            results.Count, underlyings.Length);

        return results;
    }

    /// <summary>
    /// Returns the latest cached chain snapshot for an underlying and expiration.
    /// </summary>
    public OptionChainSnapshot? GetCachedChain(string underlyingSymbol, DateOnly expiration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        return _collector.GetLatestChain(underlyingSymbol, expiration);
    }

    /// <summary>
    /// Returns all cached chain snapshots for an underlying symbol.
    /// </summary>
    public IReadOnlyList<OptionChainSnapshot> GetCachedChainsForUnderlying(string underlyingSymbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        return _collector.GetChainsForUnderlying(underlyingSymbol);
    }

    /// <summary>
    /// Returns all option quotes for an underlying symbol.
    /// </summary>
    public IReadOnlyList<OptionQuote> GetQuotesForUnderlying(string underlyingSymbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        return _collector.GetQuotesForUnderlying(underlyingSymbol);
    }

    /// <summary>
    /// Returns a summary of collected option data.
    /// </summary>
    public OptionDataSummary GetSummary()
        => _collector.GetSummary();

    /// <summary>
    /// Returns all underlyings that have active option data.
    /// </summary>
    public IReadOnlyList<string> GetTrackedUnderlyings()
        => _collector.GetTrackedUnderlyings();

    private static IReadOnlyList<DateOnly> FilterExpirations(
        IReadOnlyList<DateOnly> expirations,
        DerivativesConfig config)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var maxDate = today.AddDays(config.MaxDaysToExpiration);

        var filtered = expirations
            .Where(e => e > today && e <= maxDate)
            .OrderBy(e => e)
            .ToList();

        if (config.ExpirationFilter is { Length: > 0 })
        {
            filtered = filtered.Where(e => MatchesExpirationFilter(e, config.ExpirationFilter)).ToList();
        }

        return filtered;
    }

    private static bool MatchesExpirationFilter(DateOnly expiration, string[] filters)
    {
        foreach (var filter in filters)
        {
            switch (filter.ToUpperInvariant())
            {
                case "WEEKLY":
                    // Weeklies expire on any Friday that is not the third Friday
                    if (expiration.DayOfWeek == DayOfWeek.Friday && !IsThirdFriday(expiration))
                        return true;
                    break;

                case "MONTHLY":
                    // Monthly options expire on the third Friday of the month
                    if (IsThirdFriday(expiration))
                        return true;
                    break;

                case "QUARTERLY":
                    // Quarterly options expire in March, June, September, December
                    if (IsThirdFriday(expiration) && expiration.Month % 3 == 0)
                        return true;
                    break;

                case "LEAPS":
                    // LEAPS are options with more than 1 year to expiration
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    if ((expiration.DayNumber - today.DayNumber) > 365)
                        return true;
                    break;
            }
        }

        return false;
    }

    private static bool IsThirdFriday(DateOnly date)
    {
        if (date.DayOfWeek != DayOfWeek.Friday)
            return false;
        // Third Friday is day 15-21
        return date.Day >= 15 && date.Day <= 21;
    }
}
