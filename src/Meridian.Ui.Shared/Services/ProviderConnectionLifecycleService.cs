using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Application.Config.Credentials;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Configuration;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed class ProviderConnectionLifecycleService
{
    private const string AlpacaPaperBaseUrl = "https://paper-api.alpaca.markets";
    private const string AlpacaLiveBaseUrl = "https://api.alpaca.markets";

    private readonly IProviderCredentialStore _credentialStore;
    private readonly ConfigStore _configStore;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<ProviderConnectionLifecycleService> _logger;

    public ProviderConnectionLifecycleService(
        IProviderCredentialStore credentialStore,
        ConfigStore configStore,
        ILogger<ProviderConnectionLifecycleService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<ProviderConnectionRowDto>> GetConnectionsAsync(CancellationToken ct = default)
    {
        var metrics = _configStore.TryLoadProviderMetrics();
        var rows = new List<ProviderConnectionRowDto>();

        foreach (var descriptor in ProviderCredentialCatalog.All)
        {
            var status = await _credentialStore.GetStatusAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
            var providerMetrics = FindMetrics(metrics, descriptor.ProviderId);
            rows.Add(BuildRow(descriptor, status, providerMetrics));
        }

        return rows;
    }

    public async Task<ProviderCredentialMutationResultDto> SaveCredentialsAsync(
        string providerId,
        ProviderCredentialUpsertRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var descriptor = RequireDescriptor(providerId);
        var credentials = request.Credentials ?? new Dictionary<string, string?>();

        await _credentialStore.SaveAsync(
            new ProviderCredentialSaveRequest(
                descriptor.ProviderId,
                credentials,
                request.Environment,
                request.RequestedBy ?? "browser-workstation"),
            ct).ConfigureAwait(false);

        var status = await _credentialStore.GetStatusAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
        return BuildMutationResult(status, BuildSaveWarnings(status));
    }

    public async Task<ProviderCredentialVerificationResultDto> VerifyAsync(
        string providerId,
        CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);
        var read = await _credentialStore.ReadForProviderAsync(descriptor.ProviderId, ct).ConfigureAwait(false);

        if (!descriptor.RequiresCredentials)
        {
            return new ProviderCredentialVerificationResultDto(
                descriptor.ProviderId,
                Success: true,
                ProviderVerificationStateDto.NotRequired,
                ProviderContinuityHealthDto.Healthy,
                LastVerifiedAt: null,
                LastError: null,
                ExternalAccountId: null,
                Warnings: ["No credential verification is required for this provider."]);
        }

        var status = await _credentialStore.GetStatusAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
        if (status.CredentialState is ProviderCredentialStateDto.Missing or ProviderCredentialStateDto.Partial || read is null)
        {
            return new ProviderCredentialVerificationResultDto(
                descriptor.ProviderId,
                Success: false,
                ProviderVerificationStateDto.NotVerified,
                ProviderContinuityHealthDto.Blocked,
                LastVerifiedAt: null,
                LastError: "Provider credentials are missing or incomplete.",
                ExternalAccountId: null,
                Warnings: ["Add the required credential fields before verification."]);
        }

        if (descriptor.ProviderId.Equals("alpaca", StringComparison.OrdinalIgnoreCase))
        {
            return await VerifyAlpacaAsync(read, ct).ConfigureAwait(false);
        }

        var verifiedAt = DateTimeOffset.UtcNow;
        await _credentialStore.RecordVerificationAsync(
            new ProviderCredentialVerificationUpdate(
                descriptor.ProviderId,
                Success: true,
                VerifiedAt: verifiedAt,
                Actor: "browser-workstation"),
            ct).ConfigureAwait(false);

        return new ProviderCredentialVerificationResultDto(
            descriptor.ProviderId,
            Success: true,
            ProviderVerificationStateDto.Verified,
            ProviderContinuityHealthDto.Healthy,
            LastVerifiedAt: verifiedAt,
            LastError: null,
            ExternalAccountId: null,
            Warnings: ["Credential presence was verified locally; provider-specific live connectivity checks can be added behind this shared route."]);
    }

    public async Task<ProviderCredentialMutationResultDto> DeleteCredentialsAsync(
        string providerId,
        string? actor,
        CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);
        await _credentialStore.DeleteAsync(descriptor.ProviderId, actor ?? "browser-workstation", ct).ConfigureAwait(false);
        var status = await _credentialStore.GetStatusAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
        return BuildMutationResult(status, BuildDeleteWarnings(status));
    }

    private async Task<ProviderCredentialVerificationResultDto> VerifyAlpacaAsync(
        ProviderCredentialReadResult read,
        CancellationToken ct)
    {
        var keyId = read.Get("KeyId");
        var secretKey = read.Get("SecretKey");
        var environment = AlpacaCredentialEnvironment.NormalizeTradingEnvironment(read.Environment);
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
        {
            return new ProviderCredentialVerificationResultDto(
                "alpaca",
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{AlpacaBaseUrl(environment)}/v2/account");
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
            await _credentialStore.RecordVerificationAsync(
                new ProviderCredentialVerificationUpdate(
                    "alpaca",
                    Success: true,
                    ExternalAccountId: accountId,
                    VerifiedAt: verifiedAt,
                    Actor: "browser-workstation"),
                ct).ConfigureAwait(false);

            return new ProviderCredentialVerificationResultDto(
                "alpaca",
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
            var message = $"Alpaca /v2/account verification failed: {ex.Message}";
            _logger.LogWarning(ex, "Provider connection center Alpaca verification failed for {Environment}", environment);
            var verifiedAt = DateTimeOffset.UtcNow;
            await _credentialStore.RecordVerificationAsync(
                new ProviderCredentialVerificationUpdate(
                    "alpaca",
                    Success: false,
                    ErrorMessage: message,
                    VerifiedAt: verifiedAt,
                    Actor: "browser-workstation"),
                ct).ConfigureAwait(false);

            return new ProviderCredentialVerificationResultDto(
                "alpaca",
                Success: false,
                ProviderVerificationStateDto.Failed,
                ProviderContinuityHealthDto.Blocked,
                LastVerifiedAt: verifiedAt,
                LastError: message,
                ExternalAccountId: read.ExternalAccountId,
                Warnings: ["Alpaca account verification failed; downstream brokerage sync remains blocked."]);
        }
    }

    private static ProviderConnectionRowDto BuildRow(
        ProviderCredentialCatalogEntry descriptor,
        ProviderCredentialStoreStatus status,
        ProviderMetrics? metrics)
    {
        var health = ResolveHealth(status, metrics);
        var lastSuccessfulAt = status.LastSuccessfulAt ?? (metrics?.IsConnected == true ? metrics.Timestamp : null);
        var lastFailureAt = status.LastFailureAt ?? (metrics is { IsConnected: false, ConnectionFailures: > 0 } ? metrics.Timestamp : null);

        return new ProviderConnectionRowDto(
            ProviderId: descriptor.ProviderId,
            DisplayName: descriptor.DisplayName,
            Capability: descriptor.Capability,
            CredentialState: status.CredentialState,
            CredentialSource: status.CredentialSource,
            VerificationState: status.VerificationState,
            Health: health,
            FallbackActive: metrics is { IsConnected: false, ConnectionFailures: > 0 },
            LastVerifiedAt: status.LastVerifiedAt,
            LastSuccessfulAt: lastSuccessfulAt,
            LastFailureAt: lastFailureAt,
            LastError: status.LastError,
            MaskedKeyPreview: status.MaskedKeyPreview,
            Environment: status.Environment,
            ExternalAccountId: status.ExternalAccountId,
            AffectedWorkflows: descriptor.AffectedWorkflows ?? [],
            RecommendedAction: ResolveRecommendedAction(descriptor, status, health),
            ActionHref: descriptor.ResolvedActionHref);
    }

    private static ProviderContinuityHealthDto ResolveHealth(
        ProviderCredentialStoreStatus status,
        ProviderMetrics? metrics)
    {
        if (status.CredentialState is ProviderCredentialStateDto.Partial or ProviderCredentialStateDto.Invalid)
        {
            return ProviderContinuityHealthDto.Blocked;
        }

        if (status.CredentialState == ProviderCredentialStateDto.Missing)
        {
            return ProviderContinuityHealthDto.Warning;
        }

        if (metrics is { IsConnected: false, ConnectionFailures: > 0 })
        {
            return ProviderContinuityHealthDto.Degraded;
        }

        if (metrics is { IsConnected: true })
        {
            return ProviderContinuityHealthDto.Healthy;
        }

        return status.VerificationState switch
        {
            ProviderVerificationStateDto.Verified or ProviderVerificationStateDto.NotRequired => ProviderContinuityHealthDto.Healthy,
            ProviderVerificationStateDto.Failed => ProviderContinuityHealthDto.Blocked,
            _ => ProviderContinuityHealthDto.Warning
        };
    }

    private static string ResolveRecommendedAction(
        ProviderCredentialCatalogEntry descriptor,
        ProviderCredentialStoreStatus status,
        ProviderContinuityHealthDto health)
    {
        if (status.CredentialSource == ProviderCredentialSourceDto.Environment)
        {
            return "Migrate this legacy environment credential into the encrypted Meridian store.";
        }

        if (status.CredentialState is ProviderCredentialStateDto.Missing or ProviderCredentialStateDto.Partial)
        {
            return descriptor.RecommendedActionWhenMissing;
        }

        if (status.CredentialState == ProviderCredentialStateDto.Invalid)
        {
            return "Re-enter credentials and verify the provider before routing dependent workflows.";
        }

        if (health is ProviderContinuityHealthDto.Degraded or ProviderContinuityHealthDto.Blocked)
        {
            return "Verify credentials and review provider health before accepting downstream readiness.";
        }

        return "No credential repair action required.";
    }

    private static ProviderCredentialMutationResultDto BuildMutationResult(
        ProviderCredentialStoreStatus status,
        IReadOnlyList<string> warnings)
        => new(
            status.ProviderId,
            status.CredentialState,
            status.CredentialSource,
            status.VerificationState,
            ResolveHealth(status, metrics: null),
            status.MaskedKeyPreview,
            status.Environment,
            warnings);

    private static IReadOnlyList<string> BuildSaveWarnings(ProviderCredentialStoreStatus status)
    {
        var warnings = new List<string>
        {
            "Credentials were saved to the encrypted local Meridian store; user environment variables were not changed."
        };
        if (status.Environment?.Equals(AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase) == true)
        {
            warnings.Add("Live endpoint selected. Paper remains the default for new Alpaca credential setup.");
        }

        if (status.CredentialState is ProviderCredentialStateDto.Missing or ProviderCredentialStateDto.Partial)
        {
            warnings.Add("Credential setup is incomplete.");
        }

        return warnings;
    }

    private static IReadOnlyList<string> BuildDeleteWarnings(ProviderCredentialStoreStatus status)
        => status.CredentialSource == ProviderCredentialSourceDto.Environment
            ? ["Local credentials were deleted, but legacy environment variables are still visible as read-only fallback."]
            : ["Local provider credentials were deleted from the encrypted Meridian store."];

    private static ProviderMetrics? FindMetrics(ProviderMetricsStatus? metrics, string providerId)
        => metrics?.Providers.FirstOrDefault(provider =>
            provider.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase) ||
            provider.ProviderType.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    private static ProviderCredentialCatalogEntry RequireDescriptor(string providerId)
        => ProviderCredentialCatalog.Find(providerId)
           ?? throw new ArgumentException($"Provider '{providerId}' is not in the provider credential catalog.", nameof(providerId));

    private static string AlpacaBaseUrl(string environment)
        => environment.Equals(AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
            ? AlpacaLiveBaseUrl
            : AlpacaPaperBaseUrl;

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
