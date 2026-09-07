using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.DataIntegration.Monitoring;
using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk.AccountingSystem;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed class ProviderConnectionLifecycleService
{
    private const string AlpacaPaperTradingApiEndpoint = "https://paper-api.alpaca.markets/v2";
    private const string AlpacaLiveTradingApiEndpoint = "https://api.alpaca.markets/v2";

    private readonly IProviderCredentialStore _credentialStore;
    private readonly ConfigStore _configStore;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IReadOnlyList<IAccountingSystemProvider> _accountingSystemProviders;
    private readonly IProviderSetupRegistry _setupRegistry;
    private readonly ILogger<ProviderConnectionLifecycleService> _logger;
    private readonly ProviderCredentialScope? _ownershipScope;

    public ProviderConnectionLifecycleService(
        IProviderCredentialStore credentialStore,
        ConfigStore configStore,
        ILogger<ProviderConnectionLifecycleService> logger,
        IHttpClientFactory? httpClientFactory = null,
        IEnumerable<IAccountingSystemProvider>? accountingSystemProviders = null,
        IProviderSetupRegistry? setupRegistry = null,
        ProviderCredentialScope? ownershipScope = null)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        _ownershipScope = ownershipScope;
        _credentialStore = ownershipScope is null ? credentialStore : new ScopedCredentialStore(
            credentialStore as IScopedProviderCredentialStore ?? throw new InvalidOperationException("Credential vault does not support scoped ownership."), ownershipScope);
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
        _accountingSystemProviders = accountingSystemProviders?.ToArray() ?? [];
        _setupRegistry = setupRegistry ?? new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create());
    }

    /// <summary>Binds the lifecycle to retained connection ownership after the HTTP boundary resolves its tenant.</summary>
    public ProviderConnectionLifecycleService ForConnection(string connectionId, string tenantId, string providerId)
    {
        var descriptor = RequireDescriptor(providerId);
        var connection = RequireOwnedConnection(connectionId, tenantId);
        if (!string.Equals(connection.ProviderFamilyId, descriptor.ProviderId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Credential connection ownership could not be established.");
        return BindConnection(connection);
    }

    private ProviderConnectionLifecycleService BindConnection(ProviderConnectionConfig connection)
    {
        var scope = new ProviderCredentialScope(connection.TenantId!, connection.ConnectionId, connection.ExternalAccountId!, connection.CredentialEnvironment!);
        return new ProviderConnectionLifecycleService(_credentialStore, _configStore, _logger, _httpClientFactory,
            _accountingSystemProviders, _setupRegistry, scope);
    }

    /// <summary>Reads one authorized connection without borrowing provider-wide health or credentials.</summary>
    public async Task<ProviderConnectionRowDto> GetConnectionStatusForTenantAsync(string connectionId, string tenantId, CancellationToken ct = default)
    {
        var connection = RequireOwnedConnection(connectionId, tenantId);
        var descriptor = RequireDescriptor(connection.ProviderFamilyId);
        var selected = BindConnection(connection);
        var status = await selected._credentialStore.GetStatusAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
        return selected.BuildRow(descriptor, status, null) with
        {
            DisplayName = connection.DisplayName,
            ExternalAccountId = connection.ExternalAccountId,
            Environment = connection.CredentialEnvironment
        };
    }

    private ProviderConnectionConfig RequireOwnedConnection(string connectionId, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var matches = (ConfigStore.LoadConfig(_configStore.ConfigPath).ProviderConnections?.Connections ?? [])
            .Where(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var connection = matches.Length == 1 ? matches[0] : null;
        if (connection is null || !string.Equals(connection.TenantId, tenantId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(connection.ExternalAccountId) || string.IsNullOrWhiteSpace(connection.CredentialEnvironment))
            throw new UnauthorizedAccessException("Credential connection ownership could not be established.");
        return connection;
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
        var credentials = NormalizeCredentialFields(descriptor, request.Credentials ?? new Dictionary<string, string?>());

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
        CancellationToken ct = default,
        string? actor = null)
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
            return await VerifyAlpacaAsync(read, ct, actor).ConfigureAwait(false);
        }

        if (_ownershipScope is not null)
        {
            // A provider-wide verifier cannot prove which connection its credentials belong to.
            return new ProviderCredentialVerificationResultDto(descriptor.ProviderId, false,
                ProviderVerificationStateDto.NotVerified, ProviderContinuityHealthDto.Blocked, null,
                "Connection-scoped live verification is not available for this provider.", null,
                ["Use a verifier bound to this retained connection before relying on these credentials."]);
        }

        var accountingVerification = _accountingSystemProviders
            .FirstOrDefault(provider => string.Equals(provider.ProviderId, descriptor.ProviderId, StringComparison.OrdinalIgnoreCase))
            as IAccountingSystemConnectionVerifier;
        if (accountingVerification is not null)
        {
            var result = await accountingVerification.VerifyConnectionAsync(ct).ConfigureAwait(false);
            await _credentialStore.RecordVerificationAsync(new ProviderCredentialVerificationUpdate(
                descriptor.ProviderId, result.Success, result.LastError, result.ExternalCompanyId,
                result.VerifiedAtUtc, actor ?? "provider-connection-lifecycle"), ct).ConfigureAwait(false);
            return new ProviderCredentialVerificationResultDto(
                descriptor.ProviderId,
                result.Success,
                result.Success ? ProviderVerificationStateDto.Verified : ProviderVerificationStateDto.Failed,
                result.Success ? ProviderContinuityHealthDto.Healthy : ProviderContinuityHealthDto.Blocked,
                result.VerifiedAtUtc,
                result.LastError,
                result.ExternalCompanyId,
                result.Warnings);
        }

        var verifiedAt = DateTimeOffset.UtcNow;
        await _credentialStore.RecordVerificationAsync(
            new ProviderCredentialVerificationUpdate(
                descriptor.ProviderId,
                Success: true,
                VerifiedAt: verifiedAt,
                Actor: actor ?? "provider-connection-lifecycle"),
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
        CancellationToken ct,
        string? actor)
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{AlpacaTradingApiEndpoint(environment)}/account");
            request.Headers.TryAddWithoutValidation("APCA-API-KEY-ID", keyId);
            request.Headers.TryAddWithoutValidation("APCA-API-SECRET-KEY", secretKey);

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Provider verification returned an unsuccessful status.", null, response.StatusCode);
            }

            var account = await response.Content.ReadFromJsonAsync<AlpacaAccountVerificationResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            var accountId = FirstNonBlank(account?.AccountNumber, account?.Id);
            if (string.IsNullOrWhiteSpace(accountId))
                throw new InvalidOperationException("Provider account identity is missing.");
            var verifiedAt = DateTimeOffset.UtcNow;
            await _credentialStore.RecordVerificationAsync(
                new ProviderCredentialVerificationUpdate(
                    "alpaca",
                    Success: true,
                    ExternalAccountId: accountId,
                    VerifiedAt: verifiedAt,
                    Actor: actor ?? "provider-connection-lifecycle"),
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
            const string message = "Alpaca account verification failed.";
            // Provider reason phrases, JSON paths and transport exceptions can echo credentials.
            _logger.LogWarning("Alpaca account verification failed for {Environment} ({FailureType})",
                environment, ex.GetType().Name);
            var verifiedAt = DateTimeOffset.UtcNow;
            await _credentialStore.RecordVerificationAsync(
                new ProviderCredentialVerificationUpdate(
                    "alpaca",
                    Success: false,
                    ErrorMessage: message,
                    VerifiedAt: verifiedAt,
                    Actor: actor ?? "provider-connection-lifecycle"),
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

    private static IReadOnlyDictionary<string, string?> NormalizeCredentialFields(
        ProviderCredentialCatalogEntry descriptor,
        IReadOnlyDictionary<string, string?> credentials)
    {
        var allowedFields = descriptor.RequiredFields.ToDictionary(
            static field => field.Name,
            static field => field.Name,
            StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var unknownFields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in credentials)
        {
            var trimmedKey = key?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedKey))
            {
                continue;
            }

            if (!allowedFields.TryGetValue(trimmedKey, out var canonicalName))
            {
                unknownFields.Add(trimmedKey);
                continue;
            }

            normalized[canonicalName] = value;
        }

        if (unknownFields.Count > 0)
        {
            throw new ProviderCredentialValidationException(descriptor.ProviderId, unknownFields.ToArray());
        }

        return normalized;
    }

    private ProviderConnectionRowDto BuildRow(
        ProviderCredentialCatalogEntry descriptor,
        ProviderCredentialStoreStatus status,
        ProviderMetrics? metrics)
    {
        var health = ResolveHealth(status, metrics);
        var lastSuccessfulAt = status.LastSuccessfulAt ?? (metrics?.IsConnected == true ? metrics.Timestamp : null);
        var lastFailureAt = status.LastFailureAt ?? (metrics is { IsConnected: false, ConnectionFailures: > 0 } ? metrics.Timestamp : null);

        var setupDescriptor = ResolveSetupDescriptor(descriptor);

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
            ActionHref: descriptor.ResolvedActionHref,
            CredentialFields: setupDescriptor.AcceptedCredentialFields,
            EnvironmentOptions: setupDescriptor.EnvironmentOptions);
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
            "Credentials were saved to the encrypted local Meridian store; user environment variables were not changed.",
            "Rotation metadata was recorded; verify the provider before routing dependent workflows."
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

    private ProviderSetupDescriptor ResolveSetupDescriptor(ProviderCredentialCatalogEntry descriptor)
        => _setupRegistry.Find(descriptor.ProviderId)?.Descriptor
           ?? new GenericReadOnlyDataProviderSetupHandler(descriptor.ProviderId).Descriptor;

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

    private sealed class ScopedCredentialStore(IScopedProviderCredentialStore store, ProviderCredentialScope scope) : IProviderCredentialStore
    {
        public string VaultPath => store.VaultPath;
        public Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
            => store.GetScopedStatusAsync(providerId, scope, ct);
        public Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default)
            => store.ReadScopedAsync(providerId, scope, ct);
        public Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
            => store.SaveScopedAsync(request, scope, ct);
        public Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
            => store.DeleteScopedAsync(providerId, scope, actor, ct);
        public Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
            => store.RecordScopedVerificationAsync(update, scope, ct);
    }
}
