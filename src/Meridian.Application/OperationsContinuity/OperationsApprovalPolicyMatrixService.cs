using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Workstation;
using Meridian.Storage;
using Meridian.Storage.Archival;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Application.OperationsContinuity;

public interface IOperationsApprovalPolicyMatrixService
{
    OperationsApprovalPolicyMatrixDto GetMatrix();

    Task<OperationsApprovalPolicyRuleUpsertResultDto> UpsertRuleAsync(
        OperationsApprovalPolicyRuleUpsertRequestDto request,
        string actor,
        CancellationToken ct = default);
}

public sealed class OperationsApprovalPolicyMatrixService : IOperationsApprovalPolicyMatrixService
{
    private const string PolicyId = "operations-continuity-approval-policy";
    private const string Version = "2026.05";
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _persistencePath;
    private IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> _inMemoryRows = [];

    public OperationsApprovalPolicyMatrixService()
    {
    }

    public OperationsApprovalPolicyMatrixService(StorageOptions storageOptions)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "governance", "operations-approval-policy-rules.json");
    }

    public OperationsApprovalPolicyMatrixDto GetMatrix() =>
        new(
            PolicyId,
            Version,
            GeneratedAtUtc,
            MergeCustomRows(DefaultRows(), ReadCustomRows()));

    public async Task<OperationsApprovalPolicyRuleUpsertResultDto> UpsertRuleAsync(
        OperationsApprovalPolicyRuleUpsertRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = Validate(request, actor);
        var now = DateTimeOffset.UtcNow;
        var auditId = $"ops-approval-policy-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..55];
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"settings-approval-policy-{Guid.NewGuid():N}"
            : request.CorrelationId.Trim();
        var rule = Row(
            validated.PolicyKey,
            validated.WorkflowArea,
            validated.Action,
            validated.Gate,
            validated.Trigger,
            validated.RequiredPermission,
            validated.SubmitterRole,
            validated.ReviewerRole,
            validated.RequiredDistinctApprovals,
            validated.RequiresIndependentReviewer,
            validated.RequiresReportPack,
            validated.RequiresChecklistControlApprovals,
            validated.EvidenceRequirement,
            validated.AuditEventType,
            validated.Route,
            validated.Severity);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var customRows = ReadCustomRows().ToList();
            var index = customRows.FindIndex(row =>
                string.Equals(row.PolicyKey, validated.PolicyKey, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                customRows[index] = rule;
            }
            else
            {
                customRows.Add(rule);
            }

            await SaveCustomRowsAsync(customRows, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        var matrix = GetMatrix();
        var auditEvent = new OperationsApprovalPolicyRuleAuditEventDto(
            AuditId: auditId,
            EventType: "operations-approval-policy-rule-upserted",
            OccurredAtUtc: now,
            Actor: validated.Actor,
            Rationale: validated.Rationale,
            CorrelationId: correlationId,
            PolicyKey: validated.PolicyKey,
            Action: validated.Action,
            Gate: validated.Gate,
            RequiredDistinctApprovals: validated.RequiredDistinctApprovals,
            RequiresIndependentReviewer: validated.RequiresIndependentReviewer,
            RequiresReportPack: validated.RequiresReportPack,
            RequiresChecklistControlApprovals: validated.RequiresChecklistControlApprovals);

        return new OperationsApprovalPolicyRuleUpsertResultDto(rule, matrix, auditEvent);
    }

    private static IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> DefaultRows() =>
        [
            Row(
                    "operations-continuity.submit-approval",
                    "Operations close",
                    "Submit workflow for approval",
                    OperationsGateKeyDto.Approval,
                    "Broker, Security Master, ledger, and reconciliation gates have no critical blockers.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Assigned reviewer",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: false,
                    requiresReportPack: true,
                    requiresChecklistControlApprovals: true,
                    "Ready report pack plus close checklist control approvals for passed prerequisite gates.",
                    "approval-submitted",
                    UiApiRoutes.OperationsContinuityApprovalSubmit,
                    "Critical"),
            Row(
                    "operations-continuity.approve",
                    "Operations close",
                    "Approve submitted workflow",
                    OperationsGateKeyDto.Approval,
                    "Workflow is submitted and requested reviewer matches the assigned reviewer.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Assigned reviewer",
                    requiredDistinctApprovals: 2,
                    requiresIndependentReviewer: true,
                    requiresReportPack: true,
                    requiresChecklistControlApprovals: true,
                    "Ready report pack plus two distinct approval-gate control approvals.",
                    "approval-approved",
                    UiApiRoutes.OperationsContinuityApprovalApprove,
                    "Critical"),
            Row(
                    "operations-continuity.reject",
                    "Operations close",
                    "Reject submitted workflow",
                    OperationsGateKeyDto.Approval,
                    "Workflow is submitted or reviewer-assigned and rejection metadata is present.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Assigned reviewer",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: true,
                    requiresReportPack: false,
                    requiresChecklistControlApprovals: false,
                    "Reviewer, rationale, reason code, and linked evidence when available.",
                    "approval-rejected",
                    UiApiRoutes.OperationsContinuityApprovalReject,
                    "Error"),
            Row(
                    "operations-continuity.close",
                    "Operations close",
                    "Close approved workflow",
                    OperationsGateKeyDto.Approval,
                    "All gates are passed and close readiness is ready to close.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Controller",
                    requiredDistinctApprovals: 2,
                    requiresIndependentReviewer: true,
                    requiresReportPack: true,
                    requiresChecklistControlApprovals: true,
                    "Ready report pack, close-readiness pass, and close checklist control approvals.",
                    "workflow-closed",
                    UiApiRoutes.OperationsContinuityClose,
                    "Critical"),
            Row(
                    "operations-continuity.reopen",
                    "Governed exception",
                    "Reopen closed workflow",
                    OperationsGateKeyDto.Approval,
                    "Closed workflow requires incident, justification, approval reference, and impact summary.",
                    nameof(UserPermission.AdminMaintenance),
                    "Governed administrator",
                    "Accounting control approver",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: true,
                    requiresReportPack: false,
                    requiresChecklistControlApprovals: false,
                    "Incident evidence, governance approval reference, justification, and impact summary.",
                    "workflow-reopened",
                    UiApiRoutes.OperationsContinuityReopen,
                    "Critical"),
            Row(
                    "operations-continuity.security-master-override",
                    "Reference data control",
                    "Approve Security Master override",
                    OperationsGateKeyDto.SecurityMaster,
                    "A pending Security Master override request has policy reference, rationale, and expiration metadata.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ModifySecurityMaster),
                    "Security Master operator",
                    "Security Master approver",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: true,
                    requiresReportPack: false,
                    requiresChecklistControlApprovals: false,
                    "Override id, policy reference, rationale, expiration date, and linked evidence.",
                    "security-master-override-approved",
                    UiApiRoutes.OperationsContinuitySecurityMasterOverrideApprove,
                    "Error")
        ];

    private IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> ReadCustomRows()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            lock (_readGate)
            {
                return _inMemoryRows;
            }
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<ApprovalPolicyRuleSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.Rows ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private async Task SaveCustomRowsAsync(
        IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> customRows,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            lock (_readGate)
            {
                _inMemoryRows = customRows.ToArray();
            }
            return;
        }

        var snapshot = new ApprovalPolicyRuleSnapshot(
            customRows
                .OrderBy(static row => row.PolicyKey, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await AtomicFileWriter.WriteAsync(_persistencePath, json, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> MergeCustomRows(
        IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> defaults,
        IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> customRows)
    {
        if (customRows.Count == 0)
        {
            return defaults;
        }

        var merged = defaults.ToDictionary(static row => row.PolicyKey, StringComparer.OrdinalIgnoreCase);
        foreach (var customRow in customRows)
        {
            merged[customRow.PolicyKey] = customRow;
        }

        return merged.Values
            .OrderBy(static row => row.WorkflowArea, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Action, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ValidatedApprovalPolicyRule Validate(
        OperationsApprovalPolicyRuleUpsertRequestDto request,
        string actor)
    {
        var policyKey = Required(request.PolicyKey, nameof(request.PolicyKey));
        if (!policyKey.Any(static ch => char.IsLetterOrDigit(ch)))
        {
            throw new ArgumentException("PolicyKey must include at least one letter or digit.", nameof(request));
        }

        if (request.RequiredDistinctApprovals < 1)
        {
            throw new ArgumentException("RequiredDistinctApprovals must be at least 1.", nameof(request));
        }

        var route = Required(request.Route, nameof(request.Route));
        if (!route.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Route must be an absolute Meridian route.", nameof(request));
        }

        var resolvedActor = string.IsNullOrWhiteSpace(actor)
            ? Required(request.RequestedBy, nameof(request.RequestedBy))
            : actor.Trim();

        return new ValidatedApprovalPolicyRule(
            PolicyKey: policyKey,
            WorkflowArea: Required(request.WorkflowArea, nameof(request.WorkflowArea)),
            Action: Required(request.Action, nameof(request.Action)),
            Gate: request.Gate,
            Trigger: Required(request.Trigger, nameof(request.Trigger)),
            RequiredPermission: Required(request.RequiredPermission, nameof(request.RequiredPermission)),
            SubmitterRole: Required(request.SubmitterRole, nameof(request.SubmitterRole)),
            ReviewerRole: Required(request.ReviewerRole, nameof(request.ReviewerRole)),
            RequiredDistinctApprovals: request.RequiredDistinctApprovals,
            RequiresIndependentReviewer: request.RequiresIndependentReviewer,
            RequiresReportPack: request.RequiresReportPack,
            RequiresChecklistControlApprovals: request.RequiresChecklistControlApprovals,
            EvidenceRequirement: Required(request.EvidenceRequirement, nameof(request.EvidenceRequirement)),
            AuditEventType: Required(request.AuditEventType, nameof(request.AuditEventType)),
            Route: route,
            Severity: Required(request.Severity, nameof(request.Severity)),
            Actor: resolvedActor,
            Rationale: Required(request.Rationale, nameof(request.Rationale)));
    }

    private static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }

    private static OperationsApprovalPolicyMatrixRowDto Row(
        string policyKey,
        string workflowArea,
        string action,
        OperationsGateKeyDto gate,
        string trigger,
        string requiredPermission,
        string submitterRole,
        string reviewerRole,
        int requiredDistinctApprovals,
        bool requiresIndependentReviewer,
        bool requiresReportPack,
        bool requiresChecklistControlApprovals,
        string evidenceRequirement,
        string auditEventType,
        string route,
        string severity) =>
        new(
            policyKey,
            workflowArea,
            action,
            gate,
            trigger,
            requiredPermission,
            submitterRole,
            reviewerRole,
            requiredDistinctApprovals,
            requiresIndependentReviewer,
            requiresReportPack,
            requiresChecklistControlApprovals,
            evidenceRequirement,
            auditEventType,
            route,
            severity);

    private static string AnyPermission(params UserPermission[] permissions) =>
        $"Any({string.Join(",", permissions.Select(static permission => permission.ToString()))})";

    private sealed record ApprovalPolicyRuleSnapshot(
        IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> Rows);

    private sealed record ValidatedApprovalPolicyRule(
        string PolicyKey,
        string WorkflowArea,
        string Action,
        OperationsGateKeyDto Gate,
        string Trigger,
        string RequiredPermission,
        string SubmitterRole,
        string ReviewerRole,
        int RequiredDistinctApprovals,
        bool RequiresIndependentReviewer,
        bool RequiresReportPack,
        bool RequiresChecklistControlApprovals,
        string EvidenceRequirement,
        string AuditEventType,
        string Route,
        string Severity,
        string Actor,
        string Rationale);
}
