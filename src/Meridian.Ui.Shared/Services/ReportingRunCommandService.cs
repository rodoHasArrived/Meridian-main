using Meridian.Contracts.Workstation;
using Meridian.Contracts.Api;
using Meridian.Reporting;
using Meridian.Storage.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportingRunCommandService
{
    private readonly IReportingOrchestrationService _orchestrationService;
    private readonly IReportingTemplateCatalog _templateCatalog;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;
    private readonly ReportWriterDatasetSourceService? _datasetSourceService;
    private readonly ReportingRunReadinessService _readinessService;
    private readonly ReportingRunCertificationService _certificationService;
    private readonly IReportingGovernanceEndpointCoordinator? _governanceCoordinator;

    public ReportingRunCommandService(
        IReportingOrchestrationService orchestrationService,
        IReportingTemplateCatalog templateCatalog,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        ReportWriterDatasetSourceService? datasetSourceService = null,
        ReportingRunReadinessService? readinessService = null,
        ReportingRunCertificationService? certificationService = null,
        IReportingGovernanceEndpointCoordinator? governanceCoordinator = null)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _governedTemplateCatalog = governedTemplateCatalog;
        _datasetSourceService = datasetSourceService;
        _readinessService = readinessService ?? new ReportingRunReadinessService(
            templateCatalog,
            governedTemplateCatalog);
        _certificationService = certificationService ?? new ReportingRunCertificationService();
        _governanceCoordinator = governanceCoordinator;
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
        CancellationToken cancellationToken = default) =>
        await RunAsync(
            request,
            fallbackActor,
            accessContext,
            governanceCaller: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Executes and immediately reconciles a tenant-bound run into the canonical governed Draft.
    /// The method does not return a successful strict-mode result until exact artifacts have been
    /// retained and the governance aggregate is Succeeded/Draft.
    /// </summary>
    public async Task<ReportingRunResultDto> RunAsync(
        ReportingRunRequestDto request,
        string fallbackActor,
        ReportAccessQueryContext? accessContext,
        ReportingGovernanceCallerContext? governanceCaller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackActor);
        if (request.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "maxRetries must be zero or greater.");
        }

        if (request.AllowRestatement)
        {
            throw new InvalidOperationException(
                "Ordinary report generation cannot authorize a restatement. Use the governed restatement-request workflow.");
        }

        var requestedAtUtc = DateTimeOffset.UtcNow;
        var templateId = request.TemplateId.Trim();
        var governed = accessContext?.RequireBoundScope == true;
        if (governed && request.DatasetRows is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "Caller-supplied report rows are not accepted by the governed run endpoint. Select a server-owned dataset source.");
        }

        if (governed)
        {
            if (_governanceCoordinator is null || governanceCaller is null)
            {
                throw new ReportingGovernancePersistenceException(
                    "Canonical governance coordination is required before a tenant-bound reporting run can be returned.");
            }
            ValidateGovernanceCaller(accessContext!, governanceCaller, fallbackActor);
        }

        var readiness = await _readinessService
            .AssessAsync(request, accessContext, cancellationToken)
            .ConfigureAwait(false);
        var template = ResolveTemplate(request, templateId, accessContext);
        var certified = governed
            ? await _certificationService
                .CertifyAsync(template, readiness, accessContext!, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var effectiveReadiness = certified?.Readiness ?? readiness;
        var isFinal = effectiveReadiness.ResolvedParameters.Finality == ReportingFinalityDto.Final;
        if (!effectiveReadiness.CanGenerateDraft || isFinal && !effectiveReadiness.CanGenerateFinal)
        {
            throw new ReportingRunReadinessBlockedException(effectiveReadiness);
        }

        var asOfDate = effectiveReadiness.ResolvedParameters.AsOfDate;
        var actor = fallbackActor.Trim();
        var jobId = governed
            ? BuildDefaultJobId($"{accessContext.TenantId}-{templateId}", requestedAtUtc)
            : string.IsNullOrWhiteSpace(request.JobId)
            ? BuildDefaultJobId(templateId, requestedAtUtc)
            : request.JobId.Trim();
        var datasetRows = certified?.DatasetRows ?? ResolveDatasetRows(request, template, accessContext);
        var datasetSourceEvidence = certified is null
            ? ResolveDatasetSourceEvidence(request, template)
            : new DatasetSourceEvidence(
                certified.AuthoritativeSource.SourceId,
                "Certified durable ledger journal");

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
                AccessPolicy: template.AccessPolicy,
                RetryReason: request.RetryReason,
                AllowRestatement: false,
                ResolvedTemplate: effectiveReadiness.ResolvedTemplate,
                ResolvedParameters: effectiveReadiness.ResolvedParameters,
                Readiness: effectiveReadiness,
                OperationalScope: certified?.OperationalScope,
                ImmutableAccessScope: certified?.AccessScope,
                CertifiedSnapshot: certified?.Snapshot,
                AuthoritativeSource: certified?.AuthoritativeSource),
            cancellationToken).ConfigureAwait(false);

        if (governed)
        {
            var governedRun = await _governanceCoordinator!
                .CreateFromCompletedCertifiedManifestAsync(
                    manifest.RunId,
                    governanceCaller!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (governedRun.ExecutionState != GovernedReportingExecutionState.Succeeded
                || governedRun.GovernanceState != GovernedReportingState.Draft)
            {
                throw new ReportingGovernanceException(
                    $"Reporting run '{manifest.RunId}' was not durably reconciled to Succeeded/Draft governance state.");
            }
        }

        return new ReportingRunResultDto(ProjectRun(manifest, _orchestrationService.GetAudit(manifest.RunId), template));
    }

    private ReportingTemplateMetadata ResolveTemplate(
        ReportingRunRequestDto request,
        string templateId,
        ReportAccessQueryContext? accessContext)
    {
        if (_governedTemplateCatalog is not null && accessContext?.RequireBoundScope == true)
        {
            return request.Template is null
                ? _governedTemplateCatalog.Get(templateId, accessContext)
                : _governedTemplateCatalog.Get(request.Template, accessContext);
        }

        return request.Template is null
            ? _templateCatalog.Get(templateId)
            : _templateCatalog.Get(request.Template);
    }

    private static void ValidateGovernanceCaller(
        ReportAccessQueryContext accessContext,
        ReportingGovernanceCallerContext caller,
        string fallbackActor)
    {
        if (!string.Equals(caller.ActorId, accessContext.ActorPrincipalId, StringComparison.Ordinal)
            || !string.Equals(caller.ActorId, fallbackActor.Trim(), StringComparison.Ordinal)
            || !string.Equals(caller.TenantId, accessContext.TenantId, StringComparison.Ordinal)
            || !string.Equals(caller.CompanyId, accessContext.CompanyId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Reporting access and governance caller contexts must resolve to the same server-owned actor, tenant, and company.");
        }
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
            manifest.Sections.Count(static section =>
                !string.IsNullOrWhiteSpace(section.DatasetSnapshotId)
                && !string.IsNullOrWhiteSpace(section.ReconciliationCheckpointId)),
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
            ReportWriterDatasetRowCount: manifest.ReportWriterDatasetRowCount,
            RunSeriesId: ResolveRunSeriesId(manifest),
            RunAttemptOrdinal: ResolveRunAttemptOrdinal(manifest),
            PriorRunId: manifest.PriorRunId,
            RetryReason: manifest.RetryReason,
            LatestGeneratedRunId: manifest.RunId,
            IsLatestGenerated: true,
            ComparisonSummary: BuildRunComparisonSummary(manifest),
            ChangedLineCount: CountChangedLines(manifest),
            AddedLineCount: CountAddedLines(manifest),
            RemovedLineCount: CountRemovedLines(manifest),
            ResolvedTemplate: manifest.ResolvedTemplate,
            ResolvedParameters: manifest.ResolvedParameters,
            Readiness: manifest.Readiness);

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

    private static string ResolveRunSeriesId(ReportingOutputManifest manifest) =>
        string.IsNullOrWhiteSpace(manifest.RunSeriesId) ? manifest.RunId : manifest.RunSeriesId.Trim();

    private static int ResolveRunAttemptOrdinal(ReportingOutputManifest manifest) =>
        manifest.RunAttemptOrdinal is > 0 ? manifest.RunAttemptOrdinal.Value : 1;

    private static string? BuildRunComparisonSummary(ReportingOutputManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.PriorRunId))
        {
            return "First generated attempt in this run series.";
        }

        var changed = CountChangedLines(manifest);
        var added = CountAddedLines(manifest);
        var removed = CountRemovedLines(manifest);
        return manifest.ReportWriterGridDiffs.IsDefaultOrEmpty
            ? $"Compared with {manifest.PriorRunId}; no report-writer line comparison was retained."
            : $"{changed} changed, {added} added, {removed} removed lines compared with {manifest.PriorRunId}.";
    }

    private static int CountChangedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.ChangedRowCount);

    private static int CountAddedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.AddedRowCount);

    private static int CountRemovedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.RemovedRowCount);

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

public sealed class ReportingRunReadinessBlockedException : InvalidOperationException
{
    public ReportingRunReadinessBlockedException(ReportingRunReadinessDto readiness)
        : base($"Reporting run readiness is {readiness?.Status}; the requested run cannot be generated.")
    {
        Readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public ReportingRunReadinessDto Readiness { get; }
}
