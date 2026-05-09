using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Manages personal Alpaca Trading API-key connectivity for paper-first brokerage sync.
/// </summary>
public sealed class AlpacaBrokerageConnectionService
{
    private const string ProviderId = "alpaca";
    private const string ConnectedAtEnv = "ALPACA_BROKERAGE_CONNECTED_AT";
    private const string VerifiedAtEnv = "ALPACA_BROKERAGE_VERIFIED_AT";
    private const string ExternalAccountIdEnv = "ALPACA_BROKERAGE_ACCOUNT_ID";
    private const string LastErrorEnv = "ALPACA_BROKERAGE_LAST_ERROR";
    private const string PaperBaseUrl = "https://paper-api.alpaca.markets";
    private const string LiveBaseUrl = "https://api.alpaca.markets";

    private static readonly IReadOnlyList<string> Scopes =
    [
        "trading:account",
        "brokerage-sync:read"
    ];

    private readonly ILogger<AlpacaBrokerageConnectionService> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    public AlpacaBrokerageConnectionService(
        ILogger<AlpacaBrokerageConnectionService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
    }

    public Task<BrokerageConnectionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(BuildStatus(lastError: ReadCredential(LastErrorEnv)));
    }

    public async Task<BrokerageConnectionStatusDto> ConnectAsync(
        AlpacaBrokerageConnectionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var keyId = request.KeyId?.Trim();
        var secretKey = request.SecretKey?.Trim();
        var environment = AlpacaCredentialEnvironment.NormalizeTradingEnvironment(request.Environment);

        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
        {
            return BuildStatus(
                lastError: "Alpaca API key id and secret key are required.",
                environmentOverride: environment);
        }

        SetCredential(AlpacaCredentialEnvironment.KeyIdName, keyId);
        SetCredential(AlpacaCredentialEnvironment.SecretKeyName, secretKey);
        SetCredential(AlpacaCredentialEnvironment.TradingEnvironmentName, environment);
        ClearCredential(ExternalAccountIdEnv);
        ClearCredential(VerifiedAtEnv);

        try
        {
            var account = await VerifyAccountAsync(keyId, secretKey, environment, ct).ConfigureAwait(false);
            var verifiedAt = DateTimeOffset.UtcNow;
            SetCredential(ConnectedAtEnv, verifiedAt.ToString("O"));
            SetCredential(VerifiedAtEnv, verifiedAt.ToString("O"));
            SetCredential(ExternalAccountIdEnv, account.AccountId);
            ClearCredential(LastErrorEnv);

            return BuildStatus(lastError: null, environmentOverride: environment);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = $"Alpaca /v2/account verification failed: {ex.Message}";
            _logger.LogWarning(ex, "Alpaca API-key verification failed for {Environment} environment", environment);
            SetCredential(LastErrorEnv, message);
            return BuildStatus(lastError: message, environmentOverride: environment);
        }
    }

    public Task<BrokerageConnectionStatusDto> RevokeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ClearCredential(AlpacaCredentialEnvironment.KeyIdName);
        ClearCredential(AlpacaCredentialEnvironment.SecretKeyName);
        ClearCredential(AlpacaCredentialEnvironment.TradingEnvironmentName);
        ClearCredential(ConnectedAtEnv);
        ClearCredential(VerifiedAtEnv);
        ClearCredential(ExternalAccountIdEnv);
        ClearCredential(LastErrorEnv);

        foreach (var alias in AlpacaCredentialEnvironment.KeyIdAliases)
        {
            ClearCredential(alias);
        }

        foreach (var alias in AlpacaCredentialEnvironment.SecretKeyAliases)
        {
            ClearCredential(alias);
        }

        return Task.FromResult(BuildStatus(lastError: null));
    }

    private async Task<AlpacaAccountVerification> VerifyAccountAsync(
        string keyId,
        string secretKey,
        string environment,
        CancellationToken ct)
    {
        using var client = _httpClientFactory?.CreateClient(nameof(AlpacaBrokerageConnectionService)) ?? new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(environment)}/v2/account");
        request.Headers.TryAddWithoutValidation("APCA-API-KEY-ID", keyId);
        request.Headers.TryAddWithoutValidation("APCA-API-SECRET-KEY", secretKey);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"status {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})");
        }

        var account = await response.Content.ReadFromJsonAsync<AlpacaAccountVerificationResponse>(cancellationToken: ct)
            .ConfigureAwait(false);
        var accountId = FirstNonBlank(account?.AccountNumber, account?.Id, "unknown")!;
        return new AlpacaAccountVerification(accountId);
    }

    private BrokerageConnectionStatusDto BuildStatus(
        string? lastError,
        string? environmentOverride = null)
    {
        var credentials = AlpacaCredentialEnvironment.Resolve();
        var environment = AlpacaCredentialEnvironment.NormalizeTradingEnvironment(environmentOverride ?? credentials.Environment);
        var connectedAt = ParseDate(ConnectedAtEnv);
        var verifiedAt = ParseDate(VerifiedAtEnv);
        var externalAccountId = ReadCredential(ExternalAccountIdEnv);
        var warnings = new List<string>();
        var state = BrokerageConnectionStateDto.NotConfigured;

        if (!credentials.HasCredentials)
        {
            warnings.Add("Enter Alpaca paper Trading API keys to verify /v2/account before account discovery or sync.");
        }
        else if (!string.IsNullOrWhiteSpace(lastError))
        {
            state = BrokerageConnectionStateDto.Degraded;
            warnings.Add(lastError);
        }
        else if (verifiedAt.HasValue)
        {
            state = BrokerageConnectionStateDto.Connected;
        }
        else
        {
            state = BrokerageConnectionStateDto.Disconnected;
            warnings.Add("Alpaca Trading API keys are present but /v2/account has not been verified in this workstation session.");
        }

        if (string.Equals(environment, AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Live Alpaca endpoint is selected. Paper remains the default and order placement is still governed by execution controls.");
        }

        return new BrokerageConnectionStatusDto(
            ProviderId: ProviderId,
            DisplayName: string.Equals(environment, AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
                ? "Alpaca live"
                : "Alpaca paper",
            State: state,
            IsConfigured: credentials.HasCredentials,
            IsConnected: state == BrokerageConnectionStateDto.Connected,
            AuthorizationUrl: null,
            ConnectedAt: connectedAt,
            ExpiresAt: null,
            LastError: lastError,
            Warnings: warnings,
            Scopes: Scopes,
            Environment: environment,
            ExternalAccountId: string.IsNullOrWhiteSpace(externalAccountId) ? null : externalAccountId,
            VerifiedAt: verifiedAt,
            MaskedKeyId: AlpacaCredentialEnvironment.MaskKeyId(credentials.KeyId));
    }

    private static string BaseUrl(string environment)
        => string.Equals(environment, AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : PaperBaseUrl;

    private static DateTimeOffset? ParseDate(string name)
        => DateTimeOffset.TryParse(ReadCredential(name), out var parsed) ? parsed : null;

    private static string? ReadCredential(string name)
        => AlpacaCredentialEnvironment.ReadEnvironmentValue(name);

    private static void SetCredential(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        TrySetUserEnvironment(name, value);
    }

    private static void ClearCredential(string name)
    {
        Environment.SetEnvironmentVariable(name, null);
        TrySetUserEnvironment(name, null);
    }

    private static void TrySetUserEnvironment(string name, string? value)
    {
        try
        {
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        }
        catch (Exception ex) when (
            ex is PlatformNotSupportedException
            || ex is System.Security.SecurityException
            || ex is UnauthorizedAccessException
            || ex is System.IO.IOException)
        {
            // Process-level storage is sufficient when durable user-profile storage is unavailable.
        }
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record AlpacaAccountVerification(string AccountId);

    private sealed record AlpacaAccountVerificationResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("account_number")] string? AccountNumber);
}
