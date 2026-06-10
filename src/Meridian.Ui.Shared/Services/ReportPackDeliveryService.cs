using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Storage.Archival;
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
        return attempt.Package!;
    }

    public ReportPackDeliveryArtifactContent GetArtifact(Guid reportId, Guid attemptId, string artifactName, string? token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        var attempt = GetDeliveredAttempt(reportId, attemptId)
            ?? throw new KeyNotFoundException("delivery package not found");
        EnsureValidPackageToken(attempt, token);
        var package = attempt.Package!;
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
            artifactEvidenceLinks);
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
            DeliveryEvidencePacket: deliveryEvidencePacket);
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
        var artifacts = formats
            .Select(format => BuildArtifact(manifest, policy, reportId, packageId, attemptId, token, format))
            .ToArray();
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
            DateTimeOffset.UtcNow,
            retainedManifestPath,
            PublicationEvidenceHash: null,
            BuildIntegritySummary(artifacts, publicationEvidenceHash: null),
            manifest.RunId,
            manifest.TemplateId,
            manifest.ScheduleId,
            manifest.Artifacts.ToArray());
    }

    private static ReportPackDeliveryEvidencePacketDto BuildDeliveryEvidencePacket(
        ReportPackWorkflowRecordDto record,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        string packageId,
        ReportPackDeliveryModeDto deliveryMode,
        DateTimeOffset deliveredAtUtc,
        IReadOnlyList<ReportPackDeliveryArtifactDto> artifacts,
        IReadOnlyList<ReportPackEvidenceLinkDto> requestEvidenceLinks,
        IReadOnlyList<ReportPackEvidenceLinkDto> artifactEvidenceLinks)
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
                .Concat(restatementContents));
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
                artifact.VersionStamp)
            : BuildReportingRunDeliveryArtifactContent(
                package.PackageId,
                package.ReportingRunId!,
                package.ReportingTemplateId ?? string.Empty,
                package.ReportingScheduleId,
                package.SourceArtifacts ?? [],
                package.DistributionId,
                artifact.ArtifactName,
                artifact.Format,
                artifact.ContentType,
                artifact.RetainedPath,
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
            versionStamp);

        return format switch
        {
            GovernanceReportArtifactFormatDto.Csv => BuildDeliveryArtifactCsv(rows),
            GovernanceReportArtifactFormatDto.Xlsx => XlsxWorkbookWriter.CreateWorkbook(
            [
                new XlsxWorksheet(
                    "Delivery",
                    ["Field", "Value"],
                    rows.Select(static row => (IReadOnlyList<object?>)[row.Key, row.Value]).ToArray())
            ]),
            GovernanceReportArtifactFormatDto.Html => BuildDeliveryArtifactHtml(rows),
            GovernanceReportArtifactFormatDto.Pdf => BuildDeliveryArtifactPdf(rows),
            _ => JsonSerializer.SerializeToUtf8Bytes(rows.ToDictionary(static row => row.Key, static row => row.Value))
        };
    }

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
            versionStamp);

        return format switch
        {
            GovernanceReportArtifactFormatDto.Csv => BuildDeliveryArtifactCsv(rows),
            GovernanceReportArtifactFormatDto.Xlsx => XlsxWorkbookWriter.CreateWorkbook(
            [
                new XlsxWorksheet(
                    "ReportingRun",
                    ["Field", "Value"],
                    rows.Select(static row => (IReadOnlyList<object?>)[row.Key, row.Value]).ToArray())
            ]),
            GovernanceReportArtifactFormatDto.Html => BuildDeliveryArtifactHtml(rows),
            GovernanceReportArtifactFormatDto.Pdf => BuildDeliveryArtifactPdf(rows),
            _ => JsonSerializer.SerializeToUtf8Bytes(rows.ToDictionary(static row => row.Key, static row => row.Value))
        };
    }

    private static byte[] BuildReportingRunDeliveryArtifactContent(
        string packageId,
        string reportingRunId,
        string templateId,
        string? scheduleId,
        IReadOnlyList<string> sourceArtifacts,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        string versionStamp)
    {
        var rows = BuildReportingRunDeliveryArtifactRows(
            packageId,
            reportingRunId,
            templateId,
            scheduleId,
            sourceArtifacts,
            distributionId,
            artifactName,
            format,
            contentType,
            retainedPath,
            versionStamp);

        return format switch
        {
            GovernanceReportArtifactFormatDto.Csv => BuildDeliveryArtifactCsv(rows),
            GovernanceReportArtifactFormatDto.Xlsx => XlsxWorkbookWriter.CreateWorkbook(
            [
                new XlsxWorksheet(
                    "ReportingRun",
                    ["Field", "Value"],
                    rows.Select(static row => (IReadOnlyList<object?>)[row.Key, row.Value]).ToArray())
            ]),
            GovernanceReportArtifactFormatDto.Html => BuildDeliveryArtifactHtml(rows),
            GovernanceReportArtifactFormatDto.Pdf => BuildDeliveryArtifactPdf(rows),
            _ => JsonSerializer.SerializeToUtf8Bytes(rows.ToDictionary(static row => row.Key, static row => row.Value))
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
        new("versionStamp", versionStamp)
    ];

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReportingRunDeliveryArtifactRows(
        string packageId,
        string reportingRunId,
        string templateId,
        string? scheduleId,
        IReadOnlyList<string> sourceArtifacts,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        string versionStamp) =>
    [
        new("packageId", packageId),
        new("reportingRunId", reportingRunId),
        new("templateId", templateId),
        new("scheduleId", scheduleId ?? ""),
        new("sourceArtifacts", string.Join(";", sourceArtifacts)),
        new("distributionId", distributionId),
        new("artifactName", artifactName),
        new("format", format.ToString()),
        new("contentType", contentType),
        new("retainedPath", retainedPath),
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

    private static byte[] BuildDeliveryArtifactHtml(IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><title>Report Pack Delivery Artifact</title></head><body>");
        builder.AppendLine("<h1>Report Pack Delivery Artifact</h1>");
        builder.AppendLine("<table><thead><tr><th>Field</th><th>Value</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            builder.Append("<tr><td>")
                .Append(EscapeHtml(row.Key))
                .Append("</td><td>")
                .Append(EscapeHtml(row.Value))
                .AppendLine("</td></tr>");
        }

        builder.AppendLine("</tbody></table></body></html>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string EscapeHtml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static byte[] BuildDeliveryArtifactPdf(IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 10 Tf");
        var y = 760;
        foreach (var row in rows.Take(40))
        {
            content.AppendLine($"1 0 0 1 72 {y} Tm");
            content.AppendLine($"({EscapePdfText($"{row.Key}: {row.Value}")}) Tj");
            y -= 14;
        }

        content.AppendLine("ET");
        return BuildSinglePagePdf(content.ToString());
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
