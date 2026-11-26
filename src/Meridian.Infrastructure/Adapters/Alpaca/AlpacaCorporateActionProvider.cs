using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Fetches review-only observations for every documented Alpaca REST corporate-action family.
/// Implements <see cref="ICorporateActionProvider"/> following the same attribute-based
/// discovery pattern as <see cref="IHistoricalDataProvider"/> implementations.
/// </summary>
/// <remarks>
/// Uses the Market Data API endpoint
/// <c>GET https://data.alpaca.markets/v1/corporate-actions</c>,
/// which accepts Trading API (paper or live) key pairs and returns each action type in its own
/// group. The retired <c>/v2/corporate_actions/announcements</c> route is not used: it is limited
/// to 90-day windows and returns its amounts as strings. Requests are authenticated via the
/// <c>APCA-API-KEY-ID</c> and <c>APCA-API-SECRET-KEY</c> headers read from the environment /
/// configuration. Coverage is bounded by process date, US region and complete data quality.
/// REST snapshots reveal observed payload revisions, not a complete amendment/deletion stream.
/// </remarks>
[DataSource("alpaca-corp-actions", "Alpaca Corporate Actions", DataSourceType.Reference, DataSourceCategory.Broker,
    Priority = 12,
    EnabledByDefault = false,
    Description = "Review-only observations for all 16 Alpaca Market Data corporate-action families.")]
[ImplementsAdr("ADR-001", "Corporate action data provider following ICorporateActionProvider contract")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory; never instantiates HttpClient directly")]
public sealed partial class AlpacaCorporateActionProvider : ICorporateActionProvider
{
    private const string BaseUrl = "https://data.alpaca.markets";

    // This is a request boundary, not a claim about the provider's available history.
    private const string HistoryStart = "1990-01-01";
    private const string Endpoint = BaseUrl + "/v1/corporate-actions";
    private const int PageSize = 1000;
    private const int MaxPages = 50;

    private sealed record ActionGroup(string Type, string Group);

    private static readonly ActionGroup[] ActionGroups =
    [
        new("cash_dividend", "cash_dividends"),
        new("forward_split", "forward_splits"),
        new("reverse_split", "reverse_splits"),
        new("stock_dividend", "stock_dividends"),
        new("spin_off", "spin_offs"),
        new("cash_merger", "cash_mergers"),
        new("stock_merger", "stock_mergers"),
        new("stock_and_cash_merger", "stock_and_cash_mergers"),
        new("unit_split", "unit_splits"),
        new("redemption", "redemptions"),
        new("name_change", "name_changes"),
        new("worthless_removal", "worthless_removals"),
        new("rights_distribution", "rights_distributions"),
        new("partial_call", "partial_calls"),
        new("reorganization", "reorganizations"),
        new("capital_gains_distribution", "capital_gains_distributions"),
    ];

    public string ProviderId => "alpaca";

    // Fixture corroboration does not grant provider release or accounting acceptance. Expanded
    // endpoint observations remain review-only until the governed release process authorizes
    // them; ingestion retains evidence and never applies these records automatically.
    public CorporateActionProviderReleaseStatusDto ReleaseStatus =>
        CorporateActionProviderReleaseStatusDto.ReviewOnly;

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
    /// <remarks>
    /// Keeps the degrade-to-empty contract of <see cref="ICorporateActionProvider.FetchAsync"/>:
    /// a fetch that failed or was cut short yields no observations. Callers that must tell that
    /// apart from an empty history use <see cref="FetchWithOutcomeAsync"/>.
    /// </remarks>
    public async Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
        => (await FetchWithOutcomeAsync(ticker, securityId, ct).ConfigureAwait(false)).Observations;

    /// <inheritdoc />
    public async Task<CorporateActionFetchResult> FetchWithOutcomeAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ct.ThrowIfCancellationRequested();
        var through = DateOnly.FromDateTime(DateTime.UtcNow);

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
            return CorporateActionFetchResult.Unavailable("Alpaca API credentials are not configured.") with
            {
                Failures = [new("credentials-missing")],
                Coverage = BuildCoverage(through, CorporateActionTypeCoverageStatus.Unavailable),
            };
        }

        using var client = _httpClientFactory.CreateClient("alpaca-corp-actions");
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-KEY-ID", keyId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-SECRET-KEY", secretKey);

        var result = await FetchActionsAsync(client, ticker.Trim(), securityId, through, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Fetched {Count} corporate action(s) for {Ticker} from Alpaca ({FetchStatus}).",
            result.Observations.Count, ticker, result.Status);

        return result;
    }

    private async Task<CorporateActionFetchResult> FetchActionsAsync(
        HttpClient client,
        string ticker,
        Guid securityId,
        DateOnly throughDate,
        CancellationToken ct)
    {
        var through = throughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var baseUrl = $"/v1/corporate-actions?symbols={Uri.EscapeDataString(ticker)}"
            + $"&types={string.Join(',', ActionGroups.Select(static group => group.Type))}"
            + $"&start={HistoryStart}&end={through}&limit={PageSize}&region=us&data_quality=complete&sort=asc";
        var byGroup = ActionGroups.ToDictionary(
            static group => group.Group, static _ => new List<CorporateActionCommand>(), StringComparer.Ordinal);
        var relatedByType = new Dictionary<string, int>(StringComparer.Ordinal);
        var versionsById = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        // Pages read in full so far. A failure before the first one means nothing was read
        // (unavailable); a failure after it means the history was cut short (incomplete).
        var pagesRead = 0;

        try
        {
            while (true)
            {
                var url = pageToken is null ? baseUrl : $"{baseUrl}&page_token={Uri.EscapeDataString(pageToken)}";
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode || CorporateActionFetchResult.IsPartialResponse(response))
                {
                    // A refused request is not an empty history. It is logged where an operator
                    // will see it, and no partial result is returned as if it were complete.
                    // Only the status code is reported; the response body is never read.
                    var statusCode = (int)response.StatusCode;
                    _logger.LogWarning(
                        "Alpaca corporate actions returned {StatusCode} for {Ticker}; no actions were taken from this fetch.",
                        statusCode, ticker);
                    return NotComplete(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Alpaca returned HTTP {statusCode} for page {pagesRead + 1}."),
                        CorporateActionFetchResult.FromHttpFailure(response).Failures.Single());
                }

                await using var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct)
                    .ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Alpaca corporate-actions response is not an object.");
                ValidateUniqueProperties(document.RootElement);

                if (!document.RootElement.TryGetProperty("corporate_actions", out var groups)
                    || groups.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Missing corporate-action groups.");
                {
                    foreach (var property in groups.EnumerateObject())
                    {
                        if (!byGroup.ContainsKey(property.Name))
                            return NotComplete("Alpaca returned an unrequested corporate-action family.",
                                new("unexpected-event-type"));
                    }

                    foreach (var group in ActionGroups)
                    {
                        if (!groups.TryGetProperty(group.Group, out var actions))
                            continue;
                        if (actions.ValueKind != JsonValueKind.Array)
                            throw new JsonException("Invalid corporate-action group.");

                        foreach (var payload in actions.EnumerateArray())
                        {
                            var action = payload.Deserialize(AlpacaCorporateActionJsonContext.Default.AlpacaAction);
                            if (action is null)
                                throw new JsonException("Invalid corporate-action record.");
                            if (string.IsNullOrWhiteSpace(action.Id)
                                || !TryDate(action.ProcessDate, out var processDate)
                                || processDate < new DateOnly(1990, 1, 1) || processDate > throughDate)
                                return NotComplete("Alpaca returned missing identity or an invalid process date for the requested window.",
                                    new("invalid-record", Scope: group.Type));
                            var evidenceHash = ComputePayloadHash(payload);
                            var version = SourceVersion(evidenceHash, group.Type);
                            var nativeId = action.Id.Trim();
                            if (versionsById.TryGetValue(nativeId, out var previousVersion) && previousVersion != version)
                                return NotComplete("Alpaca returned conflicting revisions of one event within a paged snapshot.",
                                    new("conflicting-event-revisions", Scope: group.Type));
                            versionsById[nativeId] = version;
                            if (IsRelatedSymbolOnly(action, ticker, group.Type)
                                || IsAdditionalRelatedSymbolOnly(payload, ticker, group.Type))
                            {
                                relatedByType[group.Type] = relatedByType.GetValueOrDefault(group.Type) + 1;
                                continue;
                            }
                            if (group.Type == "cash_dividend"
                                && !string.IsNullOrWhiteSpace(action.SubType)
                                && action.SubType != "return_of_capital")
                                return NotComplete("Alpaca returned an unmapped cash-dividend subtype.",
                                    new("unsupported-event-subtype", Scope: group.Type));

                            if (MapToCommand(action, payload, securityId, ticker, group.Type, evidenceHash) is not { } command)
                                return NotComplete("Alpaca returned an invalid corporate-action record.",
                                    new("invalid-record", Scope: group.Type));
                            byGroup[group.Group].Add(command);
                        }
                    }
                }

                if (!document.RootElement.TryGetProperty("next_page_token", out var token)
                    || token.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new JsonException("Invalid corporate-action pagination token.");
                pagesRead++;
                pageToken = document.RootElement.TryGetProperty("next_page_token", out var next)
                    && next.ValueKind == JsonValueKind.String
                        ? next.GetString()
                        : null;
                if (string.IsNullOrEmpty(pageToken))
                    break;

                // A token seen before would request the same page again, and a history longer
                // than the page limit is not followed further; either way the fetch stops here
                // without having reached the end of the provider's history.
                if (!seenPageTokens.Add(pageToken))
                {
                    _logger.LogWarning(
                        "Alpaca corporate actions repeated a page token for {Ticker} after {PagesRead} page(s); no actions were taken from this fetch.",
                        ticker, pagesRead);
                    return NotComplete("Alpaca repeated a next_page_token; pagination did not terminate.");
                }

                if (pagesRead >= MaxPages)
                {
                    _logger.LogWarning(
                        "Alpaca corporate actions exceeded {MaxPages} pages for {Ticker}; no actions were taken from this fetch.",
                        MaxPages, ticker);
                    return NotComplete(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Alpaca pagination exceeded the {MaxPages}-page limit."));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Failed to fetch Alpaca corporate actions for {Ticker}.", ticker);

            // The exception type is reported, not its message, so nothing the provider sent is echoed.
            return NotComplete(string.Create(
                CultureInfo.InvariantCulture,
                $"Alpaca request for page {pagesRead + 1} failed ({ex.GetType().Name})."),
                CorporateActionFetchResult.FromException(ex).Failures.Single());
        }

        return CorporateActionFetchResult.Complete(
            ActionGroups.SelectMany(group => byGroup[group.Group])
                .DistinctBy(static action => (action.SourceEventId, action.SourceEventVersion)).ToArray()) with
        {
            Coverage = BuildCoverage(throughDate, CorporateActionTypeCoverageStatus.Complete, byGroup, relatedByType),
        };

        CorporateActionFetchResult NotComplete(string reason, CorporateActionFetchFailure? failure = null)
        {
            var count = byGroup.Values.Sum(static commands => commands.Count);
            var incomplete = pagesRead > 0 || count > 0;
            var result = incomplete
                ? CorporateActionFetchResult.Incomplete(reason, count)
                : CorporateActionFetchResult.Unavailable(reason);
            return result with
            {
                Failures = failure is null ? result.Failures : [failure],
                Coverage = BuildCoverage(throughDate, incomplete
                    ? CorporateActionTypeCoverageStatus.Incomplete : CorporateActionTypeCoverageStatus.Unavailable,
                    byGroup, relatedByType),
            };
        }
    }

    private static CorporateActionFetchCoverage BuildCoverage(
        DateOnly through,
        CorporateActionTypeCoverageStatus status,
        IReadOnlyDictionary<string, List<CorporateActionCommand>>? actions = null,
        IReadOnlyDictionary<string, int>? related = null)
        => new(
            Endpoint, "process_date", DateOnly.ParseExact(HistoryStart, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            through, "us", "complete",
            ActionGroups.Select(group => new CorporateActionTypeCoverage(
                    group.Type, status,
                    actions?.GetValueOrDefault(group.Group)?.DistinctBy(static action =>
                        (action.SourceEventId, action.SourceEventVersion)).Count() ?? 0,
                    related?.GetValueOrDefault(group.Type) ?? 0,
                    related?.GetValueOrDefault(group.Type) > 0
                        ? "Recipient-only observations are not actions on the queried source security." : null))
                .ToArray());
}
