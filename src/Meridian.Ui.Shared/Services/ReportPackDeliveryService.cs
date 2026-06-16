using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Meridian.Storage.Export;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportPackDeliveryStoreOptions(string SnapshotPath);

public sealed record ReportPackDeliveryArtifactContent(
    string ArtifactName,
    string ContentType,
    byte[] Content);

public interface IReportPackDeliveryRecordStore
{
    IReadOnlyList<ReportPackDeliveryAttemptDto> Load();

    void Save(IReadOnlyList<ReportPackDeliveryAttemptDto> attempts);
}

public sealed class FileReportPackDeliveryRecordStore : IReportPackDeliveryRecordStore
{
    private readonly ReportPackDeliveryStoreOptions _options;
    private readonly ILogger<FileReportPackDeliveryRecordStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportPackDeliveryRecordStore(
        ReportPackDeliveryStoreOptions options,
        ILogger<FileReportPackDeliveryRecordStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportPackDeliveryAttemptDto> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_options.SnapshotPath);
                return JsonSerializer.Deserialize<ReportPackDeliverySnapshot>(json, _jsonOptions)?.Attempts ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to load report-pack delivery snapshot from {SnapshotPath}.", _options.SnapshotPath);
                return [];
            }
        }
    }

    public void Save(IReadOnlyList<ReportPackDeliveryAttemptDto> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        lock (_gate)
        {
            var snapshot = new ReportPackDeliverySnapshot(
                attempts
                    .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
                    .ThenBy(static attempt => attempt.ReportId)
                    .ThenBy(static attempt => attempt.DistributionId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private sealed record ReportPackDeliverySnapshot(IReadOnlyList<ReportPackDeliveryAttemptDto> Attempts);
}

public sealed class ReportPackDeliveryService
{
    private static readonly TimeSpan PackageAccessWindow = TimeSpan.FromDays(14);
    private readonly ReportPackWorkflowService _workflowService;
    private readonly IReportPackDeliveryRecordStore? _store;
    private readonly List<ReportPackDeliveryAttemptDto> _attempts;
    private readonly object _gate = new();

    public ReportPackDeliveryService(
        ReportPackWorkflowService workflowService,
        IReportPackDeliveryRecordStore? store = null)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _store = store;
        _attempts = _store?.Load().ToList() ?? [];
    }

    public IReadOnlyList<ReportPackDeliveryAttemptDto> ListAttempts(int limit = 100)
    {
        lock (_gate)
        {
            return _attempts
                .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
                .ThenBy(static attempt => attempt.ReportId)
                .ThenBy(static attempt => attempt.DistributionId, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray();
        }
    }

    public IReadOnlyList<ReportPackDeliveryAttemptDto> GetHistory(Guid reportId, int limit = 100)
    {
        lock (_gate)
        {
            return _attempts
                .Where(attempt => attempt.ReportId == reportId)
                .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
                .ThenBy(static attempt => attempt.DistributionId, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray();
        }
    }

    public ReportPackDeliveryPackageDto GetPackage(Guid reportId, Guid attemptId, string? token)
    {
        var attempt = GetDeliveredAttempt(reportId, attemptId)
            ?? throw new KeyNotFoundException("delivery package not found");
        EnsureValidPackageToken(attempt, token);
        EnsureDeliverablePackage(attempt.Package!);
        return attempt.Package!;
    }

    public ReportPackDeliveryPackageDto GetPortalPackage(string packageId, string? token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ReportPackDeliveryAttemptDto? attempt;
        lock (_gate)
        {
            attempt = _attempts.FirstOrDefault(item =>
                item.Package is not null
                && string.Equals(item.Package.PackageId, packageId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (attempt is null)
        {
            throw new KeyNotFoundException("delivery package not found");
        }

        EnsureValidPackageToken(attempt, token);
        EnsureDeliverablePackage(attempt.Package!);
        return attempt.Package!;
    }

    public ReportPackDeliveryArtifactContent GetArtifact(Guid reportId, Guid attemptId, string artifactName, string? token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        var attempt = GetDeliveredAttempt(reportId, attemptId)
            ?? throw new KeyNotFoundException("delivery package not found");
        EnsureValidPackageToken(attempt, token);
        var package = attempt.Package!;
        EnsureDeliverablePackage(package);
        var artifact = package.Artifacts.FirstOrDefault(item =>
            string.Equals(item.ArtifactName, artifactName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (artifact is null)
        {
            throw new KeyNotFoundException("delivery artifact not found");
        }

        return new ReportPackDeliveryArtifactContent(
            artifact.ArtifactName,
            artifact.ContentType,
            BuildDeliveryArtifactContent(package, artifact));
    }

    public ReportPackDeliveryAttemptDto Deliver(Guid reportId, ReportPackDeliveryRequestDto request, string fallbackActor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DistributionId);
        EnsureHumanOrigin(request.ActionOrigin, "publish reports");
        var actor = ResolveActor(request.Actor, fallbackActor);
        return AppendAttempt(
            reportId,
            request.DistributionId,
            ReportPackDeliveryStateDto.Delivered,
            actor,
            request.DeliveryReference,
            request.Note,
            failureReason: null,
            request.EvidenceLinks,
            request.Formats,
            request.DeliveryMode);
    }

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

    public ReportPackDeliveryAttemptDto? DeliverLatestForTemplate(
        string templateId,
        ReportingScheduleDeliveryTargetDto target,
        string fallbackActor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.DistributionId);

        var normalizedTemplateId = templateId.Trim();
        var record = _workflowService
            .ListRecords(200)
            .Where(static item => item.State is ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated)
            .Where(item => string.Equals(item.TemplateId.Name, normalizedTemplateId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static item => item.Publication?.SignedOffAt ?? item.UpdatedAt)
            .ThenByDescending(static item => item.Version)
            .ThenBy(static item => item.ReportId)
            .FirstOrDefault();
        if (record is null)
        {
            return null;
        }

        return Deliver(
            record.ReportId,
            new ReportPackDeliveryRequestDto(
                target.DistributionId,
                Actor: fallbackActor,
                DeliveryReference: $"schedule:{normalizedTemplateId}:{record.ReportId:N}:{target.DistributionId}",
                Note: NormalizeNullable(target.Note) ?? $"Scheduled delivery for {normalizedTemplateId}.",
                Formats: target.Formats,
                DeliveryMode: target.DeliveryMode),
            fallbackActor);
    }

    public ReportPackDeliveryAttemptDto DeliverReportingRun(
        ReportingOutputManifest manifest,
        ReportingScheduleDeliveryTargetDto target,
        string fallbackActor)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.DistributionId);

        var actor = ResolveActor(null, fallbackActor);
        var policy = ReportPackRunReadService.ResolveDistributionPolicy(target.DistributionId);
        lock (_gate)
        {
            var reportId = BuildReportingRunReportId(manifest.RunId);
            var normalizedDistributionId = policy.DistributionId;
            var attemptNumber = _attempts.Count(attempt =>
                string.Equals(attempt.DeliveryReference, BuildReportingRunDeliveryReference(manifest, normalizedDistributionId), StringComparison.OrdinalIgnoreCase)
                || (attempt.ReportId == reportId
                    && string.Equals(attempt.DistributionId, normalizedDistributionId, StringComparison.OrdinalIgnoreCase))) + 1;
            var attemptId = Guid.NewGuid();
            var reference = BuildReportingRunDeliveryReference(manifest, normalizedDistributionId);
            var package = BuildDeliveryPackage(manifest, policy, reportId, attemptId, attemptNumber, target.Formats, target.DeliveryMode);
            var packageEvidenceLinks = BuildArtifactEvidenceLinks(package.Artifacts, "reporting-run-delivery");
            var attempt = new ReportPackDeliveryAttemptDto(
                attemptId,
                reportId,
                normalizedDistributionId,
                policy.Recipient,
                policy.RecipientRole,
                policy.Channel,
                ReportPackDeliveryStateDto.Delivered,
                DateTimeOffset.UtcNow,
                actor,
                attemptNumber,
                reference,
                NormalizeNullable(target.Note) ?? $"Scheduled reporting-run delivery for {manifest.TemplateId}.",
                FailureReason: null,
                EvidenceLinks: packageEvidenceLinks,
                Package: package);
            _attempts.Add(attempt);
            _store?.Save(_attempts);
            return attempt;
        }
    }

    public ReportPackDeliveryAttemptDto RecordFailure(Guid reportId, ReportPackDeliveryFailureRequestDto request, string fallbackActor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DistributionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FailureReason);
        EnsureHumanOrigin(request.ActionOrigin, "record delivery failures");
        var actor = ResolveActor(request.Actor, fallbackActor);
        return AppendAttempt(
            reportId,
            request.DistributionId,
            ReportPackDeliveryStateDto.Failed,
            actor,
            request.DeliveryReference,
            request.Note,
            request.FailureReason,
            request.EvidenceLinks,
            formats: null,
            deliveryMode: null);
    }

    public static IReadOnlyList<string> ValidateDeliverablePackage(ReportPackDeliveryAttemptDto? attempt)
    {
        if (attempt is null)
        {
            return ["No delivery attempt is available."];
        }

        if (attempt.State != ReportPackDeliveryStateDto.Delivered)
        {
            return [$"Delivery attempt is {attempt.State}; only delivered attempts have retained packages."];
        }

        return attempt.Package is null
            ? ["Delivered attempt does not include a retained package."]
            : ValidateDeliverablePackage(attempt.Package);
    }

    private ReportPackDeliveryAttemptDto AppendAttempt(
        Guid reportId,
        string distributionId,
        ReportPackDeliveryStateDto state,
        string actor,
        string? deliveryReference,
        string? note,
        string? failureReason,
        IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks,
        IReadOnlyList<GovernanceReportArtifactFormatDto>? formats,
        ReportPackDeliveryModeDto? deliveryMode)
    {
        var record = _workflowService.GetRecord(reportId)
            ?? throw new KeyNotFoundException("report pack not found");
        if (record.State is not (ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated))
        {
            throw new InvalidOperationException("Report-pack delivery requires a published or restated workflow record.");
        }

        var policy = ReportPackRunReadService.ResolveDistributionPolicy(distributionId);
        lock (_gate)
        {
            var normalizedDistributionId = policy.DistributionId;
            var attemptNumber = _attempts.Count(attempt =>
                attempt.ReportId == reportId
                && string.Equals(attempt.DistributionId, normalizedDistributionId, StringComparison.OrdinalIgnoreCase)) + 1;
            var attemptId = Guid.NewGuid();
            var reference = string.IsNullOrWhiteSpace(deliveryReference)
                ? $"delivery:{normalizedDistributionId}:{reportId:N}:{attemptNumber}"
                : deliveryReference.Trim();
            var requestEvidenceLinks = NormalizeEvidenceLinks(evidenceLinks);
            var package = state == ReportPackDeliveryStateDto.Delivered
                ? BuildDeliveryPackage(record, policy, attemptId, attemptNumber, formats, deliveryMode, requestEvidenceLinks)
                : null;
            var packageEvidenceLinks = package is null
                ? []
                : BuildArtifactEvidenceLinks(package.Artifacts, "report-pack-delivery");
            var attempt = new ReportPackDeliveryAttemptDto(
                attemptId,
                reportId,
                normalizedDistributionId,
                policy.Recipient,
                policy.RecipientRole,
                policy.Channel,
                state,
                DateTimeOffset.UtcNow,
                actor,
                attemptNumber,
                reference,
                NormalizeNullable(note),
                NormalizeNullable(failureReason),
                NormalizeEvidenceLinks(requestEvidenceLinks.Concat(packageEvidenceLinks).ToArray()),
                package);
            _attempts.Add(attempt);
            _store?.Save(_attempts);
            return attempt;
        }
    }

    private static string ResolveActor(string? requestActor, string fallbackActor)
    {
        var actor = string.IsNullOrWhiteSpace(requestActor) ? fallbackActor : requestActor.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        return actor;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ReportPackDeliveryPackageDto BuildDeliveryPackage(
        ReportPackWorkflowRecordDto record,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        Guid attemptId,
        int attemptNumber,
        IReadOnlyList<GovernanceReportArtifactFormatDto>? requestedFormats,
        ReportPackDeliveryModeDto? requestedMode,
        IReadOnlyList<ReportPackEvidenceLinkDto> requestEvidenceLinks)
    {
        var formats = NormalizeFormats(requestedFormats);
        var mode = requestedMode ?? InferDeliveryMode(policy.Channel);
        var packageId = $"pkg-{record.ReportId:N}-{policy.DistributionId}-{attemptNumber}";
        var retainedManifestPath = $"workstation/reporting/deliveries/{record.ReportId:N}/{policy.DistributionId}/{attemptNumber}/manifest.json";
        var token = BuildSecureToken(record.ReportId, policy.DistributionId, attemptId);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var accessExpiresAtUtc = BuildAccessExpiry(createdAtUtc);
        var artifacts = formats
            .Select(format => BuildArtifact(record, policy, packageId, attemptId, token, format))
            .ToArray();
        var artifactEvidenceLinks = BuildArtifactEvidenceLinks(artifacts, "report-pack-delivery");
        var deliveryEvidencePacket = BuildDeliveryEvidencePacket(
            record,
            policy,
            packageId,
            mode,
            createdAtUtc,
            artifacts,
            requestEvidenceLinks,
            artifactEvidenceLinks,
            record.Publication?.BrandingTheme);
        var secureLink = mode switch
        {
            ReportPackDeliveryModeDto.EmailLink => UiApiRoutes.WithQuery(
                UiApiRoutes.WithParam(
                    UiApiRoutes.WithParam(UiApiRoutes.ReportingPackWorkflowDeliveryPackage, "reportId", record.ReportId.ToString("D")),
                    "attemptId",
                    attemptId.ToString("D")),
                $"token={Uri.EscapeDataString(token)}"),
            ReportPackDeliveryModeDto.SecurePortal => UiApiRoutes.WithQuery(
                UiApiRoutes.WithParam(UiApiRoutes.ReportingPackDeliveryPortalPackage, "packageId", packageId),
                $"token={Uri.EscapeDataString(token)}"),
            ReportPackDeliveryModeDto.EvidenceVault => retainedManifestPath,
            _ => $"/reporting/report-packs/{record.ReportId:D}/packages/{packageId}"
        };
        var portalRoute = $"/reporting/report-packs/{record.ReportId:D}/packages/{packageId}";

        return new ReportPackDeliveryPackageDto(
            packageId,
            record.ReportId,
            policy.DistributionId,
            mode,
            secureLink,
            portalRoute,
            formats,
            artifacts,
            createdAtUtc,
            retainedManifestPath,
            record.Publication?.EvidenceHash,
            BuildIntegritySummary(artifacts, record.Publication?.EvidenceHash),
            PublicationManifestId: record.Publication?.ManifestId,
            PublicationRetainedManifestPath: record.Publication?.RetainedManifestPath,
            PublicationSignedOffBy: record.Publication?.SignedOffBy,
            PublicationSignedOffAtUtc: record.Publication?.SignedOffAt,
            PublicationEvidenceLinks: record.Publication?.EvidenceLinks,
            LineProvenance: record.LineProvenance,
            RestatementReasonCode: record.Restatement?.ReasonCode,
            RestatementPriorVersionReportId: record.Restatement?.PriorVersionReportId,
            RestatementApprover: record.Restatement?.Approver,
            RestatementChangedLines: record.Restatement?.ChangedLines,
            RestatementEvidenceLinks: record.Restatement?.EvidenceLinks,
            DeliveryEvidencePacket: deliveryEvidencePacket,
            BrandingTheme: record.Publication?.BrandingTheme,
            DeliveryAccessSummary: BuildDeliveryAccessSummary(mode, secureLink, portalRoute),
            DeliveryChannelSummary: BuildDeliveryChannelSummary(mode, policy),
            DownloadSummary: BuildDeliveryDownloadSummary(artifacts, retainedManifestPath),
            AccessExpiresAtUtc: accessExpiresAtUtc,
            AccessLinks: BuildDeliveryAccessLinks(mode, secureLink, portalRoute, retainedManifestPath, accessExpiresAtUtc, artifacts),
            Notifications: BuildDeliveryNotifications(mode, policy, packageId, secureLink, portalRoute, createdAtUtc, accessExpiresAtUtc));
    }

    private static ReportPackDeliveryPackageDto BuildDeliveryPackage(
        ReportingOutputManifest manifest,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        Guid reportId,
        Guid attemptId,
        int attemptNumber,
        IReadOnlyList<GovernanceReportArtifactFormatDto>? requestedFormats,
        ReportPackDeliveryModeDto? requestedMode)
    {
        var formats = NormalizeFormats(requestedFormats);
        var mode = requestedMode ?? InferDeliveryMode(policy.Channel);
        var packageId = $"pkg-{reportId:N}-{policy.DistributionId}-{attemptNumber}";
        var retainedManifestPath = $"workstation/reporting/runs/{Uri.EscapeDataString(manifest.RunId)}/deliveries/{policy.DistributionId}/{attemptNumber}/manifest.json";
        var token = BuildSecureToken(reportId, policy.DistributionId, attemptId);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var accessExpiresAtUtc = BuildAccessExpiry(createdAtUtc);
        var artifacts = formats
            .Select(format => BuildArtifact(manifest, policy, reportId, packageId, attemptId, token, format))
            .ToArray();
        var artifactEvidenceLinks = BuildArtifactEvidenceLinks(artifacts, "reporting-run-delivery");
        var deliveryEvidencePacket = BuildReportingRunDeliveryEvidencePacket(
            manifest,
            policy,
            reportId,
            packageId,
            mode,
            createdAtUtc,
            artifacts,
            artifactEvidenceLinks);
        var secureLink = mode switch
        {
            ReportPackDeliveryModeDto.EmailLink => UiApiRoutes.WithQuery(
                UiApiRoutes.WithParam(
                    UiApiRoutes.WithParam(UiApiRoutes.ReportingPackWorkflowDeliveryPackage, "reportId", reportId.ToString("D")),
                    "attemptId",
                    attemptId.ToString("D")),
                $"token={Uri.EscapeDataString(token)}"),
            ReportPackDeliveryModeDto.SecurePortal => UiApiRoutes.WithQuery(
                UiApiRoutes.WithParam(UiApiRoutes.ReportingPackDeliveryPortalPackage, "packageId", packageId),
                $"token={Uri.EscapeDataString(token)}"),
            ReportPackDeliveryModeDto.EvidenceVault => retainedManifestPath,
            _ => $"/reporting/runs/{Uri.EscapeDataString(manifest.RunId)}/packages/{packageId}"
        };
        var portalRoute = $"/reporting/runs/{Uri.EscapeDataString(manifest.RunId)}/packages/{packageId}";

        return new ReportPackDeliveryPackageDto(
            packageId,
            reportId,
            policy.DistributionId,
            mode,
            secureLink,
            portalRoute,
            formats,
            artifacts,
            createdAtUtc,
            retainedManifestPath,
            PublicationEvidenceHash: null,
            BuildIntegritySummary(artifacts, publicationEvidenceHash: null),
            manifest.RunId,
            manifest.TemplateId,
            manifest.ScheduleId,
            manifest.Artifacts.ToArray(),
            DeliveryEvidencePacket: deliveryEvidencePacket,
            ReportingRunAsOfDate: manifest.AsOfDate.ToString("yyyy-MM-dd"),
            ReportingRunStatus: manifest.Status.ToString(),
            ReportingRunTrigger: manifest.Trigger.ToString(),
            ReportingRunAttemptCount: manifest.AttemptCount,
            ReportingRunSectionCount: manifest.Sections.Length,
            ReportingRunLineageLinkedSections: manifest.Sections.Count(static section => section.Lineage is not null),
            BrandingTheme: manifest.BrandingTheme,
            DeliveryAccessSummary: BuildDeliveryAccessSummary(mode, secureLink, portalRoute),
            DeliveryChannelSummary: BuildDeliveryChannelSummary(mode, policy),
            DownloadSummary: BuildDeliveryDownloadSummary(artifacts, retainedManifestPath),
            AccessExpiresAtUtc: accessExpiresAtUtc,
            AccessLinks: BuildDeliveryAccessLinks(mode, secureLink, portalRoute, retainedManifestPath, accessExpiresAtUtc, artifacts),
            GeneratedReportWriterGrids: ProjectGeneratedReportWriterGrids(manifest.ReportWriterGrids, manifest.RenderedReportWriterGrids),
            RenderedReportWriterGrids: manifest.RenderedReportWriterGrids.IsDefaultOrEmpty
                ? []
                : manifest.RenderedReportWriterGrids
                    .Select(EnrichReportWriterGridRender)
                    .ToArray(),
            Notifications: BuildDeliveryNotifications(mode, policy, packageId, secureLink, portalRoute, createdAtUtc, accessExpiresAtUtc),
            ReportWriterDatasetSourceId: manifest.ReportWriterDatasetSourceId,
            ReportWriterDatasetSourceLabel: manifest.ReportWriterDatasetSourceLabel,
            ReportWriterDatasetRowCount: manifest.ReportWriterDatasetRowCount);
    }

    private static DateTimeOffset BuildAccessExpiry(DateTimeOffset createdAtUtc) =>
        createdAtUtc.Add(PackageAccessWindow);

    private static string BuildDeliveryAccessSummary(
        ReportPackDeliveryModeDto deliveryMode,
        string secureLink,
        string portalRoute) =>
        deliveryMode switch
        {
            ReportPackDeliveryModeDto.EmailLink => $"Email-link package is available through the token-gated route {secureLink}.",
            ReportPackDeliveryModeDto.SecurePortal => $"Secure-portal package is available through the token-gated portal route {secureLink}.",
            ReportPackDeliveryModeDto.EvidenceVault => $"Evidence-vault package is retained at {secureLink}.",
            _ => $"Internal-route package is available at {portalRoute}."
        };

    private static string BuildDeliveryChannelSummary(
        ReportPackDeliveryModeDto deliveryMode,
        ReportPackRunReadService.ReportPackDistributionPolicy policy) =>
        $"{deliveryMode} delivery to {policy.Recipient} via {policy.Channel}.";

    private static string BuildDeliveryDownloadSummary(
        IReadOnlyCollection<ReportPackDeliveryArtifactDto> artifacts,
        string retainedManifestPath)
    {
        var formats = artifacts
            .Select(static artifact => artifact.Format.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static format => format, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var formatSummary = formats.Length == 0 ? "no retained artifacts" : string.Join("/", formats);
        return $"{artifacts.Count} artifact(s) retained as {formatSummary}; manifest {retainedManifestPath}.";
    }

    private static IReadOnlyList<ReportPackDeliveryAccessLinkDto> BuildDeliveryAccessLinks(
        ReportPackDeliveryModeDto deliveryMode,
        string secureLink,
        string portalRoute,
        string retainedManifestPath,
        DateTimeOffset accessExpiresAtUtc,
        IReadOnlyList<ReportPackDeliveryArtifactDto> artifacts)
    {
        var links = new List<ReportPackDeliveryAccessLinkDto>
        {
            new(
                deliveryMode switch
                {
                    ReportPackDeliveryModeDto.EmailLink => "email-link",
                    ReportPackDeliveryModeDto.SecurePortal => "secure-portal",
                    ReportPackDeliveryModeDto.EvidenceVault => "evidence-vault",
                    _ => "internal-route"
                },
                deliveryMode switch
                {
                    ReportPackDeliveryModeDto.EmailLink => "Email link package",
                    ReportPackDeliveryModeDto.SecurePortal => "Secure portal package",
                    ReportPackDeliveryModeDto.EvidenceVault => "Evidence vault manifest",
                    _ => "Internal package route"
                },
                string.IsNullOrWhiteSpace(secureLink) ? portalRoute : secureLink,
                RequiresToken: deliveryMode is ReportPackDeliveryModeDto.EmailLink or ReportPackDeliveryModeDto.SecurePortal,
                ExpiresAtUtc: deliveryMode is ReportPackDeliveryModeDto.EmailLink or ReportPackDeliveryModeDto.SecurePortal
                    ? accessExpiresAtUtc
                    : null,
                Description: BuildDeliveryAccessSummary(deliveryMode, secureLink, portalRoute)),
            new(
                "operator-route",
                "Operator package route",
                portalRoute,
                RequiresToken: false,
                Description: "Internal operator route for inspecting the retained package metadata."),
            new(
                "manifest",
                "Retained manifest",
                retainedManifestPath,
                RequiresToken: false,
                Description: "Retained manifest path for audit and evidence review.")
        };

        foreach (var artifact in artifacts.Where(static item => !string.IsNullOrWhiteSpace(item.DownloadRoute)))
        {
            links.Add(new ReportPackDeliveryAccessLinkDto(
                $"artifact-{artifact.Format.ToString().ToLowerInvariant()}",
                $"{artifact.Format} download",
                artifact.DownloadRoute!,
                RequiresToken: true,
                ExpiresAtUtc: accessExpiresAtUtc,
                Description: $"{artifact.ArtifactName} retained as {artifact.ContentType} with SHA-256 checksum {artifact.ChecksumSha256}."));

            if (artifact.Format == GovernanceReportArtifactFormatDto.Xlsx)
            {
                links.Add(new ReportPackDeliveryAccessLinkDto(
                    "artifact-xls",
                    "XLS compatibility download",
                    BuildArtifactFormatAliasRoute(artifact.DownloadRoute!, "xls"),
                    RequiresToken: true,
                    ExpiresAtUtc: accessExpiresAtUtc,
                    Description: $"{artifact.ArtifactName} served through the XLS compatibility alias as the canonical XLSX workbook."));
            }
        }

        return links;
    }

    private static IReadOnlyList<ReportPackDeliveryNotificationDto> BuildDeliveryNotifications(
        ReportPackDeliveryModeDto deliveryMode,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        string packageId,
        string secureLink,
        string portalRoute,
        DateTimeOffset createdAtUtc,
        DateTimeOffset accessExpiresAtUtc)
    {
        var href = string.IsNullOrWhiteSpace(secureLink) ? portalRoute : secureLink;
        return deliveryMode switch
        {
            ReportPackDeliveryModeDto.EmailLink =>
            [
                new ReportPackDeliveryNotificationDto(
                    $"delivery-notification:{packageId}:email-link",
                    "EmailLink",
                    policy.Recipient,
                    policy.RecipientRole,
                    deliveryMode,
                    $"Report package available for {policy.Recipient}",
                    $"A token-gated report package is ready for {policy.Recipient} via {policy.Channel}. Access expires at {accessExpiresAtUtc:O}.",
                    href,
                    RequiresToken: true,
                    createdAtUtc,
                    ExpiresAtUtc: accessExpiresAtUtc,
                    Status: "ReadyToSend")
            ],
            ReportPackDeliveryModeDto.SecurePortal =>
            [
                new ReportPackDeliveryNotificationDto(
                    $"delivery-notification:{packageId}:secure-portal",
                    "SecurePortal",
                    policy.Recipient,
                    policy.RecipientRole,
                    deliveryMode,
                    $"Secure portal package published for {policy.Recipient}",
                    $"A secure portal package is published for {policy.Recipient} via {policy.Channel}. Access expires at {accessExpiresAtUtc:O}.",
                    href,
                    RequiresToken: true,
                    createdAtUtc,
                    ExpiresAtUtc: accessExpiresAtUtc,
                    Status: "PortalPublished")
            ],
            ReportPackDeliveryModeDto.EvidenceVault =>
            [
                new ReportPackDeliveryNotificationDto(
                    $"delivery-notification:{packageId}:evidence-vault",
                    "EvidenceVault",
                    policy.Recipient,
                    policy.RecipientRole,
                    deliveryMode,
                    $"Evidence vault package retained for {policy.Recipient}",
                    $"The retained report package manifest is available for {policy.Recipient} via {policy.Channel}.",
                    href,
                    RequiresToken: false,
                    createdAtUtc,
                    Status: "Retained")
            ],
            _ =>
            [
                new ReportPackDeliveryNotificationDto(
                    $"delivery-notification:{packageId}:internal-route",
                    "InternalRoute",
                    policy.Recipient,
                    policy.RecipientRole,
                    deliveryMode,
                    $"Internal report package route available for {policy.Recipient}",
                    $"The internal report package route is available for {policy.Recipient} via {policy.Channel}.",
                    href,
                    RequiresToken: false,
                    createdAtUtc,
                    Status: "Internal")
            ]
        };
    }

    private static string BuildArtifactFormatAliasRoute(string downloadRoute, string format)
    {
        var separator = downloadRoute.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{downloadRoute}{separator}format={Uri.EscapeDataString(format)}";
    }

    private static ReportPackDeliveryEvidencePacketDto BuildReportingRunDeliveryEvidencePacket(
        ReportingOutputManifest manifest,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        Guid reportId,
        string packageId,
        ReportPackDeliveryModeDto deliveryMode,
        DateTimeOffset deliveredAtUtc,
        IReadOnlyList<ReportPackDeliveryArtifactDto> artifacts,
        IReadOnlyList<ReportPackEvidenceLinkDto> artifactEvidenceLinks)
    {
        var sourceArtifacts = DistinctValues(manifest.Artifacts);
        var packageContents = DistinctValues(
            artifacts.Select(static artifact => artifact.ArtifactName)
                .Concat(sourceArtifacts.Select(static artifact => $"source-artifact:{artifact}")));
        var supportEvidenceIds = DistinctValues(
            artifactEvidenceLinks.Select(static link => link.EvidenceId)
                .Concat(sourceArtifacts.Select(static artifact => $"reporting-run-source:{artifact}")));

        return new ReportPackDeliveryEvidencePacketDto(
            PacketId: $"reporting-run-delivery:{packageId}",
            PacketKind: "ReportingRunDelivery",
            PackageId: packageId,
            ReportId: reportId,
            FundProfileId: "reporting-run",
            FundAccountId: manifest.TemplateId,
            Period: manifest.AsOfDate.ToString("yyyy-MM-dd"),
            PackageContents: packageContents,
            SupportEvidenceIds: supportEvidenceIds,
            RecipientList:
            [
                new ReportPackDeliveryRecipientDto(
                    policy.DistributionId,
                    policy.Recipient,
                    policy.RecipientRole,
                    policy.Channel)
            ],
            EntitlementScope: BuildEntitlementScope(manifest.AccessPolicy),
            ApprovalChain: [],
            DatasetVersion: manifest.RunId,
            TemplateVersion: manifest.TemplateId,
            DeliveryChannel: $"{deliveryMode} via {policy.Channel}",
            DeliveredAtUtc: deliveredAtUtc,
            DeliveryEvidence: artifactEvidenceLinks,
            RequestHistory:
            [
                $"reporting-run:{manifest.RunId}:{manifest.Trigger}:{manifest.Status}",
                $"schedule:{manifest.ScheduleId ?? "adhoc"}",
                $"delivery-request:{policy.DistributionId}"
            ],
            AuditEventReferences:
            [
                $"{manifest.RunId}:{manifest.AttemptCount}:RunGenerated"
            ],
            BlockedDownstreamOutputs: []);
    }

    private static ReportPackDeliveryEvidencePacketDto BuildDeliveryEvidencePacket(
        ReportPackWorkflowRecordDto record,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        string packageId,
        ReportPackDeliveryModeDto deliveryMode,
        DateTimeOffset deliveredAtUtc,
        IReadOnlyList<ReportPackDeliveryArtifactDto> artifacts,
        IReadOnlyList<ReportPackEvidenceLinkDto> requestEvidenceLinks,
        IReadOnlyList<ReportPackEvidenceLinkDto> artifactEvidenceLinks,
        ReportBrandingThemeDto? brandingTheme)
    {
        var lineProvenance = record.LineProvenance ?? [];
        var publicationEvidenceLinks = record.Publication?.EvidenceLinks ?? [];
        var restatementEvidenceLinks = record.Restatement?.EvidenceLinks ?? [];
        var restatementContents = record.Restatement?.ChangedLines
            .Select(static line => $"restatement-line:{line.LineKey}")
            .ToArray() ?? [];
        var deliveryEvidence = NormalizeEvidenceLinks(requestEvidenceLinks.Concat(artifactEvidenceLinks).ToArray());
        var packageContents = DistinctValues(
            artifacts.Select(static artifact => artifact.ArtifactName)
                .Concat(lineProvenance.Select(static line => $"report-line:{line.LineKey}"))
                .Concat(restatementContents)
                .Append(string.IsNullOrWhiteSpace(brandingTheme?.ThemeId) ? null : $"branding-theme:{brandingTheme.ThemeId.Trim()}"));
        var supportEvidenceIds = DistinctValues(
            publicationEvidenceLinks.Select(static link => link.EvidenceId)
                .Concat(lineProvenance.Select(static line => line.EvidenceId))
                .Concat(restatementEvidenceLinks.Select(static link => link.EvidenceId))
                .Concat(deliveryEvidence.Select(static link => link.EvidenceId)));
        var approvalChain = record.AuditTrail
            .Select(static item => new ReportPackDeliveryApprovalStepDto(
                item.At,
                item.Actor,
                item.Action,
                item.FromState,
                item.ToState,
                item.Note))
            .ToArray();

        return new ReportPackDeliveryEvidencePacketDto(
            PacketId: $"stakeholder-delivery:{packageId}",
            PacketKind: "StakeholderDeliveryRestatement",
            PackageId: packageId,
            ReportId: record.ReportId,
            FundProfileId: record.FundProfileId,
            FundAccountId: record.FundAccountId,
            Period: record.Period,
            PackageContents: packageContents,
            SupportEvidenceIds: supportEvidenceIds,
            RecipientList:
            [
                new ReportPackDeliveryRecipientDto(
                    policy.DistributionId,
                    policy.Recipient,
                    policy.RecipientRole,
                    policy.Channel)
            ],
            EntitlementScope: BuildEntitlementScope(record.AccessPolicy),
            ApprovalChain: approvalChain,
            DatasetVersion: record.Publication?.ManifestId ?? $"report-pack:{record.ReportId:N}:v{record.Version}",
            TemplateVersion: $"{record.TemplateId.Name}@v{record.TemplateId.Version}",
            DeliveryChannel: $"{deliveryMode} via {policy.Channel}",
            DeliveredAtUtc: deliveredAtUtc,
            DeliveryEvidence: deliveryEvidence,
            RequestHistory: BuildRequestHistory(record, policy.DistributionId),
            AmendmentReason: record.Restatement?.ReasonCode,
            RestatementLineage: BuildRestatementLineage(record.Restatement),
            AuditEventReferences: BuildAuditEventReferences(record),
            BlockedDownstreamOutputs: []);
    }

    private static IReadOnlyList<ReportPackEvidenceLinkDto> BuildArtifactEvidenceLinks(
        IReadOnlyList<ReportPackDeliveryArtifactDto> artifacts,
        string source) =>
        artifacts
            .Select(artifact => new ReportPackEvidenceLinkDto(
                artifact.EvidenceId,
                artifact.ArtifactName,
                artifact.RetainedPath,
                source))
            .ToArray();

    private static IReadOnlyList<string> DistinctValues(IEnumerable<string?> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildEntitlementScope(ReportAccessPolicyDto? accessPolicy)
    {
        if (accessPolicy is null)
        {
            return ReportAccessModeDto.CompanyWide.ToString();
        }

        var principalScope = accessPolicy.Principals is { Count: > 0 }
            ? string.Join(
                ",",
                accessPolicy.Principals
                    .Where(static principal => !string.IsNullOrWhiteSpace(principal.PrincipalId))
                    .Select(static principal => $"{principal.Kind}:{principal.PrincipalId.Trim()}"))
            : "none";

        return accessPolicy.Mode switch
        {
            ReportAccessModeDto.Private =>
                $"Private owner={accessPolicy.OwnerPrincipalId ?? "unspecified"} allowOwnerAccess={accessPolicy.AllowOwnerAccess}",
            ReportAccessModeDto.Restricted => $"Restricted principals={principalScope}",
            ReportAccessModeDto.CompanyWide when !string.IsNullOrWhiteSpace(accessPolicy.CompanyId) =>
                $"CompanyWide company={accessPolicy.CompanyId.Trim()}",
            ReportAccessModeDto.CompanyWide => ReportAccessModeDto.CompanyWide.ToString(),
            _ => accessPolicy.Mode.ToString()
        };
    }

    private static IReadOnlyList<string> BuildRequestHistory(
        ReportPackWorkflowRecordDto record,
        string distributionId)
    {
        var history = record.AuditTrail
            .Select(static item => $"{item.At:O}|{item.Actor}|{item.Action}|{item.FromState}->{item.ToState}");

        if (record.Restatement is null)
        {
            return history
                .Append($"delivery-request:{distributionId}")
                .ToArray();
        }

        return history
            .Append($"restatement:{record.Restatement.ReasonCode}:{record.Restatement.ChangedLines.Count} changed line(s)")
            .Append($"delivery-request:{distributionId}")
            .ToArray();
    }

    private static string? BuildRestatementLineage(ReportPackRestatementMetadataDto? restatement)
    {
        if (restatement is null)
        {
            return null;
        }

        return $"{restatement.PriorVersionReportId:D};reason={restatement.ReasonCode};approver={restatement.Approver};changedLines={restatement.ChangedLines.Count}";
    }

    private static IReadOnlyList<string> BuildAuditEventReferences(ReportPackWorkflowRecordDto record) =>
        record.AuditTrail
            .Select((item, index) => $"{record.ReportId:N}:{index + 1}:{item.Action}:{item.At.UtcDateTime:yyyyMMddHHmmssfff}")
            .ToArray();

    private static ReportPackDeliveryArtifactDto BuildArtifact(
        ReportPackWorkflowRecordDto record,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        string packageId,
        Guid attemptId,
        string token,
        GovernanceReportArtifactFormatDto format)
    {
        var extension = ResolveExtension(format);
        var artifactName = $"{record.TemplateId.Name}-v{record.TemplateId.Version}-{record.Period}-{policy.DistributionId}.{extension}";
        var retainedPath = $"workstation/reporting/deliveries/{record.ReportId:N}/{policy.DistributionId}/{packageId}/{artifactName}";
        var evidenceId = $"delivery-artifact:{record.ReportId:N}:{policy.DistributionId}:{format.ToString().ToLowerInvariant()}";
        var versionStamp = $"delivery-artifact:{record.ReportId:N}:{policy.DistributionId}:{record.Version}:{format.ToString().ToLowerInvariant()}";
        var contentType = ResolveContentType(format);
        var downloadRoute = BuildArtifactDownloadRoute(record.ReportId, attemptId, artifactName, token);
        var content = BuildDeliveryArtifactContent(
            packageId,
            record.ReportId,
            policy.DistributionId,
            artifactName,
            format,
            contentType,
            retainedPath,
            record.Publication?.EvidenceHash,
            record.Publication?.ManifestId,
            record.Publication?.RetainedManifestPath,
            record.Publication?.SignedOffBy,
            record.Publication?.SignedOffAt,
            record.Publication?.EvidenceLinks ?? [],
            record.LineProvenance ?? [],
            record.Restatement?.ReasonCode,
            record.Restatement?.PriorVersionReportId,
            record.Restatement?.Approver,
            record.Restatement?.ChangedLines ?? [],
            record.Restatement?.EvidenceLinks ?? [],
            record.Publication?.BrandingTheme,
            versionStamp);
        var byteSize = content.LongLength;
        var checksum = ComputeSha256Hex(content);
        return new ReportPackDeliveryArtifactDto(
            format,
            artifactName,
            contentType,
            retainedPath,
            byteSize,
            evidenceId,
            checksum,
            versionStamp,
            downloadRoute);
    }

    private static ReportPackDeliveryArtifactDto BuildArtifact(
        ReportingOutputManifest manifest,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        Guid reportId,
        string packageId,
        Guid attemptId,
        string token,
        GovernanceReportArtifactFormatDto format)
    {
        var extension = ResolveExtension(format);
        var safeRunId = SanitizeArtifactName(manifest.RunId);
        var artifactName = $"{safeRunId}-{policy.DistributionId}.{extension}";
        var retainedPath = $"workstation/reporting/runs/{Uri.EscapeDataString(manifest.RunId)}/deliveries/{policy.DistributionId}/{packageId}/{artifactName}";
        var evidenceId = $"reporting-run-delivery:{safeRunId}:{policy.DistributionId}:{format.ToString().ToLowerInvariant()}";
        var versionStamp = $"reporting-run-delivery:{safeRunId}:{policy.DistributionId}:{manifest.AttemptCount}:{format.ToString().ToLowerInvariant()}";
        var contentType = ResolveContentType(format);
        var downloadRoute = BuildArtifactDownloadRoute(reportId, attemptId, artifactName, token);
        var content = BuildReportingRunDeliveryArtifactContent(
            packageId,
            manifest,
            policy.DistributionId,
            artifactName,
            format,
            contentType,
            retainedPath,
            versionStamp);
        var byteSize = content.LongLength;
        var checksum = ComputeSha256Hex(content);
        return new ReportPackDeliveryArtifactDto(
            format,
            artifactName,
            contentType,
            retainedPath,
            byteSize,
            evidenceId,
            checksum,
            versionStamp,
            downloadRoute);
    }

    private static string BuildIntegritySummary(
        IReadOnlyList<ReportPackDeliveryArtifactDto> artifacts,
        string? publicationEvidenceHash)
    {
        var hashSummary = string.IsNullOrWhiteSpace(publicationEvidenceHash)
            ? "without a publication evidence hash"
            : $"against publication evidence hash {publicationEvidenceHash.Trim()}";
        return $"{artifacts.Count} artifact(s) retained with SHA-256 checksums {hashSummary}.";
    }

    private static void EnsureDeliverablePackage(ReportPackDeliveryPackageDto package)
    {
        var issues = ValidateDeliverablePackage(package);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException($"Report-pack delivery package is incomplete: {string.Join(" ", issues)}");
        }
    }

    private static IReadOnlyList<string> ValidateDeliverablePackage(ReportPackDeliveryPackageDto package)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(package.PackageId))
        {
            issues.Add("Package id is required.");
        }

        if (string.IsNullOrWhiteSpace(package.DistributionId))
        {
            issues.Add("Distribution id is required.");
        }

        if (string.IsNullOrWhiteSpace(package.SecureLink))
        {
            issues.Add("Secure link or retained vault route is required.");
        }

        if (string.IsNullOrWhiteSpace(package.PortalRoute))
        {
            issues.Add("Portal route is required.");
        }

        if (string.IsNullOrWhiteSpace(package.RetainedManifestPath))
        {
            issues.Add("Retained manifest path is required.");
        }

        if (string.IsNullOrWhiteSpace(package.IntegritySummary))
        {
            issues.Add("Integrity summary is required.");
        }

        if (package.DeliveryMode is ReportPackDeliveryModeDto.EmailLink or ReportPackDeliveryModeDto.SecurePortal)
        {
            if (package.AccessExpiresAtUtc is null)
            {
                issues.Add($"{package.DeliveryMode} packages require an access expiration.");
            }
            else if (package.AccessExpiresAtUtc <= package.CreatedAtUtc)
            {
                issues.Add("Access expiration must be after package creation.");
            }
        }

        var formats = package.Formats?.Distinct().ToArray() ?? [];
        if (formats.Length == 0)
        {
            issues.Add("At least one delivery format is required.");
        }

        var artifacts = package.Artifacts ?? [];
        if (artifacts.Count == 0)
        {
            issues.Add("At least one retained delivery artifact is required.");
        }

        var artifactFormats = artifacts.Select(static artifact => artifact.Format).ToHashSet();
        var missingFormats = formats.Where(format => !artifactFormats.Contains(format)).OrderBy(static format => format).ToArray();
        if (missingFormats.Length > 0)
        {
            issues.Add($"Retained artifacts are missing requested format(s): {string.Join("/", missingFormats.Select(static format => format.ToString()))}.");
        }

        foreach (var artifact in artifacts)
        {
            var label = string.IsNullOrWhiteSpace(artifact.ArtifactName) ? artifact.Format.ToString() : artifact.ArtifactName.Trim();
            if (string.IsNullOrWhiteSpace(artifact.ArtifactName))
            {
                issues.Add($"Artifact {label} requires a name.");
            }

            if (string.IsNullOrWhiteSpace(artifact.ContentType))
            {
                issues.Add($"Artifact {label} requires a content type.");
            }

            if (string.IsNullOrWhiteSpace(artifact.RetainedPath))
            {
                issues.Add($"Artifact {label} requires a retained path.");
            }

            if (artifact.ByteSize <= 0)
            {
                issues.Add($"Artifact {label} requires a positive byte size.");
            }

            if (string.IsNullOrWhiteSpace(artifact.EvidenceId))
            {
                issues.Add($"Artifact {label} requires an evidence id.");
            }

            if (string.IsNullOrWhiteSpace(artifact.ChecksumSha256))
            {
                issues.Add($"Artifact {label} requires a SHA-256 checksum.");
            }

            if (string.IsNullOrWhiteSpace(artifact.VersionStamp))
            {
                issues.Add($"Artifact {label} requires a version stamp.");
            }
        }

        var packet = package.DeliveryEvidencePacket;
        if (packet is null)
        {
            issues.Add("Delivery evidence packet is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(packet.PacketId))
            {
                issues.Add("Delivery evidence packet id is required.");
            }

            if (!string.Equals(packet.PackageId, package.PackageId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("Delivery evidence packet package id must match the package.");
            }

            if ((packet.PackageContents?.Count ?? 0) == 0)
            {
                issues.Add("Delivery evidence packet requires package contents.");
            }

            if ((packet.SupportEvidenceIds?.Count ?? 0) == 0)
            {
                issues.Add("Delivery evidence packet requires support evidence ids.");
            }

            if ((packet.RecipientList?.Count ?? 0) == 0)
            {
                issues.Add("Delivery evidence packet requires a recipient list.");
            }

            if ((packet.DeliveryEvidence?.Count ?? 0) == 0)
            {
                issues.Add("Delivery evidence packet requires retained artifact evidence links.");
            }
        }

        return issues
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildArtifactDownloadRoute(
        Guid reportId,
        Guid attemptId,
        string artifactName,
        string token)
    {
        var route = UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(
                UiApiRoutes.WithParam(
                    UiApiRoutes.ReportingPackWorkflowDeliveryArtifact,
                    "reportId",
                    reportId.ToString("D")),
                "attemptId",
                attemptId.ToString("D")),
            "artifactName",
            artifactName);
        return UiApiRoutes.WithQuery(route, $"token={Uri.EscapeDataString(token)}");
    }

    private static byte[] BuildDeliveryArtifactContent(
        ReportPackDeliveryPackageDto package,
        ReportPackDeliveryArtifactDto artifact) =>
        string.IsNullOrWhiteSpace(package.ReportingRunId)
            ? BuildDeliveryArtifactContent(
                package.PackageId,
                package.ReportId,
                package.DistributionId,
                artifact.ArtifactName,
                artifact.Format,
                artifact.ContentType,
                artifact.RetainedPath,
                package.PublicationEvidenceHash,
                package.PublicationManifestId,
                package.PublicationRetainedManifestPath,
                package.PublicationSignedOffBy,
                package.PublicationSignedOffAtUtc,
                package.PublicationEvidenceLinks ?? [],
                package.LineProvenance ?? [],
                package.RestatementReasonCode,
                package.RestatementPriorVersionReportId,
                package.RestatementApprover,
                package.RestatementChangedLines ?? [],
                package.RestatementEvidenceLinks ?? [],
                package.BrandingTheme,
                artifact.VersionStamp)
            : BuildReportingRunDeliveryArtifactContent(
                package.PackageId,
                package.ReportingRunId!,
                package.ReportingTemplateId ?? string.Empty,
                package.ReportingScheduleId,
                package.SourceArtifacts ?? [],
                package.ReportingRunAsOfDate,
                package.ReportingRunStatus,
                package.ReportingRunTrigger,
                package.ReportingRunAttemptCount,
                package.ReportingRunSectionCount,
                package.ReportingRunLineageLinkedSections,
                package.GeneratedReportWriterGrids ?? [],
                package.RenderedReportWriterGrids ?? [],
                package.DistributionId,
                artifact.ArtifactName,
                artifact.Format,
                artifact.ContentType,
                artifact.RetainedPath,
                package.BrandingTheme,
                artifact.VersionStamp);

    private static byte[] BuildDeliveryArtifactContent(
        string packageId,
        Guid reportId,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        string? publicationEvidenceHash,
        string? publicationManifestId,
        string? publicationRetainedManifestPath,
        string? publicationSignedOffBy,
        DateTimeOffset? publicationSignedOffAtUtc,
        IReadOnlyList<ReportPackEvidenceLinkDto> publicationEvidenceLinks,
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        string? restatementReasonCode,
        Guid? restatementPriorVersionReportId,
        string? restatementApprover,
        IReadOnlyList<ReportPackChangedLineDto> restatementChangedLines,
        IReadOnlyList<ReportPackEvidenceLinkDto> restatementEvidenceLinks,
        ReportBrandingThemeDto? brandingTheme,
        string versionStamp)
    {
        var rows = BuildDeliveryArtifactRows(
            packageId,
            reportId,
            distributionId,
            artifactName,
            format,
            contentType,
            retainedPath,
            publicationEvidenceHash,
            publicationManifestId,
            publicationRetainedManifestPath,
            publicationSignedOffBy,
            publicationSignedOffAtUtc,
            publicationEvidenceLinks,
            lineProvenance,
            restatementReasonCode,
            restatementPriorVersionReportId,
            restatementApprover,
            restatementChangedLines,
            restatementEvidenceLinks,
            brandingTheme,
            versionStamp);

        return format switch
        {
            GovernanceReportArtifactFormatDto.Csv => BuildDeliveryArtifactCsv(rows),
            GovernanceReportArtifactFormatDto.Xlsx => XlsxWorkbookWriter.CreateWorkbook(BuildDeliveryArtifactWorksheets(rows, brandingTheme)),
            GovernanceReportArtifactFormatDto.Html => BuildDeliveryArtifactHtml(rows, brandingTheme),
            GovernanceReportArtifactFormatDto.Pdf => BuildDeliveryArtifactPdf(rows, brandingTheme),
            _ => JsonSerializer.SerializeToUtf8Bytes(rows.ToDictionary(static row => row.Key, static row => row.Value))
        };
    }

    private static IReadOnlyList<XlsxWorksheet> BuildDeliveryArtifactWorksheets(
        IReadOnlyList<KeyValuePair<string, string>> rows,
        ReportBrandingThemeDto? brandingTheme)
    {
        var worksheets = new List<XlsxWorksheet>
        {
            new(
                "Delivery",
                ["Field", "Value"],
                rows.Select(static row => (IReadOnlyList<object?>)[row.Key, row.Value]).ToArray())
        };

        if (brandingTheme is not null)
        {
            worksheets.Add(new XlsxWorksheet("Branding", ["Field", "Value"], BuildBrandingWorksheetRows(brandingTheme)));
        }

        return worksheets;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildBrandingWorksheetRows(ReportBrandingThemeDto theme) =>
    [
        ["Theme ID", theme.ThemeId],
        ["Theme name", theme.Name],
        ["Firm name", theme.FirmName],
        ["Primary color", theme.PrimaryColor],
        ["Accent color", theme.AccentColor],
        ["Text color", theme.TextColor],
        ["Background color", theme.BackgroundColor],
        ["Logo URI", theme.LogoUri],
        ["Footer text", theme.FooterText],
        ["Disclaimer", theme.Disclaimer],
        ["Built in", theme.IsBuiltIn]
    ];

    private static byte[] BuildReportingRunDeliveryArtifactContent(
        string packageId,
        ReportingOutputManifest manifest,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        string versionStamp)
    {
        var rows = BuildReportingRunDeliveryArtifactRows(
            packageId,
            manifest,
            distributionId,
            artifactName,
            format,
            contentType,
            retainedPath,
            manifest.BrandingTheme,
            versionStamp);

        var gridRows = BuildReportingRunGridArtifactRows(manifest.ReportWriterGrids, manifest.RenderedReportWriterGrids);
        var renderedGridRows = BuildReportingRunRenderedGridArtifactRows(manifest.RenderedReportWriterGrids);
        var renderedGridDictionaryRows = BuildReportingRunGridDataDictionaryArtifactRows(manifest.RenderedReportWriterGrids);
        var renderedGridValidationRows = BuildReportingRunGridValidationArtifactRows(manifest.RenderedReportWriterGrids);
        var allRows = rows
            .Concat(gridRows)
            .Concat(renderedGridRows)
            .Concat(renderedGridDictionaryRows)
            .Concat(renderedGridValidationRows)
            .ToArray();

        return format switch
        {
            GovernanceReportArtifactFormatDto.Csv => BuildDeliveryArtifactCsv(allRows),
            GovernanceReportArtifactFormatDto.Xlsx => XlsxWorkbookWriter.CreateWorkbook(
            [
                new XlsxWorksheet(
                    "ReportingRun",
                    ["Field", "Value"],
                    rows.Select(static row => (IReadOnlyList<object?>)[row.Key, row.Value]).ToArray()),
                new XlsxWorksheet(
                    "ReportWriterGrids",
                    ["Grid ID", "Title", "Kind", "Artifact", "Dimensions", "Metrics", "Formulas", "Validation", "Passed", "Warnings", "Failed"],
                    BuildReportingRunGridWorksheetRows(manifest.ReportWriterGrids, manifest.RenderedReportWriterGrids)),
                new XlsxWorksheet(
                    "ReportWriterGridRows",
                    ["Kind", "Grid ID", "Row Key", "Field", "Value"],
                    BuildReportingRunRenderedGridWorksheetRows(manifest.RenderedReportWriterGrids)),
                new XlsxWorksheet(
                    "ReportWriterDictionary",
                    ["Grid ID", "Key", "Label", "Role", "Source field", "Data type", "Generated", "Description"],
                    BuildReportingRunGridDataDictionaryWorksheetRows(manifest.RenderedReportWriterGrids)),
                new XlsxWorksheet(
                    "ReportWriterValidation",
                    ["Grid ID", "Check", "Status", "Detail"],
                    BuildReportingRunGridValidationWorksheetRows(manifest.RenderedReportWriterGrids))
            ]),
            GovernanceReportArtifactFormatDto.Html => BuildDeliveryArtifactHtml(allRows, manifest.BrandingTheme),
            GovernanceReportArtifactFormatDto.Pdf => BuildDeliveryArtifactPdf(allRows, manifest.BrandingTheme),
            _ => JsonSerializer.SerializeToUtf8Bytes(allRows.ToDictionary(static row => row.Key, static row => row.Value))
        };
    }

    private static byte[] BuildReportingRunDeliveryArtifactContent(
        string packageId,
        string reportingRunId,
        string templateId,
        string? scheduleId,
        IReadOnlyList<string> sourceArtifacts,
        string? asOfDate,
        string? status,
        string? trigger,
        int? attemptCount,
        int? sectionCount,
        int? lineageLinkedSections,
        IReadOnlyList<WorkstationGeneratedReportWriterGridPayload> generatedReportWriterGrids,
        IReadOnlyList<ReportWriterGridRenderDto> renderedReportWriterGrids,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        ReportBrandingThemeDto? brandingTheme,
        string versionStamp)
    {
        var rows = BuildReportingRunDeliveryArtifactRows(
            packageId,
            reportingRunId,
            templateId,
            scheduleId,
            sourceArtifacts,
            asOfDate,
            status,
            trigger,
            attemptCount,
            sectionCount,
            lineageLinkedSections,
            distributionId,
            artifactName,
            format,
            contentType,
            retainedPath,
            brandingTheme,
            versionStamp);

        var gridRows = BuildReportingRunGridArtifactRows(generatedReportWriterGrids);
        var renderedGridRows = BuildReportingRunRenderedGridArtifactRows(renderedReportWriterGrids);
        var renderedGridDictionaryRows = BuildReportingRunGridDataDictionaryArtifactRows(renderedReportWriterGrids);
        var renderedGridValidationRows = BuildReportingRunGridValidationArtifactRows(renderedReportWriterGrids);
        var allRows = rows
            .Concat(gridRows)
            .Concat(renderedGridRows)
            .Concat(renderedGridDictionaryRows)
            .Concat(renderedGridValidationRows)
            .ToArray();

        return format switch
        {
            GovernanceReportArtifactFormatDto.Csv => BuildDeliveryArtifactCsv(allRows),
            GovernanceReportArtifactFormatDto.Xlsx => XlsxWorkbookWriter.CreateWorkbook(
            [
                new XlsxWorksheet(
                    "ReportingRun",
                    ["Field", "Value"],
                    rows.Select(static row => (IReadOnlyList<object?>)[row.Key, row.Value]).ToArray()),
                new XlsxWorksheet(
                    "ReportWriterGrids",
                    ["Grid ID", "Title", "Kind", "Artifact", "Dimensions", "Metrics", "Formulas", "Validation", "Passed", "Warnings", "Failed"],
                    BuildReportingRunGridWorksheetRows(generatedReportWriterGrids)),
                new XlsxWorksheet(
                    "ReportWriterGridRows",
                    ["Kind", "Grid ID", "Row Key", "Field", "Value"],
                    BuildReportingRunRenderedGridWorksheetRows(renderedReportWriterGrids)),
                new XlsxWorksheet(
                    "ReportWriterDictionary",
                    ["Grid ID", "Key", "Label", "Role", "Source field", "Data type", "Generated", "Description"],
                    BuildReportingRunGridDataDictionaryWorksheetRows(renderedReportWriterGrids)),
                new XlsxWorksheet(
                    "ReportWriterValidation",
                    ["Grid ID", "Check", "Status", "Detail"],
                    BuildReportingRunGridValidationWorksheetRows(renderedReportWriterGrids))
            ]),
            GovernanceReportArtifactFormatDto.Html => BuildDeliveryArtifactHtml(allRows, brandingTheme),
            GovernanceReportArtifactFormatDto.Pdf => BuildDeliveryArtifactPdf(allRows, brandingTheme),
            _ => JsonSerializer.SerializeToUtf8Bytes(allRows.ToDictionary(static row => row.Key, static row => row.Value))
        };
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildDeliveryArtifactRows(
        string packageId,
        Guid reportId,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        string? publicationEvidenceHash,
        string? publicationManifestId,
        string? publicationRetainedManifestPath,
        string? publicationSignedOffBy,
        DateTimeOffset? publicationSignedOffAtUtc,
        IReadOnlyList<ReportPackEvidenceLinkDto> publicationEvidenceLinks,
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        string? restatementReasonCode,
        Guid? restatementPriorVersionReportId,
        string? restatementApprover,
        IReadOnlyList<ReportPackChangedLineDto> restatementChangedLines,
        IReadOnlyList<ReportPackEvidenceLinkDto> restatementEvidenceLinks,
        ReportBrandingThemeDto? brandingTheme,
        string versionStamp)
    {
        var rows = new List<KeyValuePair<string, string>>
        {
            new("packageId", packageId),
            new("reportId", reportId.ToString("D")),
            new("distributionId", distributionId),
            new("artifactName", artifactName),
            new("format", format.ToString()),
            new("contentType", contentType),
            new("retainedPath", retainedPath),
            new("publicationEvidenceHash", string.IsNullOrWhiteSpace(publicationEvidenceHash) ? "" : publicationEvidenceHash.Trim()),
            new("publicationManifestId", publicationManifestId ?? ""),
            new("publicationRetainedManifestPath", publicationRetainedManifestPath ?? ""),
            new("publicationSignedOffBy", publicationSignedOffBy ?? ""),
            new("publicationSignedOffAtUtc", publicationSignedOffAtUtc?.ToString("O") ?? ""),
            new("publicationEvidenceLinkCount", publicationEvidenceLinks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("lineProvenanceCount", lineProvenance.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("restatementReasonCode", restatementReasonCode ?? ""),
            new("restatementPriorVersionReportId", restatementPriorVersionReportId?.ToString("D") ?? ""),
            new("restatementApprover", restatementApprover ?? ""),
            new("restatementChangedLineCount", restatementChangedLines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("restatementEvidenceLinkCount", restatementEvidenceLinks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("brandingThemeId", brandingTheme?.ThemeId ?? ""),
            new("brandingThemeName", brandingTheme?.Name ?? ""),
            new("brandingFirmName", brandingTheme?.FirmName ?? ""),
            new("brandingPrimaryColor", brandingTheme?.PrimaryColor ?? ""),
            new("brandingAccentColor", brandingTheme?.AccentColor ?? ""),
            new("brandingLogoUri", brandingTheme?.LogoUri ?? ""),
            new("brandingFooterText", brandingTheme?.FooterText ?? ""),
            new("brandingDisclaimer", brandingTheme?.Disclaimer ?? ""),
            new("versionStamp", versionStamp)
        };

        for (var index = 0; index < publicationEvidenceLinks.Count; index++)
        {
            var link = publicationEvidenceLinks[index];
            var prefix = $"publicationEvidenceLinks[{index}]";
            rows.Add(new($"{prefix}.evidenceId", link.EvidenceId));
            rows.Add(new($"{prefix}.label", link.Label));
            rows.Add(new($"{prefix}.route", link.Route ?? ""));
            rows.Add(new($"{prefix}.source", link.Source));
            rows.Add(new($"{prefix}.capturedAtUtc", link.CapturedAtUtc?.ToString("O") ?? ""));
        }

        for (var index = 0; index < lineProvenance.Count; index++)
        {
            var line = lineProvenance[index];
            var prefix = $"lineProvenance[{index}]";
            rows.Add(new($"{prefix}.lineKey", line.LineKey));
            rows.Add(new($"{prefix}.sourceKind", line.SourceKind));
            rows.Add(new($"{prefix}.sourceId", line.SourceId));
            rows.Add(new($"{prefix}.evidenceId", line.EvidenceId));
            rows.Add(new($"{prefix}.reportValue", line.ReportValue ?? ""));
            rows.Add(new($"{prefix}.runId", line.RunId ?? ""));
            rows.Add(new($"{prefix}.ledgerEntryId", line.LedgerEntryId ?? ""));
            rows.Add(new($"{prefix}.reconciliationCaseId", line.ReconciliationCaseId ?? ""));
            rows.Add(new($"{prefix}.sourceSessionId", line.SourceSessionId ?? ""));
            rows.Add(new($"{prefix}.reconciliationRunId", line.ReconciliationRunId ?? ""));
            rows.Add(new($"{prefix}.providerEventId", line.ProviderEventId ?? ""));
            rows.Add(new($"{prefix}.securityMasterId", line.SecurityMasterId ?? ""));
            rows.Add(new($"{prefix}.securityDefinitionId", line.SecurityDefinitionId ?? ""));
            rows.Add(new($"{prefix}.reconciliationOutcome", line.ReconciliationOutcome ?? ""));
            rows.Add(new($"{prefix}.approvalId", line.ApprovalId ?? ""));
            rows.Add(new($"{prefix}.financialRecordExplorerId", line.FinancialRecordExplorerId ?? ""));
            rows.Add(new($"{prefix}.financialRecordHref", line.FinancialRecordHref ?? ""));
        }

        for (var index = 0; index < restatementChangedLines.Count; index++)
        {
            var line = restatementChangedLines[index];
            var prefix = $"restatementChangedLines[{index}]";
            var evidenceLinks = line.EvidenceLinks ?? [];
            rows.Add(new($"{prefix}.lineKey", line.LineKey));
            rows.Add(new($"{prefix}.previousValue", line.PreviousValue));
            rows.Add(new($"{prefix}.currentValue", line.CurrentValue));
            rows.Add(new($"{prefix}.evidenceLinkCount", evidenceLinks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            for (var evidenceIndex = 0; evidenceIndex < evidenceLinks.Count; evidenceIndex++)
            {
                var link = evidenceLinks[evidenceIndex];
                var evidencePrefix = $"{prefix}.evidenceLinks[{evidenceIndex}]";
                rows.Add(new($"{evidencePrefix}.evidenceId", link.EvidenceId));
                rows.Add(new($"{evidencePrefix}.label", link.Label));
                rows.Add(new($"{evidencePrefix}.route", link.Route ?? ""));
                rows.Add(new($"{evidencePrefix}.source", link.Source));
                rows.Add(new($"{evidencePrefix}.capturedAtUtc", link.CapturedAtUtc?.ToString("O") ?? ""));
            }
        }

        for (var index = 0; index < restatementEvidenceLinks.Count; index++)
        {
            var link = restatementEvidenceLinks[index];
            var prefix = $"restatementEvidenceLinks[{index}]";
            rows.Add(new($"{prefix}.evidenceId", link.EvidenceId));
            rows.Add(new($"{prefix}.label", link.Label));
            rows.Add(new($"{prefix}.route", link.Route ?? ""));
            rows.Add(new($"{prefix}.source", link.Source));
            rows.Add(new($"{prefix}.capturedAtUtc", link.CapturedAtUtc?.ToString("O") ?? ""));
        }

        return rows;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunDeliveryArtifactRows(
        string packageId,
        ReportingOutputManifest manifest,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        ReportBrandingThemeDto? brandingTheme,
        string versionStamp) =>
    [
        new("packageId", packageId),
        new("reportingRunId", manifest.RunId),
        new("templateId", manifest.TemplateId),
        new("scheduleId", manifest.ScheduleId ?? ""),
        new("asOfDate", manifest.AsOfDate.ToString("yyyy-MM-dd")),
        new("status", manifest.Status.ToString()),
        new("trigger", manifest.Trigger.ToString()),
        new("attemptCount", manifest.AttemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("sectionCount", manifest.Sections.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("lineageLinkedSections", manifest.Sections.Count(static section => section.Lineage is not null).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("sourceArtifacts", string.Join(";", manifest.Artifacts)),
        new("distributionId", distributionId),
        new("artifactName", artifactName),
        new("format", format.ToString()),
        new("contentType", contentType),
        new("retainedPath", retainedPath),
        new("brandingThemeId", manifest.BrandingTheme?.ThemeId ?? manifest.BrandingThemeId ?? ""),
        new("brandingThemeName", manifest.BrandingTheme?.Name ?? ""),
        new("brandingFirmName", manifest.BrandingTheme?.FirmName ?? ""),
        new("brandingPrimaryColor", manifest.BrandingTheme?.PrimaryColor ?? ""),
        new("brandingAccentColor", manifest.BrandingTheme?.AccentColor ?? ""),
        new("brandingLogoUri", manifest.BrandingTheme?.LogoUri ?? ""),
        new("brandingFooterText", manifest.BrandingTheme?.FooterText ?? ""),
        new("brandingDisclaimer", manifest.BrandingTheme?.Disclaimer ?? ""),
        new("versionStamp", versionStamp)
    ];

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunGridArtifactRows(
        System.Collections.Immutable.ImmutableArray<ReportingRunReportWriterGridArtifact> grids,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        var rows = new List<KeyValuePair<string, string>>(grids.Length * 11 + 1)
        {
            new("generatedReportWriterGridCount", grids.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        for (var index = 0; index < grids.Length; index++)
        {
            var grid = grids[index];
            var prefix = $"generatedReportWriterGrids[{index}]";
            rows.Add(new($"{prefix}.gridId", grid.GridId));
            rows.Add(new($"{prefix}.title", grid.Title));
            rows.Add(new($"{prefix}.kind", grid.Kind));
            rows.Add(new($"{prefix}.artifact", grid.Artifact));
            rows.Add(new($"{prefix}.dimensionCount", grid.DimensionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new($"{prefix}.metricCount", grid.MetricCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new($"{prefix}.formulaCount", grid.FormulaCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var validation = BuildGeneratedGridValidationSummary(grid.GridId, renderedGrids);
            rows.Add(new($"{prefix}.validationSummary", validation.Summary ?? ""));
            rows.Add(new($"{prefix}.validationPassedCount", validation.Passed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
            rows.Add(new($"{prefix}.validationWarningCount", validation.Warning?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
            rows.Add(new($"{prefix}.validationFailedCount", validation.Failed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
        }

        return rows;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunGridArtifactRows(
        IReadOnlyList<WorkstationGeneratedReportWriterGridPayload> grids)
    {
        if (grids.Count == 0)
        {
            return [];
        }

        var rows = new List<KeyValuePair<string, string>>(grids.Count * 11 + 1)
        {
            new("generatedReportWriterGridCount", grids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        for (var index = 0; index < grids.Count; index++)
        {
            var grid = grids[index];
            var prefix = $"generatedReportWriterGrids[{index}]";
            rows.Add(new($"{prefix}.gridId", grid.GridId));
            rows.Add(new($"{prefix}.title", grid.Title));
            rows.Add(new($"{prefix}.kind", grid.Kind));
            rows.Add(new($"{prefix}.artifact", grid.Artifact));
            rows.Add(new($"{prefix}.dimensionCount", grid.DimensionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new($"{prefix}.metricCount", grid.MetricCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new($"{prefix}.formulaCount", grid.FormulaCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new($"{prefix}.validationSummary", grid.ValidationSummary ?? ""));
            rows.Add(new($"{prefix}.validationPassedCount", grid.ValidationPassedCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
            rows.Add(new($"{prefix}.validationWarningCount", grid.ValidationWarningCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
            rows.Add(new($"{prefix}.validationFailedCount", grid.ValidationFailedCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
        }

        return rows;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunRenderedGridArtifactRows(
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> grids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return BuildReportingRunRenderedGridArtifactRows(grids.AsEnumerable().ToArray());
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunRenderedGridArtifactRows(
        IReadOnlyList<ReportWriterGridRenderDto> grids)
    {
        if (grids.Count == 0)
        {
            return [];
        }

        var rows = new List<KeyValuePair<string, string>>
        {
            new("renderedReportWriterGridCount", grids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        for (var gridIndex = 0; gridIndex < grids.Count; gridIndex++)
        {
            var grid = grids[gridIndex];
            var prefix = $"renderedReportWriterGrids[{gridIndex}]";
            rows.Add(new($"{prefix}.gridId", grid.GridId));
            rows.Add(new($"{prefix}.title", grid.Title));
            rows.Add(new($"{prefix}.kind", grid.Kind.ToString()));
            rows.Add(new($"{prefix}.columnCount", grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new($"{prefix}.rowCount", grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new($"{prefix}.warningCount", grid.Warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (grid.Lineage is not null)
            {
                rows.Add(new($"{prefix}.lineage.inputRowCount", grid.Lineage.InputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                rows.Add(new($"{prefix}.lineage.filteredInputRowCount", grid.Lineage.FilteredInputRowCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
                rows.Add(new($"{prefix}.lineage.outputRowCount", grid.Lineage.OutputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                rows.Add(new($"{prefix}.lineage.sourceFields", string.Join(";", grid.Lineage.SourceFields)));
            }

            for (var columnIndex = 0; columnIndex < grid.Columns.Count; columnIndex++)
            {
                var column = grid.Columns[columnIndex];
                var columnPrefix = $"{prefix}.columns[{columnIndex}]";
                rows.Add(new($"{columnPrefix}.key", column.Key));
                rows.Add(new($"{columnPrefix}.label", column.Label));
                rows.Add(new($"{columnPrefix}.role", column.Role));
            }

            for (var warningIndex = 0; warningIndex < grid.Warnings.Count; warningIndex++)
            {
                rows.Add(new($"{prefix}.warnings[{warningIndex}]", grid.Warnings[warningIndex]));
            }

            for (var rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
            {
                var renderedRow = grid.Rows[rowIndex];
                var rowPrefix = $"{prefix}.rows[{rowIndex}]";
                rows.Add(new($"{rowPrefix}.rowKey", renderedRow.RowKey));
                foreach (var value in renderedRow.Values.OrderBy(static value => value.Key, StringComparer.OrdinalIgnoreCase))
                {
                    rows.Add(new($"{rowPrefix}.values.{value.Key}", value.Value));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunGridWorksheetRows(
        System.Collections.Immutable.ImmutableArray<ReportingRunReportWriterGridArtifact> grids,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return grids
            .Select(grid =>
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, renderedGrids);
                return (IReadOnlyList<object?>)
                [
                    grid.GridId,
                    grid.Title,
                    grid.Kind,
                    grid.Artifact,
                    grid.DimensionCount,
                    grid.MetricCount,
                    grid.FormulaCount,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed
                ];
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunGridWorksheetRows(
        IReadOnlyList<WorkstationGeneratedReportWriterGridPayload> grids)
    {
        if (grids.Count == 0)
        {
            return [];
        }

        return grids
            .Select(static grid => (IReadOnlyList<object?>)
            [
                grid.GridId,
                grid.Title,
                grid.Kind,
                grid.Artifact,
                grid.DimensionCount,
                grid.MetricCount,
                grid.FormulaCount,
                grid.ValidationSummary,
                grid.ValidationPassedCount,
                grid.ValidationWarningCount,
                grid.ValidationFailedCount
            ])
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunRenderedGridWorksheetRows(
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> grids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return BuildReportingRunRenderedGridWorksheetRows(grids.AsEnumerable().ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunRenderedGridWorksheetRows(
        IReadOnlyList<ReportWriterGridRenderDto> grids)
    {
        if (grids.Count == 0)
        {
            return [];
        }

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var grid in grids)
        {
            rows.Add(["grid", grid.GridId, "", "title", grid.Title]);
            rows.Add(["grid", grid.GridId, "", "kind", grid.Kind.ToString()]);
            if (grid.Lineage is not null)
            {
                rows.Add(["lineage", grid.GridId, "", "inputRowCount", grid.Lineage.InputRowCount]);
                rows.Add(["lineage", grid.GridId, "", "filteredInputRowCount", grid.Lineage.FilteredInputRowCount]);
                rows.Add(["lineage", grid.GridId, "", "outputRowCount", grid.Lineage.OutputRowCount]);
                rows.Add(["lineage", grid.GridId, "", "sourceFields", string.Join(";", grid.Lineage.SourceFields)]);
            }

            foreach (var column in grid.Columns)
            {
                rows.Add(["column", grid.GridId, "", column.Key, $"{column.Label} ({column.Role})"]);
            }

            foreach (var warning in grid.Warnings)
            {
                rows.Add(["warning", grid.GridId, "", "warning", warning]);
            }

            foreach (var renderedRow in grid.Rows)
            {
                foreach (var value in renderedRow.Values.OrderBy(static value => value.Key, StringComparer.OrdinalIgnoreCase))
                {
                    rows.Add(["row", grid.GridId, renderedRow.RowKey, value.Key, value.Value]);
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunGridDataDictionaryArtifactRows(
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> grids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return BuildReportingRunGridDataDictionaryArtifactRows(grids.AsEnumerable().ToArray());
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunGridDataDictionaryArtifactRows(
        IReadOnlyList<ReportWriterGridRenderDto> grids)
    {
        if (grids.Count == 0)
        {
            return [];
        }

        var rows = new List<KeyValuePair<string, string>>();
        for (var gridIndex = 0; gridIndex < grids.Count; gridIndex++)
        {
            var grid = grids[gridIndex];
            var dictionary = ResolveReportWriterGridDataDictionary(grid);
            var prefix = $"renderedReportWriterGrids[{gridIndex}].dataDictionary";
            rows.Add(new($"{prefix}.fieldCount", dictionary.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            for (var fieldIndex = 0; fieldIndex < dictionary.Count; fieldIndex++)
            {
                var field = dictionary[fieldIndex];
                var fieldPrefix = $"{prefix}[{fieldIndex}]";
                rows.Add(new($"{fieldPrefix}.gridId", grid.GridId));
                rows.Add(new($"{fieldPrefix}.key", field.Key));
                rows.Add(new($"{fieldPrefix}.label", field.Label));
                rows.Add(new($"{fieldPrefix}.role", field.Role));
                rows.Add(new($"{fieldPrefix}.sourceField", field.SourceField));
                rows.Add(new($"{fieldPrefix}.dataType", field.DataType));
                rows.Add(new($"{fieldPrefix}.isGenerated", field.IsGenerated ? "true" : "false"));
                rows.Add(new($"{fieldPrefix}.description", field.Description));
            }
        }

        return rows;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunGridValidationArtifactRows(
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> grids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return BuildReportingRunGridValidationArtifactRows(grids.AsEnumerable().ToArray());
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunGridValidationArtifactRows(
        IReadOnlyList<ReportWriterGridRenderDto> grids)
    {
        if (grids.Count == 0)
        {
            return [];
        }

        var rows = new List<KeyValuePair<string, string>>();
        for (var gridIndex = 0; gridIndex < grids.Count; gridIndex++)
        {
            var grid = grids[gridIndex];
            var checks = ResolveReportWriterGridValidationChecks(grid);
            var prefix = $"renderedReportWriterGrids[{gridIndex}].validationChecks";
            rows.Add(new($"{prefix}.checkCount", checks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            for (var checkIndex = 0; checkIndex < checks.Count; checkIndex++)
            {
                var check = checks[checkIndex];
                var checkPrefix = $"{prefix}[{checkIndex}]";
                rows.Add(new($"{checkPrefix}.gridId", grid.GridId));
                rows.Add(new($"{checkPrefix}.checkId", check.CheckId));
                rows.Add(new($"{checkPrefix}.status", check.Status));
                rows.Add(new($"{checkPrefix}.detail", check.Detail));
            }
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunGridDataDictionaryWorksheetRows(
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> grids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return BuildReportingRunGridDataDictionaryWorksheetRows(grids.AsEnumerable().ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunGridDataDictionaryWorksheetRows(
        IReadOnlyList<ReportWriterGridRenderDto> grids)
    {
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var grid in grids)
        {
            foreach (var field in ResolveReportWriterGridDataDictionary(grid))
            {
                rows.Add([
                    grid.GridId,
                    field.Key,
                    field.Label,
                    field.Role,
                    field.SourceField,
                    field.DataType,
                    field.IsGenerated ? "true" : "false",
                    field.Description
                ]);
            }
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunGridValidationWorksheetRows(
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> grids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return BuildReportingRunGridValidationWorksheetRows(grids.AsEnumerable().ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildReportingRunGridValidationWorksheetRows(
        IReadOnlyList<ReportWriterGridRenderDto> grids)
    {
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var grid in grids)
        {
            foreach (var check in ResolveReportWriterGridValidationChecks(grid))
            {
                rows.Add([
                    grid.GridId,
                    check.CheckId,
                    check.Status,
                    check.Detail
                ]);
            }
        }

        return rows;
    }

    private static IReadOnlyList<ReportWriterGridDataDictionaryFieldDto> ResolveReportWriterGridDataDictionary(
        ReportWriterGridRenderDto grid) =>
        grid.DataDictionary is { Count: > 0 }
            ? grid.DataDictionary
            : grid.Columns.Select(column => BuildReportWriterGridDataDictionaryField(grid, column)).ToArray();

    private static ReportWriterGridRenderDto EnrichReportWriterGridRender(ReportWriterGridRenderDto grid) =>
        grid with
        {
            DataDictionary = ResolveReportWriterGridDataDictionary(grid),
            ValidationChecks = ResolveReportWriterGridValidationChecks(grid)
        };

    private static ReportWriterGridDataDictionaryFieldDto BuildReportWriterGridDataDictionaryField(
        ReportWriterGridRenderDto grid,
        ReportWriterGridColumnDto column)
    {
        var sourceFields = grid.Lineage?.SourceFields ?? [];
        var sourceField = ResolveReportWriterGridSourceField(grid, column);
        var isFormula = string.Equals(column.Role, "formula", StringComparison.OrdinalIgnoreCase);
        var isGenerated = isFormula
            || string.IsNullOrWhiteSpace(sourceField)
            || !sourceFields.Contains(sourceField, StringComparer.OrdinalIgnoreCase);
        var dataType = InferReportWriterGridDataType(grid, column.Key);
        return new ReportWriterGridDataDictionaryFieldDto(
            column.Key,
            column.Label,
            column.Role,
            isGenerated ? "" : sourceField,
            dataType,
            isGenerated,
            isGenerated
                ? $"{column.Label} is generated by the retained report-writer grid as a {column.Role} column."
                : $"{column.Label} maps to source field {sourceField} and exports as {dataType}.");
    }

    private static string ResolveReportWriterGridSourceField(
        ReportWriterGridRenderDto grid,
        ReportWriterGridColumnDto column)
    {
        if (grid.Lineage?.Metrics.FirstOrDefault(metric =>
                string.Equals(metric.Name, column.Key, StringComparison.OrdinalIgnoreCase)) is { } metric)
        {
            return metric.SourceField;
        }

        if (grid.Lineage?.Formulas.FirstOrDefault(formula =>
                string.Equals(formula.Name, column.Key, StringComparison.OrdinalIgnoreCase)) is { } formula)
        {
            return string.Join(";", formula.SourceFields);
        }

        return grid.Lineage?.SourceFields.FirstOrDefault(field =>
            string.Equals(field, column.Key, StringComparison.OrdinalIgnoreCase)) ?? column.Key;
    }

    private static string InferReportWriterGridDataType(ReportWriterGridRenderDto grid, string columnKey)
    {
        var values = grid.Rows
            .Select(row => row.Values.TryGetValue(columnKey, out var value) ? value : null)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(25)
            .ToArray();

        return values.Length > 0 && values.All(static value =>
            decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
                ? "decimal"
                : "string";
    }

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> ResolveReportWriterGridValidationChecks(
        ReportWriterGridRenderDto grid) =>
        grid.ValidationChecks is { Count: > 0 }
            ? grid.ValidationChecks
            : BuildReportWriterGridValidationChecks(grid);

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> BuildReportWriterGridValidationChecks(
        ReportWriterGridRenderDto grid)
    {
        var dictionary = ResolveReportWriterGridDataDictionary(grid);
        return
        [
            new ReportWriterGridValidationCheckDto(
                "row-count",
                grid.Lineage is null || grid.Lineage.OutputRowCount == grid.Rows.Count ? "Passed" : "Failed",
                grid.Lineage is null
                    ? $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); no lineage row count was retained."
                    : $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); lineage expected {grid.Lineage.OutputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}."),
            new ReportWriterGridValidationCheckDto(
                "column-dictionary",
                grid.Columns.Count == dictionary.Count ? "Passed" : "Failed",
                $"Dictionary covers {dictionary.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} rendered column(s)."),
            new ReportWriterGridValidationCheckDto(
                "source-field-lineage",
                grid.Lineage is { SourceFields.Count: > 0 } ? "Passed" : "Warning",
                grid.Lineage is { SourceFields.Count: > 0 }
                    ? $"Lineage retains {grid.Lineage.SourceFields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} source field(s)."
                    : "No source field lineage was retained with this grid."),
            new ReportWriterGridValidationCheckDto(
                "warnings",
                grid.Warnings.Count == 0 ? "Passed" : "Warning",
                grid.Warnings.Count == 0
                    ? "No render warnings were retained."
                    : $"{grid.Warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} render warning(s) retained: {string.Join("; ", grid.Warnings)}")
        ];
    }

    private static IReadOnlyList<WorkstationGeneratedReportWriterGridPayload> ProjectGeneratedReportWriterGrids(
        System.Collections.Immutable.ImmutableArray<ReportingRunReportWriterGridArtifact> grids,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (grids.IsDefaultOrEmpty)
        {
            return [];
        }

        return grids
            .Select(grid =>
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, renderedGrids);
                return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId,
                    grid.Title,
                    grid.Kind,
                    grid.Artifact,
                    grid.DimensionCount,
                    grid.MetricCount,
                    grid.FormulaCount,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
            })
            .ToArray();
    }

    private static (string? Summary, int? Passed, int? Warning, int? Failed) BuildGeneratedGridValidationSummary(
        string gridId,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (renderedGrids.IsDefaultOrEmpty)
        {
            return (null, null, null, null);
        }

        var renderedGrid = renderedGrids.FirstOrDefault(grid =>
            string.Equals(grid.GridId, gridId, StringComparison.OrdinalIgnoreCase));
        if (renderedGrid is null)
        {
            return (null, null, null, null);
        }

        var checks = ResolveReportWriterGridValidationChecks(renderedGrid);
        if (checks.Count == 0)
        {
            return (null, null, null, null);
        }

        var passed = checks.Count(static check => string.Equals(check.Status, "Passed", StringComparison.OrdinalIgnoreCase));
        var warning = checks.Count(static check => string.Equals(check.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        var failed = checks.Count - passed - warning;
        var summary = failed > 0
            ? $"{passed} passed / {checks.Count} checks, {failed} failed"
            : warning > 0
                ? $"{passed} passed / {checks.Count} checks, {warning} review"
                : $"{passed} passed / {checks.Count} checks";
        return (summary, passed, warning, failed);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunDeliveryArtifactRows(
        string packageId,
        string reportingRunId,
        string templateId,
        string? scheduleId,
        IReadOnlyList<string> sourceArtifacts,
        string? asOfDate,
        string? status,
        string? trigger,
        int? attemptCount,
        int? sectionCount,
        int? lineageLinkedSections,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        ReportBrandingThemeDto? brandingTheme,
        string versionStamp) =>
    [
        new("packageId", packageId),
        new("reportingRunId", reportingRunId),
        new("templateId", templateId),
        new("scheduleId", scheduleId ?? ""),
        new("asOfDate", asOfDate ?? ""),
        new("status", status ?? ""),
        new("trigger", trigger ?? ""),
        new("attemptCount", attemptCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""),
        new("sectionCount", sectionCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""),
        new("lineageLinkedSections", lineageLinkedSections?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""),
        new("sourceArtifacts", string.Join(";", sourceArtifacts)),
        new("distributionId", distributionId),
        new("artifactName", artifactName),
        new("format", format.ToString()),
        new("contentType", contentType),
        new("retainedPath", retainedPath),
        new("brandingThemeId", brandingTheme?.ThemeId ?? ""),
        new("brandingThemeName", brandingTheme?.Name ?? ""),
        new("brandingFirmName", brandingTheme?.FirmName ?? ""),
        new("brandingPrimaryColor", brandingTheme?.PrimaryColor ?? ""),
        new("brandingAccentColor", brandingTheme?.AccentColor ?? ""),
        new("brandingLogoUri", brandingTheme?.LogoUri ?? ""),
        new("brandingFooterText", brandingTheme?.FooterText ?? ""),
        new("brandingDisclaimer", brandingTheme?.Disclaimer ?? ""),
        new("versionStamp", versionStamp)
    ];

    private static byte[] BuildDeliveryArtifactCsv(IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder, ["field", "value"]);
        foreach (var row in rows)
        {
            AppendCsvRow(builder, [row.Key, row.Value]);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendCsvRow(StringBuilder builder, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(EscapeCsvValue(value));
            first = false;
        }

        builder.AppendLine();
    }

    private static string EscapeCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static byte[] BuildDeliveryArtifactHtml(
        IReadOnlyList<KeyValuePair<string, string>> rows,
        ReportBrandingThemeDto? brandingTheme)
    {
        var primaryColor = NormalizeHtmlColor(brandingTheme?.PrimaryColor, "#1F4E79");
        var accentColor = NormalizeHtmlColor(brandingTheme?.AccentColor, "#2F9E8F");
        var textColor = NormalizeHtmlColor(brandingTheme?.TextColor, "#111827");
        var backgroundColor = NormalizeHtmlColor(brandingTheme?.BackgroundColor, "#FFFFFF");
        var firmName = string.IsNullOrWhiteSpace(brandingTheme?.FirmName)
            ? "Meridian"
            : brandingTheme.FirmName.Trim();
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><title>Report Pack Delivery Artifact</title>");
        builder.Append("<style>:root{color:")
            .Append(textColor)
            .Append(";background:")
            .Append(backgroundColor)
            .Append("}body{font-family:Arial,sans-serif;margin:0;background:")
            .Append(backgroundColor)
            .Append(";color:")
            .Append(textColor)
            .Append("}.brand-header{border-top:8px solid ")
            .Append(primaryColor)
            .Append(";padding:24px 32px 18px}.brand-kicker{color:")
            .Append(accentColor)
            .Append(";font-size:12px;text-transform:uppercase;letter-spacing:.12em}.brand-footer{border-top:2px solid ")
            .Append(accentColor)
            .Append(";margin:24px 32px 0;padding:12px 0 24px;font-size:12px}table{border-collapse:collapse;margin:0 32px 24px;width:calc(100% - 64px)}th{background:")
            .Append(primaryColor)
            .Append(";color:")
            .Append(backgroundColor)
            .Append("}td,th{border:1px solid #d0d5dd;padding:8px;text-align:left;vertical-align:top}</style></head><body>");
        builder.Append("<header class=\"brand-header\"><div class=\"brand-kicker\">")
            .Append(EscapeHtml(firmName))
            .Append("</div><h1>Report Pack Delivery Artifact</h1>");
        if (!string.IsNullOrWhiteSpace(brandingTheme?.LogoUri))
        {
            builder.Append("<p>Logo: ")
                .Append(EscapeHtml(brandingTheme.LogoUri.Trim()))
                .Append("</p>");
        }

        builder.AppendLine("</header>");
        builder.AppendLine("<table><thead><tr><th>Field</th><th>Value</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            builder.Append("<tr><td>")
                .Append(EscapeHtml(row.Key))
                .Append("</td><td>")
                .Append(EscapeHtml(row.Value))
                .AppendLine("</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
        if (!string.IsNullOrWhiteSpace(brandingTheme?.FooterText) || !string.IsNullOrWhiteSpace(brandingTheme?.Disclaimer))
        {
            builder.Append("<footer class=\"brand-footer\">");
            if (!string.IsNullOrWhiteSpace(brandingTheme.FooterText))
            {
                builder.Append("<div>")
                    .Append(EscapeHtml(brandingTheme.FooterText.Trim()))
                    .Append("</div>");
            }

            if (!string.IsNullOrWhiteSpace(brandingTheme.Disclaimer))
            {
                builder.Append("<div>")
                    .Append(EscapeHtml(brandingTheme.Disclaimer.Trim()))
                    .Append("</div>");
            }

            builder.AppendLine("</footer>");
        }

        builder.AppendLine("</body></html>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string NormalizeHtmlColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return normalized.Length == 7
               && normalized[0] == '#'
               && normalized.Skip(1).All(Uri.IsHexDigit)
            ? normalized
            : fallback;
    }

    private static string EscapeHtml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static byte[] BuildDeliveryArtifactPdf(
        IReadOnlyList<KeyValuePair<string, string>> rows,
        ReportBrandingThemeDto? brandingTheme)
    {
        var content = new StringBuilder();
        var (red, green, blue) = NormalizePdfColor(brandingTheme?.PrimaryColor);
        content.AppendLine($"{red} {green} {blue} rg");
        content.AppendLine("72 742 468 34 re f");
        content.AppendLine("BT");
        content.AppendLine("/F1 12 Tf");
        content.AppendLine("1 1 1 rg");
        content.AppendLine("1 0 0 1 84 764 Tm");
        content.AppendLine($"({EscapePdfText(string.IsNullOrWhiteSpace(brandingTheme?.FirmName) ? "Meridian" : brandingTheme.FirmName.Trim())}) Tj");
        content.AppendLine("0 0 0 rg");
        content.AppendLine("/F1 10 Tf");
        var y = 724;
        foreach (var row in rows.Take(40))
        {
            content.AppendLine($"1 0 0 1 72 {y} Tm");
            content.AppendLine($"({EscapePdfText($"{row.Key}: {row.Value}")}) Tj");
            y -= 14;
        }

        if (!string.IsNullOrWhiteSpace(brandingTheme?.FooterText))
        {
            content.AppendLine("1 0 0 1 72 42 Tm");
            content.AppendLine($"({EscapePdfText(brandingTheme.FooterText.Trim())}) Tj");
        }

        if (!string.IsNullOrWhiteSpace(brandingTheme?.Disclaimer))
        {
            content.AppendLine("1 0 0 1 72 28 Tm");
            content.AppendLine($"({EscapePdfText(brandingTheme.Disclaimer.Trim())}) Tj");
        }

        content.AppendLine("ET");
        return BuildSinglePagePdf(content.ToString());
    }

    private static (string Red, string Green, string Blue) NormalizePdfColor(string? value)
    {
        var normalized = NormalizeHtmlColor(value, "#1F4E79");
        var red = Convert.ToInt32(normalized.Substring(1, 2), 16) / 255m;
        var green = Convert.ToInt32(normalized.Substring(3, 2), 16) / 255m;
        var blue = Convert.ToInt32(normalized.Substring(5, 2), 16) / 255m;
        return (
            red.ToString("0.###", CultureInfo.InvariantCulture),
            green.ToString("0.###", CultureInfo.InvariantCulture),
            blue.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static string EscapePdfText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static byte[] BuildSinglePagePdf(string contentStream)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>\nstream\n{contentStream}\nendstream"
        };

        var builder = new StringBuilder();
        var offsets = new List<int>(objects.Length + 1) { 0 };
        builder.AppendLine("%PDF-1.4");
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.AppendLine($"{index + 1} 0 obj");
            builder.AppendLine(objects[index]);
            builder.AppendLine("endobj");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.AppendLine("xref");
        builder.AppendLine($"0 {objects.Length + 1}");
        builder.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
        {
            builder.AppendLine($"{offset:0000000000} 00000 n ");
        }

        builder.AppendLine("trailer");
        builder.AppendLine($"<< /Size {objects.Length + 1} /Root 1 0 R >>");
        builder.AppendLine("startxref");
        builder.AppendLine(xrefOffset.ToString());
        builder.AppendLine("%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static IReadOnlyList<GovernanceReportArtifactFormatDto> NormalizeFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto>? requestedFormats)
    {
        var formats = requestedFormats is { Count: > 0 }
            ? requestedFormats
            : [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx, GovernanceReportArtifactFormatDto.Csv];

        var seen = new HashSet<GovernanceReportArtifactFormatDto>();
        var normalized = new List<GovernanceReportArtifactFormatDto>(formats.Count);
        foreach (var format in formats)
        {
            if (!Enum.IsDefined(format))
            {
                throw new ArgumentException($"Unsupported report-pack artifact format '{format}'.", nameof(requestedFormats));
            }

            if (seen.Add(format))
            {
                normalized.Add(format);
            }
        }

        return normalized.ToArray();
    }

    private static ReportPackDeliveryModeDto InferDeliveryMode(string channel)
    {
        if (channel.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EmailLink;
        }

        if (channel.Contains("portal", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.SecurePortal;
        }

        if (channel.Contains("vault", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EvidenceVault;
        }

        return ReportPackDeliveryModeDto.InternalRoute;
    }

    private static string ResolveExtension(GovernanceReportArtifactFormatDto format) =>
        format switch
        {
            GovernanceReportArtifactFormatDto.Csv => "csv",
            GovernanceReportArtifactFormatDto.Xlsx => "xlsx",
            GovernanceReportArtifactFormatDto.Html => "html",
            GovernanceReportArtifactFormatDto.Pdf => "pdf",
            _ => "json"
        };

    private static string ResolveContentType(GovernanceReportArtifactFormatDto format) =>
        format switch
        {
            GovernanceReportArtifactFormatDto.Csv => "text/csv",
            GovernanceReportArtifactFormatDto.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            GovernanceReportArtifactFormatDto.Html => "text/html",
            GovernanceReportArtifactFormatDto.Pdf => "application/pdf",
            _ => "application/json"
        };

    private static string BuildSecureToken(Guid reportId, string distributionId, Guid attemptId)
    {
        return ComputeSha256Hex($"{reportId:D}:{distributionId}:{attemptId:D}:report-pack-delivery")[..24];
    }

    private static Guid BuildReportingRunReportId(string runId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(runId));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string BuildReportingRunDeliveryReference(ReportingOutputManifest manifest, string distributionId) =>
        $"schedule:{manifest.TemplateId}:{manifest.RunId}:{distributionId}";

    private static string SanitizeArtifactName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        return builder.Length == 0 ? "reporting-run" : builder.ToString();
    }

    private static string ComputeSha256Hex(string value)
    {
        return ComputeSha256Hex(Encoding.UTF8.GetBytes(value));
    }

    private static string ComputeSha256Hex(byte[] value)
    {
        var bytes = SHA256.HashData(value);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private ReportPackDeliveryAttemptDto? GetDeliveredAttempt(Guid reportId, Guid attemptId)
    {
        lock (_gate)
        {
            return _attempts.FirstOrDefault(item =>
                item.ReportId == reportId
                && item.AttemptId == attemptId
                && item.State == ReportPackDeliveryStateDto.Delivered
                && item.Package is not null);
        }
    }

    private static void EnsureValidPackageToken(ReportPackDeliveryAttemptDto attempt, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedAccessException("A valid package token is required.");
        }

        var expectedToken = BuildSecureToken(attempt.ReportId, attempt.DistributionId, attempt.AttemptId);
        var suppliedToken = token.Trim();
        var expectedBytes = Encoding.ASCII.GetBytes(expectedToken);
        var suppliedBytes = Encoding.ASCII.GetBytes(suppliedToken);
        if (expectedBytes.Length != suppliedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            throw new UnauthorizedAccessException("A valid package token is required.");
        }

        if (attempt.Package?.AccessExpiresAtUtc is { } accessExpiresAtUtc
            && accessExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAccessException("The package token has expired.");
        }
    }

    private static IReadOnlyList<ReportPackEvidenceLinkDto> NormalizeEvidenceLinks(
        IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks) =>
        evidenceLinks?
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .GroupBy(static link => link.EvidenceId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var link = group.First();
                return link with
                {
                    EvidenceId = link.EvidenceId.Trim(),
                    Label = string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId.Trim() : link.Label.Trim(),
                    Route = string.IsNullOrWhiteSpace(link.Route) ? null : link.Route.Trim(),
                    Source = string.IsNullOrWhiteSpace(link.Source) ? "report-pack-delivery" : link.Source.Trim()
                };
            })
            .ToArray() ?? [];
}
