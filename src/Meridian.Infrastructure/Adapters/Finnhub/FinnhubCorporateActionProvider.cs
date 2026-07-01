using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Finnhub;

/// <summary>
/// Fetches dividend and split history from Finnhub corporate-action endpoints.
/// </summary>
/// <remarks>
/// Finnhub exposes dividend and split evidence through separate <c>/stock/dividend</c>
/// and <c>/stock/split</c> endpoints. This adapter projects those payloads into the
/// shared <see cref="ICorporateActionProvider"/> contract while preserving
/// provider-supplied dividend dates, currency, and split factors.
/// </remarks>
[DataSource("finnhub-corp-actions", "Finnhub Corporate Actions", DataSourceType.Reference, DataSourceCategory.Free,
    Priority = 18,
    EnabledByDefault = false,
    Description = "Dividend and split extraction from Finnhub corporate-action endpoints.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
[RequiresCredential("FINNHUB_API_KEY",
    EnvironmentVariables = new[] { "FINNHUB_API_KEY", "FINNHUB__APIKEY" },
    DisplayName = "API Key",
    Description = "Finnhub API key from https://finnhub.io/dashboard")]
public sealed partial class FinnhubCorporateActionProvider : ICorporateActionProvider
{
    private static readonly DateOnly DefaultStartDate = new(2000, 1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FinnhubCorporateActionProvider> _logger;

    public FinnhubCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FinnhubCorporateActionProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => "finnhub";

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
                "Finnhub API key not configured; corporate action fetch for {Ticker} skipped.",
                ticker);
            return [];
        }

        var normalizedSymbol = SymbolNormalization.Normalize(ticker);
        using var client = _httpClientFactory.CreateClient(HttpClientNames.FinnhubHistorical);
        client.BaseAddress ??= new Uri("https://finnhub.io/api/v1/");
        if (!client.DefaultRequestHeaders.Accept.Any(header =>
                string.Equals(header.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        client.DefaultRequestHeaders.Remove("X-Finnhub-Token");
        client.DefaultRequestHeaders.Add("X-Finnhub-Token", apiKey);

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
            "Fetched {Count} corporate action(s) for {Ticker} from Finnhub.",
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
        var (from, to) = BuildDateWindow();
        var url =
            $"stock/dividend?symbol={Uri.EscapeDataString(symbol)}&from={from}&to={to}&token={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Finnhub dividends returned {StatusCode} for {Symbol}; skipping dividend actions.",
                    (int)response.StatusCode,
                    symbol);
                return [];
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    FinnhubCorporateActionJsonContext.Default.FinnhubDividendArray,
                    ct)
                .ConfigureAwait(false)
                ?? [];

            return payload
                .Select(dividend => MapDividend(dividend, securityId))
                .Where(static command => command is not null)
                .Cast<CorporateActionCommand>()
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Finnhub dividends for {Symbol}.", symbol);
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
        var (from, to) = BuildDateWindow();
        var url =
            $"stock/split?symbol={Uri.EscapeDataString(symbol)}&from={from}&to={to}&token={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Finnhub splits returned {StatusCode} for {Symbol}; skipping split actions.",
                    (int)response.StatusCode,
                    symbol);
                return [];
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    FinnhubCorporateActionJsonContext.Default.FinnhubSplitArray,
                    ct)
                .ConfigureAwait(false)
                ?? [];

            return payload
                .Select(split => MapSplit(split, securityId))
                .Where(static command => command is not null)
                .Cast<CorporateActionCommand>()
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Finnhub splits for {Symbol}.", symbol);
            return [];
        }
    }

    private CorporateActionCommand? MapDividend(
        FinnhubDividend dividend,
        Guid securityId)
    {
        if (!TryParseDate(dividend.Date, out var exDate) ||
            dividend.Amount is not > 0m)
        {
            return null;
        }

        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: "Dividend",
            ExDate: exDate,
            RecordDate: TryParseDate(dividend.RecordDate, out var recordDate) ? recordDate : null,
            PayableDate: TryParseDate(dividend.PayDate ?? dividend.PaymentDate, out var payDate) ? payDate : null,
            Amount: dividend.Amount,
            Currency: NormalizeCurrency(dividend.Currency),
            SplitFromFactor: null,
            SplitToFactor: null,
            Description: BuildDividendDescription(dividend),
            SourceProvider: ProviderId);
    }

    private CorporateActionCommand? MapSplit(
        FinnhubSplit split,
        Guid securityId)
    {
        if (!TryParseDate(split.Date, out var exDate) ||
            split.FromFactor is not > 0m ||
            split.ToFactor is not > 0m)
        {
            return null;
        }

        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: "Split",
            ExDate: exDate,
            RecordDate: null,
            PayableDate: null,
            Amount: null,
            Currency: null,
            SplitFromFactor: split.FromFactor,
            SplitToFactor: split.ToFactor,
            Description: $"Finnhub split factor {split.FromFactor.Value.ToString(CultureInfo.InvariantCulture)}:{split.ToFactor.Value.ToString(CultureInfo.InvariantCulture)}.",
            SourceProvider: ProviderId);
    }

    private static (string From, string To) BuildDateWindow()
    {
        return (
            DefaultStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private string? ResolveApiKey()
    {
        return FirstNonEmpty(
            _configuration["FINNHUB_API_KEY"],
            _configuration["FINNHUB__APIKEY"],
            _configuration["Backfill:Providers:Finnhub:ApiKey"],
            _configuration["Backfill:Providers:finnhub:ApiKey"],
            Environment.GetEnvironmentVariable("FINNHUB_API_KEY"),
            Environment.GetEnvironmentVariable("FINNHUB__APIKEY"));
    }

    private static string BuildDividendDescription(FinnhubDividend dividend)
    {
        var frequency = string.IsNullOrWhiteSpace(dividend.Frequency)
            ? null
            : dividend.Frequency.Trim();

        return frequency is null
            ? "Finnhub dividend amount."
            : $"Finnhub dividend amount; frequency {frequency}.";
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value) &&
            DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
    }

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

    private sealed class FinnhubDividend
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("amount")]
        public decimal? Amount { get; init; }

        [JsonPropertyName("adjustedAmount")]
        public decimal? AdjustedAmount { get; init; }

        [JsonPropertyName("currency")]
        public string? Currency { get; init; }

        [JsonPropertyName("declarationDate")]
        public string? DeclarationDate { get; init; }

        [JsonPropertyName("freq")]
        public string? Frequency { get; init; }

        [JsonPropertyName("payDate")]
        public string? PayDate { get; init; }

        [JsonPropertyName("paymentDate")]
        public string? PaymentDate { get; init; }

        [JsonPropertyName("recordDate")]
        public string? RecordDate { get; init; }
    }

    private sealed class FinnhubSplit
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("fromFactor")]
        public decimal? FromFactor { get; init; }

        [JsonPropertyName("toFactor")]
        public decimal? ToFactor { get; init; }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(FinnhubDividend[]))]
    [JsonSerializable(typeof(FinnhubSplit[]))]
    private sealed partial class FinnhubCorporateActionJsonContext : JsonSerializerContext;
}
