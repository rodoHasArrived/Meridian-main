using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Application.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportTemplateRegistryService
{
    private readonly ConcurrentDictionary<string, ReportTemplateGovernanceRecordDto> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReportTemplateGovernanceStore? _store;

    public ReportTemplateRegistryService(IReportTemplateGovernanceStore? store = null)
    {
        _store = store;
        foreach (var template in new DefaultReportingTemplateCatalog().ListTemplates())
        {
            var definition = new ReportTemplateDefinitionDto(
                new VersionedReportTemplateIdDto(template.TemplateId, ParseMajorVersion(template.Version)),
                template.Name,
                [],
                template.Sections.ToArray());
            var now = DateTimeOffset.UtcNow;
            var record = new ReportTemplateGovernanceRecordDto(
                definition,
                ReportTemplateLifecycleStatusDto.Approved,
                template.Family.ToString(),
                IsBuiltIn: true,
                IsLatestApproved: true,
                CreatedBy: "system",
                CreatedAt: now,
                UpdatedBy: "system",
                UpdatedAt: now,
                ValidationIssues: [],
                AuditTrail: [new ReportTemplateAuditEventDto(now, "system", "seed-built-in", ReportTemplateLifecycleStatusDto.Approved, ReportTemplateLifecycleStatusDto.Approved, "Built-in template catalog")],
                ApprovedBy: "system",
                ApprovedAt: now,
                DecisionRationale: "Built-in Reporting template",
                ApprovalReference: $"builtin:{template.TemplateId}@{template.Version}");
            _templates[ToKey(definition.TemplateId)] = record;
        }

        foreach (var record in _store?.Load() ?? [])
        {
            if (!record.IsBuiltIn)
            {
                _templates[ToKey(record.Definition.TemplateId)] = record;
            }
        }

        ReconcileLatestApprovedTemplates();
    }

    public ReportTemplateDefinitionDto Register(ReportTemplateDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var normalized = NormalizeDefinition(definition);
        var now = DateTimeOffset.UtcNow;
        var validationIssues = ValidateDefinition(normalized);
        var record = new ReportTemplateGovernanceRecordDto(
            normalized,
            ReportTemplateLifecycleStatusDto.Approved,
            "Custom",
            IsBuiltIn: false,
            IsLatestApproved: true,
            CreatedBy: "legacy-register",
            CreatedAt: now,
            UpdatedBy: "legacy-register",
            UpdatedAt: now,
            ValidationIssues: validationIssues,
            AuditTrail: [new ReportTemplateAuditEventDto(now, "legacy-register", "register", ReportTemplateLifecycleStatusDto.Approved, ReportTemplateLifecycleStatusDto.Approved, "Compatibility template registration")],
            ApprovedBy: "legacy-register",
            ApprovedAt: now,
            DecisionRationale: "Compatibility registration");
        MarkPriorApprovedTemplatesNotLatest(normalized.TemplateId.Name, normalized.TemplateId.Version);
        _templates[ToKey(normalized.TemplateId)] = record;
        PersistTemplates();
        return normalized;
    }

    public ReportTemplateDefinitionDto? Get(VersionedReportTemplateIdDto id) =>
        _templates.TryGetValue(ToKey(id), out var template) && template.Status == ReportTemplateLifecycleStatusDto.Approved
            ? template.Definition
            : null;

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> List(bool includeSuperseded = false) =>
        _templates.Values
            .Where(record => includeSuperseded || record.Status != ReportTemplateLifecycleStatusDto.Superseded)
            .OrderBy(record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(record => record.Definition.TemplateId.Version)
            .ToArray();

    public ReportTemplateGovernanceRecordDto CreateDraft(ReportTemplateDraftRequestDto request, string actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var name = NormalizeIdentifier(request.Name);
        var latestVersion = _templates.Values
            .Where(record => string.Equals(record.Definition.TemplateId.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(record => record.Definition.TemplateId.Version)
            .DefaultIfEmpty(0)
            .Max();
        var nextVersion = latestVersion + 1;
        var basedOnTemplateId = request.BasedOnVersion is { } basedOnVersion
            ? new VersionedReportTemplateIdDto(name, basedOnVersion)
            : latestVersion > 0
                ? new VersionedReportTemplateIdDto(name, latestVersion)
                : null;
        var definition = NormalizeDefinition(new ReportTemplateDefinitionDto(
            new VersionedReportTemplateIdDto(name, nextVersion),
            request.DisplayName,
            request.Parameters,
            request.Sections));
        var now = DateTimeOffset.UtcNow;
        var record = new ReportTemplateGovernanceRecordDto(
            definition,
            ReportTemplateLifecycleStatusDto.Draft,
            string.IsNullOrWhiteSpace(request.Family) ? "Custom" : request.Family.Trim(),
            IsBuiltIn: false,
            IsLatestApproved: false,
            CreatedBy: actor.Trim(),
            CreatedAt: now,
            UpdatedBy: actor.Trim(),
            UpdatedAt: now,
            ValidationIssues: ValidateDefinition(definition),
            AuditTrail: [new ReportTemplateAuditEventDto(now, actor.Trim(), "draft", ReportTemplateLifecycleStatusDto.Draft, ReportTemplateLifecycleStatusDto.Draft, request.Rationale?.Trim())],
            DecisionRationale: string.IsNullOrWhiteSpace(request.Rationale) ? null : request.Rationale.Trim(),
            BasedOnTemplateId: basedOnTemplateId);
        _templates[ToKey(definition.TemplateId)] = record;
        PersistTemplates();
        return record;
    }

    public ReportTemplateGovernanceRecordDto Submit(VersionedReportTemplateIdDto id, string actor, string? note = null)
    {
        var record = GetRecord(id);
        if (record.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in report templates are already approved and cannot be submitted.");
        }

        if (record.Status is not (ReportTemplateLifecycleStatusDto.Draft or ReportTemplateLifecycleStatusDto.Rejected))
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} cannot be submitted from {record.Status}.");
        }

        var validationIssues = ValidateDefinition(record.Definition);
        if (validationIssues.Count > 0)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} is not ready for review: {string.Join("; ", validationIssues)}");
        }

        return Transition(record, ReportTemplateLifecycleStatusDto.InReview, actor, "submit", note);
    }

    public ReportTemplateGovernanceRecordDto Approve(VersionedReportTemplateIdDto id, ReportTemplateDecisionRequestDto request, string actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Rationale);
        var record = GetRecord(id);
        if (record.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in report templates are immutable.");
        }

        if (record.Status != ReportTemplateLifecycleStatusDto.InReview)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} must be in review before approval.");
        }

        var validationIssues = ValidateDefinition(record.Definition);
        if (validationIssues.Count > 0)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} cannot be approved: {string.Join("; ", validationIssues)}");
        }

        MarkPriorApprovedTemplatesNotLatest(record.Definition.TemplateId.Name, record.Definition.TemplateId.Version);
        var approved = Transition(record, ReportTemplateLifecycleStatusDto.Approved, actor, "approve", request.Rationale.Trim()) with
        {
            ApprovedBy = actor.Trim(),
            ApprovedAt = DateTimeOffset.UtcNow,
            DecisionRationale = request.Rationale.Trim(),
            ApprovalReference = string.IsNullOrWhiteSpace(request.ApprovalReference) ? null : request.ApprovalReference.Trim(),
            IsLatestApproved = true
        };
        _templates[ToKey(approved.Definition.TemplateId)] = approved;
        PersistTemplates();
        return approved;
    }

    public ReportTemplateGovernanceRecordDto Reject(VersionedReportTemplateIdDto id, ReportTemplateDecisionRequestDto request, string actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Rationale);
        var record = GetRecord(id);
        if (record.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in report templates are immutable.");
        }

        if (record.Status != ReportTemplateLifecycleStatusDto.InReview)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} must be in review before rejection.");
        }

        var rejected = Transition(record, ReportTemplateLifecycleStatusDto.Rejected, actor, "reject", request.Rationale.Trim()) with
        {
            RejectedBy = actor.Trim(),
            RejectedAt = DateTimeOffset.UtcNow,
            DecisionRationale = request.Rationale.Trim(),
            ApprovalReference = string.IsNullOrWhiteSpace(request.ApprovalReference) ? null : request.ApprovalReference.Trim()
        };
        _templates[ToKey(rejected.Definition.TemplateId)] = rejected;
        PersistTemplates();
        return rejected;
    }

    public RenderReportTemplateResponseDto Render(RenderReportTemplateRequestDto request)
    {
        var template = Get(request.TemplateId) ?? throw new ArgumentException("Template not found.");
        var missing = template.Parameters.Where(p => p.Required && !request.Parameters.ContainsKey(p.Name)).Select(p => p.Name).ToArray();
        var sections = template.Sections is { Count: > 0 }
            ? string.Join(',', template.Sections)
            : "sections:not-configured";
        var rendered = $"template:{template.TemplateId.Name}@v{template.TemplateId.Version};sections={sections};" + string.Join(';', request.Parameters.OrderBy(k => k.Key).Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return new RenderReportTemplateResponseDto(template.TemplateId, rendered, missing);
    }

    private static string ToKey(VersionedReportTemplateIdDto id) => $"{id.Name}:{id.Version}";

    private static int ParseMajorVersion(string version)
    {
        var major = version.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(major, out var value) && value > 0 ? value : 1;
    }

    private static string NormalizeIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    private static ReportTemplateDefinitionDto NormalizeDefinition(ReportTemplateDefinitionDto definition)
    {
        var id = new VersionedReportTemplateIdDto(
            NormalizeIdentifier(definition.TemplateId.Name),
            definition.TemplateId.Version);
        if (id.Version <= 0)
        {
            throw new ArgumentException("Template version must be greater than zero.");
        }

        var parameters = definition.Parameters
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .Select(static parameter => parameter with { Name = parameter.Name.Trim() })
            .GroupBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sections = definition.Sections?
            .Where(static section => !string.IsNullOrWhiteSpace(section))
            .Select(static section => section.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static section => section, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        return definition with
        {
            TemplateId = id,
            DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? string.Empty : definition.DisplayName.Trim(),
            Parameters = parameters,
            Sections = sections
        };
    }

    private static IReadOnlyList<string> ValidateDefinition(ReportTemplateDefinitionDto definition)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.TemplateId.Name))
        {
            issues.Add("Template name is required.");
        }

        if (definition.TemplateId.Version <= 0)
        {
            issues.Add("Template version must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(definition.DisplayName))
        {
            issues.Add("Display name is required.");
        }

        if (definition.Sections is null || definition.Sections.Count == 0)
        {
            issues.Add("At least one report section is required.");
        }

        var duplicateParameters = definition.Parameters
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(static parameter => parameter.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateParameters.Length > 0)
        {
            issues.Add($"Parameter names must be unique: {string.Join(", ", duplicateParameters)}.");
        }

        return issues;
    }

    private ReportTemplateGovernanceRecordDto GetRecord(VersionedReportTemplateIdDto id) =>
        _templates.TryGetValue(ToKey(id), out var record)
            ? record
            : throw new KeyNotFoundException($"Template {id.Name}@v{id.Version} was not found.");

    private ReportTemplateGovernanceRecordDto Transition(
        ReportTemplateGovernanceRecordDto record,
        ReportTemplateLifecycleStatusDto target,
        string actor,
        string action,
        string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var now = DateTimeOffset.UtcNow;
        var next = record with
        {
            Status = target,
            UpdatedBy = actor.Trim(),
            UpdatedAt = now,
            ValidationIssues = ValidateDefinition(record.Definition),
            SubmittedBy = target == ReportTemplateLifecycleStatusDto.InReview ? actor.Trim() : record.SubmittedBy,
            SubmittedAt = target == ReportTemplateLifecycleStatusDto.InReview ? now : record.SubmittedAt,
            AuditTrail = record.AuditTrail.Append(new ReportTemplateAuditEventDto(now, actor.Trim(), action, record.Status, target, note)).ToArray()
        };
        _templates[ToKey(next.Definition.TemplateId)] = next;
        PersistTemplates();
        return next;
    }

    private void MarkPriorApprovedTemplatesNotLatest(string templateName, int newLatestVersion)
    {
        foreach (var record in _templates.Values.Where(record =>
                     string.Equals(record.Definition.TemplateId.Name, templateName, StringComparison.OrdinalIgnoreCase)
                     && record.Definition.TemplateId.Version != newLatestVersion
                     && record.Status == ReportTemplateLifecycleStatusDto.Approved))
        {
            var updated = record with
            {
                Status = record.IsBuiltIn ? record.Status : ReportTemplateLifecycleStatusDto.Superseded,
                IsLatestApproved = false
            };
            _templates[ToKey(updated.Definition.TemplateId)] = updated;
        }
    }

    private void ReconcileLatestApprovedTemplates()
    {
        foreach (var group in _templates.Values.GroupBy(record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase))
        {
            var latestApprovedVersion = group
                .Where(static record => record.Status == ReportTemplateLifecycleStatusDto.Approved)
                .Select(static record => (int?)record.Definition.TemplateId.Version)
                .Max();
            if (latestApprovedVersion is null)
            {
                continue;
            }

            foreach (var record in group)
            {
                var updated = record with
                {
                    IsLatestApproved = record.Status == ReportTemplateLifecycleStatusDto.Approved
                        && record.Definition.TemplateId.Version == latestApprovedVersion.Value
                };
                _templates[ToKey(updated.Definition.TemplateId)] = updated;
            }
        }
    }

    private void PersistTemplates()
    {
        _store?.Save(_templates.Values.Where(static record => !record.IsBuiltIn).ToArray());
    }
}

public sealed record ReportTemplateGovernanceStoreOptions(string SnapshotPath);

public interface IReportTemplateGovernanceStore
{
    IReadOnlyList<ReportTemplateGovernanceRecordDto> Load();
    void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records);
}

public sealed class FileReportTemplateGovernanceStore : IReportTemplateGovernanceStore
{
    private readonly ReportTemplateGovernanceStoreOptions _options;
    private readonly ILogger<FileReportTemplateGovernanceStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportTemplateGovernanceStore(
        ReportTemplateGovernanceStoreOptions options,
        ILogger<FileReportTemplateGovernanceStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> Load()
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
                return JsonSerializer.Deserialize<ReportTemplateGovernanceSnapshot>(json, _jsonOptions)?.Records ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to load report template governance snapshot from {SnapshotPath}.", _options.SnapshotPath);
                return [];
            }
        }
    }

    public void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            var snapshot = new ReportTemplateGovernanceSnapshot(
                records
                    .OrderBy(static record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(static record => record.Definition.TemplateId.Version)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private sealed record ReportTemplateGovernanceSnapshot(IReadOnlyList<ReportTemplateGovernanceRecordDto> Records);
}

public sealed record ReportPackWorkflowRecordStoreOptions(string SnapshotPath);

public interface IReportPackWorkflowRecordStore
{
    IReadOnlyList<ReportPackWorkflowRecordDto> Load();
    void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records);
}

public sealed class FileReportPackWorkflowRecordStore : IReportPackWorkflowRecordStore
{
    private readonly ReportPackWorkflowRecordStoreOptions _options;
    private readonly ILogger<FileReportPackWorkflowRecordStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportPackWorkflowRecordStore(
        ReportPackWorkflowRecordStoreOptions options,
        ILogger<FileReportPackWorkflowRecordStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportPackWorkflowRecordDto> Load()
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
                return JsonSerializer.Deserialize<ReportPackWorkflowSnapshot>(json, _jsonOptions)?.Records ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to load report-pack workflow snapshot from {SnapshotPath}.", _options.SnapshotPath);
                return [];
            }
        }
    }

    public void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            var snapshot = new ReportPackWorkflowSnapshot(
                records
                    .OrderByDescending(static record => record.UpdatedAt)
                    .ThenBy(static record => record.ReportId)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private sealed record ReportPackWorkflowSnapshot(IReadOnlyList<ReportPackWorkflowRecordDto> Records);
}

public sealed class ReportPackWorkflowService
{
    private static readonly IReadOnlyDictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]> AllowedTransitions =
        new Dictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]>
        {
            [ReportPackWorkflowStateDto.Draft] = [ReportPackWorkflowStateDto.InReview, ReportPackWorkflowStateDto.Validated],
            [ReportPackWorkflowStateDto.Validated] = [ReportPackWorkflowStateDto.InReview, ReportPackWorkflowStateDto.PendingApproval, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.InReview] = [ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.PendingApproval, ReportPackWorkflowStateDto.Rejected, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.PendingApproval] = [ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.Rejected] = [ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.Approved] = [ReportPackWorkflowStateDto.Published],
            [ReportPackWorkflowStateDto.Published] = [ReportPackWorkflowStateDto.Restated, ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Restated] = [ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Archived] = []
        };

    private readonly ConcurrentDictionary<Guid, ReportPackWorkflowRecordDto> _records = new();
    private readonly IReportPackWorkflowRecordStore? _store;

    public ReportPackWorkflowService(IReportPackWorkflowRecordStore? store = null)
    {
        _store = store;
        foreach (var record in _store?.Load() ?? [])
        {
            _records[record.ReportId] = record;
        }
    }

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
        PersistRecords();
        return record;
    }

    public ReportPackWorkflowRecordDto Submit(Guid reportId, string actor, string role, string? note = null) =>
        TransitionCore(reportId, ReportPackWorkflowStateDto.InReview, actor, role, note);

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
        PersistRecords();
        return next;
    }

    public ReportPackWorkflowRecordDto Reject(Guid reportId, string reason, string actor, string role, IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var transitioned = TransitionCore(reportId, ReportPackWorkflowStateDto.Rejected, actor, role, reason.Trim());
        var rejectedAt = DateTimeOffset.UtcNow;
        var next = transitioned with
        {
            Rejection = new ReportPackRejectionMetadataDto(
                reason.Trim(),
                actor.Trim(),
                role.Trim(),
                rejectedAt,
                evidenceLinks is null ? null : NormalizeEvidenceLinks(evidenceLinks))
        };
        _records[reportId] = next;
        PersistRecords();
        return next;
    }

    public ReportPackWorkflowRecordDto Reject(Guid reportId, ReportPackRejectRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Reject(reportId, request.Reason, request.Actor, request.ActorRole, request.EvidenceLinks);
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
        PersistRecords();
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
        PersistRecords();
        return next;
    }

    public IReadOnlyList<ReportPackWorkflowRecordDto> GetHistory(string period, string fundAccountId) =>
        _records.Values.Where(x => x.Period == period && x.FundAccountId == fundAccountId).OrderByDescending(x => x.Version).ToArray();

    public IReadOnlyList<ReportPackWorkflowRecordDto> ListRecords(int limit = 25) =>
        _records.Values
            .OrderByDescending(static x => x.UpdatedAt)
            .ThenByDescending(static x => x.Version)
            .ThenBy(static x => x.ReportId)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();

    private void PersistRecords()
    {
        _store?.Save(_records.Values.ToArray());
    }

    private static void EnsureRole(ReportPackWorkflowStateDto target, string role)
    {
        var normalized = role.Trim().ToLowerInvariant();

        var allowed =
            target == ReportPackWorkflowStateDto.InReview ||
            target == ReportPackWorkflowStateDto.Validated ||
            target == ReportPackWorkflowStateDto.PendingApproval
                ? normalized is "operator" or "reviewer" or "validator" or "admin"
                : target switch
        {
            ReportPackWorkflowStateDto.Rejected => normalized is "reviewer" or "approver" or "admin",
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
                ReconciliationRunId = string.IsNullOrWhiteSpace(item.ReconciliationRunId) ? null : item.ReconciliationRunId.Trim(),
                ProviderEventId = string.IsNullOrWhiteSpace(item.ProviderEventId) ? null : item.ProviderEventId.Trim(),
                SecurityMasterId = string.IsNullOrWhiteSpace(item.SecurityMasterId) ? null : item.SecurityMasterId.Trim(),
                SecurityDefinitionId = string.IsNullOrWhiteSpace(item.SecurityDefinitionId) ? null : item.SecurityDefinitionId.Trim(),
                ReconciliationOutcome = string.IsNullOrWhiteSpace(item.ReconciliationOutcome) ? null : item.ReconciliationOutcome.Trim(),
                ApprovalId = string.IsNullOrWhiteSpace(item.ApprovalId) ? null : item.ApprovalId.Trim()
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

        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.LedgerEntryId),
            "ledger entries");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.ProviderEventId),
            "provider events");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.SecurityMasterId) || !string.IsNullOrWhiteSpace(line.SecurityDefinitionId),
            "Security Master definitions");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => (!string.IsNullOrWhiteSpace(line.ReconciliationRunId) || !string.IsNullOrWhiteSpace(line.ReconciliationCaseId))
                && !string.IsNullOrWhiteSpace(line.ReconciliationOutcome),
            "reconciliation outcomes");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.ApprovalId),
            "approval references");
    }

    private static void EnsureLineProvenanceCategory(
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        Func<ReportPackLineProvenanceDto, bool> predicate,
        string category)
    {
        var missing = lineProvenance
            .Where(line => !predicate(line))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires {category} for: {string.Join(", ", missing.Order(StringComparer.OrdinalIgnoreCase))}.");
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

        var missingPointers = lineProvenance
            .SelectMany(EnumerateRetainedProvenancePointers)
            .Where(pointer => !retainedEvidenceIds.Contains(pointer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingPointers.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack publication has orphan provenance pointers: {string.Join(", ", missingPointers.Order(StringComparer.OrdinalIgnoreCase))}.");
        }
    }

    private static IEnumerable<string> EnumerateRetainedProvenancePointers(ReportPackLineProvenanceDto line)
    {
        yield return line.SourceId;

        if (!string.IsNullOrWhiteSpace(line.RunId))
            yield return line.RunId;
        if (!string.IsNullOrWhiteSpace(line.SourceSessionId))
            yield return line.SourceSessionId;
        if (!string.IsNullOrWhiteSpace(line.LedgerEntryId))
            yield return line.LedgerEntryId;
        if (!string.IsNullOrWhiteSpace(line.ReconciliationCaseId))
            yield return line.ReconciliationCaseId;
        if (!string.IsNullOrWhiteSpace(line.ReconciliationRunId))
            yield return line.ReconciliationRunId;
        if (!string.IsNullOrWhiteSpace(line.ProviderEventId))
            yield return line.ProviderEventId;
        if (!string.IsNullOrWhiteSpace(line.SecurityMasterId))
            yield return line.SecurityMasterId;
        if (!string.IsNullOrWhiteSpace(line.SecurityDefinitionId))
            yield return line.SecurityDefinitionId;
        if (!string.IsNullOrWhiteSpace(line.ApprovalId))
            yield return line.ApprovalId;
    }
}
