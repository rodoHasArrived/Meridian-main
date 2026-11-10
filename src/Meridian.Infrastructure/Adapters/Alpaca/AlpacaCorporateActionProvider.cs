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
/// <para>
/// Endpoint used, on the Trading API host of the credentials' trading environment:
/// <c>GET /v2/corporate_actions/announcements?ca_types=dividend,split&amp;symbol={ticker}&amp;date_type=ex_date&amp;since={date}&amp;until={date}</c>.
/// </para>
/// <para>
/// The endpoint requires both <c>since</c> and <c>until</c> and refuses a span longer than 90
/// days, so history is read as consecutive ex-date windows from <see cref="ForwardDays"/> ahead
/// of today back through <see cref="DefaultLookbackDays"/>. Requests are paced because the
/// Trading API request budget is shared with order flow on the same key. The result is
/// all-or-nothing: if any window fails, nothing is returned, so a partial history is never
/// presented as the history.
/// </para>
/// <para>
/// Amounts and rates arrive as JSON strings and the security is named by
/// <c>target_symbol</c> / <c>initiating_symbol</c>; an announcement that names neither as the
/// requested ticker is not bound to the security. Requests are authenticated via the
/// <c>APCA-API-KEY-ID</c> and <c>APCA-API-SECRET-KEY</c> headers.
/// </para>
/// </remarks>
[DataSource("alpaca-corp-actions", "Alpaca Corporate Actions", DataSourceType.Reference, DataSourceCategory.Broker,
    Priority = 12,
    EnabledByDefault = false,
    Description = "Dividend and split announcements from the Alpaca corporate-actions endpoints.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
public sealed partial class AlpacaCorporateActionProvider : ICorporateActionProvider
{
    /// <summary>Longest <c>since</c>–<c>until</c> span the announcements endpoint accepts.</summary>
    internal const int MaxWindowDays = 90;

    /// <summary>
    /// How far back ex-dates are read by default: about two years, sized so the lookback plus
    /// <see cref="ForwardDays"/> is exactly nine full windows.
    /// </summary>
    internal const int DefaultLookbackDays = 728;

    /// <summary>How far ahead already-announced ex-dates are read.</summary>
    internal const int ForwardDays = 90;

    public string ProviderId => "alpaca";

    public CorporateActionProviderReleaseStatusDto ReleaseStatus =>
        CorporateActionProviderReleaseStatusDto.AcceptanceEligible;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ICredentialContext? _credentials;
    private readonly ILogger<AlpacaCorporateActionProvider> _logger;
    private readonly bool? _useSandbox;
    private readonly TimeProvider _timeProvider;
    private readonly AlpacaReferenceRequestPacing _pacing;
    private readonly int _lookbackDays;

    /// <param name="httpClientFactory">Factory for the <c>alpaca-corp-actions</c> client.</param>
    /// <param name="configuration">Configuration consulted for credentials when no context is supplied.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="credentials">Selected credential record, which also names its trading environment.</param>
    /// <param name="pacing">Request pacing; the process-wide <see cref="AlpacaReferenceRequestPacing.Shared"/> when omitted.</param>
    public AlpacaCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AlpacaCorporateActionProvider> logger,
        ICredentialContext? credentials = null,
        AlpacaReferenceRequestPacing? pacing = null)
        : this(httpClientFactory, configuration, logger, credentials, pacing,
            useSandbox: null, timeProvider: null, lookbackDays: DefaultLookbackDays)
    {
    }

    /// <summary>
    /// Test seam: pins the trading environment, clock and lookback so a fixture run does not
    /// read process environment or the wall clock.
    /// </summary>
    internal AlpacaCorporateActionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AlpacaCorporateActionProvider> logger,
        ICredentialContext? credentials,
        AlpacaReferenceRequestPacing? pacing,
        bool? useSandbox,
        TimeProvider? timeProvider,
        int lookbackDays)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _credentials = credentials;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentOutOfRangeException.ThrowIfNegative(lookbackDays);
        _useSandbox = useSandbox;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pacing = pacing ?? AlpacaReferenceRequestPacing.Shared;
        _lookbackDays = lookbackDays;
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
        client.BaseAddress = new Uri(AlpacaTradingApi.ResolveBaseUrl(ResolveUseSandbox()));
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-KEY-ID", keyId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-SECRET-KEY", secretKey);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var commandsByEvent = new Dictionary<string, CorporateActionCommand>(StringComparer.Ordinal);
        foreach (var (since, until) in BuildExDateWindows(today.AddDays(-_lookbackDays), today.AddDays(ForwardDays)))
        {
            var window = await FetchWindowAsync(client, ticker, securityId, since, until, ct).ConfigureAwait(false);
            if (window is null)
            {
                // A history with a hole in it is indistinguishable from a complete one downstream.
                _logger.LogWarning(
                    "Alpaca corporate action history for {Ticker} is incomplete; returning no actions rather than a partial history.",
                    ticker);
                return [];
            }

            foreach (var command in window)
            {
                // Windows do not overlap, but the announcement id still decides identity.
                var eventKey = command.SourceEventId
                    ?? FormattableString.Invariant($"{command.ActionType}|{command.ExDate:O}|{command.Amount}|{command.SplitFromFactor}|{command.SplitToFactor}");
                commandsByEvent.TryAdd(eventKey, command);
            }
        }

        var results = commandsByEvent.Values
            .OrderBy(static command => command.ExDate)
            .ThenBy(static command => command.ActionType, StringComparer.Ordinal)
            .ThenBy(static command => command.SourceEventId, StringComparer.Ordinal)
            .ToArray();

        _logger.LogDebug(
            "Fetched {Count} corporate action(s) for {Ticker} from Alpaca.",
            results.Length, ticker);

        return results;
    }

    /// <summary>
    /// The selected credential record names its own trading environment; without one the
    /// canonical <c>ALPACA_TRADING_ENVIRONMENT</c> contract decides, as for the brokerage gateway.
    /// </summary>
    private bool? ResolveUseSandbox() => _useSandbox
        ?? (_credentials?.CredentialEnvironment is { } selectedEnvironment
            ? !string.Equals(
                Meridian.Core.Config.AlpacaCredentialEnvironment.NormalizeTradingEnvironment(selectedEnvironment),
                Meridian.Core.Config.AlpacaCredentialEnvironment.LiveEnvironment,
                StringComparison.OrdinalIgnoreCase)
            : null);

    /// <summary>
    /// Splits an inclusive ex-date range into consecutive, non-overlapping windows no longer
    /// than the endpoint accepts, newest first.
    /// </summary>
    internal static IReadOnlyList<(DateOnly Since, DateOnly Until)> BuildExDateWindows(DateOnly earliest, DateOnly latest)
    {
        var windows = new List<(DateOnly Since, DateOnly Until)>();
        var until = latest;
        while (until >= earliest)
        {
            var since = until.AddDays(-MaxWindowDays);
            if (since < earliest)
                since = earliest;
            windows.Add((since, until));
            until = since.AddDays(-1);
        }

        return windows;
    }

    /// <summary>
    /// Reads one ex-date window. Returns <see langword="null"/> when the window could not be
    /// read, which is different from a window that holds no announcements.
    /// </summary>
    private async Task<IReadOnlyList<CorporateActionCommand>?> FetchWindowAsync(
        HttpClient client,
        string ticker,
        Guid securityId,
        DateOnly since,
        DateOnly until,
        CancellationToken ct)
    {
        var url = "/v2/corporate_actions/announcements?ca_types=dividend,split" +
                  $"&symbol={Uri.EscapeDataString(ticker)}" +
                  "&date_type=ex_date" +
                  $"&since={since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                  $"&until={until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        try
        {
            await _pacing.WaitAsync(ct).ConfigureAwait(false);
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Alpaca corporate actions returned {StatusCode} for {Ticker} ex-dates {Since} to {Until}.",
                    (int)response.StatusCode, ticker, since, until);
                return null;
            }

            await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Alpaca corporate actions returned a non-array payload for {Ticker} ex-dates {Since} to {Until}.",
                    ticker, since, until);
                return null;
            }

            return document.RootElement.EnumerateArray()
                .Select(payload => new
                {
                    Announcement = payload.Deserialize(
                        AlpacaCorporateActionJsonContext.Default.AlpacaAnnouncement),
                    Payload = payload,
                })
                .Where(item => item.Announcement is not null && NamesTicker(item.Announcement, ticker))
                .Select(item => MapToCommand(item.Announcement!, item.Payload, securityId))
                .Where(cmd => cmd is not null)
                .Select(cmd => cmd!)
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to fetch Alpaca corporate actions for {Ticker} ex-dates {Since} to {Until}.",
                ticker, since, until);
            return null;
        }
    }

    private static bool NamesTicker(AlpacaAnnouncement announcement, string ticker) =>
        string.Equals(announcement.TargetSymbol?.Trim(), ticker.Trim(), StringComparison.OrdinalIgnoreCase)
        || string.Equals(announcement.InitiatingSymbol?.Trim(), ticker.Trim(), StringComparison.OrdinalIgnoreCase);

    private CorporateActionCommand? MapToCommand(
        AlpacaAnnouncement announcement,
        JsonElement completeProviderPayload,
        Guid securityId)
    {
        if (!TryParseAnnouncementDate(announcement.ExDate, out var exDate))
            return null;

        TryParseAnnouncementDate(announcement.RecordDate, out var recordDate);
        TryParseAnnouncementDate(announcement.PayableDate, out var payableDate);

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

    private static bool TryParseAnnouncementDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

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

        /// <summary>The security the action applies to; the announcement has no plain symbol field.</summary>
        [JsonPropertyName("target_symbol")]
        public string? TargetSymbol { get; init; }

        [JsonPropertyName("initiating_symbol")]
        public string? InitiatingSymbol { get; init; }

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

    // cash, old_rate and new_rate arrive as JSON strings ("0.53"); read both encodings.
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    [JsonSerializable(typeof(AlpacaAnnouncement))]
    private sealed partial class AlpacaCorporateActionJsonContext : JsonSerializerContext;
}
