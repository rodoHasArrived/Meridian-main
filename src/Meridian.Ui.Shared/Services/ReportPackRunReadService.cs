using System.Globalization;
using Meridian.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportPackRunReadService
{
    private const int DefaultRecentRunLimit = 12;

    private readonly IReportingTemplateCatalog _templateCatalog;
    private readonly IReportingRunStore? _runStore;
    private readonly ReportPackWorkflowService? _workflowService;
    private readonly ReportTemplateRegistryService? _templateRegistry;
    private readonly ReportPackDeliveryService? _deliveryService;
    private readonly ReportingScheduleService? _scheduleService;

    public ReportPackRunReadService(
        IReportingTemplateCatalog templateCatalog,
        IReportingRunStore? runStore = null,
        ReportPackWorkflowService? workflowService = null,
        ReportTemplateRegistryService? templateRegistry = null,
        ReportPackDeliveryService? deliveryService = null,
        ReportingScheduleService? scheduleService = null)
    {
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _runStore = runStore;
        _workflowService = workflowService;
        _templateRegistry = templateRegistry;
        _deliveryService = deliveryService;
        _scheduleService = scheduleService;
    }

    public WorkstationReportingPayload BuildPayload(int recentRunLimit = DefaultRecentRunLimit) =>
        BuildPayload(accessContext: null, recentRunLimit);

    public WorkstationReportingPayload BuildPayload(
        ReportAccessQueryContext? accessContext,
        int recentRunLimit = DefaultRecentRunLimit)
    {
        var profiles = BuildProfiles();
        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();
        var templates = BuildTemplates(accessContext);
        var familyByTemplate = templates
            .GroupBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Family, StringComparer.OrdinalIgnoreCase);
        var workflowRecords = FilterWorkflowRecords(_workflowService?.ListRecords(200) ?? [], accessContext);
        var runs = BuildRecentRuns(Math.Clamp(recentRunLimit, 1, 200), familyByTemplate, workflowRecords);
        var deliveryAttempts = FilterDeliveryAttempts(
            _deliveryService?.ListAttempts(500) ?? [],
            accessContext,
            templates,
            workflowRecords);
        var schedules = FilterSchedules(
            _scheduleService?.ListSchedules(100) ?? [],
            accessContext,
            templates);
        var scheduleDeliveryPlans = BuildScheduleDeliveryPlans(schedules, deliveryAttempts);
        var distributions = BuildDistributionRecords(workflowRecords, deliveryAttempts);
        var pendingDistributionCount = distributions.Count(static distribution => distribution.PendingItems > 0);
        var selectedFundProfileId = ResolveSelectedFundProfileId(workflowRecords);
        var reportLineProvenanceExplorer = FinancialRecordExplorerReadService.BuildReportLineProvenanceExplorer(
            workflowRecords,
            deliveryAttempts);

        return new WorkstationReportingPayload(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            Profiles: profiles,
            ReportPackDistributions: distributions,
            Summary: $"{profiles.Length} export/reporting profiles are available for Accounting and Reporting workflows; {distributions.Length} distribution recipients are visible; {pendingDistributionCount} have pending work.",
            Templates: templates,
            RecentRuns: runs.Select(static run => run.Payload).ToArray(),
            Schedules: schedules,
            DeliveryAttempts: deliveryAttempts,
            SelectedFundProfileId: selectedFundProfileId,
            ScheduleDeliveryPlans: scheduleDeliveryPlans,
            ReportLineProvenanceExplorer: reportLineProvenanceExplorer);
    }

    public static WorkstationReportingPayload BuildFallbackPayload() =>
        new ReportPackRunReadService(new DefaultReportingTemplateCatalog()).BuildPayload();

    public static ReportPackDistributionPolicy ResolveDistributionPolicy(string distributionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distributionId);
        return DistributionPolicies.FirstOrDefault(policy =>
                   string.Equals(policy.DistributionId, distributionId.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Unknown report-pack distribution '{distributionId}'.");
    }

    private static WorkstationReportingProfilePayload[] BuildProfiles() =>
        ExportProfile.GetBuiltInProfiles()
            .Select(static profile => new WorkstationReportingProfilePayload(
                Id: profile.Id,
                Name: profile.Name,
                TargetTool: profile.TargetTool,
                Format: profile.Format.ToString(),
                Description: profile.Description ?? string.Empty,
                LoaderScript: profile.IncludeLoaderScript,
                DataDictionary: profile.IncludeDataDictionary))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? ResolveSelectedFundProfileId(IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords) =>
        workflowRecords
            .Where(static record => !string.IsNullOrWhiteSpace(record.FundProfileId))
            .OrderByDescending(static record => record.UpdatedAt)
            .Select(static record => record.FundProfileId.Trim())
            .FirstOrDefault();

    private WorkstationReportingTemplatePayload[] BuildTemplates(ReportAccessQueryContext? accessContext)
    {
        if (_templateRegistry is not null)
        {
            return _templateRegistry
                .List()
                .Where(record => IsAccessible(record.Definition.AccessPolicy, accessContext))
                .Select(record => ProjectTemplateRecord(record, accessContext))
                .OrderBy(static template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static template => template.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return _templateCatalog
            .ListTemplates()
            .Select(template => ProjectCatalogTemplate(template, accessContext))
            .OrderBy(static template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ReportPackWorkflowRecordDto> FilterWorkflowRecords(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null)
        {
            return records;
        }

        return records
            .Where(record => IsAccessible(record.AccessPolicy, accessContext))
            .ToArray();
    }

    private static IReadOnlyList<ReportingScheduleRecordDto> FilterSchedules(
        IReadOnlyList<ReportingScheduleRecordDto> schedules,
        ReportAccessQueryContext? accessContext,
        IReadOnlyList<WorkstationReportingTemplatePayload> visibleTemplates)
    {
        if (accessContext is null)
        {
            return schedules;
        }

        var visibleTemplateIds = visibleTemplates
            .Where(static template => template.IsAccessible)
            .Select(static template => template.TemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return schedules
            .Where(schedule => visibleTemplateIds.Contains(schedule.TemplateId))
            .ToArray();
    }

    private static IReadOnlyList<ReportPackDeliveryAttemptDto> FilterDeliveryAttempts(
        IReadOnlyList<ReportPackDeliveryAttemptDto> attempts,
        ReportAccessQueryContext? accessContext,
        IReadOnlyList<WorkstationReportingTemplatePayload> visibleTemplates,
        IReadOnlyList<ReportPackWorkflowRecordDto> visibleWorkflowRecords)
    {
        if (accessContext is null)
        {
            return attempts;
        }

        var visibleTemplateIds = visibleTemplates
            .Where(static template => template.IsAccessible)
            .Select(static template => template.TemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleReportIds = visibleWorkflowRecords
            .Select(static record => record.ReportId)
            .ToHashSet();
        return attempts
            .Where(attempt =>
                visibleReportIds.Contains(attempt.ReportId)
                || (!string.IsNullOrWhiteSpace(attempt.Package?.ReportingTemplateId)
                    && visibleTemplateIds.Contains(attempt.Package.ReportingTemplateId)))
            .ToArray();
    }

    private static bool IsAccessible(ReportAccessPolicyDto? policy, ReportAccessQueryContext? accessContext) =>
        accessContext is null || ReportAccessPolicyEvaluator.Evaluate(policy, accessContext).IsAccessible;

    private static WorkstationReportingTemplatePayload ProjectCatalogTemplate(
        ReportingTemplateMetadata template,
        ReportAccessQueryContext? accessContext)
    {
        var accessPolicy = ReportAccessPolicyEvaluator.Normalize(null);
        var accessEvaluation = EvaluateForProjection(accessPolicy, accessContext);
        return new WorkstationReportingTemplatePayload(
            template.TemplateId,
            template.Family.ToString(),
            template.Name,
            template.Version,
            template.Sections.ToArray(),
            LifecycleStatus: ReportTemplateLifecycleStatusDto.Approved.ToString(),
            IsBuiltIn: true,
            IsLatestApproved: true,
            ApprovalSummary: $"Built-in approved template for {template.Family}.",
            AuthoringRoute: $"/api/fund-structure/reporting/templates/{template.TemplateId}/versions/1",
            ReportWriterGrids: [],
            AccessMode: accessPolicy.Mode.ToString(),
            AccessSummary: ReportAccessPolicyEvaluator.BuildSummary(accessPolicy),
            IsAccessible: accessEvaluation.IsAccessible);
    }

    private static WorkstationReportingTemplatePayload ProjectTemplateRecord(
        ReportTemplateGovernanceRecordDto record,
        ReportAccessQueryContext? accessContext)
    {
        var definition = record.Definition;
        var accessPolicy = ReportAccessPolicyEvaluator.Normalize(definition.AccessPolicy);
        var accessEvaluation = EvaluateForProjection(accessPolicy, accessContext);
        return new WorkstationReportingTemplatePayload(
            definition.TemplateId.Name,
            record.Family,
            definition.DisplayName,
            definition.TemplateId.Version.ToString(),
            definition.Sections?.ToArray() ?? [],
            LifecycleStatus: record.Status.ToString(),
            IsBuiltIn: record.IsBuiltIn,
            IsLatestApproved: record.IsLatestApproved,
            ApprovalSummary: BuildTemplateApprovalSummary(record),
            AuthoringRoute: $"/api/fund-structure/reporting/templates/{Uri.EscapeDataString(definition.TemplateId.Name)}/versions/{definition.TemplateId.Version}",
            ReportWriterGrids: ProjectReportWriterGrids(definition),
            AccessMode: accessPolicy.Mode.ToString(),
            AccessSummary: ReportAccessPolicyEvaluator.BuildSummary(accessPolicy),
            IsAccessible: accessEvaluation.IsAccessible,
            CreatedBy: record.CreatedBy,
            CreatedAt: record.CreatedAt,
            UpdatedBy: record.UpdatedBy,
            UpdatedAt: record.UpdatedAt,
            SubmittedBy: record.SubmittedBy,
            SubmittedAt: record.SubmittedAt,
            ApprovedBy: record.ApprovedBy,
            ApprovedAt: record.ApprovedAt,
            RejectedBy: record.RejectedBy,
            RejectedAt: record.RejectedAt,
            DecisionRationale: record.DecisionRationale,
            ApprovalReference: record.ApprovalReference,
            BasedOnTemplateId: record.BasedOnTemplateId,
            AuditTrail: record.AuditTrail,
            ValidationIssues: record.ValidationIssues);
    }

    private static ReportAccessEvaluationDto EvaluateForProjection(
        ReportAccessPolicyDto accessPolicy,
        ReportAccessQueryContext? accessContext) =>
        accessContext is null
            ? new ReportAccessEvaluationDto(true, ReportAccessPolicyEvaluator.BuildSummary(accessPolicy), [])
            : ReportAccessPolicyEvaluator.Evaluate(accessPolicy, accessContext);

    private static IReadOnlyList<WorkstationReportWriterGridPayload> ProjectReportWriterGrids(ReportTemplateDefinitionDto definition) =>
        definition.Grids?
            .Select(static grid => new WorkstationReportWriterGridPayload(
                grid.GridId,
                grid.Title,
                grid.Kind.ToString(),
                (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0),
                grid.Metrics?.Count ?? 0,
                grid.Formulas?.Count ?? 0,
                RowFields: grid.RowFields?.ToArray() ?? [],
                ColumnFields: grid.ColumnFields?.ToArray() ?? [],
                Metrics: grid.Metrics?
                    .Select(static metric => new WorkstationReportWriterMetricPayload(
                        metric.Name,
                        metric.SourceField,
                        metric.Function.ToString(),
                        metric.Label))
                    .ToArray() ?? [],
                Formulas: grid.Formulas?
                    .Select(static formula => new WorkstationReportWriterFormulaPayload(
                        formula.Name,
                        formula.Expression,
                        formula.Label))
                    .ToArray() ?? [],
                TopN: grid.TopN,
                SortBy: grid.SortBy,
                SortDescending: grid.SortDescending,
                Filters: grid.Filters?
                    .Select(static filter => new WorkstationReportWriterFilterPayload(
                        filter.Field,
                        filter.Operator.ToString(),
                        filter.Value,
                        filter.Label))
                    .ToArray() ?? []))
            .ToArray() ?? [];

    private static string BuildTemplateApprovalSummary(ReportTemplateGovernanceRecordDto record) =>
        record.Status switch
        {
            ReportTemplateLifecycleStatusDto.Approved when !string.IsNullOrWhiteSpace(record.ApprovalReference) =>
                $"Approved by {record.ApprovedBy ?? record.UpdatedBy} ({record.ApprovalReference}).",
            ReportTemplateLifecycleStatusDto.Approved =>
                $"Approved by {record.ApprovedBy ?? record.UpdatedBy}.",
            ReportTemplateLifecycleStatusDto.InReview =>
                $"In review after submission by {record.SubmittedBy ?? record.UpdatedBy}.",
            ReportTemplateLifecycleStatusDto.Rejected =>
                $"Rejected by {record.RejectedBy ?? record.UpdatedBy}.",
            ReportTemplateLifecycleStatusDto.Draft =>
                $"Draft by {record.CreatedBy}.",
            _ => record.DecisionRationale ?? record.Status.ToString()
        };

    private UnifiedReportingRun[] BuildRecentRuns(
        int limit,
        IReadOnlyDictionary<string, string> familyByTemplate,
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords)
    {
        var genericRuns = _runStore?
            .ListRuns(limit)
            .Select(run => ProjectGenericRun(run, familyByTemplate)) ?? [];
        var workflowRuns = workflowRecords
            .Take(limit)
            .Select(ProjectWorkflowRun);

        return genericRuns
            .Concat(workflowRuns)
            .OrderByDescending(static run => run.UpdatedAtUtc)
            .ThenBy(static run => run.Payload.RunId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public static WorkstationReportPackDistributionPayload[] BuildDistributionRecords(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<ReportPackDeliveryAttemptDto>? deliveryAttempts = null)
    {
        var records = workflowRecords
            .OrderByDescending(static record => record.UpdatedAt)
            .ThenBy(static record => record.ReportId)
            .ToArray();
        var latestActionAt = records.Select(static record => (DateTimeOffset?)record.UpdatedAt).FirstOrDefault();
        var latestPublishedAt = records
            .Where(static record => record.Publication is not null)
            .Select(static record => (DateTimeOffset?)record.Publication!.SignedOffAt)
            .OrderByDescending(static value => value)
            .FirstOrDefault();
        var blockedCount = records.Count(static record => record.State == ReportPackWorkflowStateDto.Rejected);
        var pendingPublicationCount = records.Count(static record => record.State == ReportPackWorkflowStateDto.Approved);
        var pendingApprovalCount = records.Count(static record =>
            record.State is ReportPackWorkflowStateDto.Draft
                or ReportPackWorkflowStateDto.Validated
                or ReportPackWorkflowStateDto.InReview
                or ReportPackWorkflowStateDto.PendingApproval);
        var pendingDeliveryCount = records.Count(static record =>
            record.Publication is not null
            && record.State is ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated);

        return DistributionPolicies
            .Select(policy => BuildDistribution(
                policy,
                blockedCount,
                pendingApprovalCount,
                pendingPublicationCount,
                pendingDeliveryCount,
                latestActionAt,
                latestPublishedAt,
                deliveryAttempts ?? []))
            .ToArray();
    }

    public static ReportingScheduleDeliveryPlanDto[] BuildScheduleDeliveryPlans(
        IReadOnlyList<ReportingScheduleRecordDto> schedules,
        IReadOnlyList<ReportPackDeliveryAttemptDto>? deliveryAttempts = null)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var attempts = deliveryAttempts ?? [];
        return schedules
            .OrderBy(static schedule => schedule.DueAtUtc)
            .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(schedule => BuildScheduleDeliveryPlans(schedule, attempts))
            .ToArray();
    }

    private static IEnumerable<ReportingScheduleDeliveryPlanDto> BuildScheduleDeliveryPlans(
        ReportingScheduleRecordDto schedule,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        foreach (var target in schedule.DeliveryTargets ?? [])
        {
            if (string.IsNullOrWhiteSpace(target.DistributionId))
            {
                continue;
            }

            var distributionId = target.DistributionId.Trim();
            var formats = ResolveScheduleDeliveryFormats(target.Formats);
            var latestAttempt = FindLatestScheduleDeliveryAttempt(schedule, distributionId, deliveryAttempts);
            ReportPackDistributionPolicy? policy = null;
            ReportingScheduleDeliveryPlanDto? fallbackPlan = null;
            try
            {
                policy = ResolveDistributionPolicy(distributionId);
            }
            catch (KeyNotFoundException)
            {
                fallbackPlan = new ReportingScheduleDeliveryPlanDto(
                    PlanId: BuildScheduleDeliveryPlanId(schedule.ScheduleId, distributionId),
                    ScheduleId: schedule.ScheduleId,
                    TemplateId: schedule.TemplateId,
                    DistributionId: distributionId,
                    Recipient: distributionId,
                    RecipientRole: "Unknown recipient",
                    Channel: "Unknown channel",
                    DeliveryMode: target.DeliveryMode ?? ReportPackDeliveryModeDto.InternalRoute,
                    Formats: formats,
                    IsReady: false,
                    ReadinessSummary: $"Delivery target '{distributionId}' is not backed by a known report-pack distribution policy.",
                    Route: "/api/workstation/reporting",
                    DueAtUtc: schedule.DueAtUtc,
                    NextAsOfDate: schedule.NextAsOfDate,
                    Owner: schedule.RequestedBy,
                    Note: NormalizeOptional(target.Note),
                    LastDeliveryAttemptId: latestAttempt?.AttemptId,
                    LastDeliveryState: latestAttempt?.State,
                    LastDeliveryAtUtc: latestAttempt?.AttemptedAtUtc,
                    LastDeliveryPackageRoute: latestAttempt?.Package?.PortalRoute,
                    LastDeliverySecureLink: latestAttempt?.Package?.SecureLink,
                    VersionStamp: BuildScheduleDeliveryPlanVersionStamp(schedule, distributionId, formats),
                    LastDeliveryArtifactCount: latestAttempt?.Package?.Artifacts.Count ?? 0,
                    LastDeliveryIntegritySummary: latestAttempt?.Package?.IntegritySummary);
            }

            if (fallbackPlan is not null)
            {
                yield return fallbackPlan;
                continue;
            }

            if (policy is null)
            {
                continue;
            }

            var deliveryMode = target.DeliveryMode ?? InferScheduleDeliveryMode(policy.Channel);
            var readiness = EvaluateScheduleDeliveryReadiness(schedule, policy, deliveryMode, formats, latestAttempt);
            yield return new ReportingScheduleDeliveryPlanDto(
                PlanId: BuildScheduleDeliveryPlanId(schedule.ScheduleId, policy.DistributionId),
                ScheduleId: schedule.ScheduleId,
                TemplateId: schedule.TemplateId,
                DistributionId: policy.DistributionId,
                Recipient: policy.Recipient,
                RecipientRole: policy.RecipientRole,
                Channel: policy.Channel,
                DeliveryMode: deliveryMode,
                Formats: formats,
                IsReady: readiness.IsReady,
                ReadinessSummary: readiness.Summary,
                Route: policy.Route,
                DueAtUtc: schedule.DueAtUtc,
                NextAsOfDate: schedule.NextAsOfDate,
                Owner: policy.Owner,
                Note: NormalizeOptional(target.Note),
                LastDeliveryAttemptId: latestAttempt?.AttemptId,
                LastDeliveryState: latestAttempt?.State,
                LastDeliveryAtUtc: latestAttempt?.AttemptedAtUtc,
                LastDeliveryPackageRoute: latestAttempt?.Package?.PortalRoute,
                LastDeliverySecureLink: latestAttempt?.Package?.SecureLink,
                VersionStamp: BuildScheduleDeliveryPlanVersionStamp(schedule, policy.DistributionId, formats),
                LastDeliveryArtifactCount: latestAttempt?.Package?.Artifacts.Count ?? 0,
                LastDeliveryIntegritySummary: latestAttempt?.Package?.IntegritySummary,
                ReadinessBlockers: readiness.Blockers);
        }
    }

    private static ReportPackDeliveryAttemptDto? FindLatestScheduleDeliveryAttempt(
        ReportingScheduleRecordDto schedule,
        string distributionId,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        var referencePrefix = $"schedule:{schedule.TemplateId.Trim()}:";
        return deliveryAttempts
            .Where(attempt => string.Equals(attempt.DistributionId, distributionId, StringComparison.OrdinalIgnoreCase))
            .Where(attempt => attempt.DeliveryReference.StartsWith(referencePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .ThenByDescending(static attempt => attempt.AttemptNumber)
            .FirstOrDefault();
    }

    private static IReadOnlyList<GovernanceReportArtifactFormatDto> ResolveScheduleDeliveryFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto>? formats)
    {
        IReadOnlyList<GovernanceReportArtifactFormatDto> requested = formats is { Count: > 0 }
            ? formats
            : [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx, GovernanceReportArtifactFormatDto.Csv];
        return requested
            .Distinct()
            .ToArray();
    }

    private static ScheduleDeliveryReadiness EvaluateScheduleDeliveryReadiness(
        ReportingScheduleRecordDto schedule,
        ReportPackDistributionPolicy policy,
        ReportPackDeliveryModeDto deliveryMode,
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats,
        ReportPackDeliveryAttemptDto? latestAttempt)
    {
        var blockers = new List<string>();
        if (schedule.State != ReportingScheduleStateDto.Active)
        {
            blockers.Add($"Schedule '{schedule.ScheduleId}' is {schedule.State}; delivery will not run until it is active.");
        }

        if (formats.Count == 0)
        {
            blockers.Add($"Schedule '{schedule.ScheduleId}' has no requested delivery formats.");
        }

        if (!IsDeliveryModeCompatible(policy.Channel, deliveryMode))
        {
            blockers.Add($"Delivery mode {deliveryMode} is not compatible with {policy.Channel} for {policy.Recipient}.");
        }

        if (schedule.DueAtUtc <= DateTimeOffset.UtcNow &&
            latestAttempt?.State != ReportPackDeliveryStateDto.Delivered)
        {
            blockers.Add($"Schedule '{schedule.ScheduleId}' is due and has no successful retained delivery package for {policy.Recipient}.");
        }

        var missingFormats = FindMissingDeliveryFormats(formats, latestAttempt);
        if (missingFormats.Length > 0)
        {
            blockers.Add($"Latest delivery package for {policy.Recipient} is missing requested artifact format(s): {FormatScheduleDeliveryFormats(missingFormats)}.");
        }

        var summary = blockers.Count == 0
            ? $"Ready to deliver {FormatScheduleDeliveryFormats(formats)} by {deliveryMode} to {policy.Recipient} when schedule '{schedule.ScheduleId}' runs."
            : string.Join(" ", blockers);
        return new ScheduleDeliveryReadiness(blockers.Count == 0, summary, blockers.ToArray());
    }

    private static bool IsDeliveryModeCompatible(string channel, ReportPackDeliveryModeDto deliveryMode)
    {
        if (deliveryMode == ReportPackDeliveryModeDto.EmailLink)
        {
            return true;
        }

        if (channel.Contains("portal", StringComparison.OrdinalIgnoreCase))
        {
            return deliveryMode == ReportPackDeliveryModeDto.SecurePortal;
        }

        if (channel.Contains("vault", StringComparison.OrdinalIgnoreCase))
        {
            return deliveryMode == ReportPackDeliveryModeDto.EvidenceVault;
        }

        return deliveryMode == ReportPackDeliveryModeDto.InternalRoute;
    }

    private static GovernanceReportArtifactFormatDto[] FindMissingDeliveryFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats,
        ReportPackDeliveryAttemptDto? latestAttempt)
    {
        if (latestAttempt?.Package is null || latestAttempt.State != ReportPackDeliveryStateDto.Delivered)
        {
            return [];
        }

        var deliveredFormats = latestAttempt.Package.Artifacts
            .Select(static artifact => artifact.Format)
            .ToHashSet();
        return formats
            .Where(format => !deliveredFormats.Contains(format))
            .Distinct()
            .OrderBy(static format => format)
            .ToArray();
    }

    private static string BuildScheduleDeliveryPlanId(string scheduleId, string distributionId) =>
        $"schedule-delivery:{scheduleId}:{distributionId}";

    private static string BuildScheduleDeliveryPlanVersionStamp(
        ReportingScheduleRecordDto schedule,
        string distributionId,
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats) =>
        $"schedule-delivery-plan:{schedule.ScheduleId}:{distributionId}:{schedule.UpdatedAtUtc.UtcDateTime:yyyyMMddHHmmss}:formats-{formats.Count}";

    private static string FormatScheduleDeliveryFormats(IReadOnlyList<GovernanceReportArtifactFormatDto> formats) =>
        string.Join("/", formats.Select(static format => format.ToString()));

    private static ReportPackDeliveryModeDto InferScheduleDeliveryMode(string channel)
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WorkstationReportPackDistributionPayload BuildDistribution(
        ReportPackDistributionPolicy policy,
        int blockedCount,
        int pendingApprovalCount,
        int pendingPublicationCount,
        int pendingDeliveryCount,
        DateTimeOffset? latestActionAt,
        DateTimeOffset? latestPublishedAt,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        var latestDelivery = deliveryAttempts
            .Where(attempt =>
                string.Equals(attempt.DistributionId, policy.DistributionId, StringComparison.OrdinalIgnoreCase)
                && attempt.State == ReportPackDeliveryStateDto.Delivered)
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .FirstOrDefault();
        var (state, pendingItems, pendingSummary, dueAtUtc) = ResolveDistributionState(
            policy,
            blockedCount,
            pendingApprovalCount,
            pendingPublicationCount,
            pendingDeliveryCount,
            latestActionAt,
            latestPublishedAt,
            latestDelivery?.AttemptedAtUtc);

        return new WorkstationReportPackDistributionPayload(
            policy.DistributionId,
            policy.Recipient,
            policy.RecipientRole,
            policy.Channel,
            state,
            pendingItems,
            pendingSummary,
            policy.Owner,
            dueAtUtc,
            latestDelivery?.AttemptedAtUtc,
            policy.Route);
    }

    private static (string State, int PendingItems, string PendingSummary, DateTimeOffset? DueAtUtc) ResolveDistributionState(
        ReportPackDistributionPolicy policy,
        int blockedCount,
        int pendingApprovalCount,
        int pendingPublicationCount,
        int pendingDeliveryCount,
        DateTimeOffset? latestActionAt,
        DateTimeOffset? latestPublishedAt,
        DateTimeOffset? latestDeliveryAt)
    {
        if (blockedCount > 0)
        {
            return (
                "Blocked",
                blockedCount,
                $"{blockedCount} rejected report pack{Plural(blockedCount)} must be corrected before {policy.Recipient} receives a package.",
                latestActionAt?.Add(policy.CorrectionSla));
        }

        if (pendingPublicationCount > 0)
        {
            return (
                "Pending publication",
                pendingPublicationCount,
                $"{pendingPublicationCount} approved report pack{Plural(pendingPublicationCount)} must be published before {policy.Recipient} delivery.",
                latestActionAt?.Add(policy.PublicationSla));
        }

        if (pendingApprovalCount > 0)
        {
            return (
                "Pending approval",
                pendingApprovalCount,
                $"{pendingApprovalCount} report pack{Plural(pendingApprovalCount)} still need review or approval before {policy.Recipient} delivery.",
                latestActionAt?.Add(policy.ApprovalSla));
        }

        if (pendingDeliveryCount > 0)
        {
            if (latestPublishedAt is not null && latestDeliveryAt is not null && latestDeliveryAt >= latestPublishedAt)
            {
                return (
                    "Delivered",
                    0,
                    $"{policy.Recipient} received the latest governed report pack by {policy.Channel}.",
                    null);
            }

            return (
                "Pending delivery",
                pendingDeliveryCount,
                $"{pendingDeliveryCount} published report pack{Plural(pendingDeliveryCount)} are ready for {policy.Channel} delivery to {policy.Recipient}.",
                latestPublishedAt?.Add(policy.DeliverySla));
        }

        return (
            "No package queued",
            0,
            $"No governed report pack is queued for {policy.Recipient}.",
            null);
    }

    private static UnifiedReportingRun ProjectGenericRun(
        ReportingRunSnapshot run,
        IReadOnlyDictionary<string, string> familyByTemplate)
    {
        var manifest = run.Manifest;
        var family = familyByTemplate.TryGetValue(manifest.TemplateId, out var templateFamily)
            ? templateFamily
            : "ReportingRun";

        return new UnifiedReportingRun(
            new WorkstationReportingRunPayload(
                RunId: manifest.RunId,
                TemplateId: manifest.TemplateId,
                Family: family,
                Status: manifest.Status.ToString(),
                Trigger: manifest.Trigger.ToString(),
                AsOfDate: manifest.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                AttemptCount: manifest.AttemptCount,
                SectionCount: manifest.Sections.Length,
                LineageLinkedSections: manifest.Sections.Count(static section => section.Lineage is not null),
                Artifacts: manifest.Artifacts
                    .Concat(BuildGenericRunDrilldownArtifacts(manifest))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                AuditActions: run.AuditTrail
                    .OrderBy(static audit => audit.TimestampUtc)
                    .Select(static audit => audit.Action)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FailureReason: manifest.FailureReason,
                DrilldownLinks: BuildGenericRunDrilldownLinks(manifest).ToArray(),
                NextActions: BuildGenericRunNextActions(manifest).ToArray()),
            run.UpdatedAtUtc);
    }

    private static UnifiedReportingRun ProjectWorkflowRun(ReportPackWorkflowRecordDto record)
    {
        var auditActions = record.AuditTrail
            .OrderBy(static audit => audit.At)
            .Select(static audit => audit.Action)
            .Concat(BuildWorkflowStatusActions(record))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var artifacts = BuildWorkflowArtifacts(record)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new UnifiedReportingRun(
            new WorkstationReportingRunPayload(
                RunId: $"report-pack:{record.ReportId:D}",
                TemplateId: $"{record.TemplateId.Name}:v{record.TemplateId.Version}",
                Family: "GovernedReportPack",
                Status: record.State.ToString(),
                Trigger: "Workflow",
                AsOfDate: record.Period,
                AttemptCount: Math.Max(1, record.Version),
                SectionCount: record.LineProvenance?.Count ?? record.Restatement?.ChangedLines.Count ?? 0,
                LineageLinkedSections: record.LineProvenance?.Count(static line => !string.IsNullOrWhiteSpace(line.EvidenceId)) ?? 0,
                Artifacts: artifacts,
                AuditActions: auditActions,
                FailureReason: record.Rejection?.Reason,
                DrilldownLinks: BuildWorkflowDrilldownLinks(record).ToArray(),
                NextActions: BuildWorkflowNextActions(record).ToArray()),
            record.UpdatedAt);
    }

    private static IEnumerable<string> BuildGenericRunDrilldownArtifacts(ReportingOutputManifest manifest)
    {
        yield return $"reporting-run://{manifest.RunId}/manifest";
        yield return $"reporting-run://{manifest.RunId}/audit";

        if (!string.IsNullOrWhiteSpace(manifest.ScheduleId))
        {
            yield return $"schedule:{manifest.ScheduleId}";
        }
    }

    private static IEnumerable<string> BuildWorkflowArtifacts(ReportPackWorkflowRecordDto record)
    {
        var reportId = Uri.EscapeDataString(record.ReportId.ToString("D"));
        yield return $"/api/fund-structure/report-packs/{reportId}/evidence-bundle";
        yield return $"/api/fund-structure/reporting/packs/history?period={Uri.EscapeDataString(record.Period)}&fundAccountId={Uri.EscapeDataString(record.FundAccountId)}";
        yield return $"/reporting/report-packs/{reportId}";
        yield return $"fund-account:{record.FundAccountId}";
        yield return $"period:{record.Period}";

        foreach (var line in record.LineProvenance ?? [])
        {
            if (!string.IsNullOrWhiteSpace(line.LineKey))
            {
                yield return $"/api/fund-structure/report-packs/{reportId}/ledger-provenance?scopeKey={Uri.EscapeDataString(line.LineKey)}";
            }

            if (!string.IsNullOrWhiteSpace(line.EvidenceId))
            {
                yield return $"evidence:{line.EvidenceId}";
            }
        }

        if (record.Publication is { } publication)
        {
            yield return $"publication-manifest:{publication.ManifestId}";
            yield return publication.RetainedManifestPath;
            yield return $"evidence-hash:{publication.EvidenceHash}";
            foreach (var link in publication.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(link.Route))
                {
                    yield return link.Route!;
                }
            }
        }

        if (record.Restatement is { } restatement)
        {
            yield return $"restatement:{restatement.ReasonCode}";
            yield return $"prior-report:{restatement.PriorVersionReportId:D}";
            foreach (var link in restatement.EvidenceLinks ?? [])
            {
                if (!string.IsNullOrWhiteSpace(link.Route))
                {
                    yield return link.Route!;
                }
            }
        }
    }

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildGenericRunDrilldownLinks(
        ReportingOutputManifest manifest)
    {
        yield return BuildRunLink(
            id: $"{manifest.RunId}:manifest",
            kind: "manifest",
            label: "Run manifest",
            href: $"reporting-run://{manifest.RunId}/manifest",
            method: "GET",
            isBrowserNavigable: false,
            source: "ReportingOrchestration");
        yield return BuildRunLink(
            id: $"{manifest.RunId}:audit",
            kind: "audit",
            label: "Approval audit trail",
            href: $"reporting-run://{manifest.RunId}/audit",
            method: "GET",
            isBrowserNavigable: false,
            source: "ReportingOrchestration");

        if (!string.IsNullOrWhiteSpace(manifest.ScheduleId))
        {
            yield return BuildRunLink(
                id: $"{manifest.RunId}:schedule",
                kind: "schedule",
                label: "Schedule source",
                href: $"schedule:{manifest.ScheduleId}",
                method: "GET",
                isBrowserNavigable: false,
                source: "ReportingOrchestration");
        }
    }

    private static IEnumerable<WorkstationReportingRunNextActionPayload> BuildGenericRunNextActions(
        ReportingOutputManifest manifest)
    {
        if (manifest.Status == ReportingRunStatus.Draft)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:submit",
                kind: "approval-submit",
                label: "Submit run for review",
                href: $"reporting-run://{manifest.RunId}/approval/submit",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.InReview)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:approve",
                kind: "approval",
                label: "Approve reporting run",
                href: $"reporting-run://{manifest.RunId}/approval/approve",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.Approved)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:release",
                kind: "publication",
                label: "Release reporting run",
                href: $"reporting-run://{manifest.RunId}/publication/release",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.Released)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:review-release",
                kind: "publication-review",
                label: "Review released artifacts",
                href: $"reporting-run://{manifest.RunId}/manifest",
                method: "GET",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.Failed)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:inspect-failure",
                kind: "failure-review",
                label: "Inspect failed run",
                href: $"reporting-run://{manifest.RunId}/audit",
                method: "GET",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
    }

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildWorkflowDrilldownLinks(
        ReportPackWorkflowRecordDto record)
    {
        var reportId = EscapeReportId(record.ReportId);
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:report-pack",
            kind: "report-pack",
            label: "Report-pack manifest",
            href: $"/api/fund-structure/report-packs/{reportId}",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:evidence-bundle",
            kind: "evidence",
            label: "Evidence bundle",
            href: $"/api/fund-structure/report-packs/{reportId}/evidence-bundle",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:history",
            kind: "history",
            label: "Workflow history",
            href: $"/api/fund-structure/reporting/packs/history?period={Uri.EscapeDataString(record.Period)}&fundAccountId={Uri.EscapeDataString(record.FundAccountId)}",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");

        foreach (var line in record.LineProvenance ?? [])
        {
            if (!string.IsNullOrWhiteSpace(line.LineKey))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:ledger:{line.LineKey}",
                    kind: "ledger-provenance",
                    label: $"Ledger provenance: {line.LineKey}",
                    href: $"/api/fund-structure/report-packs/{reportId}/ledger-provenance?scopeKey={Uri.EscapeDataString(line.LineKey)}",
                    method: "GET",
                    isBrowserNavigable: true,
                    source: "ReportPackWorkflow");
            }

            if (!string.IsNullOrWhiteSpace(line.EvidenceId))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:line-evidence:{line.EvidenceId}",
                    kind: "evidence",
                    label: $"Line evidence: {line.LineKey}",
                    href: $"evidence:{line.EvidenceId}",
                    method: "GET",
                    isBrowserNavigable: false,
                    source: "ReportPackWorkflow");
            }
        }

        foreach (var link in BuildPublicationLinks(record))
        {
            yield return link;
        }

        foreach (var link in BuildRestatementLinks(record))
        {
            yield return link;
        }
    }

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildPublicationLinks(
        ReportPackWorkflowRecordDto record)
    {
        if (record.Publication is not { } publication)
        {
            yield break;
        }

        yield return BuildRunLink(
            id: $"{record.ReportId:D}:publication-manifest",
            kind: "publication",
            label: "Publication manifest",
            href: publication.RetainedManifestPath,
            method: "GET",
            isBrowserNavigable: false,
            source: "ReportPackWorkflow");

        foreach (var link in publication.EvidenceLinks)
        {
            if (!string.IsNullOrWhiteSpace(link.Route))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:publication:{link.EvidenceId}",
                    kind: "publication-evidence",
                    label: string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId : link.Label,
                    href: link.Route!,
                    method: "GET",
                    isBrowserNavigable: IsHttpRoute(link.Route),
                    source: string.IsNullOrWhiteSpace(link.Source) ? "publication" : link.Source!);
            }
        }
    }

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildRestatementLinks(
        ReportPackWorkflowRecordDto record)
    {
        if (record.Restatement is not { } restatement)
        {
            yield break;
        }

        var priorReportId = EscapeReportId(restatement.PriorVersionReportId);
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:prior-report",
            kind: "restatement",
            label: "Prior report version",
            href: $"/api/fund-structure/report-packs/{priorReportId}",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");

        foreach (var link in restatement.EvidenceLinks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(link.Route))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:restatement:{link.EvidenceId}",
                    kind: "restatement-evidence",
                    label: string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId : link.Label,
                    href: link.Route!,
                    method: "GET",
                    isBrowserNavigable: IsHttpRoute(link.Route),
                    source: string.IsNullOrWhiteSpace(link.Source) ? "restatement" : link.Source!);
            }
        }
    }

    private static IEnumerable<WorkstationReportingRunNextActionPayload> BuildWorkflowNextActions(
        ReportPackWorkflowRecordDto record)
    {
        var reportId = EscapeReportId(record.ReportId);
        if (record.State is ReportPackWorkflowStateDto.Draft or ReportPackWorkflowStateDto.Validated or ReportPackWorkflowStateDto.Rejected)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:submit",
                kind: "approval-submit",
                label: record.State == ReportPackWorkflowStateDto.Rejected ? "Return pack to review" : "Submit pack for review",
                href: $"/api/fund-structure/reporting/packs/{reportId}/submit",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State is ReportPackWorkflowStateDto.InReview or ReportPackWorkflowStateDto.PendingApproval)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:approve",
                kind: "approval",
                label: "Approve report pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/approve",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:reject",
                kind: "approval-reject",
                label: "Reject for correction",
                href: $"/api/fund-structure/reporting/packs/{reportId}/reject",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State == ReportPackWorkflowStateDto.Approved)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:publish",
                kind: "publication",
                label: "Publish retained report pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/publish",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State == ReportPackWorkflowStateDto.Published)
        {
            foreach (var action in BuildDeliveryActions(record))
            {
                yield return action;
            }

            yield return BuildRunAction(
                id: $"{record.ReportId:D}:restate",
                kind: "restatement",
                label: "Create restatement",
                href: $"/api/fund-structure/reporting/packs/{reportId}/restatements",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:archive",
                kind: "archive",
                label: "Archive published pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/archive",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State == ReportPackWorkflowStateDto.Restated)
        {
            foreach (var action in BuildDeliveryActions(record))
            {
                yield return action;
            }

            yield return BuildRunAction(
                id: $"{record.ReportId:D}:archive",
                kind: "archive",
                label: "Archive restated pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/archive",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
    }

    private static IEnumerable<WorkstationReportingRunNextActionPayload> BuildDeliveryActions(ReportPackWorkflowRecordDto record)
    {
        var reportId = EscapeReportId(record.ReportId);
        foreach (var policy in DistributionPolicies)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:delivery:{policy.DistributionId}",
                kind: $"delivery:{policy.DistributionId}",
                label: $"Deliver to {policy.Recipient}",
                href: $"/api/fund-structure/reporting/packs/{reportId}/deliveries",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
    }

    private static IEnumerable<string> BuildWorkflowStatusActions(ReportPackWorkflowRecordDto record)
    {
        if (record.Publication is not null)
        {
            yield return "published";
        }

        if (record.Restatement is not null)
        {
            yield return "restated";
        }

        if (record.Rejection is not null)
        {
            yield return "rejected";
        }
    }

    private static WorkstationReportingRunLinkPayload BuildRunLink(
        string id,
        string kind,
        string label,
        string href,
        string method,
        bool isBrowserNavigable,
        string source) =>
        new(id, kind, label, href, method, isBrowserNavigable, source);

    private static WorkstationReportingRunNextActionPayload BuildRunAction(
        string id,
        string kind,
        string label,
        string href,
        string method,
        bool isEnabled,
        string? disabledReason,
        bool isBrowserNavigable) =>
        new(id, kind, label, href, method, isEnabled, disabledReason, isBrowserNavigable);

    private static string EscapeReportId(Guid reportId) => Uri.EscapeDataString(reportId.ToString("D"));

    private static bool IsHttpRoute(string? route) => !string.IsNullOrWhiteSpace(route) && route.StartsWith("/", StringComparison.Ordinal);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static readonly ReportPackDistributionPolicy[] DistributionPolicies =
    [
        new(
            "board-reporting-committee",
            "Board reporting committee",
            "Board",
            "Board portal",
            "fund-controller",
            "/reporting/report-packs?recipient=board",
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4)),
        new(
            "investor-relations",
            "Investor relations",
            "Investor communications",
            "Investor portal",
            "investor-relations",
            "/reporting/report-packs?recipient=investor-relations",
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4)),
        new(
            "compliance-archive",
            "Compliance archive",
            "Compliance",
            "Retained evidence vault",
            "compliance-reviewer",
            "/reporting/evidence?subject=report-pack",
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(2)),
        new(
            "fund-operations",
            "Fund operations",
            "Operations",
            "Operations close packet",
            "fund-operations",
            "/accounting/report-pack",
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(2))
    ];

    public sealed record ReportPackDistributionPolicy(
        string DistributionId,
        string Recipient,
        string RecipientRole,
        string Channel,
        string Owner,
        string Route,
        TimeSpan ApprovalSla,
        TimeSpan PublicationSla,
        TimeSpan DeliverySla,
        TimeSpan CorrectionSla);

    private sealed record UnifiedReportingRun(WorkstationReportingRunPayload Payload, DateTimeOffset UpdatedAtUtc);

    private sealed record ScheduleDeliveryReadiness(
        bool IsReady,
        string Summary,
        IReadOnlyList<string> Blockers);
}
