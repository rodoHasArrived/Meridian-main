using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed record EvidencePacketValidationResult(
    IReadOnlyList<EvidenceEdgeDto> Edges,
    EvidenceCompletenessDto Completeness,
    IReadOnlyList<string> Warnings);

public sealed class EvidencePacketValidationService
{
    private static readonly EvidenceSlaPolicyDto[] DefaultSlaPolicies =
    [
        new(
            PolicyId: "provider-validation-freshness",
            EvidenceKind: "provider-trust",
            WorkflowKind: "provider-validation",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Provider validation evidence should be refreshed at least daily."),
        new(
            PolicyId: "replay-check-freshness",
            EvidenceKind: "paper-replay",
            WorkflowKind: "replay-check",
            FreshnessMinutes: 4 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Paper replay evidence should be refreshed before operational promotion review."),
        new(
            PolicyId: "continuity-replay-freshness",
            EvidenceKind: "run-continuity",
            WorkflowKind: "replay-check",
            FreshnessMinutes: 4 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Run continuity replay evidence should remain current for operator review."),
        new(
            PolicyId: "reconciliation-freshness",
            EvidenceKind: "reconciliation-run",
            WorkflowKind: "reconciliation",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Reconciliation evidence should be refreshed at least daily."),
        new(
            PolicyId: "reconciliation-casework-freshness",
            EvidenceKind: "break-queue",
            WorkflowKind: "reconciliation",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Reconciliation casework and break queue evidence should be refreshed at least daily."),
        new(
            PolicyId: "approval-freshness",
            EvidenceKind: "approval",
            WorkflowKind: "approval",
            FreshnessMinutes: 72 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Approval evidence should remain inside the governed review window."),
        new(
            PolicyId: "close-checklist-freshness",
            EvidenceKind: "close-checklist",
            WorkflowKind: "approval",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Close checklist evidence should be refreshed before approval or close-package publication."),
        new(
            PolicyId: "promotion-approval-freshness",
            EvidenceKind: "promotion-review",
            WorkflowKind: "approval",
            FreshnessMinutes: 72 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Promotion approval evidence should remain inside the governed review window."),
        new(
            PolicyId: "report-pack-freshness",
            EvidenceKind: "report-pack",
            WorkflowKind: "reporting",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Report-pack evidence should be refreshed at least daily."),
        new(
            PolicyId: "accounting-record-freshness",
            EvidenceKind: "accounting-record",
            WorkflowKind: "approval",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Accounting-record evidence should be refreshed before approval or close-package publication."),
        new(
            PolicyId: "accounting-record-category-freshness",
            EvidenceKind: "accounting-record-category",
            WorkflowKind: "approval",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Accounting-record category evidence should be refreshed before audit-ready approval review."),
        new(
            PolicyId: "report-export-freshness",
            EvidenceKind: "analysis-export",
            WorkflowKind: "reporting",
            FreshnessMinutes: 24 * 60,
            BreachSeverity: EvidenceValidationSeverityDto.Warning,
            RequiredForAssurance: true,
            Description: "Report export evidence should be refreshed at least daily.")
    ];

    public EvidencePacketValidationResult Validate(
        IReadOnlyList<EvidenceNodeDto> nodes,
        IEnumerable<EvidenceEdgeDto> candidateEdges,
        IReadOnlySet<string> requiredIds,
        bool enforceNoOrphanRule)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(candidateEdges);
        ArgumentNullException.ThrowIfNull(requiredIds);

        var warnings = new List<string>();
        var issues = new List<EvidenceValidationIssueDto>();
        var nodeLookup = nodes.ToDictionary(static node => node.EvidenceId, StringComparer.OrdinalIgnoreCase);
        var validatedEdges = ValidateEdges(candidateEdges, nodeLookup, warnings, issues);

        if (enforceNoOrphanRule && nodes.Count > 1)
        {
            ApplyNoOrphanWarnings(nodes, validatedEdges, warnings, issues);
        }

        ApplyLedgerIntegrityIssues(nodes, issues);
        ApplyCanonicalSubjectLinkageIssues(nodes, issues);
        var slaAssessments = ApplyEvidenceSlaIssues(nodes, issues);

        var completeness = BuildCompleteness(nodes, nodeLookup, requiredIds, issues, DefaultSlaPolicies, slaAssessments);
        return new EvidencePacketValidationResult(
            Edges: validatedEdges,
            Completeness: completeness,
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<EvidenceEdgeDto> ValidateEdges(
        IEnumerable<EvidenceEdgeDto> candidateEdges,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodes,
        List<string> warnings,
        List<EvidenceValidationIssueDto> issues)
    {
        var validated = new List<EvidenceEdgeDto>();
        foreach (var edge in candidateEdges)
        {
            if (!nodes.ContainsKey(edge.FromId) || !nodes.ContainsKey(edge.ToId))
            {
                var message = $"Evidence edge '{edge.FromId}' -> '{edge.ToId}' references a missing node.";
                warnings.Add(message);
                issues.Add(new EvidenceValidationIssueDto(
                    Code: "invalid-edge",
                    Severity: EvidenceValidationSeverityDto.Warning,
                    Message: message,
                    EvidenceId: edge.FromId));
                continue;
            }

            validated.Add(edge);
        }

        return validated
            .GroupBy(static edge => $"{edge.FromId}\n{edge.ToId}\n{edge.Relationship}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static void ApplyNoOrphanWarnings(
        IReadOnlyList<EvidenceNodeDto> nodes,
        IReadOnlyList<EvidenceEdgeDto> edges,
        List<string> warnings,
        List<EvidenceValidationIssueDto> issues)
    {
        var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            linked.Add(edge.FromId);
            linked.Add(edge.ToId);
        }

        foreach (var orphan in nodes.Where(node => !linked.Contains(node.EvidenceId)))
        {
            var message = $"Evidence node '{orphan.EvidenceId}' is not linked into the packet graph.";
            warnings.Add(message);
            issues.Add(new EvidenceValidationIssueDto(
                Code: "orphan-evidence",
                Severity: EvidenceValidationSeverityDto.Warning,
                Message: message,
                EvidenceId: orphan.EvidenceId,
                EvidenceKind: orphan.Kind,
                SourceSystem: orphan.SourceSystem));
        }
    }

    private static EvidenceCompletenessDto BuildCompleteness(
        IReadOnlyList<EvidenceNodeDto> nodes,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodeLookup,
        IReadOnlySet<string> requiredIds,
        List<EvidenceValidationIssueDto> issues,
        IReadOnlyList<EvidenceSlaPolicyDto> slaPolicies,
        IReadOnlyList<EvidenceSlaAssessmentDto> slaAssessments)
    {
        var required = requiredIds.Count > 0
            ? requiredIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray()
            : nodes.Select(static node => node.EvidenceId).ToArray();
        var ready = new List<string>();
        var missing = new List<string>();
        var stale = new List<string>();
        var blockers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredId in required)
        {
            if (!nodeLookup.TryGetValue(requiredId, out var node))
            {
                missing.Add(requiredId);
                issues.Add(new EvidenceValidationIssueDto(
                    Code: "missing-required-evidence",
                    Severity: EvidenceValidationSeverityDto.Critical,
                    Message: $"Required evidence '{requiredId}' is missing from the packet.",
                    EvidenceId: requiredId));
                continue;
            }

            if (node.Status == EvidenceStatusDto.Ready)
            {
                ready.Add(requiredId);
            }

            if (node.Status is EvidenceStatusDto.Missing)
            {
                missing.Add(requiredId);
                issues.Add(new EvidenceValidationIssueDto(
                    Code: "missing-required-evidence",
                    Severity: EvidenceValidationSeverityDto.Critical,
                    Message: $"Required evidence '{requiredId}' is marked missing.",
                    EvidenceId: requiredId,
                    EvidenceKind: node.Kind,
                    SourceSystem: node.SourceSystem));
            }

            if (node.Status is EvidenceStatusDto.Stale || node.Freshness.IsStale)
            {
                stale.Add(requiredId);
                issues.Add(new EvidenceValidationIssueDto(
                    Code: "stale-required-evidence",
                    Severity: EvidenceValidationSeverityDto.Warning,
                    Message: node.Freshness.Reason ?? $"Required evidence '{requiredId}' is stale.",
                    EvidenceId: requiredId,
                    EvidenceKind: node.Kind,
                    SourceSystem: node.SourceSystem));
            }

            if (node.Status is EvidenceStatusDto.Blocked or EvidenceStatusDto.ReviewRequired)
            {
                foreach (var workItemId in node.RelatedWorkItemIds)
                {
                    blockers.Add(workItemId);
                }

                AddStateIssue(node, issues);
            }
        }

        var score = required.Length == 0
            ? 100
            : (int)Math.Round(ready.Count * 100d / required.Length, MidpointRounding.AwayFromZero);
        var hasCriticalValidationIssue = issues.Any(static issue => issue.Severity == EvidenceValidationSeverityDto.Critical);
        var hasWarningValidationIssue = issues.Any(static issue => issue.Severity == EvidenceValidationSeverityDto.Warning);
        var status =
            missing.Count > 0 || hasCriticalValidationIssue ? EvidenceStatusDto.Blocked :
            stale.Count > 0 ? EvidenceStatusDto.Stale :
            hasWarningValidationIssue ? EvidenceStatusDto.ReviewRequired :
            ready.Count == required.Length ? EvidenceStatusDto.Ready :
            EvidenceStatusDto.ReviewRequired;

        var orphanEvidenceIds = issues
            .Where(static issue => string.Equals(issue.Code, "orphan-evidence", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(issue.EvidenceId))
            .Select(static issue => issue.EvidenceId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new EvidenceCompletenessDto(
            Score: score,
            Status: status,
            RequiredIds: required,
            ReadyIds: ready.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingIds: missing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            StaleIds: stale.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            BlockingWorkItemIds: blockers.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            ValidationIssues = issues.Distinct().ToArray(),
            BlockingIssueCount = issues.Count(static issue => issue.Severity == EvidenceValidationSeverityDto.Critical),
            WarningIssueCount = issues.Count(static issue => issue.Severity == EvidenceValidationSeverityDto.Warning),
            OrphanEvidenceIds = orphanEvidenceIds,
            SlaPolicies = slaPolicies,
            SlaAssessments = slaAssessments,
            AssuranceScore = BuildAssuranceScore(required, nodeLookup, issues, slaAssessments)
        };
    }

    private static IReadOnlyList<EvidenceSlaAssessmentDto> ApplyEvidenceSlaIssues(
        IReadOnlyList<EvidenceNodeDto> nodes,
        List<EvidenceValidationIssueDto> issues)
    {
        var assessments = new List<EvidenceSlaAssessmentDto>();

        foreach (var node in nodes)
        {
            foreach (var policy in DefaultSlaPolicies.Where(policy => EvidenceKindMatches(node.Kind, policy.EvidenceKind)))
            {
                var ageMinutes = node.Freshness.AsOf.HasValue
                    ? Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - node.Freshness.AsOf.Value).TotalMinutes))
                    : (int?)null;
                var breached = node.Freshness.IsStale || ageMinutes is null || ageMinutes > policy.FreshnessMinutes;
                var message = breached
                    ? BuildSlaBreachMessage(node, policy, ageMinutes)
                    : $"Evidence '{node.EvidenceId}' satisfies policy '{policy.PolicyId}'.";
                var assessment = new EvidenceSlaAssessmentDto(
                    PolicyId: policy.PolicyId,
                    EvidenceId: node.EvidenceId,
                    EvidenceKind: node.Kind,
                    SourceSystem: node.SourceSystem,
                    AgeMinutes: ageMinutes,
                    FreshnessMinutes: policy.FreshnessMinutes,
                    IsBreached: breached,
                    Severity: breached ? policy.BreachSeverity : EvidenceValidationSeverityDto.Info,
                    Message: message);
                assessments.Add(assessment);

                if (!breached)
                {
                    continue;
                }

                issues.Add(new EvidenceValidationIssueDto(
                    Code: "evidence-sla-breached",
                    Severity: policy.BreachSeverity,
                    Message: message,
                    EvidenceId: node.EvidenceId,
                    EvidenceKind: node.Kind,
                    SourceSystem: node.SourceSystem));
            }
        }

        return assessments
            .OrderBy(static assessment => assessment.PolicyId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static assessment => assessment.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MeridianAssuranceScoreDto BuildAssuranceScore(
        IReadOnlyList<string> requiredIds,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodeLookup,
        IReadOnlyList<EvidenceValidationIssueDto> issues,
        IReadOnlyList<EvidenceSlaAssessmentDto> slaAssessments)
    {
        var components = requiredIds
            .Select(requiredId => BuildAssuranceComponent(requiredId, nodeLookup, issues, slaAssessments))
            .ToArray();
        var score = components.Length == 0
            ? 100
            : (int)Math.Round(components.Average(static component => component.Score), MidpointRounding.AwayFromZero);
        var status =
            issues.Any(static issue => issue.Severity == EvidenceValidationSeverityDto.Critical) ? EvidenceStatusDto.Blocked :
            slaAssessments.Any(static assessment => assessment.IsBreached) ? EvidenceStatusDto.Stale :
            issues.Any(static issue => issue.Severity == EvidenceValidationSeverityDto.Warning) ? EvidenceStatusDto.ReviewRequired :
            components.All(static component => component.Status == EvidenceStatusDto.Ready) ? EvidenceStatusDto.Ready :
            EvidenceStatusDto.ReviewRequired;

        return new MeridianAssuranceScoreDto(
            Score: score,
            Status: status,
            Components: components,
            SlaAssessments: slaAssessments);
    }

    private static EvidenceAssuranceComponentDto BuildAssuranceComponent(
        string requiredId,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodeLookup,
        IReadOnlyList<EvidenceValidationIssueDto> issues,
        IReadOnlyList<EvidenceSlaAssessmentDto> slaAssessments)
    {
        if (!nodeLookup.TryGetValue(requiredId, out var node))
        {
            return new EvidenceAssuranceComponentDto(
                ComponentId: requiredId,
                Label: requiredId,
                Score: 0,
                Status: EvidenceStatusDto.Missing,
                Detail: $"Required evidence '{requiredId}' is missing.");
        }

        var hasCriticalIssue = issues.Any(issue =>
            issue.Severity == EvidenceValidationSeverityDto.Critical &&
            string.Equals(issue.EvidenceId, requiredId, StringComparison.OrdinalIgnoreCase));
        var hasSlaBreach = slaAssessments.Any(assessment =>
            assessment.IsBreached &&
            string.Equals(assessment.EvidenceId, requiredId, StringComparison.OrdinalIgnoreCase));
        var hasWarningIssue = issues.Any(issue =>
            issue.Severity == EvidenceValidationSeverityDto.Warning &&
            string.Equals(issue.EvidenceId, requiredId, StringComparison.OrdinalIgnoreCase));

        var score =
            hasCriticalIssue || node.Status is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing ? 0 :
            hasSlaBreach || node.Status is EvidenceStatusDto.Stale ? 60 :
            hasWarningIssue || node.Status == EvidenceStatusDto.ReviewRequired ? 75 :
            node.Status == EvidenceStatusDto.Ready ? 100 :
            50;
        var status =
            hasCriticalIssue ? EvidenceStatusDto.Blocked :
            hasSlaBreach ? EvidenceStatusDto.Stale :
            hasWarningIssue && node.Status == EvidenceStatusDto.Ready ? EvidenceStatusDto.ReviewRequired :
            node.Status;

        return new EvidenceAssuranceComponentDto(
            ComponentId: requiredId,
            Label: node.Kind,
            Score: score,
            Status: status,
            Detail: node.Summary);
    }

    private static bool EvidenceKindMatches(string nodeKind, string policyKind)
        => string.Equals(nodeKind, policyKind, StringComparison.OrdinalIgnoreCase);

    private static string BuildSlaBreachMessage(EvidenceNodeDto node, EvidenceSlaPolicyDto policy, int? ageMinutes)
        => ageMinutes is null
            ? $"Evidence '{node.EvidenceId}' has no as-of timestamp for policy '{policy.PolicyId}'."
            : $"Evidence '{node.EvidenceId}' is {ageMinutes.Value} minute(s) old and exceeds policy '{policy.PolicyId}' freshness limit of {policy.FreshnessMinutes} minute(s).";

    private static void AddStateIssue(EvidenceNodeDto node, List<EvidenceValidationIssueDto> issues)
    {
        var code = node.Status == EvidenceStatusDto.Blocked
            ? "blocked-evidence"
            : "review-required-evidence";
        var severity = node.Status == EvidenceStatusDto.Blocked
            ? EvidenceValidationSeverityDto.Critical
            : EvidenceValidationSeverityDto.Warning;
        var message = node.Status == EvidenceStatusDto.Blocked
            ? $"Evidence '{node.EvidenceId}' is blocked."
            : $"Evidence '{node.EvidenceId}' requires review.";

        if (node.RelatedWorkItemIds.Count == 0)
        {
            issues.Add(new EvidenceValidationIssueDto(
                Code: code,
                Severity: severity,
                Message: message,
                EvidenceId: node.EvidenceId,
                EvidenceKind: node.Kind,
                SourceSystem: node.SourceSystem));
            return;
        }

        foreach (var workItemId in node.RelatedWorkItemIds)
        {
            issues.Add(new EvidenceValidationIssueDto(
                Code: code,
                Severity: severity,
                Message: message,
                EvidenceId: node.EvidenceId,
                EvidenceKind: node.Kind,
                SourceSystem: node.SourceSystem,
                RelatedWorkItemId: workItemId));
        }
    }

    private static void ApplyLedgerIntegrityIssues(
        IReadOnlyList<EvidenceNodeDto> nodes,
        List<EvidenceValidationIssueDto> issues)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "run-ledger", StringComparison.OrdinalIgnoreCase) &&
                node.Status is EvidenceStatusDto.Ready or EvidenceStatusDto.ReviewRequired)
            {
                RequireLedgerArtifact(node, "ledger-journal", "ledger-missing-journal-artifact", issues);
                RequireLedgerArtifact(node, "ledger-trial-balance", "ledger-missing-trial-balance-artifact", issues);
            }

            foreach (var artifact in node.ArtifactRefs.Where(static artifact =>
                         artifact.Kind.StartsWith("ledger-", StringComparison.OrdinalIgnoreCase)))
            {
                if (!artifact.Retained)
                {
                    issues.Add(new EvidenceValidationIssueDto(
                        Code: "ledger-artifact-not-retained",
                        Severity: EvidenceValidationSeverityDto.Warning,
                        Message: $"Ledger artifact '{artifact.ArtifactId}' is not marked retained.",
                        EvidenceId: node.EvidenceId,
                        EvidenceKind: node.Kind,
                        SourceSystem: node.SourceSystem));
                }

                if (string.IsNullOrWhiteSpace(artifact.Path) && string.IsNullOrWhiteSpace(artifact.Route))
                {
                    issues.Add(new EvidenceValidationIssueDto(
                        Code: "ledger-artifact-not-addressable",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: $"Ledger artifact '{artifact.ArtifactId}' has no retained path or route.",
                        EvidenceId: node.EvidenceId,
                        EvidenceKind: node.Kind,
                        SourceSystem: node.SourceSystem));
                }
            }
        }
    }

    private static void RequireLedgerArtifact(
        EvidenceNodeDto node,
        string artifactKind,
        string issueCode,
        List<EvidenceValidationIssueDto> issues)
    {
        if (node.ArtifactRefs.Any(artifact => string.Equals(artifact.Kind, artifactKind, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        issues.Add(new EvidenceValidationIssueDto(
            Code: issueCode,
            Severity: EvidenceValidationSeverityDto.Critical,
            Message: $"Ledger evidence '{node.EvidenceId}' is missing required '{artifactKind}' artifact evidence.",
            EvidenceId: node.EvidenceId,
            EvidenceKind: node.Kind,
            SourceSystem: node.SourceSystem));
    }

    private static void ApplyCanonicalSubjectLinkageIssues(
        IReadOnlyList<EvidenceNodeDto> nodes,
        List<EvidenceValidationIssueDto> issues)
    {
        var canonicalKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "run", "account", "fund", "strategy", "instrument", "reconciliation", "report", "approval"
        };

        foreach (var node in nodes)
        {
            foreach (var artifact in node.ArtifactRefs.Where(static artifact => artifact.Retained))
            {
                var hasCanonicalKind = !string.IsNullOrWhiteSpace(artifact.CanonicalSubjectKind);
                var hasCanonicalId = !string.IsNullOrWhiteSpace(artifact.CanonicalSubjectId);
                if (hasCanonicalKind != hasCanonicalId)
                {
                    issues.Add(new EvidenceValidationIssueDto(
                        Code: "retained-artifact-missing-canonical-subject",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: $"Retained artifact '{artifact.ArtifactId}' must provide both canonical subject kind and canonical subject id when either field is present.",
                        EvidenceId: node.EvidenceId,
                        EvidenceKind: node.Kind,
                        SourceSystem: node.SourceSystem));
                    continue;
                }

                if (!hasCanonicalKind)
                {
                    continue;
                }

                if (!canonicalKinds.Contains(artifact.CanonicalSubjectKind))
                {
                    issues.Add(new EvidenceValidationIssueDto(
                        Code: "retained-artifact-invalid-canonical-subject-kind",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: $"Retained artifact '{artifact.ArtifactId}' links to unsupported subject kind '{artifact.CanonicalSubjectKind}'.",
                        EvidenceId: node.EvidenceId,
                        EvidenceKind: node.Kind,
                        SourceSystem: node.SourceSystem));
                }
            }
        }
    }
}
