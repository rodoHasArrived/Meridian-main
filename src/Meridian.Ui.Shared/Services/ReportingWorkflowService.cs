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

    public ReportPackWorkflowRecordDto Create(string fundProfileId, string fundAccountId, string period, VersionedReportTemplateIdDto templateId, string actor)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var record = new ReportPackWorkflowRecordDto(id, fundProfileId, fundAccountId, period, templateId, ReportPackWorkflowStateDto.Draft, 1, now, actor, now,
            [new ReportPackAuditEventDto(now, actor, "create", ReportPackWorkflowStateDto.Draft, ReportPackWorkflowStateDto.Draft)]
            , null);
        _records[id] = record;
        return record;
    }

    public ReportPackWorkflowRecordDto Transition(Guid reportId, ReportPackWorkflowStateDto target, string actor, string role, string? note = null)
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
        var transitioned = Transition(reportId, ReportPackWorkflowStateDto.Restated, actor, role, note: reasonCode);
        var next = transitioned with
        {
            Version = transitioned.Version + 1,
            Restatement = new ReportPackRestatementMetadataDto(reasonCode, approver, priorVersionReportId, changedLines)
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
}
