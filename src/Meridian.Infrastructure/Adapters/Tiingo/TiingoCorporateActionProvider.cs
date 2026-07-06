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

namespace Meridian.Infrastructure.Adapters.Tiingo;

/// <summary>
/// Fetches dividend and split history from Tiingo adjusted end-of-day prices.
/// </summary>
/// <remarks>
/// Tiingo exposes corporate-action evidence as <c>divCash</c> and <c>splitFactor</c>
/// fields on the adjusted EOD price endpoint rather than through a separate
/// announcement feed. This adapter projects those fields into the shared
/// <see cref="ICorporateActionProvider"/> contract.
/// </remarks>
[DataSource("tiingo-corp-actions", "Tiingo Corporate Actions", DataSourceType.Reference, DataSourceCategory.Free,
    Priority = 15,
    EnabledByDefault = false,
    Description = "Dividend and split extraction from Tiingo adjusted EOD prices.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
[RequiresCredential("TIINGO_API_TOKEN",
    EnvironmentVariables = new[] { "TIINGO_API_TOKEN", "TIINGO__TOKEN", "TIINGO_TOKEN", "TIINGO_API_KEY" },
    DisplayName = "API Token",
    Description = "Tiingo API token from https://www.tiingo.com/account/api/token")]
public sealed partial class TiingoCorporateActionProvider : ICorporateActionProvider
{
    private static readonly DateOnly DefaultStartDate = new(2000, 1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TiingoCorporateActionProvider> _logger;

    public TiingoCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TiingoCorporateActionProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => "tiingo";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ct.ThrowIfCancellationRequested();

        var apiToken = ResolveApiToken();
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            _logger.LogDebug(
                "Tiingo API token not configured; corporate action fetch for {Ticker} skipped.", ticker);
            return [];
        }

        var normalizedSymbol = SymbolNormalization.NormalizeForTiingo(ticker);
        var startDate = DefaultStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url =
            $"/tiingo/daily/{Uri.EscapeDataString(normalizedSymbol)}/prices?startDate={startDate}&endDate={endDate}&token={Uri.EscapeDataString(apiToken)}";

        using var client = _httpClientFactory.CreateClient(HttpClientNames.TiingoHistorical);
        client.BaseAddress ??= new Uri("https://api.tiingo.com/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", apiToken);

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Tiingo corporate actions returned {StatusCode} for {Ticker}; skipping.",
                    (int)response.StatusCode,
                    ticker);
                return [];
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var prices = await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    TiingoCorporateActionJsonContext.Default.TiingoCorporateActionPriceArray,
                    ct)
                .ConfigureAwait(false)
                ?? [];

            var commands = prices
                .SelectMany(price => MapToCommands(price, securityId))
                .OrderBy(command => command.ExDate)
                .ThenBy(command => command.ActionType, StringComparer.Ordinal)
                .ToArray();

            _logger.LogDebug(
                "Fetched {Count} corporate action(s) for {Ticker} from Tiingo.",
                commands.Length,
                ticker);

            return commands;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Tiingo corporate actions for {Ticker}.", ticker);
            return [];
        }
    }

    private IEnumerable<CorporateActionCommand> MapToCommands(
        TiingoCorporateActionPrice price,
        Guid securityId)
    {
        if (!TryParseSessionDate(price.Date, out var sessionDate))
        {
            yield break;
        }

        if (price.DivCash is > 0)
        {
            yield return new CorporateActionCommand(
                SecurityId: securityId,
                ActionType: "Dividend",
                ExDate: sessionDate,
                RecordDate: null,
                PayableDate: null,
                Amount: price.DivCash,
                Currency: null,
                SplitFromFactor: null,
                SplitToFactor: null,
                Description: "Tiingo adjusted EOD dividend cash amount.",
                SourceProvider: ProviderId);
        }

        if (price.SplitFactor is > 0 and not 1m)
        {
            yield return new CorporateActionCommand(
                SecurityId: securityId,
                ActionType: price.SplitFactor.Value < 1m ? "ReverseStockSplit" : "StockSplit",
                ExDate: sessionDate,
                RecordDate: null,
                PayableDate: null,
                Amount: null,
                Currency: null,
                SplitFromFactor: 1m,
                SplitToFactor: price.SplitFactor,
                Description: $"Tiingo adjusted EOD split factor {price.SplitFactor.Value.ToString(CultureInfo.InvariantCulture)}.",
                SourceProvider: ProviderId);
        }
    }

    private string? ResolveApiToken()
    {
        return FirstNonEmpty(
            _configuration["TIINGO_API_TOKEN"],
            _configuration["TIINGO__TOKEN"],
            _configuration["Backfill:Providers:Tiingo:ApiToken"],
            _configuration["Backfill:Providers:tiingo:ApiToken"],
            Environment.GetEnvironmentVariable("TIINGO_API_TOKEN"),
            Environment.GetEnvironmentVariable("TIINGO__TOKEN"),
            Environment.GetEnvironmentVariable("TIINGO_TOKEN"),
            Environment.GetEnvironmentVariable("MDC_TIINGO_TOKEN"),
            Environment.GetEnvironmentVariable("TIINGO_API_KEY"));
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

    private static bool TryParseSessionDate(string? value, out DateOnly sessionDate)
    {
        sessionDate = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 10)
        {
            return false;
        }

        return DateOnly.TryParseExact(
            value[..10],
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out sessionDate);
    }

    private sealed class TiingoCorporateActionPrice
    {
        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("divCash")]
        public decimal? DivCash { get; init; }

        [JsonPropertyName("splitFactor")]
        public decimal? SplitFactor { get; init; }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(TiingoCorporateActionPrice[]))]
    private sealed partial class TiingoCorporateActionJsonContext : JsonSerializerContext;
}
