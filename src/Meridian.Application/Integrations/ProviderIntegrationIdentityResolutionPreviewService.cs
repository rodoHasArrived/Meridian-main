using System.Text.Json;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UiApiRoutes = Meridian.Contracts.Api.UiApiRoutes;

namespace Meridian.Application.Integrations;

/// <summary>
/// Previews security identity candidates for staged provider integration data.
/// </summary>
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
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "identity-resolution-preview",
            new ProviderIntegrationBoundaryContext(TenantId: tenantId, ConnectionId: connectionId),
            () => PreviewCoreAsync(tenantId, connectionId, recentRunLimit, ct)).ConfigureAwait(false);

    public async Task<ProviderIntegrationIdentityDecisionDto> ResolveAsync(
        string? tenantId,
        string connectionId,
        ProviderIntegrationIdentityResolutionRequestDto request,
        string reviewedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        if (request.SelectedSecurityId == Guid.Empty)
        {
            throw new ArgumentException("Choose an active matched security.", nameof(request));
        }
        var scopedStore = ResolveStore(tenantId);
        if (scopedStore is not IProviderIntegrationIdentityDecisionStore decisionStore)
        {
            throw new InvalidOperationException("Retained identity decisions are not supported by this store.");
        }
        var connection = await scopedStore.GetConnectionAsync(connectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Provider integration connection was not found.");
        var run = await scopedStore.GetSyncRunAsync(request.SyncRunId, ct).ConfigureAwait(false);
        if (run?.ConnectionId != connectionId)
        {
            throw new KeyNotFoundException("The sync run does not belong to this connection.");
        }
        var record = (await scopedStore.ListStagingRecordsAsync(request.SyncRunId, ct).ConfigureAwait(false))
            .SingleOrDefault(candidate => candidate.ConnectionId == connectionId && candidate.StagingRecordId == request.StagingRecordId)
            ?? throw new KeyNotFoundException("The staged record does not belong to this connection and run.");
        var decisions = await LoadIdentityDecisionSnapshotAsync(scopedStore, connectionId, request.SyncRunId, ct).ConfigureAwait(false);
        var preview = await ResolveRecordAsync(connection, record, decisions[record.StagingRecordId], ct).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(request.EvidenceHash, preview.EvidenceHash))
        {
            throw new InvalidOperationException("Identifier matching evidence changed. Reload and review the current evidence.");
        }
        if (!preview.SecurityCandidates.Any(candidate => candidate.Status == ProviderIntegrationIdentityResolutionStatusDto.Resolved &&
            candidate.InternalSecurityId == request.SelectedSecurityId.ToString("D")))
        {
            throw new ArgumentException("Choose an active security from the matching evidence.", nameof(request));
        }
        var decision = new ProviderIntegrationIdentityDecisionDto(Guid.NewGuid().ToString("N"), connectionId,
            request.SyncRunId, request.StagingRecordId, request.SelectedSecurityId, request.EvidenceHash,
            reviewedBy.Trim(), DateTimeOffset.UtcNow, request.Reason.Trim());
        await decisionStore.SaveIdentityDecisionAsync(decision, ct).ConfigureAwait(false);
        return decision;
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
            var decisions = await LoadIdentityDecisionSnapshotAsync(scopedStore, connectionId, syncRun.SyncRunId, ct).ConfigureAwait(false);
            foreach (var record in records.Where(record => StringComparer.Ordinal.Equals(record.ConnectionId, connectionId)))
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(await ResolveRecordAsync(connection, record, decisions[record.StagingRecordId], ct).ConfigureAwait(false));
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
                or ProviderIntegrationIdentityResolutionStatusDto.NotConfigured
                or ProviderIntegrationIdentityResolutionStatusDto.Conflict),
            rows.Count(row => row.SecurityStatus == ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier));
    }

    private static async Task<ILookup<string, ProviderIntegrationIdentityDecisionDto>> LoadIdentityDecisionSnapshotAsync(
        IProviderIntegrationManifestStore scopedStore,
        string connectionId,
        string syncRunId,
        CancellationToken ct)
    {
        IReadOnlyList<ProviderIntegrationIdentityDecisionDto> decisions = scopedStore is IProviderIntegrationIdentityDecisionStore decisionStore
            ? await decisionStore.ListIdentityDecisionsAsync(syncRunId, ct).ConfigureAwait(false)
            : [];
        return decisions
            .Where(decision => StringComparer.Ordinal.Equals(decision.ConnectionId, connectionId) &&
                StringComparer.Ordinal.Equals(decision.SyncRunId, syncRunId))
            .ToLookup(decision => decision.StagingRecordId, StringComparer.Ordinal);
    }

    private async Task<ProviderIntegrationStagingIdentityResolutionRowDto> ResolveRecordAsync(
        ProviderConnectionDto connection,
        IntegrationStagingRecordDto record,
        IEnumerable<ProviderIntegrationIdentityDecisionDto> retainedDecisions,
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
        Guid? resolvedSecurity = null;
        ProviderIntegrationIdentityResolutionStatusDto securityStatus;
        string? securityDisplayName = null;
        string? securityRoute = null;

        if (securityCandidates.Count == 0)
        {
            securityStatus = ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier;
            issues.Add(new ValidationIssueDto(
                "identity.security.identifier.missing",
                ProviderIntegrationIssueSeverityDto.Critical,
                "No security identifier was found on the staged provider record.",
                "security",
                "Map CUSIP, ISIN, FIGI, provider security id, or ticker before reconciliation."));
        }
        else if (securityMaster is null && securityCandidates.Count == 1 &&
            securityCandidates[0].IdentifierKind == "InternalSecurityId" &&
            Guid.TryParse(securityCandidates[0].IdentifierValue, out var canonicalId))
        {
            // Existing canonical-only mappings remain usable in runtimes without a resolver.
            // A supplied provider identifier still requires the complete agreement check.
            resolvedSecurity = canonicalId;
            securityRoute = CreateSecurityRoute(canonicalId);
            securityStatus = ProviderIntegrationIdentityResolutionStatusDto.Resolved;
            securityCandidates = [securityCandidates[0] with { Status = securityStatus, InternalSecurityId = canonicalId.ToString("D"), Route = securityRoute }];
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
        }

        // Bind a retained choice to the complete matching evidence. Changed source data,
        // identifier mappings or Security Master statuses require a fresh review.
        var evidenceHash = Sha256Digest.ComputeUtf8(
            record.MappedRecord.GetRawText() + "\n" + JsonSerializer.Serialize(securityCandidates,
                ProviderIntegrationContractsJsonContext.Default.IReadOnlyListProviderIntegrationIdentityCandidateDto));
        var hasConflict = securityStatus == ProviderIntegrationIdentityResolutionStatusDto.Conflict;
        var resolution = retainedDecisions
            .Where(decision => decision.ConnectionId == connection.ConnectionId && decision.SyncRunId == record.SyncRunId &&
                decision.StagingRecordId == record.StagingRecordId && decision.EvidenceHash == evidenceHash &&
                securityCandidates.Any(candidate => candidate.Status == ProviderIntegrationIdentityResolutionStatusDto.Resolved &&
                    candidate.InternalSecurityId == decision.SelectedSecurityId.ToString("D")))
            .OrderByDescending(decision => decision.ReviewedAt)
            .ThenByDescending(decision => decision.DecisionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (resolution is not null)
        {
            var selected = securityCandidates.First(candidate => candidate.InternalSecurityId == resolution.SelectedSecurityId.ToString("D"));
            resolvedSecurity = resolution.SelectedSecurityId;
            securityDisplayName = selected.DisplayName;
            securityRoute = selected.Route;
            securityStatus = ProviderIntegrationIdentityResolutionStatusDto.Resolved;
        }

        if (hasConflict && resolution is null)
        {
            issues.Add(new ValidationIssueDto("identity.security.identifier-conflict",
                ProviderIntegrationIssueSeverityDto.Critical,
                "Supplied identifiers resolve to different Security Master records.",
                "security", "Resolve source conflict by reviewing every identifier and recording the chosen security and reason."));
        }
        else if (securityStatus == ProviderIntegrationIdentityResolutionStatusDto.NotFound)
        {
            issues.Add(new ValidationIssueDto("identity.security.not-found", ProviderIntegrationIssueSeverityDto.Critical,
                "Security identifiers did not resolve to an active Security Master record.", "security",
                "Create or approve a Security Master record before canonical promotion."));
        }
        else if (securityStatus == ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired)
        {
            issues.Add(new ValidationIssueDto("identity.security.review-required", ProviderIntegrationIssueSeverityDto.Critical,
                "One or more supplied identifiers did not resolve to the same active Security Master record.", "security",
                "Review the identifier evidence before using the staged provider record."));
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
            issues)
        {
            EvidenceHash = evidenceHash,
            HasIdentifierConflict = hasConflict,
            OperatorResolution = resolution
        };
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
            SecurityDetailDto? detail;
            if (candidate.IdentifierKind == "InternalSecurityId" && Guid.TryParse(candidate.IdentifierValue, out var internalId))
            {
                detail = await securityMaster!.GetByIdAsync(internalId, ct).ConfigureAwait(false);
            }
            else if (Enum.TryParse<SecurityIdentifierKind>(candidate.IdentifierKind, ignoreCase: true, out var kind))
            {
                detail = await securityMaster!.GetByIdentifierAsync(kind, candidate.IdentifierValue, candidate.Provider, ct).ConfigureAwait(false);
            }
            else
            {
                resolvedCandidates.Add(candidate with { Status = ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired });
                continue;
            }

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

        }

        var matches = resolvedCandidates.Where(candidate => candidate.InternalSecurityId is not null).ToArray();
        if (matches.Select(candidate => candidate.InternalSecurityId).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            return (ProviderIntegrationIdentityResolutionStatusDto.Conflict, null, null, null, resolvedCandidates);
        }
        if (matches.Length == 0)
        {
            return (ProviderIntegrationIdentityResolutionStatusDto.NotFound, null, null, null, resolvedCandidates);
        }
        var match = matches[0];
        var aggregateStatus = resolvedCandidates.All(candidate => candidate.Status == ProviderIntegrationIdentityResolutionStatusDto.Resolved)
            ? ProviderIntegrationIdentityResolutionStatusDto.Resolved
            : ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired;
        return (aggregateStatus, Guid.Parse(match.InternalSecurityId!), match.DisplayName, match.Route, resolvedCandidates);
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
        AddCandidate(candidates, "InternalSecurityId", TryReadString(record, "security.internalSecurityId"), provider: null, priority: 0);
        AddCandidate(candidates, "Cusip", TryReadString(record, "security.cusip"), provider: null, priority: 1);
        AddCandidate(candidates, "Isin", TryReadString(record, "security.isin"), provider: null, priority: 2);
        AddCandidate(candidates, "Sedol", TryReadString(record, "security.sedol"), provider: null, priority: 3);
        AddCandidate(candidates, "Figi", TryReadString(record, "security.figi"), provider: null, priority: 4);
        AddCandidate(candidates, "ProviderSymbol", TryReadString(record, "security.providerSecurityId"), connection.ProviderId, priority: 5);
        AddCandidate(candidates, "Ticker", TryReadString(record, "security.ticker"), provider: null, priority: 6);
        AddCandidate(candidates, "Ticker", TryReadString(record, "security.symbol"), provider: null, priority: 7);
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

        if (candidates.Any(candidate => candidate.IdentifierKind == kind && candidate.IdentifierValue == value.Trim() && candidate.Provider == provider))
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
