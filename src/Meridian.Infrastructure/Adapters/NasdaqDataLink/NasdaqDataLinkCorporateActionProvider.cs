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

namespace Meridian.Infrastructure.Adapters.NasdaqDataLink;

/// <summary>
/// Fetches dividend and split history from Nasdaq Data Link time-series datasets.
/// </summary>
/// <remarks>
/// Nasdaq Data Link/Quandl datasets expose corporate-action evidence through the
/// <c>Ex-Dividend</c> and <c>Split Ratio</c> columns. This adapter projects those
/// dataset rows into the shared <see cref="ICorporateActionProvider"/> contract.
/// </remarks>
[DataSource("nasdaq-corp-actions", "Nasdaq Data Link Corporate Actions", DataSourceType.Reference, DataSourceCategory.Aggregator,
    Priority = 30,
    EnabledByDefault = false,
    Description = "Dividend and split extraction from Nasdaq Data Link adjusted datasets.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
[RequiresCredential("NASDAQ_DATA_LINK_API_KEY",
    EnvironmentVariables = new[] { "NASDAQ_DATA_LINK_API_KEY", "NASDAQ__APIKEY", "NASDAQ_API_KEY" },
    Optional = true,
    DisplayName = "API Key",
    Description = "Nasdaq Data Link API key from https://data.nasdaq.com/account/profile")]
public sealed partial class NasdaqDataLinkCorporateActionProvider : ICorporateActionProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NasdaqDataLinkCorporateActionProvider> _logger;

    public NasdaqDataLinkCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<NasdaqDataLinkCorporateActionProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => "nasdaq";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ct.ThrowIfCancellationRequested();

        var normalizedSymbol = SymbolNormalization.NormalizeForNasdaqDataLink(ticker);
        var database = ResolveDatabase();
        var apiKey = ResolveApiKey();
        var url = $"/api/v3/datasets/{Uri.EscapeDataString(database)}/{Uri.EscapeDataString(normalizedSymbol)}.json";
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            url += $"?api_key={Uri.EscapeDataString(apiKey)}";
        }

        using var client = _httpClientFactory.CreateClient(HttpClientNames.NasdaqDataLinkHistorical);
        client.BaseAddress ??= new Uri("https://data.nasdaq.com/");

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Nasdaq Data Link corporate actions returned {StatusCode} for {Ticker}; skipping.",
                    (int)response.StatusCode,
                    ticker);
                return [];
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dataset = (await JsonSerializer.DeserializeAsync(
                    jsonStream,
                    NasdaqDataLinkCorporateActionJsonContext.Default.NasdaqDataLinkDatasetResponse,
                    ct)
                .ConfigureAwait(false))
                ?.Dataset;

            if (dataset?.ColumnNames is null || dataset.Data is null || dataset.Data.Length == 0)
            {
                return [];
            }

            var columnIndex = BuildColumnIndex(dataset.ColumnNames);
            var commands = dataset.Data
                .SelectMany(row => MapToCommands(row, columnIndex, securityId))
                .OrderBy(command => command.ExDate)
                .ThenBy(command => command.ActionType, StringComparer.Ordinal)
                .ToArray();

            _logger.LogDebug(
                "Fetched {Count} corporate action(s) for {Ticker} from Nasdaq Data Link.",
                commands.Length,
                ticker);

            return commands;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Nasdaq Data Link corporate actions for {Ticker}.", ticker);
            return [];
        }
    }

    private static IEnumerable<CorporateActionCommand> MapToCommands(
        JsonElement[] row,
        IReadOnlyDictionary<string, int> columnIndex,
        Guid securityId)
    {
        if (!TryGetString(row, columnIndex, "Date", out var sessionDateText) ||
            !ProviderDateParsing.TryParseProviderDate(sessionDateText, out var sessionDate))
        {
            yield break;
        }

        if (TryGetPositiveDecimal(row, columnIndex, "Ex-Dividend", out var dividendAmount))
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
                Description: "Nasdaq Data Link dataset ex-dividend amount.",
                SourceProvider: "nasdaq");
        }

        if (TryGetPositiveDecimal(row, columnIndex, "Split Ratio", out var splitRatio) &&
            splitRatio != 1m)
        {
            yield return new CorporateActionCommand(
                SecurityId: securityId,
                ActionType: splitRatio < 1m ? "ReverseStockSplit" : "StockSplit",
                ExDate: sessionDate,
                RecordDate: null,
                PayableDate: null,
                Amount: null,
                Currency: null,
                SplitFromFactor: 1m,
                SplitToFactor: splitRatio,
                Description: $"Nasdaq Data Link dataset split ratio {splitRatio.ToString(CultureInfo.InvariantCulture)}.",
                SourceProvider: "nasdaq");
        }
    }

    private string ResolveDatabase()
    {
        return FirstNonEmpty(
                _configuration["NASDAQ_DATA_LINK_DATABASE"],
                _configuration["NASDAQ__DATABASE"],
                _configuration["Backfill:Providers:Nasdaq:Database"],
                _configuration["Backfill:Providers:NasdaqDataLink:Database"],
                Environment.GetEnvironmentVariable("NASDAQ_DATA_LINK_DATABASE"),
                Environment.GetEnvironmentVariable("NASDAQ__DATABASE"))
            ?? "WIKI";
    }

    private string? ResolveApiKey()
    {
        return FirstNonEmpty(
            _configuration["NASDAQ_DATA_LINK_API_KEY"],
            _configuration["NASDAQ__APIKEY"],
            _configuration["Backfill:Providers:Nasdaq:ApiKey"],
            _configuration["Backfill:Providers:NasdaqDataLink:ApiKey"],
            Environment.GetEnvironmentVariable("NASDAQ_DATA_LINK_API_KEY"),
            Environment.GetEnvironmentVariable("NASDAQ__APIKEY"),
            Environment.GetEnvironmentVariable("NASDAQ_API_KEY"));
    }

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

    private static Dictionary<string, int> BuildColumnIndex(string[] columns)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Length; i++)
        {
            index[columns[i]] = i;
        }

        return index;
    }

    private static bool TryGetString(
        JsonElement[] row,
        IReadOnlyDictionary<string, int> columnIndex,
        string column,
        out string? value)
    {
        value = null;
        if (!columnIndex.TryGetValue(column, out var index) || index >= row.Length)
        {
            return false;
        }

        var element = row[index];
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetPositiveDecimal(
        JsonElement[] row,
        IReadOnlyDictionary<string, int> columnIndex,
        string column,
        out decimal value)
    {
        value = default;
        if (!columnIndex.TryGetValue(column, out var index) || index >= row.Length)
        {
            return false;
        }

        var element = row[index];
        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                element.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var stringValue) => stringValue,
            _ => 0m
        };

        if (parsed <= 0m)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private sealed class NasdaqDataLinkDatasetResponse
    {
        [JsonPropertyName("dataset")]
        public NasdaqDataLinkDataset? Dataset { get; init; }
    }

    private sealed class NasdaqDataLinkDataset
    {
        [JsonPropertyName("column_names")]
        public string[]? ColumnNames { get; init; }

        [JsonPropertyName("data")]
        public JsonElement[][]? Data { get; init; }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(NasdaqDataLinkDatasetResponse))]
    private sealed partial class NasdaqDataLinkCorporateActionJsonContext : JsonSerializerContext;
}
