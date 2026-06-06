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

    public ReportPackRunReadService(
        IReportingTemplateCatalog templateCatalog,
        IReportingRunStore? runStore = null,
        ReportPackWorkflowService? workflowService = null,
        ReportTemplateRegistryService? templateRegistry = null)
    {
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _runStore = runStore;
        _workflowService = workflowService;
        _templateRegistry = templateRegistry;
    }

    public WorkstationReportingPayload BuildPayload(int recentRunLimit = DefaultRecentRunLimit)
    {
        var profiles = BuildProfiles();
        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();
        var templates = BuildTemplates();
        var familyByTemplate = templates
            .GroupBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Family, StringComparer.OrdinalIgnoreCase);
        var workflowRecords = _workflowService?.ListRecords(200) ?? [];
        var runs = BuildRecentRuns(Math.Clamp(recentRunLimit, 1, 200), familyByTemplate, workflowRecords);
        var distributions = BuildDistributionRecords(workflowRecords);
        var pendingDistributionCount = distributions.Count(static distribution => distribution.PendingItems > 0);

        return new WorkstationReportingPayload(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            Profiles: profiles,
            ReportPackDistributions: distributions,
            Summary: $"{profiles.Length} export/reporting profiles are available for Accounting and Reporting workflows; {distributions.Length} distribution recipients are visible; {pendingDistributionCount} have pending work.",
            Templates: templates,
            RecentRuns: runs.Select(static run => run.Payload).ToArray());
    }

    public static WorkstationReportingPayload BuildFallbackPayload() =>
        new ReportPackRunReadService(new DefaultReportingTemplateCatalog()).BuildPayload();

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

    private WorkstationReportingTemplatePayload[] BuildTemplates()
    {
        if (_templateRegistry is not null)
        {
            return _templateRegistry
                .List()
                .Select(ProjectTemplateRecord)
                .OrderBy(static template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static template => template.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return _templateCatalog
            .ListTemplates()
            .Select(static template => new WorkstationReportingTemplatePayload(
                template.TemplateId,
                template.Family.ToString(),
                template.Name,
                template.Version,
                template.Sections.ToArray(),
                LifecycleStatus: ReportTemplateLifecycleStatusDto.Approved.ToString(),
                IsBuiltIn: true,
                IsLatestApproved: true,
                ApprovalSummary: $"Built-in approved template for {template.Family}.",
                AuthoringRoute: $"/api/fund-structure/reporting/templates/{template.TemplateId}/versions/1"))
            .OrderBy(static template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorkstationReportingTemplatePayload ProjectTemplateRecord(ReportTemplateGovernanceRecordDto record)
    {
        var definition = record.Definition;
        return new WorkstationReportingTemplatePayload(
            definition.TemplateId.Name,
            record.Family,
            definition.DisplayName,
            definition.TemplateId.Version.ToString(),
            definition.Sections.ToArray(),
            LifecycleStatus: record.Status.ToString(),
            IsBuiltIn: record.IsBuiltIn,
            IsLatestApproved: record.IsLatestApproved,
            ApprovalSummary: BuildTemplateApprovalSummary(record),
            AuthoringRoute: $"/api/fund-structure/reporting/templates/{Uri.EscapeDataString(definition.TemplateId.Name)}/versions/{definition.TemplateId.Version}");
    }

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
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords)
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
                latestPublishedAt))
            .ToArray();
    }

    private static WorkstationReportPackDistributionPayload BuildDistribution(
        ReportPackDistributionPolicy policy,
        int blockedCount,
        int pendingApprovalCount,
        int pendingPublicationCount,
        int pendingDeliveryCount,
        DateTimeOffset? latestActionAt,
        DateTimeOffset? latestPublishedAt)
    {
        var (state, pendingItems, pendingSummary, dueAtUtc) = ResolveDistributionState(
            policy,
            blockedCount,
            pendingApprovalCount,
            pendingPublicationCount,
            pendingDeliveryCount,
            latestActionAt,
            latestPublishedAt);

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
            LastSentAtUtc: null,
            policy.Route);
    }

    private static (string State, int PendingItems, string PendingSummary, DateTimeOffset? DueAtUtc) ResolveDistributionState(
        ReportPackDistributionPolicy policy,
        int blockedCount,
        int pendingApprovalCount,
        int pendingPublicationCount,
        int pendingDeliveryCount,
        DateTimeOffset? latestActionAt,
        DateTimeOffset? latestPublishedAt)
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

    private sealed record ReportPackDistributionPolicy(
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
}
