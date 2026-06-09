using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
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
        var token = BuildSecureToken(record.ReportId, policy.DistributionId, attemptId);
        var artifacts = formats
            .Select(format => BuildArtifact(record, policy, packageId, attemptId, token, format))
            .ToArray();
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
            retainedManifestPath,
            record.Publication?.EvidenceHash,
            BuildIntegritySummary(artifacts, record.Publication?.EvidenceHash));
    }

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
        BuildDeliveryArtifactContent(
            package.PackageId,
            package.ReportId,
            package.DistributionId,
            artifact.ArtifactName,
            artifact.Format,
            artifact.ContentType,
            artifact.RetainedPath,
            package.PublicationEvidenceHash,
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

    private static IReadOnlyList<KeyValuePair<string, string>> BuildDeliveryArtifactRows(
        string packageId,
        Guid reportId,
        string distributionId,
        string artifactName,
        GovernanceReportArtifactFormatDto format,
        string contentType,
        string retainedPath,
        string? publicationEvidenceHash,
        string versionStamp) =>
    [
        new("packageId", packageId),
        new("reportId", reportId.ToString("D")),
        new("distributionId", distributionId),
        new("artifactName", artifactName),
        new("format", format.ToString()),
        new("contentType", contentType),
        new("retainedPath", retainedPath),
        new("publicationEvidenceHash", string.IsNullOrWhiteSpace(publicationEvidenceHash) ? "" : publicationEvidenceHash.Trim()),
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
