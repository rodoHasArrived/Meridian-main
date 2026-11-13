using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Config;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.AlphaVantage;

/// <summary>
/// Fetches dividend and split history from Alpha Vantage daily adjusted series.
/// </summary>
/// <remarks>
/// Alpha Vantage returns corporate-action evidence as daily adjusted time-series
/// fields: <c>7. dividend amount</c> and <c>8. split coefficient</c>. This adapter
/// projects those fields into the shared <see cref="ICorporateActionProvider"/> contract.
/// </remarks>
[DataSource("alphavantage-corp-actions", "Alpha Vantage Corporate Actions", DataSourceType.Reference, DataSourceCategory.Free,
    Priority = 25,
    EnabledByDefault = false,
    Description = "Dividend and split extraction from Alpha Vantage daily adjusted prices.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
[RequiresCredential("ALPHA_VANTAGE_API_KEY",
    EnvironmentVariables = new[] { "ALPHA_VANTAGE_API_KEY", "ALPHAVANTAGE__APIKEY", "ALPHAVANTAGE_API_KEY", "MDC_ALPHA_VANTAGE_API_KEY" },
    DisplayName = "API Key",
    Description = "Alpha Vantage API key from https://www.alphavantage.co/support/#api-key")]
public sealed partial class AlphaVantageCorporateActionProvider : ICorporateActionProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ICredentialContext? _credentials;
    private readonly ILogger<AlphaVantageCorporateActionProvider> _logger;
    private readonly RateLimiterRegistry _rateLimiterRegistry;
    private readonly RequestBudgetConfiguration _quotaBudget;

    public AlphaVantageCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AlphaVantageCorporateActionProvider> logger,
        ICredentialContext? credentials = null,
        RateLimiterRegistry? rateLimiterRegistry = null,
        AlphaVantageQuotaConfig? quotaConfig = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _credentials = credentials;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimiterRegistry = rateLimiterRegistry ?? RateLimiterRegistry.Shared;
        _quotaBudget = ProviderQuotaBudgets.AlphaVantage(quotaConfig ?? configuration.GetSection("ProviderQuotas:AlphaVantage").Get<AlphaVantageQuotaConfig>());
    }

    public string ProviderId => "alphavantage";

    public CorporateActionProviderReleaseStatusDto ReleaseStatus =>
        CorporateActionProviderReleaseStatusDto.ReviewOnly;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
    {
        var result = await FetchWithOutcomeAsync(ticker, securityId, ct).ConfigureAwait(false);
        if (result.Failures.FirstOrDefault(failure => failure.Code == "rate-limited") is { } rateLimit)
        {
            throw new RateLimitException(
                "Alpha Vantage corporate-action rate limit exceeded.",
                provider: ProviderId,
                symbol: SymbolNormalization.Normalize(ticker),
                retryAfter: rateLimit.RetryAfter ?? TimeSpan.FromMinutes(1));
        }

        return result.Actions;
    }

    /// <inheritdoc />
    public async Task<CorporateActionFetchResult> FetchWithOutcomeAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ct.ThrowIfCancellationRequested();

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug(
                "Alpha Vantage API key not configured; corporate action fetch for {Ticker} skipped.", ticker);
            return CorporateActionFetchResult.Failed("credentials-missing");
        }

        var normalizedSymbol = SymbolNormalization.Normalize(ticker);
        var url =
            $"/query?function=TIME_SERIES_DAILY_ADJUSTED&symbol={Uri.EscapeDataString(normalizedSymbol)}&outputsize=full";

        using var client = _httpClientFactory.CreateClient(HttpClientNames.AlphaVantageHistorical);
        client.BaseAddress ??= new Uri("https://www.alphavantage.co/");

        try
        {
            using var response = await SendAsync(client, url, ct).ConfigureAwait(false);
            if (response is null)
                return CorporateActionFetchResult.Failed("credentials-missing");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var failure = CorporateActionFetchResult.FromHttpFailure(response);
                return failure with
                {
                    Failures = failure.Failures.Select(item => item with
                    {
                        RetryAfter = item.RetryAfter ?? TimeSpan.FromMinutes(1),
                    }).ToArray(),
                };
            }

            if (!response.IsSuccessStatusCode || CorporateActionFetchResult.IsPartialResponse(response))
            {
                _logger.LogDebug(
                    "Alpha Vantage corporate actions returned {StatusCode} for {Ticker}; skipping.",
                    (int)response.StatusCode,
                    ticker);
                return CorporateActionFetchResult.FromHttpFailure(response);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (IsRateLimitBody(json))
            {
                return CorporateActionFetchResult.Failed("rate-limited", retryAfter: TimeSpan.FromMinutes(1));
            }

            if (IsErrorBody(json))
            {
                _logger.LogDebug(
                    "Alpha Vantage corporate actions returned an error body for {Ticker}; skipping.",
                    normalizedSymbol);
                return CorporateActionFetchResult.Failed("provider-error");
            }

            var series = JsonSerializer.Deserialize(
                    json,
                    AlphaVantageCorporateActionJsonContext.Default.AlphaVantageDailyAdjustedResponse)
                ?.TimeSeries;

            if (series is null)
                return CorporateActionFetchResult.Failed("invalid-payload");

            var commands = series
                .Where(entry => entry.Value is not null)
                .SelectMany(entry => MapToCommands(entry.Key, entry.Value, securityId))
                .OrderBy(command => command.ExDate)
                .ThenBy(command => command.ActionType, StringComparer.Ordinal)
                .ToArray();

            _logger.LogDebug(
                "Fetched {Count} corporate action(s) for {Ticker} from Alpha Vantage.",
                commands.Length,
                ticker);

            return series.Any(entry => !ProviderDateParsing.TryParseProviderDate(entry.Key, out _) || entry.Value is null
                || !decimal.TryParse(entry.Value.DividendAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var dividend)
                || dividend < 0m || !TryParsePositiveDecimal(entry.Value.SplitCoefficient, out _))
                ? new CorporateActionFetchResult(commands, CorporateActionFetchStatus.Partial, [new("invalid-record")])
                : CorporateActionFetchResult.Complete(commands);
        }
        catch (RateLimitException ex)
        {
            return CorporateActionFetchResult.FromException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Failed to fetch Alpha Vantage corporate actions for {Ticker}.", ticker);
            return CorporateActionFetchResult.FromException(ex);
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpClient client, string url, CancellationToken ct)
    {
        while (true)
        {
            var apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
                return null;
            var budget = _rateLimiterRegistry.GetOrCreate(ProviderId, apiKey, "api-requests", _quotaBudget);
            await budget.WaitForSlotAsync(ct).ConfigureAwait(false);
            if (!string.Equals(apiKey, ResolveApiKey(), StringComparison.Ordinal))
                continue;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}&apikey={Uri.EscapeDataString(apiKey)}");
            request.Options.Set(SharedResiliencePolicies.ProviderOwnsRetries, true);
            var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var retryAfter = HttpResiliencePolicy.ExtractRetryAfter(response, _rateLimiterRegistry.TimeProvider);
            if (retryAfter is { } delay)
                budget.ApplyCooldown(delay);
            else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                budget.ApplyCooldown(TimeSpan.FromMinutes(1));
            return response;
        }
    }

    private static IEnumerable<CorporateActionCommand> MapToCommands(
        string sessionDateText,
        AlphaVantageDailyPrice price,
        Guid securityId)
    {
        if (!ProviderDateParsing.TryParseProviderDate(sessionDateText, out var sessionDate))
        {
            yield break;
        }

        if (TryParsePositiveDecimal(price.DividendAmount, out var dividendAmount))
        {
            yield return new CorporateActionCommand(
                SecurityId: securityId,
                ActionType: "Dividend",
                ExDate: sessionDate,
                RecordDate: null,
                PayableDate: null,
                Amount: dividendAmount,
                Currency: null,
                SplitFromFactor: null,
                SplitToFactor: null,
                Description: "Alpha Vantage daily adjusted dividend amount.",
                SourceProvider: "alphavantage");
        }

        if (TryParsePositiveDecimal(price.SplitCoefficient, out var splitCoefficient) &&
            splitCoefficient != 1m)
        {
            yield return new CorporateActionCommand(
                SecurityId: securityId,
                ActionType: splitCoefficient < 1m ? "ReverseStockSplit" : "StockSplit",
                ExDate: sessionDate,
                RecordDate: null,
                PayableDate: null,
                Amount: null,
                Currency: null,
                SplitFromFactor: 1m,
                SplitToFactor: splitCoefficient,
                Description: $"Alpha Vantage daily adjusted split coefficient {splitCoefficient.ToString(CultureInfo.InvariantCulture)}.",
                SourceProvider: "alphavantage");
        }
    }

    private static bool IsRateLimitBody(string json)
        => json.Contains("\"Note\"", StringComparison.Ordinal) ||
            json.Contains("Thank you for using Alpha Vantage", StringComparison.Ordinal);

    private static bool IsErrorBody(string json)
        => json.Contains("\"Error Message\"", StringComparison.Ordinal) ||
            json.Contains("\"Information\"", StringComparison.Ordinal);

    private string? ResolveApiKey()
    {
        if (_credentials is not null)
            return _credentials.Get("ALPHA_VANTAGE_API_KEY");

        return FirstNonEmpty(
            _configuration["ALPHA_VANTAGE_API_KEY"],
            _configuration["ALPHAVANTAGE__APIKEY"],
            _configuration["Backfill:Providers:AlphaVantage:ApiKey"],
            _configuration["Backfill:Providers:alphavantage:ApiKey"],
            Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY"),
            Environment.GetEnvironmentVariable("ALPHAVANTAGE__APIKEY"),
            Environment.GetEnvironmentVariable("ALPHAVANTAGE_API_KEY"),
            Environment.GetEnvironmentVariable("MDC_ALPHA_VANTAGE_API_KEY"));
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParsePositiveDecimal(string? value, out decimal result)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
            result > 0m;
    }

    private sealed class AlphaVantageDailyAdjustedResponse
    {
        [JsonPropertyName("Time Series (Daily)")]
        public Dictionary<string, AlphaVantageDailyPrice>? TimeSeries { get; init; }
    }

    private sealed class AlphaVantageDailyPrice
    {
        [JsonPropertyName("7. dividend amount")]
        public string? DividendAmount { get; init; }

        [JsonPropertyName("8. split coefficient")]
        public string? SplitCoefficient { get; init; }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(AlphaVantageDailyAdjustedResponse))]
    private sealed partial class AlphaVantageCorporateActionJsonContext : JsonSerializerContext;
}
