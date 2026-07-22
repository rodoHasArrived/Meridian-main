using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Fail-closed projection policy for legacy report-pack delivery records. Delivery attempts do not
/// carry an independent tenant/access authority, so they are visible only through an exact,
/// immutable workflow or governed-run binding. Credential-bearing links are never returned by a
/// workstation read model.
/// </summary>
public static class ReportingDeliveryReadModelSecurity
{
    public const string RetiredAccessSummary =
        "Legacy delivery access is retired; issue an opaque access grant through canonical secure distribution.";

    private const string RedactedCredentialText = "Legacy credential-bearing content was redacted.";

    private static readonly HashSet<string> CredentialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "access_token",
        "accesstoken",
        "id_token",
        "idtoken",
        "refresh_token",
        "refreshtoken",
        "bearer",
        "authorization",
        "auth",
        "grant",
        "secret",
        "credential",
        "credentials",
        "password",
        "passwd",
        "jwt",
        "signature",
        "sig",
        "key",
        "api_key",
        "apikey",
        "code"
    };

    public static IReadOnlyList<ReportPackDeliveryAttemptDto> FilterVisibleAttempts(
        IReadOnlyList<ReportPackDeliveryAttemptDto> attempts,
        ReportAccessQueryContext? accessContext,
        IReadOnlyList<ReportPackWorkflowRecordDto>? workflowRecords = null,
        IReadOnlyList<ReportingRunSnapshot>? reportingRuns = null)
    {
        ArgumentNullException.ThrowIfNull(attempts);

        if (!HasBoundAuthority(accessContext))
        {
            return [];
        }

        var context = accessContext!;
        var visibleReportIds = (workflowRecords ?? [])
            .Where(record => ReportPackRunReadService.IsWorkflowRecordAccessible(record, context))
            .Select(static record => record.ReportId)
            .ToHashSet();
        var visibleRunIds = (reportingRuns ?? [])
            .Select(static snapshot => snapshot.Manifest)
            .Where(ReportPackRunReadService.HasCertifiedImmutableGovernanceBinding)
            .Where(manifest => ReportAccessPolicyEvaluator.Evaluate(manifest, context).IsAccessible)
            .Select(static manifest => manifest.RunId)
            .ToHashSet(StringComparer.Ordinal);

        return attempts
            .Where(attempt => visibleReportIds.Contains(attempt.ReportId)
                || (!string.IsNullOrWhiteSpace(attempt.Package?.ReportingRunId)
                    && visibleRunIds.Contains(attempt.Package.ReportingRunId.Trim())))
            .Select(SanitizeAttempt)
            .ToArray();
    }

    public static (IReadOnlyList<ReportPackDeliveryAttemptDto> Attempts, bool SourceAvailable)
        ListVisibleAttempts(IServiceProvider services, int limit)
    {
        ArgumentNullException.ThrowIfNull(services);

        var deliveryService = services.GetService<ReportPackDeliveryService>();
        var deliveryStore = services.GetService<IReportPackDeliveryRecordStore>();
        if (deliveryService is null && deliveryStore is null)
        {
            return ([], false);
        }

        var boundedLimit = Math.Clamp(limit, 1, 500);
        var attempts = deliveryService?.ListAttempts(boundedLimit)
            ?? deliveryStore!.Load()
                .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
                .ThenBy(static attempt => attempt.ReportId)
                .ThenBy(static attempt => attempt.DistributionId, StringComparer.OrdinalIgnoreCase)
                .Take(boundedLimit)
                .ToArray();
        var context = ResolveCurrentAccessContext(services);
        var workflowRecords = services.GetService<ReportPackWorkflowService>()?.ListRecords(500) ?? [];
        var reportingRuns = services.GetService<IReportingRunStore>()?.ListRuns(500) ?? [];
        return (FilterVisibleAttempts(attempts, context, workflowRecords, reportingRuns), true);
    }

    public static ReportAccessQueryContext? ResolveCurrentAccessContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext is null
            || !EndpointAuthorization.TryResolveActor(httpContext, out var actor))
        {
            return null;
        }

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
        var companyId = EndpointAuthorization.ResolveCompanyId(httpContext);
        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            return null;
        }

        return new ReportAccessQueryContext(
            ActorPrincipalId: actor,
            GroupPrincipalIds: EndpointAuthorization.ResolveReportGroupPrincipalIds(httpContext),
            CompanyId: companyId,
            HasGlobalOverride: EndpointAuthorization.HasPermission(httpContext, UserPermission.AdminMaintenance),
            TenantId: tenant.TenantId,
            RequireBoundScope: true);
    }

    public static ReportPackDeliveryAttemptDto SanitizeAttempt(ReportPackDeliveryAttemptDto attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return attempt with
        {
            DeliveryReference = SanitizeHref(attempt.DeliveryReference),
            Note = SanitizeOptionalText(attempt.Note),
            FailureReason = SanitizeOptionalText(attempt.FailureReason),
            EvidenceLinks = SanitizeEvidenceLinks(attempt.EvidenceLinks),
            Package = attempt.Package is null ? null : SanitizePackage(attempt.Package)
        };
    }

    public static ReportPackDeliveryPackageDto SanitizePackage(ReportPackDeliveryPackageDto package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var legacyAccessWasUnsafe = IsUnsafeHref(package.SecureLink)
            || IsUnsafeHref(package.PortalRoute)
            || package.AccessLinks?.Any(static link => IsUnsafeHref(link.Href)) == true
            || package.Notifications?.Any(static notification => IsUnsafeHref(notification.Href)) == true;
        return package with
        {
            SecureLink = SanitizeHref(package.SecureLink),
            PortalRoute = SanitizeHref(package.PortalRoute),
            RetainedManifestPath = SanitizeText(package.RetainedManifestPath),
            Artifacts = package.Artifacts
                .Select(static artifact => artifact with
                {
                    RetainedPath = SanitizeText(artifact.RetainedPath),
                    DownloadRoute = SanitizeOptionalHref(artifact.DownloadRoute)
                })
                .ToArray(),
            SourceArtifacts = package.SourceArtifacts?
                .Select(SanitizeText)
                .Where(static artifact => !string.IsNullOrWhiteSpace(artifact))
                .ToArray(),
            PublicationRetainedManifestPath = SanitizeOptionalText(package.PublicationRetainedManifestPath),
            PublicationSignOffReason = SanitizeOptionalText(package.PublicationSignOffReason),
            PublicationSignOffContext = SanitizeOptionalText(package.PublicationSignOffContext),
            PublicationEvidenceLinks = SanitizeEvidenceLinks(package.PublicationEvidenceLinks),
            LineProvenance = package.LineProvenance?
                .Select(static line => line with
                {
                    FinancialRecordHref = SanitizeOptionalHref(line.FinancialRecordHref)
                })
                .ToArray(),
            RestatementChangedLines = package.RestatementChangedLines?
                .Select(static line => line with
                {
                    PreviousValue = SanitizeText(line.PreviousValue),
                    CurrentValue = SanitizeText(line.CurrentValue),
                    EvidenceLinks = SanitizeEvidenceLinks(line.EvidenceLinks)
                })
                .ToArray(),
            RestatementEvidenceLinks = SanitizeEvidenceLinks(package.RestatementEvidenceLinks),
            DeliveryAccessSummary = legacyAccessWasUnsafe || ContainsCredentialMaterial(package.DeliveryAccessSummary)
                ? RetiredAccessSummary
                : SanitizeOptionalText(package.DeliveryAccessSummary),
            DeliveryChannelSummary = SanitizeOptionalText(package.DeliveryChannelSummary),
            DownloadSummary = SanitizeOptionalText(package.DownloadSummary),
            AccessExpiresAtUtc = legacyAccessWasUnsafe ? null : package.AccessExpiresAtUtc,
            AccessLinks = package.AccessLinks?
                .Where(static link => !IsUnsafeHref(link.Href))
                .Select(static link => link with
                {
                    Kind = SanitizeText(link.Kind),
                    Label = SanitizeText(link.Label),
                    Href = SanitizeHref(link.Href),
                    Description = SanitizeOptionalText(link.Description)
                })
                .ToArray(),
            Notifications = package.Notifications?
                .Where(static notification => !IsUnsafeHref(notification.Href))
                .Select(static notification => notification with
                {
                    Channel = SanitizeText(notification.Channel),
                    Recipient = SanitizeText(notification.Recipient),
                    RecipientRole = SanitizeText(notification.RecipientRole),
                    Subject = SanitizeText(notification.Subject),
                    Body = SanitizeText(notification.Body),
                    Href = SanitizeHref(notification.Href),
                    Status = SanitizeText(notification.Status)
                })
                .ToArray(),
            DeliveryEvidencePacket = package.DeliveryEvidencePacket is null
                ? null
                : SanitizeEvidencePacket(package.DeliveryEvidencePacket),
            BrandingTheme = package.BrandingTheme is null
                ? null
                : package.BrandingTheme with
                {
                    LogoUri = SanitizeOptionalHref(package.BrandingTheme.LogoUri),
                    FooterText = SanitizeOptionalText(package.BrandingTheme.FooterText),
                    Disclaimer = SanitizeOptionalText(package.BrandingTheme.Disclaimer)
                }
        };
    }

    public static IReadOnlyList<ReportPackEvidenceLinkDto>? SanitizeEvidenceLinks(
        IReadOnlyList<ReportPackEvidenceLinkDto>? links) =>
        links?
            .Select(static link => link with
            {
                Label = SanitizeText(link.Label),
                Route = SanitizeOptionalHref(link.Route),
                Source = SanitizeText(link.Source)
            })
            .ToArray();

    public static string SanitizeHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return href;
        }

        var candidate = href.Trim();
        if (HasDangerousSchemeOrUserInfo(candidate))
        {
            return string.Empty;
        }

        if (ContainsCredentialMaterial(candidate))
        {
            var delimiter = candidate.IndexOfAny(['?', '#']);
            if (delimiter < 0)
            {
                return string.Empty;
            }

            candidate = candidate[..delimiter];
        }

        return IsRetiredLegacyReportingHref(candidate) ? string.Empty : candidate;
    }

    public static string? SanitizeOptionalHref(string? href) =>
        string.IsNullOrWhiteSpace(href) ? null : NullIfEmpty(SanitizeHref(href));

    public static bool IsSafeRetainedEvidenceHref(string? href) =>
        !string.IsNullOrWhiteSpace(href) && !IsUnsafeHref(href);

    public static bool IsUnsafeHref(string? href) =>
        !string.IsNullOrWhiteSpace(href)
        && (ContainsCredentialMaterial(href)
            || IsRetiredLegacyReportingHref(href)
            || HasDangerousSchemeOrUserInfo(href));

    public static bool ContainsCredentialMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var decoded = Decode(value);
        if (decoded.Contains("bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return decoded
            .Split(['?', '#', '&', ';', '\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ExtractCredentialKey)
            .Any(IsCredentialKey);
    }

    private static bool HasBoundAuthority(ReportAccessQueryContext? context) =>
        context?.RequireBoundScope == true
        && !string.IsNullOrWhiteSpace(context.ActorPrincipalId)
        && !string.IsNullOrWhiteSpace(context.TenantId)
        && !string.IsNullOrWhiteSpace(context.CompanyId);

    private static ReportPackDeliveryEvidencePacketDto SanitizeEvidencePacket(
        ReportPackDeliveryEvidencePacketDto packet) =>
        packet with
        {
            PackageContents = packet.PackageContents.Select(SanitizeText).ToArray(),
            SupportEvidenceIds = packet.SupportEvidenceIds.Select(SanitizeText).ToArray(),
            EntitlementScope = SanitizeText(packet.EntitlementScope),
            ApprovalChain = packet.ApprovalChain
                .Select(static step => step with { Note = SanitizeOptionalText(step.Note) })
                .ToArray(),
            DeliveryEvidence = SanitizeEvidenceLinks(packet.DeliveryEvidence) ?? [],
            RequestHistory = packet.RequestHistory.Select(SanitizeText).ToArray(),
            AmendmentReason = SanitizeOptionalText(packet.AmendmentReason),
            RestatementLineage = SanitizeOptionalText(packet.RestatementLineage),
            AuditEventReferences = packet.AuditEventReferences?.Select(SanitizeText).ToArray(),
            BlockedDownstreamOutputs = packet.BlockedDownstreamOutputs?.Select(SanitizeText).ToArray()
        };

    private static string SanitizeText(string value) =>
        ContainsCredentialMaterial(value) || ContainsRetiredLegacyReportingReference(value)
            ? RedactedCredentialText
            : value;

    private static string? SanitizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : SanitizeText(value);

    private static bool IsCredentialKey(string key)
    {
        if (CredentialKeys.Contains(key))
        {
            return true;
        }

        var compact = key.Replace("-", "_", StringComparison.Ordinal);
        return compact.EndsWith("_token", StringComparison.OrdinalIgnoreCase)
            || compact.EndsWith("_secret", StringComparison.OrdinalIgnoreCase)
            || compact.EndsWith("_signature", StringComparison.OrdinalIgnoreCase)
            || compact.EndsWith("_grant", StringComparison.OrdinalIgnoreCase)
            || compact.EndsWith("_api_key", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCredentialKey(string value)
    {
        var decoded = Decode(value).Trim().TrimStart('?', '#', '&', ';');
        var delimiter = decoded.LastIndexOfAny(['/', '\\', ':']);
        return delimiter >= 0 ? decoded[(delimiter + 1)..] : decoded;
    }

    private static string ExtractCredentialKey(string segment)
    {
        var delimiter = segment.IndexOfAny(['=', ':']);
        return delimiter > 0 ? NormalizeCredentialKey(segment[..delimiter]) : string.Empty;
    }

    private static string Decode(string value)
    {
        var decoded = value.Replace('+', ' ');
        for (var pass = 0; pass < 3; pass++)
        {
            try
            {
                var next = Uri.UnescapeDataString(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                {
                    return decoded;
                }

                decoded = next;
            }
            catch (UriFormatException)
            {
                return decoded;
            }
        }

        return decoded;
    }

    private static bool HasDangerousSchemeOrUserInfo(string href)
    {
        if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            && (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(absolute.UserInfo);
    }

    private static bool IsRetiredLegacyReportingHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var path = href.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            && (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            path = absolute.AbsolutePath;
        }
        else
        {
            var delimiter = path.IndexOfAny(['?', '#']);
            if (delimiter >= 0)
            {
                path = path[..delimiter];
            }
        }

        return path.StartsWith("/portal/reporting/packages/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/reporting/report-packs/", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("/reporting/runs/", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/packages/", StringComparison.OrdinalIgnoreCase))
            || (path.StartsWith("/api/fund-structure/reporting/packs/", StringComparison.OrdinalIgnoreCase)
                && !path.Equals("/api/fund-structure/reporting/packs/history", StringComparison.OrdinalIgnoreCase))
            || (path.StartsWith("/api/fund-structure/report-packs/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/evidence-bundle", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsRetiredLegacyReportingReference(string value) =>
        value.Contains("/portal/reporting/packages/", StringComparison.OrdinalIgnoreCase)
        || value.Contains("/reporting/report-packs/", StringComparison.OrdinalIgnoreCase)
        || value.Contains("/api/fund-structure/reporting/packs/", StringComparison.OrdinalIgnoreCase)
        || value.Contains("/evidence-bundle", StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
