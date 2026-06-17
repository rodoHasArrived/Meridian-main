using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationPromotionReadinessService
{
    private const string PromotionTarget = "reconciliation-staging";
    private const int DefaultRecentRunLimit = 10;

    private readonly ProviderIntegrationIdentityResolutionPreviewService identityResolution;

    public ProviderIntegrationPromotionReadinessService(
        ProviderIntegrationIdentityResolutionPreviewService identityResolution)
    {
        this.identityResolution = identityResolution ?? throw new ArgumentNullException(nameof(identityResolution));
    }

    public async Task<ProviderIntegrationPromotionReadinessPreviewDto> PreviewAsync(
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await PreviewAsync(null, connectionId, recentRunLimit, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationPromotionReadinessPreviewDto> PreviewAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var identityPreview = await identityResolution
            .PreviewAsync(tenantId, connectionId, recentRunLimit, ct)
            .ConfigureAwait(false);

        var rows = identityPreview.Rows
            .Select(BuildReadinessRow)
            .OrderByDescending(row => row.Status)
            .ThenBy(row => row.Capability)
            .ThenBy(row => row.StagingRecordId, StringComparer.Ordinal)
            .ToArray();

        return new ProviderIntegrationPromotionReadinessPreviewDto(
            identityPreview.ConnectionId,
            identityPreview.SyncRunIds,
            rows,
            rows.Length,
            rows.Count(row => row.Status == ProviderIntegrationPromotionReadinessStatusDto.ReadyForReconciliation),
            rows.Count(row => row.Status == ProviderIntegrationPromotionReadinessStatusDto.ReviewRequired),
            rows.Count(row => row.Status == ProviderIntegrationPromotionReadinessStatusDto.Blocked));
    }

    private static ProviderIntegrationPromotionReadinessRowDto BuildReadinessRow(
        ProviderIntegrationStagingIdentityResolutionRowDto row)
    {
        var issues = new List<ValidationIssueDto>(row.Issues);
        var status = ProviderIntegrationPromotionReadinessStatusDto.ReadyForReconciliation;

        ApplyAccountGate(row, issues, ref status);
        ApplySecurityGate(row, issues, ref status);
        ApplyExistingIssueGate(row, ref status);

        return new ProviderIntegrationPromotionReadinessRowDto(
            row.StagingRecordId,
            row.SyncRunId,
            row.Capability,
            PromotionTarget,
            status,
            row.ProviderAccountId,
            row.InternalAccountId,
            row.InternalSecurityId,
            row.SecurityDisplayName,
            row.SecurityRoute,
            issues);
    }

    private static void ApplyAccountGate(
        ProviderIntegrationStagingIdentityResolutionRowDto row,
        ICollection<ValidationIssueDto> issues,
        ref ProviderIntegrationPromotionReadinessStatusDto status)
    {
        switch (row.AccountStatus)
        {
            case ProviderIntegrationIdentityResolutionStatusDto.Resolved:
                return;
            case ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired:
                RequireAtLeast(ref status, ProviderIntegrationPromotionReadinessStatusDto.ReviewRequired);
                issues.Add(new ValidationIssueDto(
                    "promotion.account.review-required",
                    ProviderIntegrationIssueSeverityDto.Warning,
                    "Account mapping must be reviewed before reconciliation promotion.",
                    "providerAccountId",
                    "Confirm the internal account match for this provider account."));
                return;
            default:
                RequireAtLeast(ref status, ProviderIntegrationPromotionReadinessStatusDto.Blocked);
                issues.Add(new ValidationIssueDto(
                    "promotion.account.blocked",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Account identity is not ready for reconciliation promotion.",
                    "providerAccountId",
                    "Map provider account id and resolve it to an internal account."));
                return;
        }
    }

    private static void ApplySecurityGate(
        ProviderIntegrationStagingIdentityResolutionRowDto row,
        ICollection<ValidationIssueDto> issues,
        ref ProviderIntegrationPromotionReadinessStatusDto status)
    {
        switch (row.SecurityStatus)
        {
            case ProviderIntegrationIdentityResolutionStatusDto.Resolved:
                return;
            case ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired:
            case ProviderIntegrationIdentityResolutionStatusDto.NotConfigured:
                RequireAtLeast(ref status, ProviderIntegrationPromotionReadinessStatusDto.ReviewRequired);
                issues.Add(new ValidationIssueDto(
                    "promotion.security.review-required",
                    ProviderIntegrationIssueSeverityDto.Warning,
                    "Security matching must be reviewed before reconciliation promotion.",
                    "security",
                    "Resolve or approve the Security Master match for this staged record."));
                return;
            default:
                RequireAtLeast(ref status, ProviderIntegrationPromotionReadinessStatusDto.Blocked);
                issues.Add(new ValidationIssueDto(
                    "promotion.security.blocked",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identity is not ready for reconciliation promotion.",
                    "security",
                    "Map a durable security identifier and resolve it to Security Master."));
                return;
        }
    }

    private static void ApplyExistingIssueGate(
        ProviderIntegrationStagingIdentityResolutionRowDto row,
        ref ProviderIntegrationPromotionReadinessStatusDto status)
    {
        if (row.Issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical))
        {
            RequireAtLeast(ref status, ProviderIntegrationPromotionReadinessStatusDto.Blocked);
            return;
        }

        if (row.Issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Warning))
        {
            RequireAtLeast(ref status, ProviderIntegrationPromotionReadinessStatusDto.ReviewRequired);
        }
    }

    private static void RequireAtLeast(
        ref ProviderIntegrationPromotionReadinessStatusDto status,
        ProviderIntegrationPromotionReadinessStatusDto minimum)
    {
        if (status < minimum)
        {
            status = minimum;
        }
    }
}
