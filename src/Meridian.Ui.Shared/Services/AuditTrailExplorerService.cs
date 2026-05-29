using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Projects execution audit records into the shared cross-object audit timeline contract.
/// </summary>
public sealed class AuditTrailExplorerService
{
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly IOperationsContinuityWorkflowService? _operationsContinuity;

    public AuditTrailExplorerService(
        ExecutionAuditTrailService? auditTrail = null,
        IOperationsContinuityWorkflowService? operationsContinuity = null)
    {
        _auditTrail = auditTrail;
        _operationsContinuity = operationsContinuity;
    }

    public async Task<AuditTrailExplorerResultDto> SearchAsync(
        AuditTrailExplorerQueryDto? query = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        query ??= new AuditTrailExplorerQueryDto();

        var entries = new List<AuditTrailTimelineEntryDto>();
        if (_auditTrail is not null)
        {
            var executionEntries = await _auditTrail.GetAllAsync(ct).ConfigureAwait(false);
            entries.AddRange(executionEntries.Select(MapExecution));
        }

        if (_operationsContinuity is not null)
        {
            var workflows = await _operationsContinuity.ListAsync(ct: ct).ConfigureAwait(false);
            foreach (var workflow in workflows)
            {
                ct.ThrowIfCancellationRequested();
                var timeline = await _operationsContinuity.GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
                entries.AddRange(timeline.Select(MapOperations));
            }
        }

        var matched = entries
            .Where(entry => Matches(entry, query))
            .OrderByDescending(static entry => entry.OccurredAt)
            .ThenBy(static entry => entry.AuditId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var returned = matched
            .Take(Math.Clamp(query.Limit, 1, 1_000))
            .ToArray();

        return new AuditTrailExplorerResultDto(
            DateTimeOffset.UtcNow,
            matched.Length,
            returned.Length,
            returned);
    }

    private static bool Matches(AuditTrailTimelineEntryDto entry, AuditTrailExplorerQueryDto query)
    {
        if (!MatchesText(entry, query.SearchText))
        {
            return false;
        }

        return MatchesValue(entry.RunId, query.RunId) &&
            MatchesValue(entry.Category, query.Category) &&
            MatchesValue(entry.Action, query.Action) &&
            MatchesValue(entry.Outcome, query.Outcome) &&
            MatchesValue(entry.Actor, query.Actor) &&
            MatchesValue(entry.Symbol, query.Symbol) &&
            MatchesValue(entry.CorrelationId, query.CorrelationId) &&
            MatchesValue(entry.ObjectKind, query.ObjectKind) &&
            MatchesValue(entry.ObjectId, query.ObjectId) &&
            MatchesRelatedObjectId(entry, query.RelatedObjectId) &&
            (!query.FromUtc.HasValue || entry.OccurredAt >= query.FromUtc.Value) &&
            (!query.ToUtc.HasValue || entry.OccurredAt <= query.ToUtc.Value);
    }

    private static bool MatchesValue(string? actual, string? expected)
        => string.IsNullOrWhiteSpace(expected) ||
           string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesRelatedObjectId(AuditTrailTimelineEntryDto entry, string? expected)
        => string.IsNullOrWhiteSpace(expected) ||
           (entry.RelatedObjectIds?.Any(related => MatchesValue(related, expected)) ?? false);

    private static bool MatchesText(AuditTrailTimelineEntryDto entry, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var needle = searchText.Trim();
        return Contains(entry.AuditId, needle) ||
            Contains(entry.ObjectKind, needle) ||
            Contains(entry.ObjectId, needle) ||
            Contains(entry.Category, needle) ||
            Contains(entry.Action, needle) ||
            Contains(entry.Outcome, needle) ||
            Contains(entry.Actor, needle) ||
            Contains(entry.RunId, needle) ||
            Contains(entry.Symbol, needle) ||
            Contains(entry.CorrelationId, needle) ||
            Contains(entry.Message, needle) ||
            Contains(entry.Reason, needle) ||
            Contains(entry.Scope, needle) ||
            Contains(entry.EvidenceRoute, needle) ||
            (entry.RelatedObjectIds?.Any(related => Contains(related, needle)) ?? false) ||
            (entry.Metadata?.Any(pair => Contains(pair.Key, needle) || Contains(pair.Value, needle)) ?? false);
    }

    private static bool Contains(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static AuditTrailTimelineEntryDto MapExecution(ExecutionAuditEntry entry)
    {
        var objectKind = ResolveExecutionObjectKind(entry);
        var objectId = ResolveExecutionObjectId(entry);
        var related = BuildExecutionRelatedObjectIds(entry, objectId);
        return new AuditTrailTimelineEntryDto(
            entry.AuditId,
            entry.OccurredAt,
            objectKind,
            objectId,
            entry.Category,
            entry.Action,
            entry.Outcome,
            Actor: entry.Actor,
            RunId: entry.RunId,
            Symbol: entry.Symbol,
            CorrelationId: entry.CorrelationId,
            Reason: entry.Reason,
            Scope: entry.Scope,
            Message: entry.Message,
            Metadata: entry.Metadata,
            RelatedObjectIds: related,
            EvidenceRoute: BuildEvidenceRoute(entry));
    }

    private static AuditTrailTimelineEntryDto MapOperations(OperationsTimelineEntryDto entry)
    {
        var objectKind = ResolveOperationsObjectKind(entry);
        var objectId = ResolveOperationsObjectId(entry);
        var related = BuildOperationsRelatedObjectIds(entry, objectId);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflowId"] = entry.WorkflowId.ToString("D"),
            ["fundAccountId"] = entry.FundAccountId.ToString("D"),
            ["periodId"] = entry.PeriodId,
            ["fromState"] = entry.FromState.ToString(),
            ["toState"] = entry.ToState.ToString(),
            ["currentHash"] = entry.CurrentHash
        };

        if (entry.Gate.HasValue)
        {
            metadata["gate"] = entry.Gate.Value.ToString();
        }

        if (entry.CorrelationKeys is not null)
        {
            AddIfPresent(metadata, "runId", entry.CorrelationKeys.RunId);
            AddIfPresent(metadata, "ledgerBatchId", entry.CorrelationKeys.LedgerBatchId);
            AddIfPresent(metadata, "reconciliationCaseId", entry.CorrelationKeys.ReconciliationCaseId);
        }

        var evidenceRoute = entry.References.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link.Route))?.Route
            ?? $"/api/workstation/operations/continuity/{entry.WorkflowId:D}/timeline";
        return new AuditTrailTimelineEntryDto(
            entry.AuditId.ToString("D"),
            entry.OccurredAtUtc,
            objectKind,
            objectId,
            "OperationsContinuity",
            entry.EventType,
            entry.ToState.ToString(),
            Actor: entry.Actor,
            RunId: entry.CorrelationKeys?.RunId,
            CorrelationId: entry.CorrelationId,
            Reason: entry.Rationale,
            Scope: entry.PeriodId,
            Message: BuildOperationsMessage(entry),
            Metadata: metadata,
            RelatedObjectIds: related,
            EvidenceRoute: evidenceRoute);
    }

    private static string ResolveExecutionObjectKind(ExecutionAuditEntry entry)
    {
        if (entry.Category.Contains("Promotion", StringComparison.OrdinalIgnoreCase) ||
            entry.Action.Contains("Promotion", StringComparison.OrdinalIgnoreCase))
        {
            return AuditTrailObjectKindDto.Promotion.ToString();
        }

        if (!string.IsNullOrWhiteSpace(entry.OrderId) ||
            entry.Category.Contains("Order", StringComparison.OrdinalIgnoreCase))
        {
            return AuditTrailObjectKindDto.Order.ToString();
        }

        if (entry.Action.Contains("ManualOverride", StringComparison.OrdinalIgnoreCase))
        {
            return AuditTrailObjectKindDto.OperatorAction.ToString();
        }

        if (entry.Category.Contains("Control", StringComparison.OrdinalIgnoreCase) ||
            entry.Action.Contains("CircuitBreaker", StringComparison.OrdinalIgnoreCase) ||
            entry.Action.Contains("Override", StringComparison.OrdinalIgnoreCase))
        {
            return AuditTrailObjectKindDto.ExecutionControl.ToString();
        }

        if (!string.IsNullOrWhiteSpace(entry.RunId))
        {
            return AuditTrailObjectKindDto.Run.ToString();
        }

        return AuditTrailObjectKindDto.Audit.ToString();
    }

    private static string ResolveExecutionObjectId(ExecutionAuditEntry entry)
        => FirstNonBlank(
            GetMetadata(entry, "overrideId"),
            entry.OrderId,
            entry.CorrelationId,
            entry.RunId,
            entry.Symbol,
            entry.AuditId) ?? entry.AuditId;

    private static IReadOnlyList<string> BuildExecutionRelatedObjectIds(ExecutionAuditEntry entry, string objectId)
        => new[]
            {
                entry.RunId,
                entry.OrderId,
                entry.CorrelationId,
                entry.Symbol,
                GetMetadata(entry, "overrideId"),
                GetMetadata(entry, "kind"),
                GetMetadata(entry, "strategyId")
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Where(value => !string.Equals(value, objectId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ResolveOperationsObjectKind(OperationsTimelineEntryDto entry)
        => entry.Gate switch
        {
            OperationsGateKeyDto.Reconciliation => AuditTrailObjectKindDto.Reconciliation.ToString(),
            OperationsGateKeyDto.Approval => AuditTrailObjectKindDto.Evidence.ToString(),
            _ when entry.EventType.Contains("report", StringComparison.OrdinalIgnoreCase) => AuditTrailObjectKindDto.Report.ToString(),
            _ => AuditTrailObjectKindDto.Audit.ToString()
        };

    private static string ResolveOperationsObjectId(OperationsTimelineEntryDto entry)
        => FirstNonBlank(
            entry.CorrelationKeys?.ReconciliationCaseId,
            entry.CorrelationKeys?.LedgerBatchId,
            entry.CorrelationKeys?.RunId,
            entry.CorrelationId,
            entry.WorkflowId.ToString("D"),
            entry.AuditId.ToString("D")) ?? entry.AuditId.ToString("D");

    private static IReadOnlyList<string> BuildOperationsRelatedObjectIds(OperationsTimelineEntryDto entry, string objectId)
        => new[]
            {
                entry.WorkflowId.ToString("D"),
                entry.FundAccountId.ToString("D"),
                entry.PeriodId,
                entry.CorrelationKeys?.RunId,
                entry.CorrelationKeys?.LedgerBatchId,
                entry.CorrelationKeys?.ReconciliationCaseId,
                entry.CorrelationId
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Where(value => !string.Equals(value, objectId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildOperationsMessage(OperationsTimelineEntryDto entry)
    {
        var gate = entry.Gate.HasValue ? $" for {entry.Gate.Value}" : string.Empty;
        return $"Operations continuity event '{entry.EventType}' moved {entry.FromState} to {entry.ToState}{gate}.";
    }

    private static void AddIfPresent(IDictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private static string BuildEvidenceRoute(ExecutionAuditEntry entry)
    {
        var overrideId = GetMetadata(entry, "overrideId");
        if (!string.IsNullOrWhiteSpace(overrideId) &&
            entry.Action.Contains("ManualOverride", StringComparison.OrdinalIgnoreCase))
        {
            return $"/api/execution/controls/manual-overrides/{Uri.EscapeDataString(overrideId)}/clear";
        }

        if (entry.Action.Contains("CircuitBreaker", StringComparison.OrdinalIgnoreCase))
        {
            return "/api/execution/controls/circuit-breaker";
        }

        var query = FirstNonBlank(entry.CorrelationId, entry.AuditId);
        return string.IsNullOrWhiteSpace(query)
            ? "/api/execution/audit/search"
            : $"/api/execution/audit/search?searchText={Uri.EscapeDataString(query)}";
    }

    private static string? GetMetadata(ExecutionAuditEntry entry, string key)
        => entry.Metadata is not null && entry.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
