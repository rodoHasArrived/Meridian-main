using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Application.Config.Credentials;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Manages personal Alpaca Trading API-key connectivity for paper-first brokerage sync.
/// </summary>
public sealed class AlpacaBrokerageConnectionService
{
    private const string ProviderId = "alpaca";
    private const string PaperBaseUrl = "https://paper-api.alpaca.markets";
    private const string LiveBaseUrl = "https://api.alpaca.markets";

    private static readonly IReadOnlyList<string> Scopes =
    [
        "trading:account",
        "brokerage-sync:read"
    ];

    private readonly ILogger<AlpacaBrokerageConnectionService> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IProviderCredentialStore _credentialStore;

    public AlpacaBrokerageConnectionService(
        ILogger<AlpacaBrokerageConnectionService> logger,
        IHttpClientFactory? httpClientFactory = null,
        IProviderCredentialStore? credentialStore = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
        _credentialStore = credentialStore ?? new FileProviderCredentialStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "data"));
    }

    public async Task<BrokerageConnectionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var status = await _credentialStore.GetStatusAsync(ProviderId, ct).ConfigureAwait(false);
        return BuildStatus(status);
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
            var status = await _credentialStore.GetStatusAsync(ProviderId, ct).ConfigureAwait(false);
            return BuildStatus(
                status,
                lastErrorOverride: "Alpaca API key id and secret key are required.",
                environmentOverride: environment);
        }

        await _credentialStore.SaveAsync(
            new ProviderCredentialSaveRequest(
                ProviderId,
                new Dictionary<string, string?>
                {
                    ["KeyId"] = keyId,
                    ["SecretKey"] = secretKey
                },
                Environment: environment,
                Actor: "browser-workstation"),
            ct).ConfigureAwait(false);

        try
        {
            var account = await VerifyAccountAsync(keyId, secretKey, environment, ct).ConfigureAwait(false);
            var verifiedAt = DateTimeOffset.UtcNow;
            await _credentialStore.RecordVerificationAsync(
                new ProviderCredentialVerificationUpdate(
                    ProviderId,
                    Success: true,
                    ExternalAccountId: account.AccountId,
                    VerifiedAt: verifiedAt,
                    Actor: "browser-workstation"),
                ct).ConfigureAwait(false);

            var status = await _credentialStore.GetStatusAsync(ProviderId, ct).ConfigureAwait(false);
            return BuildStatus(status, environmentOverride: environment);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = $"Alpaca /v2/account verification failed: {ex.Message}";
            _logger.LogWarning(ex, "Alpaca API-key verification failed for {Environment} environment", environment);
            await _credentialStore.RecordVerificationAsync(
                new ProviderCredentialVerificationUpdate(
                    ProviderId,
                    Success: false,
                    ErrorMessage: message,
                    VerifiedAt: DateTimeOffset.UtcNow,
                    Actor: "browser-workstation"),
                ct).ConfigureAwait(false);

            var status = await _credentialStore.GetStatusAsync(ProviderId, ct).ConfigureAwait(false);
            return BuildStatus(status, environmentOverride: environment);
        }
    }

    public async Task<BrokerageConnectionStatusDto> RevokeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _credentialStore.DeleteAsync(ProviderId, actor: "browser-workstation", ct).ConfigureAwait(false);
        var status = await _credentialStore.GetStatusAsync(ProviderId, ct).ConfigureAwait(false);
        return BuildStatus(status);
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
        ProviderCredentialStoreStatus credentialStatus,
        string? lastErrorOverride = null,
        string? environmentOverride = null)
    {
        var lastError = lastErrorOverride ?? credentialStatus.LastError;
        var environment = AlpacaCredentialEnvironment.NormalizeTradingEnvironment(
            environmentOverride ?? credentialStatus.Environment);
        var warnings = new List<string>();
        var state = BrokerageConnectionStateDto.NotConfigured;
        var isConfigured = credentialStatus.CredentialState is
            ProviderCredentialStateDto.Configured
            or ProviderCredentialStateDto.Verified
            or ProviderCredentialStateDto.Invalid;

        if (!isConfigured)
        {
            warnings.Add("Enter Alpaca paper Trading API keys to verify /v2/account before account discovery or sync.");
        }
        else if (!string.IsNullOrWhiteSpace(lastError))
        {
            state = BrokerageConnectionStateDto.Degraded;
            warnings.Add(lastError);
        }
        else if (credentialStatus.VerificationState == ProviderVerificationStateDto.Verified)
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
            IsConfigured: isConfigured,
            IsConnected: state == BrokerageConnectionStateDto.Connected,
            AuthorizationUrl: null,
            ConnectedAt: credentialStatus.LastSuccessfulAt ?? credentialStatus.SavedAt,
            ExpiresAt: null,
            LastError: lastError,
            Warnings: warnings,
            Scopes: Scopes,
            Environment: environment,
            ExternalAccountId: string.IsNullOrWhiteSpace(credentialStatus.ExternalAccountId) ? null : credentialStatus.ExternalAccountId,
            VerifiedAt: credentialStatus.LastSuccessfulAt,
            MaskedKeyId: credentialStatus.MaskedKeyPreview);
    }

    private static string BaseUrl(string environment)
        => string.Equals(environment, AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : PaperBaseUrl;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record AlpacaAccountVerification(string AccountId);

    private sealed record AlpacaAccountVerificationResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("account_number")] string? AccountNumber);
}
