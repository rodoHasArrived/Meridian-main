using Meridian.Contracts.Workstation;
using Meridian.Contracts.Api;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportingRunCommandService
{
    private readonly IReportingOrchestrationService _orchestrationService;
    private readonly IReportingTemplateCatalog _templateCatalog;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;
    private readonly ReportWriterDatasetSourceService? _datasetSourceService;

    public ReportingRunCommandService(
        IReportingOrchestrationService orchestrationService,
        IReportingTemplateCatalog templateCatalog,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        ReportWriterDatasetSourceService? datasetSourceService = null)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _governedTemplateCatalog = governedTemplateCatalog;
        _datasetSourceService = datasetSourceService;
    }

    public async Task<ReportingRunResultDto> RunAsync(
        ReportingRunRequestDto request,
        string fallbackActor,
        CancellationToken cancellationToken = default) =>
        await RunAsync(request, fallbackActor, accessContext: null, cancellationToken).ConfigureAwait(false);

    public async Task<ReportingRunResultDto> RunAsync(
        ReportingRunRequestDto request,
        string fallbackActor,
        ReportAccessQueryContext? accessContext,
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
        var accessEvaluation = _governedTemplateCatalog?.EvaluateAccess(templateId, accessContext)
            ?? ReportAccessPolicyEvaluator.Evaluate(null, accessContext);
        if (!accessEvaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(accessEvaluation.Reason);
        }

        var template = _templateCatalog.Get(templateId);
        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(requestedAtUtc.UtcDateTime);
        var actor = string.IsNullOrWhiteSpace(request.RequestedBy) ? fallbackActor.Trim() : request.RequestedBy.Trim();
        var jobId = string.IsNullOrWhiteSpace(request.JobId)
            ? BuildDefaultJobId(templateId, requestedAtUtc)
            : request.JobId.Trim();
        var datasetRows = ResolveDatasetRows(request, template, accessContext);
        var datasetSourceEvidence = ResolveDatasetSourceEvidence(request, template);

        var manifest = await _orchestrationService.ExecuteAsync(
            new ReportingJobContract(
                jobId,
                template.TemplateId,
                asOfDate,
                ReportingRunTrigger.AdHoc,
                request.MaxRetries,
                actor,
                requestedAtUtc,
                DatasetRows: datasetRows,
                ReportWriterDatasetSourceId: datasetSourceEvidence.SourceId,
                ReportWriterDatasetSourceLabel: datasetSourceEvidence.Label,
                AccessPolicy: template.AccessPolicy),
            cancellationToken).ConfigureAwait(false);

        return new ReportingRunResultDto(ProjectRun(manifest, _orchestrationService.GetAudit(manifest.RunId), template));
    }

    private static WorkstationReportingRunPayload ProjectRun(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        ReportingTemplateMetadata template) =>
        new(
            manifest.RunId,
            manifest.TemplateId,
            template.Family.ToString(),
            manifest.Status.ToString(),
            manifest.Trigger.ToString(),
            manifest.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
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
                    BuildRunAuditRoute(manifest.RunId),
                    "GET",
                    true,
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
            ],
            GeneratedReportWriterGrids: BuildGeneratedReportWriterGrids(manifest, template).ToArray(),
            ReportWriterDatasetSourceId: manifest.ReportWriterDatasetSourceId,
            ReportWriterDatasetSourceLabel: manifest.ReportWriterDatasetSourceLabel,
            ReportWriterDatasetRowCount: manifest.ReportWriterDatasetRowCount);

    private static IEnumerable<WorkstationGeneratedReportWriterGridPayload> BuildGeneratedReportWriterGrids(
        ReportingOutputManifest manifest,
        ReportingTemplateMetadata template)
    {
        if (!manifest.ReportWriterGrids.IsDefaultOrEmpty)
        {
            foreach (var grid in manifest.ReportWriterGrids)
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
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
            }

            yield break;
        }

        var templateGrids = template.ReportWriterGrids?
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? [];

        foreach (var artifact in manifest.Artifacts.Where(static artifact => artifact.StartsWith("report-writer://", StringComparison.OrdinalIgnoreCase)))
        {
            var gridId = ExtractReportWriterGridId(artifact);
            if (gridId is null)
            {
                continue;
            }

            if (templateGrids.TryGetValue(gridId, out var grid))
            {
                var dimensionCount = (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0);
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId.Trim(),
                    string.IsNullOrWhiteSpace(grid.Title) ? grid.GridId.Trim() : grid.Title.Trim(),
                    grid.Kind.ToString(),
                    artifact,
                    dimensionCount,
                    grid.Metrics?.Count ?? 0,
                    grid.Formulas?.Count ?? 0,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
                continue;
            }

            var generatedValidation = BuildGeneratedGridValidationSummary(gridId, manifest.RenderedReportWriterGrids);
            yield return new WorkstationGeneratedReportWriterGridPayload(
                gridId,
                gridId,
                "Generated",
                artifact,
                0,
                0,
                0,
                generatedValidation.Summary,
                generatedValidation.Passed,
                generatedValidation.Warning,
                generatedValidation.Failed);
        }
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

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> ResolveReportWriterGridValidationChecks(
        ReportWriterGridRenderDto grid) =>
        grid.ValidationChecks is { Count: > 0 }
            ? grid.ValidationChecks
            :
            [
                new ReportWriterGridValidationCheckDto(
                    "row-count",
                    grid.Lineage is null || grid.Lineage.OutputRowCount == grid.Rows.Count ? "Passed" : "Failed",
                    grid.Lineage is null
                        ? $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); no lineage row count was retained."
                        : $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); lineage expected {grid.Lineage.OutputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}."),
                new ReportWriterGridValidationCheckDto(
                    "column-dictionary",
                    "Passed",
                    $"Dictionary covers {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} rendered column(s)."),
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

    private static string? ExtractReportWriterGridId(string artifact)
    {
        const string marker = "/grids/";
        var index = artifact.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var gridId = artifact[(index + marker.Length)..].Trim();
        return string.IsNullOrWhiteSpace(gridId) ? null : gridId;
    }

    private static string BuildDefaultJobId(string templateId, DateTimeOffset requestedAtUtc) =>
        $"adhoc-{NormalizeToken(templateId)}-{requestedAtUtc:yyyyMMddHHmmssfff}";

    private static string BuildRunAuditRoute(string runId) =>
        UiApiRoutes.WithParam(UiApiRoutes.ReportingRunAuditTrail, "runId", runId);

    private IReadOnlyList<IReadOnlyDictionary<string, string>>? ResolveDatasetRows(
        ReportingRunRequestDto request,
        ReportingTemplateMetadata template,
        ReportAccessQueryContext? accessContext)
    {
        if (request.DatasetRows is { Count: > 0 })
        {
            return request.DatasetRows;
        }

        if (_datasetSourceService is null || template.ReportWriterGrids is not { Count: > 0 })
        {
            return request.DatasetRows;
        }

        var resolvedRows = _datasetSourceService.BuildDatasetRowsForSource(request.DatasetSourceId, accessContext);
        return resolvedRows.Count == 0 ? request.DatasetRows : resolvedRows;
    }

    private DatasetSourceEvidence ResolveDatasetSourceEvidence(
        ReportingRunRequestDto request,
        ReportingTemplateMetadata template)
    {
        if (template.ReportWriterGrids is not { Count: > 0 })
        {
            return new DatasetSourceEvidence(null, null);
        }

        if (request.DatasetRows is { Count: > 0 })
        {
            return new DatasetSourceEvidence("custom-request-dataset", "Custom request dataset");
        }

        if (_datasetSourceService is null)
        {
            return new DatasetSourceEvidence(null, null);
        }

        var sourceId = string.IsNullOrWhiteSpace(request.DatasetSourceId)
            ? "retained-reporting-rows"
            : request.DatasetSourceId.Trim();
        return new DatasetSourceEvidence(
            sourceId,
            ReportWriterDatasetSourceService.GetKnownDatasetSourceLabel(sourceId) ?? sourceId);
    }

    private sealed record DatasetSourceEvidence(string? SourceId, string? Label);

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
