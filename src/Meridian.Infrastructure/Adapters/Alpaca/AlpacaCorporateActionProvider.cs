using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Fetches corporate action history (dividends and stock splits) from the Alpaca REST API.
/// Implements <see cref="ICorporateActionProvider"/> following the same attribute-based
/// discovery pattern as <see cref="IHistoricalDataProvider"/> implementations.
/// </summary>
/// <remarks>
/// Endpoints used:
/// <list type="bullet">
///   <item><c>GET /v2/corporate-actions/announcements?ca_types=dividend&symbol={ticker}</c></item>
///   <item><c>GET /v2/corporate-actions/announcements?ca_types=split&symbol={ticker}</c></item>
/// </list>
/// The Alpaca announcements endpoint requires a Broker API key pair or a standard
/// Alpaca data subscription.  Requests are authenticated via the <c>APCA-API-KEY-ID</c>
/// and <c>APCA-API-SECRET-KEY</c> headers read from the environment / configuration.
/// </remarks>
[DataSource("alpaca-corp-actions", "Alpaca Corporate Actions")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
public sealed class AlpacaCorporateActionProvider : ICorporateActionProvider
{
    private const string BaseUrl = "https://api.alpaca.markets";

    public string ProviderId => "alpaca";

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

        var results = new List<CorporateActionCommand>();

        // Fetch dividends and splits in parallel.
        var dividendTask = FetchAnnouncementsAsync(client, ticker, securityId, "dividend", ct);
        var splitTask = FetchAnnouncementsAsync(client, ticker, securityId, "split", ct);

        await Task.WhenAll(dividendTask, splitTask).ConfigureAwait(false);

        results.AddRange(dividendTask.Result);
        results.AddRange(splitTask.Result);

        _logger.LogDebug(
            "Fetched {Count} corporate action(s) for {Ticker} from Alpaca.",
            results.Count, ticker);

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

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var announcements = JsonSerializer.Deserialize<List<AlpacaAnnouncement>>(json, _jsonOptions)
                                ?? [];

            return announcements
                .Select(a => MapToCommand(a, securityId))
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

    private CorporateActionCommand? MapToCommand(AlpacaAnnouncement announcement, Guid securityId)
    {
        if (!DateOnly.TryParse(announcement.ExDate, out var exDate))
            return null;

        DateOnly.TryParse(announcement.RecordDate, out var recordDate);
        DateOnly.TryParse(announcement.PayableDate, out var payableDate);

        var actionType = announcement.CaType?.ToUpperInvariant() switch
        {
            "DIVIDEND" => "Dividend",
            "SPLIT" => "Split",
            "MERGER" => "Merger",
            "SPINOFF" => "SpinOff",
            _ => announcement.CaType ?? "Unknown",
        };

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
            SourceProvider: ProviderId);
    }

    // ------------------------------------------------------------------
    // Internal response model (Alpaca announcements endpoint)
    // ------------------------------------------------------------------

    private sealed class AlpacaAnnouncement
    {
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

    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}
