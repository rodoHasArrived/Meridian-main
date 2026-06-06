using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Meridian.Instruments.Options;

/// <summary>
/// Instruments service that orchestrates option chain discovery, filtering,
/// and periodic snapshot collection based on <see cref="DerivativesConfig"/>.
/// </summary>
[ImplementsAdr("ADR-001", "Options chain service implementing provider abstraction")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken for graceful cancellation")]
public sealed class OptionsChainService
{
    private readonly OptionDataCollector _collector;
    private readonly IReadOnlyList<IOptionsChainProvider> _providers;
    private readonly ILogger<OptionsChainService> _logger;
    private readonly IProviderConnectionHealthSource _healthSource;
    private static readonly Meter OptionsMeter = new("Meridian.OptionsChainService", "1.0");
    private static readonly Counter<long> FallbackCounter = OptionsMeter.CreateCounter<long>("options_provider_fallback_total");
    private static readonly Counter<long> FailoverCounter = OptionsMeter.CreateCounter<long>("options_provider_failover_total");
    private static readonly Histogram<double> ProviderLatencyMs = OptionsMeter.CreateHistogram<double>("options_provider_latency_ms");

    public OptionsChainService(
        OptionDataCollector collector,
        ILogger<OptionsChainService> logger,
        IOptionsChainProvider? provider = null)
        : this(
            collector,
            logger,
            new AlwaysHealthyProviderConnectionHealthSource(),
            provider is null ? Array.Empty<IOptionsChainProvider>() : new[] { provider })
    {
    }

    public OptionsChainService(
        OptionDataCollector collector,
        ILogger<OptionsChainService> logger,
        IEnumerable<IOptionsChainProvider>? providers)
        : this(collector, logger, new AlwaysHealthyProviderConnectionHealthSource(), providers)
    {
    }

    public OptionsChainService(
        OptionDataCollector collector,
        ILogger<OptionsChainService> logger,
        IProviderConnectionHealthSource healthSource,
        IEnumerable<IOptionsChainProvider>? providers)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthSource = healthSource ?? throw new ArgumentNullException(nameof(healthSource));
        _providers = NormalizeProviders(providers);
    }

    /// <summary>
    /// Maximum allowed strike range for options chain queries.
    /// </summary>
    public const int MaxStrikeRange = 100;

    /// <summary>
    /// Returns whether an options chain provider is available.
    /// </summary>
    public bool IsProviderAvailable => _providers.Count > 0;

    /// <summary>
    /// Returns a UI-friendly status summary for the active options provider.
    /// </summary>
    public OptionsProviderStatus GetProviderStatus()
    {
        var provider = GetPrimaryProvider();
        if (provider is null)
        {
            return new OptionsProviderStatus(
                ProviderId: null,
                ProviderDisplayName: null,
                Mode: "Unavailable",
                IsFallback: false,
                Message: "No options provider configured.");
        }

        var providerId = NormalizeProviderId(provider.ProviderId);
        var isFallback = string.Equals(providerId, "synthetic", StringComparison.Ordinal);
        var mode = isFallback ? "Fallback" : "Configured";
        var message = isFallback
            ? $"{provider.ProviderDisplayName} fallback is active."
            : $"{provider.ProviderDisplayName} is configured.";

        return new OptionsProviderStatus(
            ProviderId: providerId,
            ProviderDisplayName: provider.ProviderDisplayName,
            Mode: mode,
            IsFallback: isFallback,
            Message: message);
    }

    /// <summary>
    /// Gets available expiration dates for an underlying symbol.
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(
        string underlyingSymbol,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);

        var expirations = await QueryProvidersAsync(
            provider => provider.GetExpirationsAsync(underlyingSymbol, ct),
            static result => result.Count > 0,
            $"expirations for {underlyingSymbol}",
            ct).ConfigureAwait(false);

        if (expirations is null)
        {
            _logger.LogWarning("No options chain provider configured; returning empty expirations for {Symbol}", underlyingSymbol);
            return Array.Empty<DateOnly>();
        }

        return expirations;
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

        var strikes = await QueryProvidersAsync(
            provider => provider.GetStrikesAsync(underlyingSymbol, expiration, ct),
            static result => result.Count > 0,
            $"strikes for {underlyingSymbol} {expiration}",
            ct).ConfigureAwait(false);

        if (strikes is null)
        {
            _logger.LogWarning("No options chain provider configured; returning empty strikes for {Symbol}", underlyingSymbol);
            return Array.Empty<decimal>();
        }

        return strikes;
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

        var chain = await QueryProvidersAsync(
            provider => provider.GetChainSnapshotAsync(underlyingSymbol, expiration, strikeRange, ct),
            static result => result is not null,
            $"chain snapshot for {underlyingSymbol} {expiration}",
            ct).ConfigureAwait(false);

        if (chain is null)
        {
            _logger.LogWarning("No options chain provider returned a chain for {Symbol}", underlyingSymbol);
            return null;
        }

        _collector.OnChainSnapshot(chain);
        _logger.LogInformation(
            "Fetched chain snapshot for {Symbol} expiration {Expiration} with {Contracts} contracts",
            underlyingSymbol, expiration, chain.TotalContracts);

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

        var quote = await QueryProvidersAsync(
            provider => provider.GetOptionQuoteAsync(contract, ct),
            static result => result is not null,
            $"quote for {contract}",
            ct).ConfigureAwait(false);

        if (quote is null)
        {
            _logger.LogWarning("No options chain provider returned a quote for {Contract}", contract);
            return null;
        }

        _collector.OnOptionQuote(quote);

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

        if (!config.Enabled || _providers.Count == 0)
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
                var expirations = await QueryProvidersAsync(
                    provider => provider.GetExpirationsAsync(underlying, ct),
                    static result => result.Count > 0,
                    $"configured expirations for {underlying}",
                    ct).ConfigureAwait(false) ?? Array.Empty<DateOnly>();

                var filtered = FilterExpirations(expirations, config);

                foreach (var expiry in filtered)
                {
                    ct.ThrowIfCancellationRequested();

                    var chain = await QueryProvidersAsync(
                        provider => provider.GetChainSnapshotAsync(underlying, expiry, config.StrikeRange, ct),
                        static result => result is not null,
                        $"configured chain snapshot for {underlying} {expiry}",
                        ct).ConfigureAwait(false);

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

    private IOptionsChainProvider? GetPrimaryProvider()
        => _providers.Count == 0 ? null : _providers[0];

    private async Task<T?> QueryProvidersAsync<T>(
        Func<IOptionsChainProvider, Task<T>> query,
        Func<T, bool> hasResult,
        string operation,
        CancellationToken ct)
    {
        var failures = new List<string>();

        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();
            var providerId = NormalizeProviderId(provider.ProviderId);
            var health = await _healthSource.GetHealthAsync(providerId, providerId, ct).ConfigureAwait(false);
            if (!health.IsHealthy)
            {
                var reason = $"provider {providerId} unhealthy ({health.Status})";
                failures.Add(reason);
                _logger.LogWarning("Skipping options provider {ProviderId} for {Operation}: {Reason}", providerId, operation, reason);
                continue;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var result = await query(provider).ConfigureAwait(false);
                sw.Stop();
                ProviderLatencyMs.Record(sw.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("provider", providerId),
                    new KeyValuePair<string, object?>("operation", operation));
                if (hasResult(result))
                {
                    if (failures.Count > 0)
                    {
                        _logger.LogInformation(
                            "Options provider failover success for {Operation}. Winner={ProviderId}; prior_failures={Failures}",
                            operation,
                            providerId,
                            string.Join(" | ", failures));
                        FailoverCounter.Add(1, new KeyValuePair<string, object?>("provider", providerId));
                    }
                    return result;
                }

                var emptyReason = $"provider {providerId} returned incomplete/empty result";
                failures.Add(emptyReason);
                _logger.LogDebug(
                    "Options provider {ProviderId} returned no data for {Operation}; trying fallback provider.",
                    providerId,
                    operation);
                FallbackCounter.Add(1,
                    new KeyValuePair<string, object?>("provider", providerId),
                    new KeyValuePair<string, object?>("reason", "empty"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var retriable = ex is TimeoutException || ex is HttpRequestException || ex is TaskCanceledException;
                var reason = $"{providerId} failed ({ex.GetType().Name}): {ex.Message}";
                failures.Add(reason);
                _logger.LogWarning(
                    ex,
                    "Options provider {ProviderId} failed while fetching {Operation}; retriable={Retriable}; trying fallback provider.",
                    providerId,
                    operation,
                    retriable);
                FallbackCounter.Add(1,
                    new KeyValuePair<string, object?>("provider", providerId),
                    new KeyValuePair<string, object?>("reason", retriable ? "retriable-error" : "error"));
            }
        }

        if (failures.Count > 0)
        {
            _logger.LogError("All options providers failed for {Operation}. Provenance: {Failures}", operation, string.Join(" | ", failures));
        }
        return default;
    }

    private static IReadOnlyList<IOptionsChainProvider> NormalizeProviders(IEnumerable<IOptionsChainProvider>? providers)
    {
        if (providers is null)
            return Array.Empty<IOptionsChainProvider>();

        return providers
            .OrderBy(static provider => provider.ProviderPriority)
            .GroupBy(static provider => NormalizeProviderId(provider.ProviderId), StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string NormalizeProviderId(string providerId)
        => string.IsNullOrWhiteSpace(providerId) ? string.Empty : providerId.Trim().ToLowerInvariant();

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

internal sealed class AlwaysHealthyProviderConnectionHealthSource : IProviderConnectionHealthSource
{
    public ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(string connectionId, string providerFamilyId, CancellationToken ct = default)
        => ValueTask.FromResult(new ProviderConnectionHealthSnapshot(connectionId, providerFamilyId, IsHealthy: true, Status: "unknown"));
}

/// <summary>
/// Summary of the currently active options provider for UI status surfaces.
/// </summary>
public sealed record OptionsProviderStatus(
    string? ProviderId,
    string? ProviderDisplayName,
    string Mode,
    bool IsFallback,
    string Message);
