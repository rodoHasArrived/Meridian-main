using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
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
    private readonly ILogger<TwelveDataCorporateActionProvider> _logger;

    public TwelveDataCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TwelveDataCorporateActionProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => "twelvedata";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
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
            return [];
        }

        var normalizedSymbol = SymbolNormalization.Normalize(ticker);
        using var client = _httpClientFactory.CreateClient(HttpClientNames.TwelveDataHistorical);
        client.BaseAddress ??= new Uri("https://api.twelvedata.com/");

        var dividends = await FetchDividendsAsync(client, normalizedSymbol, securityId, apiKey, ct)
            .ConfigureAwait(false);
        var splits = await FetchSplitsAsync(client, normalizedSymbol, securityId, apiKey, ct)
            .ConfigureAwait(false);

        var commands = dividends
            .Concat(splits)
            .OrderBy(command => command.ExDate)
            .ThenBy(command => command.ActionType, StringComparer.Ordinal)
            .ToArray();

        _logger.LogDebug(
            "Fetched {Count} corporate action(s) for {Ticker} from Twelve Data.",
            commands.Length,
            ticker);

        return commands;
    }

    private async Task<IReadOnlyList<CorporateActionCommand>> FetchDividendsAsync(
        HttpClient client,
        string symbol,
        Guid securityId,
        string apiKey,
        CancellationToken ct)
    {
        var url = $"/dividends?symbol={Uri.EscapeDataString(symbol)}&range=full&apikey={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Twelve Data dividends returned {StatusCode} for {Symbol}; skipping dividend actions.",
                    (int)response.StatusCode,
                    symbol);
                return [];
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    TwelveDataCorporateActionJsonContext.Default.TwelveDataDividendsResponse,
                    ct)
                .ConfigureAwait(false);

            if (payload?.Dividends is null || IsErrorStatus(payload.Status))
            {
                return [];
            }

            return payload.Dividends
                .Select(dividend => MapDividend(dividend, securityId, payload.Meta?.Currency))
                .Where(static command => command is not null)
                .Cast<CorporateActionCommand>()
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Twelve Data dividends for {Symbol}.", symbol);
            return [];
        }
    }

    private async Task<IReadOnlyList<CorporateActionCommand>> FetchSplitsAsync(
        HttpClient client,
        string symbol,
        Guid securityId,
        string apiKey,
        CancellationToken ct)
    {
        var url = $"/splits?symbol={Uri.EscapeDataString(symbol)}&range=full&apikey={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Twelve Data splits returned {StatusCode} for {Symbol}; skipping split actions.",
                    (int)response.StatusCode,
                    symbol);
                return [];
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    TwelveDataCorporateActionJsonContext.Default.TwelveDataSplitsResponse,
                    ct)
                .ConfigureAwait(false);

            if (payload?.Splits is null || IsErrorStatus(payload.Status))
            {
                return [];
            }

            return payload.Splits
                .Select(split => MapSplit(split, securityId))
                .Where(static command => command is not null)
                .Cast<CorporateActionCommand>()
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Twelve Data splits for {Symbol}.", symbol);
            return [];
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
