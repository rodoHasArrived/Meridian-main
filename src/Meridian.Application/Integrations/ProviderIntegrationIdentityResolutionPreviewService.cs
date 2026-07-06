using System.Text.Json;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UiApiRoutes = Meridian.Contracts.Api.UiApiRoutes;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationIdentityResolutionPreviewService
{
    private const int DefaultRecentRunLimit = 10;
    private const int MaxRecentRunLimit = 50;

    private readonly IProviderIntegrationManifestStore store;
    private readonly ISecurityMasterQueryService? securityMaster;
    private readonly ILogger<ProviderIntegrationIdentityResolutionPreviewService> logger;

    public ProviderIntegrationIdentityResolutionPreviewService(
        IProviderIntegrationManifestStore store,
        ISecurityMasterQueryService? securityMaster = null,
        ILogger<ProviderIntegrationIdentityResolutionPreviewService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.securityMaster = securityMaster;
        this.logger = logger ?? NullLogger<ProviderIntegrationIdentityResolutionPreviewService>.Instance;
    }

    public async Task<ProviderIntegrationStagingIdentityResolutionPreviewDto> PreviewAsync(
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await PreviewAsync(null, connectionId, recentRunLimit, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationStagingIdentityResolutionPreviewDto> PreviewAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        logger.LogDebug(
            "Provider integration operation {Operation} starting for connection {ConnectionId}.",
            nameof(PreviewAsync),
            connectionId);
        try
        {
            return await PreviewCoreAsync(tenantId, connectionId, recentRunLimit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Provider integration operation {Operation} failed for connection {ConnectionId}.",
                nameof(PreviewAsync),
                connectionId);
            throw;
        }
    }

    private async Task<ProviderIntegrationStagingIdentityResolutionPreviewDto> PreviewCoreAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var connection = await scopedStore.GetConnectionAsync(connectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{connectionId}' was not found.");
        var syncRuns = (await scopedStore.ListSyncRunsAsync(connectionId, ct).ConfigureAwait(false))
            .Take(NormalizeLimit(recentRunLimit))
            .ToArray();

        var rows = new List<ProviderIntegrationStagingIdentityResolutionRowDto>();
        foreach (var syncRun in syncRuns)
        {
            ct.ThrowIfCancellationRequested();
            var records = await scopedStore.ListStagingRecordsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false);
            foreach (var record in records.Where(record => StringComparer.Ordinal.Equals(record.ConnectionId, connectionId)))
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(await ResolveRecordAsync(connection, record, ct).ConfigureAwait(false));
            }
        }

        return new ProviderIntegrationStagingIdentityResolutionPreviewDto(
            connectionId,
            syncRuns.Select(syncRun => syncRun.SyncRunId).ToArray(),
            rows.OrderBy(row => row.SecurityStatus)
                .ThenBy(row => row.AccountStatus)
                .ThenBy(row => row.StagingRecordId, StringComparer.Ordinal)
                .ToArray(),
            rows.Count,
            rows.Count(row => row.AccountStatus == ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired),
            rows.Count(row => row.AccountStatus == ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier),
            rows.Count(row => row.SecurityStatus == ProviderIntegrationIdentityResolutionStatusDto.Resolved),
            rows.Count(row => row.SecurityStatus is ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired
                or ProviderIntegrationIdentityResolutionStatusDto.NotFound
                or ProviderIntegrationIdentityResolutionStatusDto.NotConfigured),
            rows.Count(row => row.SecurityStatus == ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier));
    }

    private async Task<ProviderIntegrationStagingIdentityResolutionRowDto> ResolveRecordAsync(
        ProviderConnectionDto connection,
        IntegrationStagingRecordDto record,
        CancellationToken ct)
    {
        var issues = new List<ValidationIssueDto>(record.ValidationWarnings);
        var providerAccountId = TryReadString(record.MappedRecord, "providerAccountId")
            ?? TryReadString(record.MappedRecord, "account.providerAccountId");
        var internalAccountId = TryReadString(record.MappedRecord, "internalAccountId")
            ?? TryReadString(record.MappedRecord, "account.internalAccountId");
        var accountStatus = ResolveAccountStatus(providerAccountId, internalAccountId, issues);
        var accountNote = accountStatus switch
        {
            ProviderIntegrationIdentityResolutionStatusDto.Resolved => "Mapped record already carries an internal account id.",
            ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired => "Provider account id is present; account matching remains a reconciliation review step.",
            _ => "Provider account id is missing from the staged record."
        };

        IReadOnlyList<ProviderIntegrationIdentityCandidateDto> securityCandidates = BuildSecurityCandidates(connection, record.MappedRecord);
        var internalSecurityId = TryReadString(record.MappedRecord, "security.internalSecurityId");
        var resolvedSecurity = TryBuildInternalSecurityCandidate(internalSecurityId);
        ProviderIntegrationIdentityResolutionStatusDto securityStatus;
        string? securityDisplayName = null;
        string? securityRoute = null;

        if (resolvedSecurity is not null)
        {
            securityStatus = ProviderIntegrationIdentityResolutionStatusDto.Resolved;
            securityRoute = CreateSecurityRoute(resolvedSecurity.Value);
        }
        else if (securityCandidates.Count == 0)
        {
            securityStatus = ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier;
            issues.Add(new ValidationIssueDto(
                "identity.security.identifier.missing",
                ProviderIntegrationIssueSeverityDto.Critical,
                "No security identifier was found on the staged provider record.",
                "security",
                "Map CUSIP, ISIN, FIGI, provider security id, or ticker before reconciliation."));
        }
        else if (securityMaster is null)
        {
            securityStatus = ProviderIntegrationIdentityResolutionStatusDto.NotConfigured;
            issues.Add(new ValidationIssueDto(
                "identity.security.resolver.not-configured",
                ProviderIntegrationIssueSeverityDto.Warning,
                "Security Master matching is not registered for this runtime.",
                "security",
                "Review staged identifiers manually or register the Security Master query service before promotion."));
        }
        else
        {
            var resolved = await ResolveSecurityAsync(securityCandidates, ct).ConfigureAwait(false);
            securityCandidates = resolved.Candidates;
            securityStatus = resolved.Status;
            resolvedSecurity = resolved.SecurityId;
            securityDisplayName = resolved.DisplayName;
            securityRoute = resolved.Route;
            if (securityStatus == ProviderIntegrationIdentityResolutionStatusDto.NotFound)
            {
                issues.Add(new ValidationIssueDto(
                    "identity.security.not-found",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identifiers did not resolve to an active Security Master record.",
                    "security",
                    "Create or approve a Security Master record before canonical promotion."));
            }
            else if (securityStatus == ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired)
            {
                issues.Add(new ValidationIssueDto(
                    "identity.security.review-required",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identifiers resolved to a non-active Security Master record.",
                    "security",
                    "Review Security Master status before using the staged provider record."));
            }
        }

        return new ProviderIntegrationStagingIdentityResolutionRowDto(
            record.StagingRecordId,
            record.SyncRunId,
            record.Capability,
            providerAccountId,
            accountStatus,
            internalAccountId,
            accountNote,
            securityStatus,
            resolvedSecurity?.ToString("D"),
            securityDisplayName,
            securityRoute,
            securityCandidates,
            issues);
    }

    private async Task<(
        ProviderIntegrationIdentityResolutionStatusDto Status,
        Guid? SecurityId,
        string? DisplayName,
        string? Route,
        IReadOnlyList<ProviderIntegrationIdentityCandidateDto> Candidates)> ResolveSecurityAsync(
            IReadOnlyList<ProviderIntegrationIdentityCandidateDto> candidates,
            CancellationToken ct)
    {
        var resolvedCandidates = new List<ProviderIntegrationIdentityCandidateDto>(candidates.Count);
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Priority))
        {
            ct.ThrowIfCancellationRequested();
            if (!Enum.TryParse<SecurityIdentifierKind>(candidate.IdentifierKind, ignoreCase: true, out var kind))
            {
                resolvedCandidates.Add(candidate with { Status = ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired });
                continue;
            }

            var detail = await securityMaster!.GetByIdentifierAsync(
                kind,
                candidate.IdentifierValue,
                candidate.Provider,
                ct).ConfigureAwait(false);
            if (detail is null)
            {
                resolvedCandidates.Add(candidate with { Status = ProviderIntegrationIdentityResolutionStatusDto.NotFound });
                continue;
            }

            var route = CreateSecurityRoute(detail.SecurityId);
            var status = detail.Status == SecurityStatusDto.Active
                ? ProviderIntegrationIdentityResolutionStatusDto.Resolved
                : ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired;
            resolvedCandidates.Add(candidate with
            {
                Status = status,
                InternalSecurityId = detail.SecurityId.ToString("D"),
                DisplayName = detail.DisplayName,
                Route = route
            });
            return (status, detail.SecurityId, detail.DisplayName, route, resolvedCandidates.Concat(candidates.Skip(resolvedCandidates.Count)).ToArray());
        }

        return (ProviderIntegrationIdentityResolutionStatusDto.NotFound, null, null, null, resolvedCandidates);
    }

    private static ProviderIntegrationIdentityResolutionStatusDto ResolveAccountStatus(
        string? providerAccountId,
        string? internalAccountId,
        ICollection<ValidationIssueDto> issues)
    {
        if (!string.IsNullOrWhiteSpace(internalAccountId))
        {
            return ProviderIntegrationIdentityResolutionStatusDto.Resolved;
        }

        if (!string.IsNullOrWhiteSpace(providerAccountId))
        {
            issues.Add(new ValidationIssueDto(
                "identity.account.review-required",
                ProviderIntegrationIssueSeverityDto.Warning,
                "Provider account id is present but has not been matched to an internal fund account.",
                "providerAccountId",
                "Confirm the account mapping before reconciliation promotion."));
            return ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired;
        }

        issues.Add(new ValidationIssueDto(
            "identity.account.provider-id.missing",
            ProviderIntegrationIssueSeverityDto.Critical,
            "Provider account id is required for account-scoped reconciliation.",
            "providerAccountId",
            "Map provider account id or choose a constant internal account mapping."));
        return ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier;
    }

    private static List<ProviderIntegrationIdentityCandidateDto> BuildSecurityCandidates(
        ProviderConnectionDto connection,
        JsonElement record)
    {
        var candidates = new List<ProviderIntegrationIdentityCandidateDto>();
        AddCandidate(candidates, "Cusip", TryReadString(record, "security.cusip"), provider: null, priority: 1);
        AddCandidate(candidates, "Isin", TryReadString(record, "security.isin"), provider: null, priority: 2);
        AddCandidate(candidates, "Sedol", TryReadString(record, "security.sedol"), provider: null, priority: 3);
        AddCandidate(candidates, "Figi", TryReadString(record, "security.figi"), provider: null, priority: 4);
        AddCandidate(candidates, "ProviderSymbol", TryReadString(record, "security.providerSecurityId"), connection.ProviderId, priority: 5);
        AddCandidate(candidates, "Ticker", TryReadString(record, "security.symbol"), provider: null, priority: 6);
        return candidates;
    }

    private static void AddCandidate(
        ICollection<ProviderIntegrationIdentityCandidateDto> candidates,
        string kind,
        string? value,
        string? provider,
        int priority)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        candidates.Add(new ProviderIntegrationIdentityCandidateDto(
            kind,
            value.Trim(),
            provider,
            priority,
            ProviderIntegrationIdentityResolutionStatusDto.NotConfigured,
            InternalSecurityId: null,
            DisplayName: null,
            Route: null));
    }

    private static Guid? TryBuildInternalSecurityCandidate(string? internalSecurityId)
        => Guid.TryParse(internalSecurityId, out var securityId) ? securityId : null;

    private static string CreateSecurityRoute(Guid securityId)
        => UiApiRoutes.SecurityMasterById.Replace("{securityId:guid}", securityId.ToString("D"), StringComparison.Ordinal);

    private static string? TryReadString(JsonElement element, string path)
    {
        var current = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(current.GetString()) ? null : current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            _ => null
        };
    }

    private static int NormalizeLimit(int recentRunLimit)
    {
        if (recentRunLimit <= 0)
        {
            return DefaultRecentRunLimit;
        }

        return Math.Min(recentRunLimit, MaxRecentRunLimit);
    }

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;
}
