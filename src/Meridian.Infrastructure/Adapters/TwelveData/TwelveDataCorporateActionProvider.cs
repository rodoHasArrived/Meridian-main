using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwelveDataQuotaConfig = Meridian.Core.Config.TwelveDataQuotaConfig;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.TwelveData;

/// <summary>
/// Fetches dividend and split history from Twelve Data fundamentals endpoints.
/// </summary>
/// <remarks>
/// Twelve Data exposes corporate-action evidence through separate <c>/dividends</c>
/// and <c>/splits</c> endpoints. This adapter projects those payloads into the
/// shared <see cref="ICorporateActionProvider"/> contract while preserving
/// provider-supplied split factors.
/// </remarks>
[DataSource("twelvedata-corp-actions", "Twelve Data Corporate Actions", DataSourceType.Reference, DataSourceCategory.Premium,
    Priority = 22,
    EnabledByDefault = false,
    Description = "Dividend and split extraction from Twelve Data fundamentals endpoints.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
[RequiresCredential("TWELVEDATA_API_KEY",
    EnvironmentVariables = new[] { "TWELVEDATA_API_KEY", "TWELVEDATA__APIKEY", "TWELVEDATA_APIKEY", "MDC_TWELVEDATA_API_KEY" },
    DisplayName = "API Key",
    Description = "Twelve Data API key from https://twelvedata.com/account/api-keys")]
public sealed partial class TwelveDataCorporateActionProvider : ICorporateActionProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ICredentialContext? _credentials;
    private readonly RateLimiterRegistry _rateLimiterRegistry;
    private readonly ILogger<TwelveDataCorporateActionProvider> _logger;
    private readonly TwelveDataQuotaConfig _quotaConfig;
    private readonly RequestBudgetConfiguration _quotaBudget;
    private const int FundamentalsCreditCost = 20;

    public TwelveDataCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TwelveDataCorporateActionProvider> logger,
        ICredentialContext? credentials = null,
        RateLimiterRegistry? rateLimiterRegistry = null,
        TwelveDataQuotaConfig? quotaConfig = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _credentials = credentials;
        _rateLimiterRegistry = rateLimiterRegistry ?? RateLimiterRegistry.Shared;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _quotaConfig = quotaConfig ?? configuration.GetSection("ProviderQuotas:TwelveData").Get<TwelveDataQuotaConfig>() ?? new();
        _quotaBudget = ProviderQuotaBudgets.TwelveData(_quotaConfig);
    }

    public string ProviderId => "twelvedata";

    public CorporateActionProviderReleaseStatusDto ReleaseStatus =>
        CorporateActionProviderReleaseStatusDto.ReviewOnly;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
        => (await FetchWithOutcomeAsync(ticker, securityId, ct).ConfigureAwait(false)).Actions;

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
                "Twelve Data API key not configured; corporate action fetch for {Ticker} skipped.",
                ticker);
            return CorporateActionFetchResult.Failed("credentials-missing");
        }

        if (!_quotaConfig.FundamentalsEntitled)
            return CorporateActionFetchResult.Failed("entitlement-unavailable");
        if (_quotaBudget.Maximum < FundamentalsCreditCost || _quotaBudget.DailyMaximum is < FundamentalsCreditCost)
            return CorporateActionFetchResult.Failed("quota-capacity-insufficient");

        var normalizedSymbol = SymbolNormalization.Normalize(ticker);
        using var client = _httpClientFactory.CreateClient(HttpClientNames.TwelveDataHistorical);
        client.BaseAddress ??= new Uri("https://api.twelvedata.com/");

        var dividends = await FetchDividendsAsync(client, normalizedSymbol, securityId, ct)
            .ConfigureAwait(false);
        var splits = await FetchSplitsAsync(client, normalizedSymbol, securityId, ct)
            .ConfigureAwait(false);

        var result = CorporateActionFetchResult.Combine(dividends, splits);
        var commands = result.Actions
            .OrderBy(command => command.ExDate)
            .ThenBy(command => command.ActionType, StringComparer.Ordinal)
            .ToArray();

        _logger.LogDebug(
            "Fetched {Count} corporate action(s) for {Ticker} from Twelve Data.",
            commands.Length,
            ticker);

        return result with { Actions = commands };
    }

    private async Task<CorporateActionFetchResult> FetchDividendsAsync(
        HttpClient client,
        string symbol,
        Guid securityId,
        CancellationToken ct)
    {
        var url = $"/dividends?symbol={Uri.EscapeDataString(symbol)}&range=full";

        try
        {
            using var response = await SendAsync(client, url, ct).ConfigureAwait(false);
            if (response is null)
                return CorporateActionFetchResult.Failed("credentials-missing");
            if (!response.IsSuccessStatusCode || CorporateActionFetchResult.IsPartialResponse(response))
            {
                _logger.LogDebug(
                    "Twelve Data dividends returned {StatusCode} for {Symbol}; skipping dividend actions.",
                    (int)response.StatusCode,
                    symbol);
                return CorporateActionFetchResult.FromHttpFailure(response, "dividends");
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    TwelveDataCorporateActionJsonContext.Default.TwelveDataDividendsResponse,
                    ct)
                .ConfigureAwait(false);

            if (payload is not null && IsErrorStatus(payload.Status))
                return CorporateActionFetchResult.Failed(
                    payload.Code switch { 401 or 403 => "unauthorized", 429 => "rate-limited", _ => "provider-error" },
                    statusCode: (int)response.StatusCode, scope: "dividends");
            if (payload?.Dividends is null)
                return CorporateActionFetchResult.Failed("invalid-payload", scope: "dividends");

            var mapped = payload.Dividends.Select(dividend => dividend is null ? null : MapDividend(dividend, securityId, payload.Meta?.Currency)).ToArray();
            var commands = mapped.Where(static command => command is not null).Cast<CorporateActionCommand>().ToArray();
            return mapped.Any(static command => command is null)
                ? new CorporateActionFetchResult(commands, CorporateActionFetchStatus.Partial, [new("invalid-record", Scope: "dividends")])
                : CorporateActionFetchResult.Complete(commands);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Failed to fetch Twelve Data dividends for {Symbol}.", symbol);
            return CorporateActionFetchResult.FromException(ex, "dividends");
        }
    }

    private async Task<CorporateActionFetchResult> FetchSplitsAsync(
        HttpClient client,
        string symbol,
        Guid securityId,
        CancellationToken ct)
    {
        var url = $"/splits?symbol={Uri.EscapeDataString(symbol)}&range=full";

        try
        {
            using var response = await SendAsync(client, url, ct).ConfigureAwait(false);
            if (response is null)
                return CorporateActionFetchResult.Failed("credentials-missing");
            if (!response.IsSuccessStatusCode || CorporateActionFetchResult.IsPartialResponse(response))
            {
                _logger.LogDebug(
                    "Twelve Data splits returned {StatusCode} for {Symbol}; skipping split actions.",
                    (int)response.StatusCode,
                    symbol);
                return CorporateActionFetchResult.FromHttpFailure(response, "splits");
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    TwelveDataCorporateActionJsonContext.Default.TwelveDataSplitsResponse,
                    ct)
                .ConfigureAwait(false);

            if (payload is not null && IsErrorStatus(payload.Status))
                return CorporateActionFetchResult.Failed(
                    payload.Code switch { 401 or 403 => "unauthorized", 429 => "rate-limited", _ => "provider-error" },
                    statusCode: (int)response.StatusCode, scope: "splits");
            if (payload?.Splits is null)
                return CorporateActionFetchResult.Failed("invalid-payload", scope: "splits");

            var mapped = payload.Splits.Select(split => split is null ? null : MapSplit(split, securityId)).ToArray();
            var commands = mapped.Where(static command => command is not null).Cast<CorporateActionCommand>().ToArray();
            return mapped.Any(static command => command is null)
                ? new CorporateActionFetchResult(commands, CorporateActionFetchStatus.Partial, [new("invalid-record", Scope: "splits")])
                : CorporateActionFetchResult.Complete(commands);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Failed to fetch Twelve Data splits for {Symbol}.", symbol);
            return CorporateActionFetchResult.FromException(ex, "splits");
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpClient client, string url, CancellationToken ct)
    {
        while (true)
        {
            var apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
                return null;

            var budget = _rateLimiterRegistry.GetOrCreate(
                ProviderId, apiKey, "api-requests", _quotaBudget);
            await budget.WaitForUnitsAsync(FundamentalsCreditCost, ct).ConfigureAwait(false);
            // The selected owner can rotate or delete its credential while waiting.
            // Never send a captured old key after pacing, or charge a different key's quota.
            if (!string.Equals(apiKey, ResolveApiKey(), StringComparison.Ordinal))
                continue;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}&apikey={Uri.EscapeDataString(apiKey)}");
            request.Options.Set(SharedResiliencePolicies.ProviderOwnsRetries, true);
            var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var retryAfter = HttpResiliencePolicy.ExtractRetryAfter(response, _rateLimiterRegistry.TimeProvider);
            if (retryAfter is { } delay)
                budget.ApplyCooldown(delay);
            else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                budget.ApplyCooldown(TimeSpan.FromMinutes(1));
            return response;
        }
    }

    private CorporateActionCommand? MapDividend(
        TwelveDataDividend dividend,
        Guid securityId,
        string? currency)
    {
        if (!ProviderDateParsing.TryParseProviderDate(dividend.ExDate, out var exDate) ||
            dividend.Amount is not > 0m)
        {
            return null;
        }

        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: "Dividend",
            ExDate: exDate,
            RecordDate: null,
            PayableDate: null,
            Amount: dividend.Amount,
            Currency: NormalizeCurrency(currency),
            SplitFromFactor: null,
            SplitToFactor: null,
            Description: "Twelve Data dividends endpoint amount.",
            SourceProvider: ProviderId);
    }

    private CorporateActionCommand? MapSplit(
        TwelveDataSplit split,
        Guid securityId)
    {
        if (!ProviderDateParsing.TryParseProviderDate(split.Date, out var exDate))
        {
            return null;
        }

        var fromFactor = split.FromFactor is > 0m ? split.FromFactor : null;
        var toFactor = split.ToFactor is > 0m ? split.ToFactor : null;
        if (fromFactor is null && toFactor is null && split.Ratio is not > 0m)
        {
            return null;
        }

        var description = string.IsNullOrWhiteSpace(split.Description)
            ? $"Twelve Data split ratio {split.Ratio?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}."
            : split.Description.Trim();
        var splitRatio = fromFactor is > 0m && toFactor is > 0m
            ? toFactor / fromFactor
            : split.Ratio;
        var resolvedFromFactor = fromFactor ?? (splitRatio is > 0m ? 1m : null);
        var resolvedToFactor = toFactor ?? splitRatio;

        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: splitRatio is > 0m and < 1m ? "ReverseStockSplit" : "StockSplit",
            ExDate: exDate,
            RecordDate: null,
            PayableDate: null,
            Amount: null,
            Currency: null,
            SplitFromFactor: resolvedFromFactor,
            SplitToFactor: resolvedToFactor,
            Description: description,
            SourceProvider: ProviderId);
    }

    private string? ResolveApiKey()
    {
        if (_credentials is not null)
            return _credentials.Get("TWELVEDATA_API_KEY");

        return FirstNonEmpty(
            _configuration["TWELVEDATA_API_KEY"],
            _configuration["TWELVEDATA__APIKEY"],
            _configuration["Backfill:Providers:TwelveData:ApiKey"],
            _configuration["Backfill:Providers:twelvedata:ApiKey"],
            Environment.GetEnvironmentVariable("TWELVEDATA_API_KEY"),
            Environment.GetEnvironmentVariable("TWELVEDATA__APIKEY"),
            Environment.GetEnvironmentVariable("TWELVEDATA_APIKEY"),
            Environment.GetEnvironmentVariable("MDC_TWELVEDATA_API_KEY"));
    }

    private static bool IsErrorStatus(string? status)
        => status is not null && !status.Equals("ok", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private sealed class TwelveDataCorporateActionMeta
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; init; }
    }

    private sealed class TwelveDataDividendsResponse
    {
        [JsonPropertyName("meta")]
        public TwelveDataCorporateActionMeta? Meta { get; init; }

        [JsonPropertyName("dividends")]
        public TwelveDataDividend[]? Dividends { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }

    private sealed class TwelveDataDividend
    {
        [JsonPropertyName("ex_date")]
        public string? ExDate { get; init; }

        [JsonPropertyName("amount")]
        public decimal? Amount { get; init; }
    }

    private sealed class TwelveDataSplitsResponse
    {
        [JsonPropertyName("splits")]
        public TwelveDataSplit[]? Splits { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }

    private sealed class TwelveDataSplit
    {
        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("ratio")]
        public decimal? Ratio { get; init; }

        [JsonPropertyName("from_factor")]
        public decimal? FromFactor { get; init; }

        [JsonPropertyName("to_factor")]
        public decimal? ToFactor { get; init; }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(TwelveDataDividendsResponse))]
    [JsonSerializable(typeof(TwelveDataSplitsResponse))]
    private sealed partial class TwelveDataCorporateActionJsonContext : JsonSerializerContext;
}
