using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Config;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Plaid;
using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk.AccountingSystem;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IProviderCredentialVerifier
{
    string ProviderId { get; }

    Task<ProviderCredentialVerificationResultDto> VerifyAsync(
        ProviderCredentialReadResult credentials,
        CancellationToken ct);
}

public sealed class AlpacaCredentialVerifier : IProviderCredentialVerifier
{
    private const string AlpacaPaperTradingApiEndpoint = "https://paper-api.alpaca.markets/v2";
    private const string AlpacaLiveTradingApiEndpoint = "https://api.alpaca.markets/v2";

    private readonly IProviderCredentialStore _credentialStore;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<AlpacaCredentialVerifier> _logger;

    public AlpacaCredentialVerifier(
        IProviderCredentialStore credentialStore,
        ILogger<AlpacaCredentialVerifier> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
    }

    public string ProviderId => "alpaca";

    public async Task<ProviderCredentialVerificationResultDto> VerifyAsync(
        ProviderCredentialReadResult credentials,
        CancellationToken ct)
    {
        var keyId = credentials.Get("KeyId");
        var secretKey = credentials.Get("SecretKey");
        var environment = AlpacaCredentialEnvironment.NormalizeTradingEnvironment(credentials.Environment);
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
        {
            return new ProviderCredentialVerificationResultDto(
                ProviderId,
                Success: false,
                ProviderVerificationStateDto.NotVerified,
                ProviderContinuityHealthDto.Blocked,
                LastVerifiedAt: null,
                LastError: "Alpaca key id and secret key are required.",
                ExternalAccountId: null,
                Warnings: ["Add Alpaca paper API keys before verification."]);
        }

        try
        {
            using var client = _httpClientFactory?.CreateClient(nameof(ProviderConnectionLifecycleService)) ?? new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{AlpacaTradingApiEndpoint(environment)}/account");
            request.Headers.TryAddWithoutValidation("APCA-API-KEY-ID", keyId);
            request.Headers.TryAddWithoutValidation("APCA-API-SECRET-KEY", secretKey);

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"status {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})");
            }

            var account = await response.Content.ReadFromJsonAsync<AlpacaAccountVerificationResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            var accountId = FirstNonBlank(account?.AccountNumber, account?.Id, "unknown");
            var verifiedAt = DateTimeOffset.UtcNow;
            await RecordAsync(true, null, accountId, verifiedAt, ct).ConfigureAwait(false);

            return new ProviderCredentialVerificationResultDto(
                ProviderId,
                Success: true,
                ProviderVerificationStateDto.Verified,
                ProviderContinuityHealthDto.Healthy,
                LastVerifiedAt: verifiedAt,
                LastError: null,
                ExternalAccountId: accountId,
                Warnings: BuildAlpacaWarnings(environment));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = $"Alpaca /v2/account verification failed: {Redact(ex.Message, keyId, secretKey)}";
            _logger.LogWarning("Provider connection center Alpaca verification failed for {Environment}: {Error}", environment, message);
            var verifiedAt = DateTimeOffset.UtcNow;
            await RecordAsync(false, message, null, verifiedAt, ct).ConfigureAwait(false);

            return new ProviderCredentialVerificationResultDto(
                ProviderId,
                Success: false,
                ProviderVerificationStateDto.Failed,
                ProviderContinuityHealthDto.Blocked,
                LastVerifiedAt: verifiedAt,
                LastError: message,
                ExternalAccountId: credentials.ExternalAccountId,
                Warnings: ["Alpaca account verification failed; downstream brokerage sync remains blocked."]);
        }
    }


    private static string Redact(string message, params string?[] secrets)
    {
        var redacted = message;
        foreach (var secret in secrets.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            redacted = redacted.Replace(secret!, "[redacted]", StringComparison.Ordinal);
        }

        return redacted;
    }

    private Task RecordAsync(bool success, string? error, string? externalAccountId, DateTimeOffset verifiedAt, CancellationToken ct)
        => _credentialStore.RecordVerificationAsync(
            new ProviderCredentialVerificationUpdate(
                ProviderId,
                success,
                ErrorMessage: error,
                ExternalAccountId: externalAccountId,
                VerifiedAt: verifiedAt,
                Actor: "browser-workstation"),
            ct);

    private static string AlpacaTradingApiEndpoint(string environment)
        => environment.Equals(AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
            ? AlpacaLiveTradingApiEndpoint
            : AlpacaPaperTradingApiEndpoint;

    private static IReadOnlyList<string> BuildAlpacaWarnings(string environment)
        => environment.Equals(AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
            ? ["Live Alpaca endpoint verified. Paper remains the default and live actions remain gated by execution controls."]
            : [];

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record AlpacaAccountVerificationResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("account_number")] string? AccountNumber);
}

public sealed class QuickBooksCredentialVerifier : IProviderCredentialVerifier
{
    private readonly IEnumerable<IAccountingSystemProvider> _accountingSystemProviders;

    public QuickBooksCredentialVerifier(IEnumerable<IAccountingSystemProvider> accountingSystemProviders)
    {
        _accountingSystemProviders = accountingSystemProviders ?? throw new ArgumentNullException(nameof(accountingSystemProviders));
    }

    public string ProviderId => "quickbooks";

    public async Task<ProviderCredentialVerificationResultDto> VerifyAsync(ProviderCredentialReadResult credentials, CancellationToken ct)
    {
        var verifier = _accountingSystemProviders
            .FirstOrDefault(provider => string.Equals(provider.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
            as IAccountingSystemConnectionVerifier;
        if (verifier is null)
        {
            return ProviderCredentialVerifierResults.Unavailable(ProviderId);
        }

        var result = await verifier.VerifyConnectionAsync(ct).ConfigureAwait(false);
        return new ProviderCredentialVerificationResultDto(
            ProviderId,
            result.Success,
            result.Success ? ProviderVerificationStateDto.Verified : ProviderVerificationStateDto.Failed,
            result.Success ? ProviderContinuityHealthDto.Healthy : ProviderContinuityHealthDto.Blocked,
            result.VerifiedAtUtc,
            result.LastError,
            result.ExternalCompanyId,
            result.Warnings);
    }
}

public sealed class PlaidCredentialVerifier : IProviderCredentialVerifier
{
    private readonly IPlaidClient? _client;

    public PlaidCredentialVerifier(IPlaidClient? client = null)
    {
        _client = client;
    }

    public string ProviderId => "plaid";

    public async Task<ProviderCredentialVerificationResultDto> VerifyAsync(ProviderCredentialReadResult credentials, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var clientId = credentials.Get("ClientId");
        var secret = credentials.Get("Secret");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            return new ProviderCredentialVerificationResultDto(
                ProviderId,
                false,
                ProviderVerificationStateDto.NotVerified,
                ProviderContinuityHealthDto.Blocked,
                null,
                "Plaid client id and secret are required.",
                null,
                ["Add Plaid client credentials before verification."]);
        }

        if (_client is null)
        {
            return ProviderCredentialVerifierResults.Unavailable(ProviderId);
        }

        try
        {
            var environment = ParsePlaidEnvironment(credentials.Environment);
            var result = await _client.SearchInstitutionsAsync(
                new PlaidClientCredentials(clientId, secret, environment),
                "Chase",
                [PlaidProductDto.Transactions],
                ["US"],
                ct).ConfigureAwait(false);
            var verifiedAt = DateTimeOffset.UtcNow;
            return new ProviderCredentialVerificationResultDto(
                ProviderId,
                true,
                ProviderVerificationStateDto.Verified,
                ProviderContinuityHealthDto.Healthy,
                verifiedAt,
                null,
                result.RequestId,
                ["Plaid institution search succeeded; stored external id is the safe Plaid request id, not a secret."]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProviderCredentialVerificationResultDto(
                ProviderId,
                false,
                ProviderVerificationStateDto.Failed,
                ProviderContinuityHealthDto.Blocked,
                DateTimeOffset.UtcNow,
                $"Plaid credential verification failed: {Redact(ex.Message, clientId, secret)}",
                null,
                ["Plaid remains unavailable until client credentials and environment are repaired."]);
        }
    }


    private static string Redact(string message, params string?[] secrets)
    {
        var redacted = message;
        foreach (var secret in secrets.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            redacted = redacted.Replace(secret!, "[redacted]", StringComparison.Ordinal);
        }

        return redacted;
    }

    private static PlaidEnvironmentDto ParsePlaidEnvironment(string? environment)
        => Enum.TryParse<PlaidEnvironmentDto>(environment, ignoreCase: true, out var parsed)
            ? parsed
            : PlaidEnvironmentDto.Sandbox;
}

public static class ProviderCredentialVerifierResults
{
    public static ProviderCredentialVerificationResultDto Unavailable(string providerId)
        => new(
            ProviderCredentialCatalog.NormalizeProviderId(providerId),
            Success: false,
            ProviderVerificationStateDto.NotVerified,
            ProviderContinuityHealthDto.Warning,
            LastVerifiedAt: null,
            LastError: "Verification unavailable for this provider.",
            ExternalAccountId: null,
            Warnings: ["Verification unavailable: Meridian has credentials for this provider, but no safe provider-specific verifier is registered yet."]);
}
