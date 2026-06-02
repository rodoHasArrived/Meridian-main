using Meridian.Application.Config;
using Meridian.Application.Config.Credentials;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Plaid;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

public sealed class ProviderReadinessService
{
    private const double DegradedScoreThreshold = 0.70;
    private readonly IServiceProvider _services;
    private readonly ConfigStore _configStore;

    public ProviderReadinessService(IServiceProvider services, ConfigStore configStore)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    }

    public async Task<ProviderReadinessSummaryDto> GetReadinessAsync(CancellationToken ct = default)
    {
        var asOf = DateTimeOffset.UtcNow;
        var connections = await GetConnectionRowsAsync(ct).ConfigureAwait(false);
        var plaidItems = await GetPlaidItemsAsync(ct).ConfigureAwait(false);
        var plaidAccounts = await GetPlaidAccountsAsync(ct).ConfigureAwait(false);
        var cfg = _configStore.Load();
        var metrics = _configStore.TryLoadProviderMetrics();
        var configuredProviders = cfg.DataSources?.Sources ?? [];
        var scorer = _services.GetService<ProviderDegradationScorer>();

        var rows = new List<ProviderReadinessRowDto>();
        var providerIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ProviderCredentialCatalog.All)
        {
            providerIds.Add(entry.ProviderId);
        }

        foreach (var source in configuredProviders)
        {
            providerIds.Add(ProviderCredentialCatalog.NormalizeProviderId(source.Id));
            providerIds.Add(ProviderCredentialCatalog.NormalizeProviderId(source.Provider.ToString()));
        }

        foreach (var connection in connections)
        {
            providerIds.Add(ProviderCredentialCatalog.NormalizeProviderId(connection.ProviderId));
        }

        foreach (var providerId in providerIds)
        {
            var descriptor = ProviderCredentialCatalog.Find(providerId);
            var connection = FindConnection(connections, providerId);
            var source = FindSource(configuredProviders, providerId);
            var providerMetrics = FindMetrics(metrics, providerId, source);
            var degradationScore = scorer?.GetScore(providerId).CompositeScore;
            var isEnabled = source?.Enabled ?? connection is not null || descriptor is not null;
            var isConnected = providerMetrics?.IsConnected
                ?? connection?.Health is ProviderContinuityHealthDto.Healthy
                ?? false;
            var fallbackActive = connection?.FallbackActive
                ?? providerMetrics is { IsConnected: false, ConnectionFailures: > 0 };
            var status = ResolveReadinessStatus(connection, isEnabled, isConnected, fallbackActive, degradationScore);

            rows.Add(new ProviderReadinessRowDto(
                ProviderId: descriptor?.ProviderId ?? providerId,
                DisplayName: connection?.DisplayName ?? descriptor?.DisplayName ?? source?.Name ?? providerId,
                Capability: connection?.Capability ?? descriptor?.Capability ?? ProviderConnectionCapabilityDto.Data,
                Status: status,
                CredentialState: connection?.CredentialState ?? (descriptor?.RequiresCredentials == true ? ProviderCredentialStateDto.Missing : ProviderCredentialStateDto.NotRequired),
                CredentialSource: connection?.CredentialSource ?? (descriptor?.RequiresCredentials == true ? ProviderCredentialSourceDto.None : ProviderCredentialSourceDto.NotRequired),
                VerificationState: connection?.VerificationState ?? (descriptor?.RequiresCredentials == true ? ProviderVerificationStateDto.NotVerified : ProviderVerificationStateDto.NotRequired),
                ConnectionHealth: connection?.Health ?? ResolveConnectionHealth(isEnabled, isConnected, fallbackActive),
                IsEnabled: isEnabled,
                IsConnected: isConnected,
                FallbackActive: fallbackActive,
                DegradationScore: degradationScore,
                LastVerifiedAt: connection?.LastVerifiedAt,
                LastSuccessfulAt: connection?.LastSuccessfulAt ?? (providerMetrics?.IsConnected == true ? providerMetrics.Timestamp : null),
                LastFailureAt: connection?.LastFailureAt ?? (providerMetrics is { IsConnected: false, ConnectionFailures: > 0 } ? providerMetrics.Timestamp : null),
                LastError: connection?.LastError,
                MaskedKeyPreview: connection?.MaskedKeyPreview,
                Environment: connection?.Environment ?? descriptor?.DefaultEnvironment,
                ExternalAccountId: connection?.ExternalAccountId,
                AffectedWorkflows: connection?.AffectedWorkflows ?? descriptor?.AffectedWorkflows ?? [],
                RecommendedAction: ResolveRecommendedAction(status, connection, descriptor, degradationScore),
                ActionHref: connection?.ActionHref ?? descriptor?.ResolvedActionHref ?? "/settings#providers",
                Evidence: BuildEvidence(providerId, connection, providerMetrics, degradationScore, plaidItems, plaidAccounts),
                RecoveryActions: BuildRecoveryActions(providerId, status, connection, descriptor)));
        }

        var ordered = rows
            .OrderByDescending(row => StatusPriority(row.Status))
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summaryStatus = ResolveSummaryStatus(ordered);
        return new ProviderReadinessSummaryDto(
            AsOf: asOf,
            Status: summaryStatus,
            TotalProviders: ordered.Length,
            ReadyProviders: ordered.Count(row => row.Status == ProviderReadinessStatusDto.Ready),
            ReviewProviders: ordered.Count(row => row.Status == ProviderReadinessStatusDto.Review),
            DegradedProviders: ordered.Count(row => row.Status == ProviderReadinessStatusDto.Degraded),
            BlockedProviders: ordered.Count(row => row.Status == ProviderReadinessStatusDto.Blocked),
            Summary: BuildSummary(summaryStatus, ordered),
            RecommendedAction: BuildSummaryAction(summaryStatus, ordered),
            Providers: ordered);
    }

    internal static ProviderReadinessStatusDto ResolveReadinessStatus(
        ProviderConnectionRowDto? connection,
        bool isEnabled,
        bool isConnected,
        bool fallbackActive,
        double? degradationScore)
    {
        if (!isEnabled)
        {
            return ProviderReadinessStatusDto.Review;
        }

        if (connection?.CredentialState is ProviderCredentialStateDto.Partial or ProviderCredentialStateDto.Invalid)
        {
            return ProviderReadinessStatusDto.Blocked;
        }

        if (connection?.VerificationState == ProviderVerificationStateDto.Failed)
        {
            return ProviderReadinessStatusDto.Blocked;
        }

        if (connection?.CredentialState == ProviderCredentialStateDto.Missing)
        {
            return ProviderReadinessStatusDto.Review;
        }

        if (degradationScore >= DegradedScoreThreshold || fallbackActive || connection?.Health == ProviderContinuityHealthDto.Degraded)
        {
            return ProviderReadinessStatusDto.Degraded;
        }

        if (isConnected || connection?.Health is ProviderContinuityHealthDto.Healthy)
        {
            return ProviderReadinessStatusDto.Ready;
        }

        return ProviderReadinessStatusDto.Review;
    }

    private async Task<IReadOnlyList<ProviderConnectionRowDto>> GetConnectionRowsAsync(CancellationToken ct)
    {
        var service = _services.GetService<ProviderConnectionLifecycleService>();
        return service is null ? [] : await service.GetConnectionsAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PlaidItemDto>> GetPlaidItemsAsync(CancellationToken ct)
    {
        var repository = _services.GetService<IPlaidConnectionRepository>();
        return repository is null ? [] : await repository.ListItemsAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PlaidAccountDto>> GetPlaidAccountsAsync(CancellationToken ct)
    {
        var repository = _services.GetService<IPlaidConnectionRepository>();
        return repository is null ? [] : await repository.ListAccountsAsync(null, ct).ConfigureAwait(false);
    }

    private static ProviderConnectionRowDto? FindConnection(IReadOnlyList<ProviderConnectionRowDto> rows, string providerId)
        => rows.FirstOrDefault(row => string.Equals(ProviderCredentialCatalog.NormalizeProviderId(row.ProviderId), providerId, StringComparison.OrdinalIgnoreCase));

    private static DataSourceConfig? FindSource(IReadOnlyList<DataSourceConfig> sources, string providerId)
        => sources.FirstOrDefault(source =>
            string.Equals(ProviderCredentialCatalog.NormalizeProviderId(source.Id), providerId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ProviderCredentialCatalog.NormalizeProviderId(source.Provider.ToString()), providerId, StringComparison.OrdinalIgnoreCase));

    private static ProviderMetrics? FindMetrics(ProviderMetricsStatus? metrics, string providerId, DataSourceConfig? source)
        => metrics?.Providers.FirstOrDefault(provider =>
            string.Equals(ProviderCredentialCatalog.NormalizeProviderId(provider.ProviderId), providerId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ProviderCredentialCatalog.NormalizeProviderId(provider.ProviderType), providerId, StringComparison.OrdinalIgnoreCase) ||
            (source is not null && string.Equals(provider.ProviderId, source.Id, StringComparison.OrdinalIgnoreCase)));

    private static ProviderContinuityHealthDto ResolveConnectionHealth(bool isEnabled, bool isConnected, bool fallbackActive)
        => !isEnabled ? ProviderContinuityHealthDto.Warning
            : fallbackActive ? ProviderContinuityHealthDto.Degraded
            : isConnected ? ProviderContinuityHealthDto.Healthy
            : ProviderContinuityHealthDto.Warning;

    private static string ResolveRecommendedAction(
        ProviderReadinessStatusDto status,
        ProviderConnectionRowDto? connection,
        ProviderCredentialCatalogEntry? descriptor,
        double? degradationScore)
        => status switch
        {
            ProviderReadinessStatusDto.Ready => "No provider readiness action required.",
            ProviderReadinessStatusDto.Blocked => connection?.RecommendedAction ?? descriptor?.RecommendedActionWhenMissing ?? "Repair provider credentials before routing dependent workflows.",
            ProviderReadinessStatusDto.Degraded when degradationScore is not null => "Review degradation evidence and fallback routing before accepting downstream readiness.",
            ProviderReadinessStatusDto.Degraded => "Review provider health and fallback routing before accepting downstream readiness.",
            _ => connection?.RecommendedAction ?? descriptor?.RecommendedActionWhenMissing ?? "Review provider setup before routing dependent workflows."
        };

    private static IReadOnlyList<ProviderReadinessEvidenceDto> BuildEvidence(
        string providerId,
        ProviderConnectionRowDto? connection,
        ProviderMetrics? metrics,
        double? degradationScore,
        IReadOnlyList<PlaidItemDto> plaidItems,
        IReadOnlyList<PlaidAccountDto> plaidAccounts)
    {
        var evidence = new List<ProviderReadinessEvidenceDto>
        {
            new(
                ProviderReadinessEvidenceKindDto.Credential,
                "Credential",
                MapCredentialStatus(connection?.CredentialState, connection?.VerificationState),
                BuildCredentialDetail(connection),
                connection?.LastVerifiedAt,
                connection?.ActionHref),
            new(
                ProviderReadinessEvidenceKindDto.Connection,
                "Connection",
                MapConnectionStatus(connection?.Health, metrics),
                BuildConnectionDetail(connection, metrics),
                connection?.LastSuccessfulAt ?? metrics?.Timestamp)
        };

        if (degradationScore is not null)
        {
            evidence.Add(new ProviderReadinessEvidenceDto(
                ProviderReadinessEvidenceKindDto.Degradation,
                "Degradation",
                degradationScore >= DegradedScoreThreshold ? ProviderReadinessStatusDto.Degraded : ProviderReadinessStatusDto.Ready,
                $"Composite degradation score {degradationScore:P0}."));
        }

        if (providerId.Equals("plaid", StringComparison.OrdinalIgnoreCase))
        {
            var linkedItems = plaidItems.Count(item => item.Status == PlaidItemStatusDto.Linked);
            evidence.Add(new ProviderReadinessEvidenceDto(
                ProviderReadinessEvidenceKindDto.Plaid,
                "Linked Plaid evidence",
                linkedItems > 0 ? ProviderReadinessStatusDto.Ready : ProviderReadinessStatusDto.Review,
                linkedItems > 0
                    ? $"{linkedItems} linked item(s), {plaidAccounts.Count} account(s)."
                    : "No linked Plaid items retained yet.",
                plaidItems.OrderByDescending(item => item.LastSyncedAt ?? item.LinkedAt).FirstOrDefault()?.LastSyncedAt));
        }

        return evidence;
    }

    private static IReadOnlyList<ProviderRecoveryActionDto> BuildRecoveryActions(
        string providerId,
        ProviderReadinessStatusDto status,
        ProviderConnectionRowDto? connection,
        ProviderCredentialCatalogEntry? descriptor)
    {
        var target = connection?.ActionHref ?? descriptor?.ResolvedActionHref ?? "/settings#providers";
        var actions = new List<ProviderRecoveryActionDto>
        {
            new($"provider.{providerId}.open-setup", "Open setup", target, RequiresMutation: false)
        };

        if (connection?.CredentialState is ProviderCredentialStateDto.Configured or ProviderCredentialStateDto.Invalid or ProviderCredentialStateDto.Verified)
        {
            actions.Add(new($"provider.{providerId}.verify", "Verify credentials", $"/api/providers/{providerId}/verify", RequiresMutation: true));
        }

        if (status is ProviderReadinessStatusDto.Degraded or ProviderReadinessStatusDto.Blocked)
        {
            actions.Add(new($"provider.{providerId}.diagnostics", "Open diagnostics", $"/api/health/providers/{providerId}/diagnostics", RequiresMutation: false));
        }

        return actions;
    }

    private static ProviderReadinessStatusDto MapCredentialStatus(
        ProviderCredentialStateDto? credentialState,
        ProviderVerificationStateDto? verificationState)
        => credentialState switch
        {
            ProviderCredentialStateDto.NotRequired => ProviderReadinessStatusDto.Ready,
            ProviderCredentialStateDto.Verified => ProviderReadinessStatusDto.Ready,
            ProviderCredentialStateDto.Configured when verificationState == ProviderVerificationStateDto.Verified => ProviderReadinessStatusDto.Ready,
            ProviderCredentialStateDto.Invalid or ProviderCredentialStateDto.Partial => ProviderReadinessStatusDto.Blocked,
            ProviderCredentialStateDto.Missing => ProviderReadinessStatusDto.Review,
            _ => ProviderReadinessStatusDto.Review
        };

    private static ProviderReadinessStatusDto MapConnectionStatus(ProviderContinuityHealthDto? health, ProviderMetrics? metrics)
        => health switch
        {
            ProviderContinuityHealthDto.Healthy => ProviderReadinessStatusDto.Ready,
            ProviderContinuityHealthDto.Blocked => ProviderReadinessStatusDto.Blocked,
            ProviderContinuityHealthDto.Degraded => ProviderReadinessStatusDto.Degraded,
            _ when metrics is { IsConnected: true } => ProviderReadinessStatusDto.Ready,
            _ when metrics is { IsConnected: false, ConnectionFailures: > 0 } => ProviderReadinessStatusDto.Degraded,
            _ => ProviderReadinessStatusDto.Review
        };

    private static string BuildCredentialDetail(ProviderConnectionRowDto? connection)
        => connection is null
            ? "No credential status is available."
            : $"{connection.CredentialState} from {connection.CredentialSource}; verification {connection.VerificationState}.";

    private static string BuildConnectionDetail(ProviderConnectionRowDto? connection, ProviderMetrics? metrics)
    {
        if (metrics is not null)
        {
            return $"{(metrics.IsConnected ? "Connected" : "Disconnected")} with {metrics.ActiveSubscriptions} subscription(s), {metrics.ConnectionFailures} failure(s), {metrics.AverageLatencyMs:0.#} ms average latency.";
        }

        return connection is null
            ? "No connection telemetry is available."
            : $"Connection health is {connection.Health}.";
    }

    private static ProviderReadinessStatusDto ResolveSummaryStatus(IReadOnlyList<ProviderReadinessRowDto> rows)
    {
        if (rows.Any(row => row.Status == ProviderReadinessStatusDto.Blocked))
        {
            return ProviderReadinessStatusDto.Blocked;
        }

        if (rows.Any(row => row.Status == ProviderReadinessStatusDto.Degraded))
        {
            return ProviderReadinessStatusDto.Degraded;
        }

        if (rows.Any(row => row.Status == ProviderReadinessStatusDto.Review))
        {
            return ProviderReadinessStatusDto.Review;
        }

        return rows.Count == 0 ? ProviderReadinessStatusDto.Unknown : ProviderReadinessStatusDto.Ready;
    }

    private static string BuildSummary(ProviderReadinessStatusDto status, IReadOnlyList<ProviderReadinessRowDto> rows)
        => status switch
        {
            ProviderReadinessStatusDto.Ready => "All provider readiness rows are ready.",
            ProviderReadinessStatusDto.Blocked => $"{rows.Count(row => row.Status == ProviderReadinessStatusDto.Blocked)} provider(s) block dependent workflows.",
            ProviderReadinessStatusDto.Degraded => $"{rows.Count(row => row.Status == ProviderReadinessStatusDto.Degraded)} provider(s) are degraded or in fallback posture.",
            ProviderReadinessStatusDto.Review => $"{rows.Count(row => row.Status == ProviderReadinessStatusDto.Review)} provider(s) need setup or review.",
            _ => "Provider readiness is unknown."
        };

    private static string BuildSummaryAction(ProviderReadinessStatusDto status, IReadOnlyList<ProviderReadinessRowDto> rows)
        => rows.FirstOrDefault(row => row.Status == status)?.RecommendedAction
           ?? "Review provider setup before accepting downstream readiness.";

    private static int StatusPriority(ProviderReadinessStatusDto status)
        => status switch
        {
            ProviderReadinessStatusDto.Blocked => 4,
            ProviderReadinessStatusDto.Degraded => 3,
            ProviderReadinessStatusDto.Review => 2,
            ProviderReadinessStatusDto.Unknown => 1,
            _ => 0
        };
}
