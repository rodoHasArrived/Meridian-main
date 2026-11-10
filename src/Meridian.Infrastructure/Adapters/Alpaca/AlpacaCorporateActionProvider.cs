using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Fetches corporate action history (dividends and stock splits) from the Alpaca REST API.
/// Implements <see cref="ICorporateActionProvider"/> following the same attribute-based
/// discovery pattern as <see cref="IHistoricalDataProvider"/> implementations.
/// </summary>
/// <remarks>
/// Uses the Market Data API endpoint
/// <c>GET https://data.alpaca.markets/v1/corporate-actions?symbols={ticker}&amp;types=cash_dividend,forward_split,reverse_split</c>,
/// which accepts Trading API (paper or live) key pairs and returns each action type in its own
/// group. The retired <c>/v2/corporate_actions/announcements</c> route is not used: it is limited
/// to 90-day windows and returns its amounts as strings. Requests are authenticated via the
/// <c>APCA-API-KEY-ID</c> and <c>APCA-API-SECRET-KEY</c> headers read from the environment /
/// configuration. Stock dividends, spin-offs and mergers are not requested.
/// </remarks>
[DataSource("alpaca-corp-actions", "Alpaca Corporate Actions", DataSourceType.Reference, DataSourceCategory.Broker,
    Priority = 12,
    EnabledByDefault = false,
    Description = "Cash dividends and stock splits from the Alpaca Market Data corporate-actions endpoint.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
public sealed partial class AlpacaCorporateActionProvider : ICorporateActionProvider
{
    private const string BaseUrl = "https://data.alpaca.markets";

    // Earlier than the provider's history, so the request always covers all of it.
    private const string HistoryStart = "1990-01-01";
    private const int PageSize = 1000;
    private const int MaxPages = 50;

    // Group name in the response, in the order results are returned, with the action it carries.
    private static readonly (string Group, bool IsSplit, string Description)[] ActionGroups =
    [
        ("cash_dividends", false, "Cash dividend"),
        ("forward_splits", true, "Forward split"),
        ("reverse_splits", true, "Reverse split"),
    ];

    public string ProviderId => "alpaca";

    public CorporateActionProviderReleaseStatusDto ReleaseStatus =>
        CorporateActionProviderReleaseStatusDto.AcceptanceEligible;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ICredentialContext? _credentials;
    private readonly ILogger<AlpacaCorporateActionProvider> _logger;

    public AlpacaCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AlpacaCorporateActionProvider> logger,
        ICredentialContext? credentials = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _credentials = credentials;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        var keyId = _credentials is not null ? _credentials.Get("ALPACA_KEY_ID") : _configuration["ALPACA_KEY_ID"]
                    ?? _configuration["APCA_API_KEY_ID"]
                    ?? Environment.GetEnvironmentVariable("ALPACA_KEY_ID")
                    ?? Environment.GetEnvironmentVariable("APCA_API_KEY_ID");

        var secretKey = _credentials is not null ? _credentials.Get("ALPACA_SECRET_KEY") : _configuration["ALPACA_SECRET_KEY"]
                        ?? _configuration["APCA_API_SECRET_KEY"]
                        ?? Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")
                        ?? Environment.GetEnvironmentVariable("APCA_API_SECRET_KEY");

        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
        {
            _logger.LogDebug(
                "Alpaca API credentials not configured; corporate action fetch for {Ticker} skipped.", ticker);
            return [];
        }

        using var client = _httpClientFactory.CreateClient("alpaca-corp-actions");
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-KEY-ID", keyId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-SECRET-KEY", secretKey);

        var results = await FetchActionsAsync(client, ticker.Trim(), securityId, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Fetched {Count} corporate action(s) for {Ticker} from Alpaca.",
            results.Count, ticker);

        return results;
    }

    private async Task<IReadOnlyList<CorporateActionCommand>> FetchActionsAsync(
        HttpClient client,
        string ticker,
        Guid securityId,
        CancellationToken ct)
    {
        var through = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var baseUrl = $"/v1/corporate-actions?symbols={Uri.EscapeDataString(ticker)}"
            + $"&types=cash_dividend,forward_split,reverse_split&start={HistoryStart}&end={through}&limit={PageSize}";
        var byGroup = ActionGroups.ToDictionary(
            static group => group.Group, static _ => new List<CorporateActionCommand>(), StringComparer.Ordinal);
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        try
        {
            for (var page = 0; ; page++)
            {
                var url = pageToken is null ? baseUrl : $"{baseUrl}&page_token={Uri.EscapeDataString(pageToken)}";
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // A refused request is not an empty history. It is logged where an operator
                    // will see it, and no partial result is returned as if it were complete.
                    _logger.LogWarning(
                        "Alpaca corporate actions returned {StatusCode} for {Ticker}; no actions were taken from this fetch.",
                        (int)response.StatusCode, ticker);
                    return [];
                }

                await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct)
                    .ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Alpaca corporate-actions response is not an object.");

                if (document.RootElement.TryGetProperty("corporate_actions", out var groups)
                    && groups.ValueKind == JsonValueKind.Object)
                {
                    foreach (var (group, isSplit, description) in ActionGroups)
                    {
                        if (!groups.TryGetProperty(group, out var actions) || actions.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var payload in actions.EnumerateArray())
                        {
                            var action = payload.Deserialize(AlpacaCorporateActionJsonContext.Default.AlpacaAction);
                            if (action is not null && MapToCommand(action, payload, securityId, ticker, isSplit, description) is { } command)
                                byGroup[group].Add(command);
                        }
                    }
                }

                pageToken = document.RootElement.TryGetProperty("next_page_token", out var next)
                    && next.ValueKind == JsonValueKind.String
                        ? next.GetString()
                        : null;
                if (string.IsNullOrEmpty(pageToken))
                    break;
                if (page + 1 >= MaxPages || !seenPageTokens.Add(pageToken))
                    throw new JsonException("Alpaca corporate-actions pagination did not terminate.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Alpaca corporate actions for {Ticker}.", ticker);
            return [];
        }

        return ActionGroups.SelectMany(group => byGroup[group.Group]).ToArray();
    }

    private CorporateActionCommand? MapToCommand(
        AlpacaAction action,
        JsonElement completeProviderPayload,
        Guid securityId,
        string ticker,
        bool isSplit,
        string description)
    {
        // The request names one symbol; a record for any other is not this security's action.
        if (!string.Equals(action.Symbol?.Trim(), ticker, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!DateOnly.TryParseExact(action.ExDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exDate))
            return null;

        var recordDate = ParseDate(action.RecordDate);
        var payableDate = ParseDate(action.PayableDate);
        string actionType;
        if (isSplit)
        {
            // A split without both rates cannot be applied, and the rates decide its direction.
            if (action.OldRate is not > 0m || action.NewRate is not > 0m || action.NewRate == action.OldRate)
                return null;
            actionType = action.NewRate < action.OldRate ? "ReverseStockSplit" : "StockSplit";
        }
        else
        {
            if (action.Rate is not > 0m)
                return null;
            actionType = "Dividend";
            if (action.Special == true)
                description = "Special cash dividend";
        }

        var evidenceHash = ComputePayloadHash(completeProviderPayload);
        var escapedId = string.IsNullOrWhiteSpace(action.Id)
            ? null
            : Uri.EscapeDataString(action.Id.Trim());
        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: actionType,
            ExDate: exDate,
            RecordDate: recordDate,
            PayableDate: payableDate,
            Amount: isSplit ? null : action.Rate,
            // The endpoint reports a per-share rate without a currency.
            Currency: null,
            SplitFromFactor: isSplit ? action.OldRate : null,
            SplitToFactor: isSplit ? action.NewRate : null,
            Description: description,
            SourceProvider: ProviderId,
            SourceEventId: action.Id,
            SourceEventVersion: escapedId is null ? null : $"payload-sha256:{evidenceHash}",
            ObservedAtUtc: DateTimeOffset.UtcNow,
            EvidenceHash: escapedId is null ? null : evidenceHash,
            EvidenceReference: escapedId is null
                ? null
                : $"alpaca://corporate-actions/announcements/{escapedId}/versions/{evidenceHash}",
            ReleaseStatus: ReleaseStatus);
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string ComputePayloadHash(JsonElement payload)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonicalPayload(writer, payload);
        }

        return Sha256Digest.ComputeUtf8(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static void WriteCanonicalPayload(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalPayload(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalPayload(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number when element.TryGetDecimal(out var value):
                writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Unsupported Alpaca corporate-action payload token.");
        }
    }

    // ------------------------------------------------------------------
    // Internal response model (one record of a Market Data corporate-actions group)
    // ------------------------------------------------------------------

    private sealed class AlpacaAction
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("ex_date")]
        public string? ExDate { get; init; }

        [JsonPropertyName("record_date")]
        public string? RecordDate { get; init; }

        [JsonPropertyName("payable_date")]
        public string? PayableDate { get; init; }

        /// <summary>Cash per share, for a cash dividend.</summary>
        [JsonPropertyName("rate")]
        public decimal? Rate { get; init; }

        [JsonPropertyName("special")]
        public bool? Special { get; init; }

        [JsonPropertyName("old_rate")]
        public decimal? OldRate { get; init; }

        [JsonPropertyName("new_rate")]
        public decimal? NewRate { get; init; }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(AlpacaAction))]
    private sealed partial class AlpacaCorporateActionJsonContext : JsonSerializerContext;
}
