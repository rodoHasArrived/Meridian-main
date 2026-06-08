using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportPackDeliveryStoreOptions(string SnapshotPath);

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
            var package = state == ReportPackDeliveryStateDto.Delivered
                ? BuildDeliveryPackage(record, policy, attemptId, attemptNumber, formats, deliveryMode)
                : null;
            var packageEvidenceLinks = package?.Artifacts
                .Select(static artifact => new ReportPackEvidenceLinkDto(
                    artifact.EvidenceId,
                    artifact.ArtifactName,
                    artifact.RetainedPath,
                    "report-pack-delivery"))
                .ToArray() ?? [];
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
                NormalizeEvidenceLinks((evidenceLinks ?? []).Concat(packageEvidenceLinks).ToArray()),
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
        ReportPackDeliveryModeDto? requestedMode)
    {
        var formats = NormalizeFormats(requestedFormats);
        var mode = requestedMode ?? InferDeliveryMode(policy.Channel);
        var packageId = $"pkg-{record.ReportId:N}-{policy.DistributionId}-{attemptNumber}";
        var retainedManifestPath = $"workstation/reporting/deliveries/{record.ReportId:N}/{policy.DistributionId}/{attemptNumber}/manifest.json";
        var artifacts = formats
            .Select(format => BuildArtifact(record, policy, packageId, format))
            .ToArray();
        var token = BuildSecureToken(record.ReportId, policy.DistributionId, attemptId);
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
            DateTimeOffset.UtcNow,
            retainedManifestPath);
    }

    private static ReportPackDeliveryArtifactDto BuildArtifact(
        ReportPackWorkflowRecordDto record,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        string packageId,
        GovernanceReportArtifactFormatDto format)
    {
        var extension = ResolveExtension(format);
        var artifactName = $"{record.TemplateId.Name}-v{record.TemplateId.Version}-{record.Period}-{policy.DistributionId}.{extension}";
        var retainedPath = $"workstation/reporting/deliveries/{record.ReportId:N}/{policy.DistributionId}/{packageId}/{artifactName}";
        var evidenceId = $"delivery-artifact:{record.ReportId:N}:{policy.DistributionId}:{format.ToString().ToLowerInvariant()}";
        var byteSize = Encoding.UTF8.GetByteCount($"{record.ReportId:D}|{policy.DistributionId}|{format}|{retainedPath}|{record.Publication?.EvidenceHash}");
        return new ReportPackDeliveryArtifactDto(
            format,
            artifactName,
            ResolveContentType(format),
            retainedPath,
            byteSize,
            evidenceId);
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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{reportId:D}:{distributionId}:{attemptId:D}:report-pack-delivery"));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..24];
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
