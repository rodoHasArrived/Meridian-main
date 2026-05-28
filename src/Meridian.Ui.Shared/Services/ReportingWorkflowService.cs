using System.Collections.Concurrent;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportTemplateRegistryService
{
    private readonly ConcurrentDictionary<string, ReportTemplateDefinitionDto> _templates = new(StringComparer.OrdinalIgnoreCase);

    public ReportTemplateDefinitionDto Register(ReportTemplateDefinitionDto definition)
    {
        _templates[ToKey(definition.TemplateId)] = definition;
        return definition;
    }

    public ReportTemplateDefinitionDto? Get(VersionedReportTemplateIdDto id) =>
        _templates.TryGetValue(ToKey(id), out var template) ? template : null;

    public RenderReportTemplateResponseDto Render(RenderReportTemplateRequestDto request)
    {
        var template = Get(request.TemplateId) ?? throw new ArgumentException("Template not found.");
        var missing = template.Parameters.Where(p => p.Required && !request.Parameters.ContainsKey(p.Name)).Select(p => p.Name).ToArray();
        var rendered = $"template:{template.TemplateId.Name}@v{template.TemplateId.Version};" + string.Join(';', request.Parameters.OrderBy(k => k.Key).Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return new RenderReportTemplateResponseDto(template.TemplateId, rendered, missing);
    }

    private static string ToKey(VersionedReportTemplateIdDto id) => $"{id.Name}:{id.Version}";
}

public sealed class ReportPackWorkflowService
{
    private static readonly IReadOnlyDictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]> AllowedTransitions =
        new Dictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]>
        {
            [ReportPackWorkflowStateDto.Draft] = [ReportPackWorkflowStateDto.Validated],
            [ReportPackWorkflowStateDto.Validated] = [ReportPackWorkflowStateDto.PendingApproval, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.PendingApproval] = [ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.Approved] = [ReportPackWorkflowStateDto.Published],
            [ReportPackWorkflowStateDto.Published] = [ReportPackWorkflowStateDto.Restated, ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Restated] = [ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Archived] = []
        };

    private readonly ConcurrentDictionary<Guid, ReportPackWorkflowRecordDto> _records = new();

    public ReportPackWorkflowRecordDto Create(
        string fundProfileId,
        string fundAccountId,
        string period,
        VersionedReportTemplateIdDto templateId,
        string actor,
        IReadOnlyList<ReportPackLineProvenanceDto>? lineProvenance = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var record = new ReportPackWorkflowRecordDto(id, fundProfileId, fundAccountId, period, templateId, ReportPackWorkflowStateDto.Draft, 1, now, actor, now,
            [new ReportPackAuditEventDto(now, actor, "create", ReportPackWorkflowStateDto.Draft, ReportPackWorkflowStateDto.Draft)]
            , null,
            NormalizeLineProvenance(lineProvenance));
        _records[id] = record;
        return record;
    }

    public ReportPackWorkflowRecordDto Transition(Guid reportId, ReportPackWorkflowStateDto target, string actor, string role, string? note = null)
    {
        if (target == ReportPackWorkflowStateDto.Published)
        {
            throw new InvalidOperationException("Report pack publication requires sign-off, evidence hash, and retained manifest metadata.");
        }

        return TransitionCore(reportId, target, actor, role, note);
    }

    private ReportPackWorkflowRecordDto TransitionCore(Guid reportId, ReportPackWorkflowStateDto target, string actor, string role, string? note = null)
    {
        if (!_records.TryGetValue(reportId, out var record)) throw new KeyNotFoundException("report pack not found");
        EnsureRole(target, role);
        if (!AllowedTransitions[record.State].Contains(target)) throw new InvalidOperationException($"invalid transition {record.State} -> {target}");
        var now = DateTimeOffset.UtcNow;
        var next = record with
        {
            State = target,
            UpdatedAt = now,
            AuditTrail = record.AuditTrail.Append(new ReportPackAuditEventDto(now, actor, target.ToString().ToLowerInvariant(), record.State, target, note)).ToArray()
        };
        _records[reportId] = next;
        return next;
    }

    public ReportPackWorkflowRecordDto Restate(Guid reportId, string actor, string role, string reasonCode, string approver, Guid priorVersionReportId, IReadOnlyList<ReportPackChangedLineDto> changedLines)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("reasonCode is required");
        if (changedLines.Count == 0) throw new ArgumentException("changedLines are required");
        var linesWithoutEvidence = changedLines
            .Where(static line => line.EvidenceLinks is null || line.EvidenceLinks.Count == 0 || line.EvidenceLinks.All(static link => string.IsNullOrWhiteSpace(link.EvidenceId)))
            .Select(static line => line.LineKey)
            .ToArray();
        if (linesWithoutEvidence.Length > 0)
        {
            throw new ArgumentException($"Restatement changed lines require evidence links: {string.Join(", ", linesWithoutEvidence)}.");
        }

        var transitioned = Transition(reportId, ReportPackWorkflowStateDto.Restated, actor, role, note: reasonCode);
        var evidenceLinks = changedLines
            .SelectMany(static line => line.EvidenceLinks ?? [])
            .GroupBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var next = transitioned with
        {
            Version = transitioned.Version + 1,
            Restatement = new ReportPackRestatementMetadataDto(reasonCode, approver, priorVersionReportId, changedLines, evidenceLinks)
        };
        _records[reportId] = next;
        return next;
    }

    public ReportPackWorkflowRecordDto Publish(
        Guid reportId,
        string actor,
        string role,
        string signedOffBy,
        string evidenceHash,
        string manifestId,
        string retainedManifestPath,
        IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks,
        string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedOffBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedManifestPath);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("Publication requires retained evidence links.", nameof(evidenceLinks));
        }

        if (!_records.TryGetValue(reportId, out var record))
        {
            throw new KeyNotFoundException("report pack not found");
        }

        EnsureLineProvenanceTraceable(record.LineProvenance ?? []);
        EnsureNoOrphanEvidence(record.LineProvenance ?? [], evidenceLinks);
        var transitioned = TransitionCore(reportId, ReportPackWorkflowStateDto.Published, actor, role, note);
        var next = transitioned with
        {
            Publication = new ReportPackPublicationManifestDto(
                manifestId.Trim(),
                retainedManifestPath.Trim(),
                evidenceHash.Trim(),
                signedOffBy.Trim(),
                DateTimeOffset.UtcNow,
                NormalizeEvidenceLinks(evidenceLinks))
        };
        _records[reportId] = next;
        return next;
    }

    public IReadOnlyList<ReportPackWorkflowRecordDto> GetHistory(string period, string fundAccountId) =>
        _records.Values.Where(x => x.Period == period && x.FundAccountId == fundAccountId).OrderByDescending(x => x.Version).ToArray();

    private static void EnsureRole(ReportPackWorkflowStateDto target, string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        var allowed = target switch
        {
            ReportPackWorkflowStateDto.Validated => normalized is "operator" or "reviewer" or "validator",
            ReportPackWorkflowStateDto.PendingApproval => normalized is "operator" or "reviewer" or "validator",
            ReportPackWorkflowStateDto.Approved => normalized is "approver" or "admin",
            ReportPackWorkflowStateDto.Published => normalized is "publisher" or "admin",
            ReportPackWorkflowStateDto.Restated => normalized is "approver" or "admin",
            ReportPackWorkflowStateDto.Archived => normalized is "admin" or "records-manager",
            _ => true
        };
        if (!allowed) throw new UnauthorizedAccessException($"Role '{role}' cannot transition to {target}.");
    }

    private static IReadOnlyList<ReportPackLineProvenanceDto> NormalizeLineProvenance(IReadOnlyList<ReportPackLineProvenanceDto>? lineProvenance) =>
        lineProvenance?
            .Where(static item =>
                !string.IsNullOrWhiteSpace(item.LineKey) &&
                !string.IsNullOrWhiteSpace(item.SourceKind) &&
                !string.IsNullOrWhiteSpace(item.SourceId) &&
                !string.IsNullOrWhiteSpace(item.EvidenceId))
            .Select(static item => item with
            {
                LineKey = item.LineKey.Trim(),
                SourceKind = item.SourceKind.Trim(),
                SourceId = item.SourceId.Trim(),
                EvidenceId = item.EvidenceId.Trim(),
                RunId = string.IsNullOrWhiteSpace(item.RunId) ? null : item.RunId.Trim(),
                LedgerEntryId = string.IsNullOrWhiteSpace(item.LedgerEntryId) ? null : item.LedgerEntryId.Trim(),
                ReconciliationCaseId = string.IsNullOrWhiteSpace(item.ReconciliationCaseId) ? null : item.ReconciliationCaseId.Trim(),
                ReportValue = string.IsNullOrWhiteSpace(item.ReportValue) ? null : item.ReportValue.Trim(),
                SourceSessionId = string.IsNullOrWhiteSpace(item.SourceSessionId) ? null : item.SourceSessionId.Trim(),
                ReconciliationRunId = string.IsNullOrWhiteSpace(item.ReconciliationRunId) ? null : item.ReconciliationRunId.Trim()
            })
            .ToArray() ?? [];

    private static IReadOnlyList<ReportPackEvidenceLinkDto> NormalizeEvidenceLinks(IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks) =>
        evidenceLinks
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
                    Source = string.IsNullOrWhiteSpace(link.Source) ? "report-pack" : link.Source.Trim()
                };
            })
            .ToArray();

    private static void EnsureLineProvenanceTraceable(IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance)
    {
        var missingValues = lineProvenance
            .Where(static line => string.IsNullOrWhiteSpace(line.ReportValue))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingValues.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires report values for: {string.Join(", ", missingValues.Order(StringComparer.OrdinalIgnoreCase))}.");
        }

        var missingSourcePointers = lineProvenance
            .Where(static line =>
                string.IsNullOrWhiteSpace(line.RunId) &&
                string.IsNullOrWhiteSpace(line.SourceSessionId) &&
                string.IsNullOrWhiteSpace(line.LedgerEntryId) &&
                string.IsNullOrWhiteSpace(line.ReconciliationCaseId) &&
                string.IsNullOrWhiteSpace(line.ReconciliationRunId))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingSourcePointers.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires run, session, ledger, or reconciliation source pointers for: {string.Join(", ", missingSourcePointers.Order(StringComparer.OrdinalIgnoreCase))}.");
        }
    }

    private static void EnsureNoOrphanEvidence(
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks)
    {
        var retainedEvidenceIds = evidenceLinks
            .Select(static link => link.EvidenceId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingEvidence = lineProvenance
            .Select(static line => line.EvidenceId)
            .Where(evidenceId => !retainedEvidenceIds.Contains(evidenceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingEvidence.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack publication has orphan evidence: {string.Join(", ", missingEvidence.Order(StringComparer.OrdinalIgnoreCase))}.");
        }
    }
}
