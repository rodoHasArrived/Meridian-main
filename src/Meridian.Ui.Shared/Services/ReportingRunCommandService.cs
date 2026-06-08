using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportingRunCommandService
{
    private readonly IReportingOrchestrationService _orchestrationService;
    private readonly IReportingTemplateCatalog _templateCatalog;

    public ReportingRunCommandService(
        IReportingOrchestrationService orchestrationService,
        IReportingTemplateCatalog templateCatalog)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
    }

    public async Task<ReportingRunResultDto> RunAsync(
        ReportingRunRequestDto request,
        string fallbackActor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackActor);
        if (request.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "maxRetries must be zero or greater.");
        }

        var requestedAtUtc = DateTimeOffset.UtcNow;
        var templateId = request.TemplateId.Trim();
        var template = _templateCatalog.Get(templateId);
        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(requestedAtUtc.UtcDateTime);
        var actor = string.IsNullOrWhiteSpace(request.RequestedBy) ? fallbackActor.Trim() : request.RequestedBy.Trim();
        var jobId = string.IsNullOrWhiteSpace(request.JobId)
            ? BuildDefaultJobId(templateId, requestedAtUtc)
            : request.JobId.Trim();

        var manifest = await _orchestrationService.ExecuteAsync(
            new ReportingJobContract(
                jobId,
                template.TemplateId,
                asOfDate,
                ReportingRunTrigger.AdHoc,
                request.MaxRetries,
                actor,
                requestedAtUtc),
            cancellationToken).ConfigureAwait(false);

        return new ReportingRunResultDto(ProjectRun(manifest, _orchestrationService.GetAudit(manifest.RunId), template.Family.ToString()));
    }

    private static WorkstationReportingRunPayload ProjectRun(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string family) =>
        new(
            manifest.RunId,
            manifest.TemplateId,
            family,
            manifest.Status.ToString(),
            manifest.Trigger.ToString(),
            manifest.AttemptCount,
            manifest.Sections.Length,
            manifest.Sections.Count(static section => section.Lineage is not null),
            manifest.Artifacts
                .Concat([
                    $"reporting-run://{manifest.RunId}/manifest",
                    $"reporting-run://{manifest.RunId}/audit"
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            auditTrail
                .OrderBy(static audit => audit.TimestampUtc)
                .Select(static audit => audit.Action)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            manifest.FailureReason,
            DrilldownLinks:
            [
                new WorkstationReportingRunLinkPayload(
                    $"{manifest.RunId}:manifest",
                    "manifest",
                    "Run manifest",
                    $"reporting-run://{manifest.RunId}/manifest",
                    "GET",
                    false,
                    "ReportingOrchestration"),
                new WorkstationReportingRunLinkPayload(
                    $"{manifest.RunId}:audit",
                    "audit",
                    "Approval audit trail",
                    $"reporting-run://{manifest.RunId}/audit",
                    "GET",
                    false,
                    "ReportingOrchestration")
            ],
            NextActions:
            [
                new WorkstationReportingRunNextActionPayload(
                    $"{manifest.RunId}:submit",
                    "approval-submit",
                    "Submit run for review",
                    $"reporting-run://{manifest.RunId}/approval/submit",
                    "POST",
                    manifest.Status == ReportingRunStatus.Draft,
                    manifest.Status == ReportingRunStatus.Draft ? null : "Only draft reporting runs can be submitted.",
                    false)
            ]);

    private static string BuildDefaultJobId(string templateId, DateTimeOffset requestedAtUtc) =>
        $"adhoc-{NormalizeToken(templateId)}-{requestedAtUtc:yyyyMMddHHmmssfff}";

    private static string NormalizeToken(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(static c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "report" : normalized;
    }
}
