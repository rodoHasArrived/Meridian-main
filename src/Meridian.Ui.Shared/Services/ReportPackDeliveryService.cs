using System.Text.Json;
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
            request.EvidenceLinks);
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
            request.EvidenceLinks);
    }

    private ReportPackDeliveryAttemptDto AppendAttempt(
        Guid reportId,
        string distributionId,
        ReportPackDeliveryStateDto state,
        string actor,
        string? deliveryReference,
        string? note,
        string? failureReason,
        IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks)
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
                NormalizeEvidenceLinks(evidenceLinks));
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
