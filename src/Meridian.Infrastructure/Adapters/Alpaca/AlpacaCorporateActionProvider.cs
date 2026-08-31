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
/// Endpoints used:
/// <list type="bullet">
///   <item><c>GET /v2/corporate-actions/announcements?ca_types=dividend&amp;symbol={ticker}</c></item>
///   <item><c>GET /v2/corporate-actions/announcements?ca_types=split&amp;symbol={ticker}</c></item>
/// </list>
/// The Alpaca announcements endpoint requires a Broker API key pair or a standard
/// Alpaca data subscription.  Requests are authenticated via the <c>APCA-API-KEY-ID</c>
/// and <c>APCA-API-SECRET-KEY</c> headers read from the environment / configuration.
/// </remarks>
[DataSource("alpaca-corp-actions", "Alpaca Corporate Actions", DataSourceType.Reference, DataSourceCategory.Broker,
    Priority = 12,
    EnabledByDefault = false,
    Description = "Dividend and split announcements from the Alpaca corporate-actions endpoints.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
public sealed partial class AlpacaCorporateActionProvider : ICorporateActionProvider
{
    private const string BaseUrl = "https://api.alpaca.markets";

    public string ProviderId => "alpaca";

    public CorporateActionProviderReleaseStatusDto ReleaseStatus =>
        CorporateActionProviderReleaseStatusDto.AcceptanceEligible;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlpacaCorporateActionProvider> _logger;

    public AlpacaCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AlpacaCorporateActionProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        var keyId = _configuration["ALPACA_KEY_ID"]
                    ?? _configuration["APCA_API_KEY_ID"]
                    ?? Environment.GetEnvironmentVariable("ALPACA_KEY_ID")
                    ?? Environment.GetEnvironmentVariable("APCA_API_KEY_ID");

        var secretKey = _configuration["ALPACA_SECRET_KEY"]
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

        // Fetch dividends and splits in parallel.
        var results = (await Task.WhenAll(
                FetchAnnouncementsAsync(client, ticker, securityId, "dividend", ct),
                FetchAnnouncementsAsync(client, ticker, securityId, "split", ct))
            .ConfigureAwait(false))
            .SelectMany(static batch => batch)
            .ToArray();

        _logger.LogDebug(
            "Fetched {Count} corporate action(s) for {Ticker} from Alpaca.",
            results.Length, ticker);

        return results;
    }

    private async Task<IReadOnlyList<CorporateActionCommand>> FetchAnnouncementsAsync(
        HttpClient client,
        string ticker,
        Guid securityId,
        string caType,
        CancellationToken ct)
    {
        // The Alpaca corporate actions API uses ca_types (plural) but accepts a single value.
        var url = $"/v2/corporate-actions/announcements?ca_types={caType}&symbol={Uri.EscapeDataString(ticker)}";

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Alpaca corporate actions returned {StatusCode} for {Ticker}/{CaType}; skipping.",
                    (int)response.StatusCode, ticker, caType);
                return [];
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Select(payload => new
                {
                    Announcement = payload.Deserialize(
                        AlpacaCorporateActionJsonContext.Default.AlpacaAnnouncement),
                    Payload = payload,
                })
                .Where(item => item.Announcement is not null)
                .Select(item => MapToCommand(item.Announcement!, item.Payload, securityId))
                .Where(cmd => cmd is not null)
                .Select(cmd => cmd!)
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to fetch Alpaca {CaType} corporate actions for {Ticker}.", caType, ticker);
            return [];
        }
    }

    private CorporateActionCommand? MapToCommand(
        AlpacaAnnouncement announcement,
        JsonElement completeProviderPayload,
        Guid securityId)
    {
        if (!DateOnly.TryParse(announcement.ExDate, out var exDate))
            return null;

        DateOnly.TryParse(announcement.RecordDate, out var recordDate);
        DateOnly.TryParse(announcement.PayableDate, out var payableDate);

        var actionType = announcement.CaType?.ToUpperInvariant() switch
        {
            "DIVIDEND" => "Dividend",
            "SPLIT" => announcement.OldRate is > 0m &&
                announcement.NewRate is > 0m &&
                announcement.NewRate.Value < announcement.OldRate.Value
                    ? "ReverseStockSplit"
                    : "StockSplit",
            "MERGER" => "Merger",
            "SPINOFF" => "SpinOff",
            _ => announcement.CaType ?? "Unknown",
        };

        var evidenceHash = ComputePayloadHash(completeProviderPayload);
        var escapedId = string.IsNullOrWhiteSpace(announcement.Id)
            ? null
            : Uri.EscapeDataString(announcement.Id.Trim());
        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: actionType,
            ExDate: exDate,
            RecordDate: recordDate == default ? null : recordDate,
            PayableDate: payableDate == default ? null : payableDate,
            Amount: announcement.Cash.HasValue && announcement.Cash.Value != 0 ? announcement.Cash : null,
            Currency: announcement.Currency,
            SplitFromFactor: announcement.OldRate,
            SplitToFactor: announcement.NewRate,
            Description: announcement.CaSubType ?? actionType,
            SourceProvider: ProviderId,
            SourceEventId: announcement.Id,
            SourceEventVersion: escapedId is null ? null : $"payload-sha256:{evidenceHash}",
            ObservedAtUtc: DateTimeOffset.UtcNow,
            EvidenceHash: escapedId is null ? null : evidenceHash,
            EvidenceReference: escapedId is null
                ? null
                : $"alpaca://corporate-actions/announcements/{escapedId}/versions/{evidenceHash}",
            ReleaseStatus: ReleaseStatus);
    }

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
    // Internal response model (Alpaca announcements endpoint)
    // ------------------------------------------------------------------

    private sealed class AlpacaAnnouncement
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("ca_type")]
        public string? CaType { get; init; }

        [JsonPropertyName("ca_sub_type")]
        public string? CaSubType { get; init; }

        [JsonPropertyName("symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("ex_date")]
        public string? ExDate { get; init; }

        [JsonPropertyName("record_date")]
        public string? RecordDate { get; init; }

        [JsonPropertyName("payable_date")]
        public string? PayableDate { get; init; }

        [JsonPropertyName("cash")]
        public decimal? Cash { get; init; }

        [JsonPropertyName("currency")]
        public string? Currency { get; init; }

        [JsonPropertyName("old_rate")]
        public decimal? OldRate { get; init; }

        [JsonPropertyName("new_rate")]
        public decimal? NewRate { get; init; }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(AlpacaAnnouncement))]
    private sealed partial class AlpacaCorporateActionJsonContext : JsonSerializerContext;
}
