using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Robinhood;

public sealed record RobinhoodReadOnlyBrokerageOptions(
    string AccessToken,
    string AccountsEndpoint,
    string PortfolioEndpointTemplate,
    string ActivityEndpointTemplate,
    string DisplayName = "Robinhood read-only")
{
    public static RobinhoodReadOnlyBrokerageOptions FromEnvironment() => new(
        AccessToken: Environment.GetEnvironmentVariable("ROBINHOOD_BROKERAGE_ACCESS_TOKEN") ?? string.Empty,
        AccountsEndpoint: Environment.GetEnvironmentVariable("ROBINHOOD_BROKERAGE_ACCOUNTS_ENDPOINT") ?? string.Empty,
        PortfolioEndpointTemplate: Environment.GetEnvironmentVariable("ROBINHOOD_BROKERAGE_PORTFOLIO_ENDPOINT_TEMPLATE") ?? string.Empty,
        ActivityEndpointTemplate: Environment.GetEnvironmentVariable("ROBINHOOD_BROKERAGE_ACTIVITY_ENDPOINT_TEMPLATE") ?? string.Empty,
        DisplayName: Environment.GetEnvironmentVariable("ROBINHOOD_BROKERAGE_DISPLAY_NAME") ?? "Robinhood read-only");
}

/// <summary>
/// Read-only Robinhood portfolio sync adapter for an authorized brokerage aggregation bridge.
/// This adapter consumes normalized Meridian brokerage DTO payloads from configured endpoints;
/// it intentionally does not call Robinhood's unofficial trading endpoints or store passwords.
/// </summary>
public sealed class RobinhoodReadOnlyBrokerageSyncAdapter :
    IBrokerageAccountCatalog,
    IBrokeragePortfolioSync,
    IBrokerageActivitySync
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RobinhoodReadOnlyBrokerageOptions _options;
    private readonly ILogger<RobinhoodReadOnlyBrokerageSyncAdapter> _logger;

    public RobinhoodReadOnlyBrokerageSyncAdapter(
        IHttpClientFactory httpClientFactory,
        RobinhoodReadOnlyBrokerageOptions options,
        ILogger<RobinhoodReadOnlyBrokerageSyncAdapter> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => "robinhood";

    public string ProviderDisplayName => _options.DisplayName;

    public async Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var endpoint = RequireEndpoint(_options.AccountsEndpoint, "accounts");
        var accounts = await GetJsonAsync<IReadOnlyList<BrokerageExternalAccountDto>>(endpoint, ct).ConfigureAwait(false);
        return accounts
            .Where(static account => string.Equals(account.ProviderId, "robinhood", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
        string externalAccountId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);
        var endpoint = BuildAccountEndpoint(
            RequireEndpoint(_options.PortfolioEndpointTemplate, "portfolio"),
            externalAccountId,
            since: null);
        return GetJsonAsync<BrokeragePortfolioSnapshotDto>(endpoint, ct);
    }

    public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
        string externalAccountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);
        var endpoint = BuildAccountEndpoint(
            RequireEndpoint(_options.ActivityEndpointTemplate, "activity"),
            externalAccountId,
            since);
        return GetJsonAsync<BrokerageActivitySnapshotDto>(endpoint, ct);
    }

    private async Task<T> GetJsonAsync<T>(string endpoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            throw new InvalidOperationException("ROBINHOOD_BROKERAGE_ACCESS_TOKEN is required for read-only Robinhood brokerage sync.");
        }

        using var client = _httpClientFactory.CreateClient(nameof(RobinhoodReadOnlyBrokerageSyncAdapter));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Robinhood read-only brokerage sync failed for {Endpoint} with status {StatusCode}: {Body}",
                endpoint,
                response.StatusCode,
                body);
            throw new InvalidOperationException($"Robinhood read-only sync endpoint failed with status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException("Robinhood read-only sync endpoint returned an empty payload.");
    }

    private static string RequireEndpoint(string endpoint, string capability)
        => string.IsNullOrWhiteSpace(endpoint)
            ? throw new InvalidOperationException($"ROBINHOOD_BROKERAGE_{capability.ToUpperInvariant()} endpoint is not configured.")
            : endpoint;

    private static string BuildAccountEndpoint(string template, string externalAccountId, DateTimeOffset? since)
    {
        var endpoint = template
            .Replace("{accountId}", Uri.EscapeDataString(externalAccountId), StringComparison.OrdinalIgnoreCase)
            .Replace("{externalAccountId}", Uri.EscapeDataString(externalAccountId), StringComparison.OrdinalIgnoreCase);
        return since.HasValue
            ? endpoint.Replace("{since}", Uri.EscapeDataString(since.Value.ToString("O")), StringComparison.OrdinalIgnoreCase)
            : endpoint.Replace("{since}", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
