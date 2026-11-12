using System.Text;
using System.Globalization;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.OperationsContinuity;
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
    private readonly PaperSessionPersistenceService? _paperSessions;

    public AuditTrailExplorerService(
        ExecutionAuditTrailService? auditTrail = null,
        IOperationsContinuityWorkflowService? operationsContinuity = null,
        PaperSessionPersistenceService? paperSessions = null)
    {
        _auditTrail = auditTrail;
        _operationsContinuity = operationsContinuity;
        _paperSessions = paperSessions;
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
            entries.AddRange(executionEntries.Select(static (entry, index) => MapExecution(entry, index + 1)));
        }

        if (_operationsContinuity is not null)
        {
            var workflows = await _operationsContinuity.ListAsync(ct: ct).ConfigureAwait(false);
            foreach (var workflow in workflows)
            {
                ct.ThrowIfCancellationRequested();
                var timeline = await _operationsContinuity.GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
                entries.AddRange(timeline.Select(static (entry, index) => MapOperations(entry, index + 1)));
            }
        }

        // The workstation fill rows are completed-order aggregates. Only actual session
        // execution reports can claim per-fill identity and an event timestamp here.
        if (_paperSessions is not null && !string.IsNullOrWhiteSpace(query.OrderId))
        {
            await _paperSessions.InitialiseAsync(ct).ConfigureAwait(false);
            foreach (var summary in _paperSessions.GetSessions())
            {
                ct.ThrowIfCancellationRequested();
                foreach (var fill in _paperSessions.GetSessionFillHistory(summary.SessionId))
                {
                    var metadata = new Dictionary<string, string>
                    {
                        ["timestampBasis"] = "execution-report-event",
                        ["sessionId"] = summary.SessionId,
                        ["clientOrderId"] = fill.ClientOrderId ?? string.Empty,
                        ["gatewayOrderId"] = fill.GatewayOrderId ?? string.Empty,
                        ["reportOrderId"] = fill.OrderId,
                        ["brokerAccountId"] = fill.BrokerAccountId ?? string.Empty,
                        ["filledQuantity"] = fill.FilledQuantity.ToString(CultureInfo.InvariantCulture),
                        ["lastFillQuantity"] = fill.LastFillQuantity?.ToString(CultureInfo.InvariantCulture) ?? "unreported",
                        ["fillPrice"] = fill.FillPrice?.ToString(CultureInfo.InvariantCulture) ?? "unreported",
                        ["priceBasis"] = fill.FillPriceIsCumulativeAverage ? "cumulative-average" : "reported-fill-price",
                        ["source"] = "paper-session-execution-report"
                    };
                    entries.Add(new AuditTrailTimelineEntryDto(
                        $"session-fill:{summary.SessionId}:{PaperSessionFillRecord.ComputeCanonicalFillId(fill):D}",
                        fill.Timestamp,
                        "Order",
                        fill.ClientOrderId ?? fill.OrderId,
                        "Fill",
                        fill.ReportType.ToString(),
                        fill.OrderStatus.ToString(),
                        Symbol: fill.Symbol,
                        Message: "Actual session execution report; recorded-at time is not exposed by this read model. Filled quantity may be cumulative; per-event quantity is shown only when reported.",
                        Metadata: metadata,
                        EvidenceRoute: $"/api/execution/sessions/{Uri.EscapeDataString(summary.SessionId)}",
                        ActionLedgerSource: "PaperSessionFillHistory",
                        ActionLedgerStatus: "SessionRecord",
                        OrderId: FirstNonBlank(fill.ClientOrderId, fill.OrderId)));
                }
            }
        }

        var orderSelection = SelectOrderContext(entries, query.OrderId);
        var matched = orderSelection.Entries
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
            returned,
            [new("ExecutionAuditTrail", _auditTrail is null ? "Unavailable" : "RetainedRecordsOnly"),
             new("OperationsContinuityTimeline", _operationsContinuity is null ? "Unavailable" : "RetainedRecordsOnly"),
             new("PaperSessionFillHistory", _paperSessions is null ? "Unavailable" : string.IsNullOrWhiteSpace(query.OrderId) ? "SelectOrderToRead" : "SessionRecordsOnly")],
            "Retained evidence only. Missing events or passed checks are not proof that a stage did not occur. Recording timestamps may differ from broker event times. Results are capped at 1000; narrow the time range if truncated." +
            (orderSelection.Status == "Ambiguous" ? " Order identity is ambiguous across canonical orders or known source contexts; no events are returned. Use the canonical client/order ID and resolve conflicting evidence." : ""),
            orderSelection.Status);
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

    private static (IEnumerable<AuditTrailTimelineEntryDto> Entries, string Status) SelectOrderContext(
        IReadOnlyList<AuditTrailTimelineEntryDto> entries, string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId)) return (entries, "NotRequested");
        var requested = orderId.Trim();
        // Canonical client/order identities take precedence over another order's coinciding
        // broker alias. Never transitively union the two identifier namespaces.
        var canonical = requested;
        var matching = entries.Where(entry => string.Equals(entry.OrderId, canonical, StringComparison.Ordinal)).ToArray();
        var status = "CanonicalOrder";
        if (matching.Length == 0)
        {
            var aliases = entries.Where(entry => string.Equals(Metadata(entry, "gatewayOrderId"), requested, StringComparison.Ordinal)).ToArray();
            if (aliases.Length == 0) return ([], "NotFound");
            var candidates = aliases.Select(entry => entry.OrderId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
            if (candidates.Length != 1 || aliases.Any(entry => string.IsNullOrWhiteSpace(entry.OrderId))) return ([], "Ambiguous");
            canonical = candidates[0]!;
            matching = entries.Where(entry => string.Equals(entry.OrderId, canonical, StringComparison.Ordinal)).ToArray();
            status = "UniqueBrokerAlias";
        }
        if (HasConflictingContext(matching)) return ([], "Ambiguous");
        var direct = matching.Select(entry => entry.AuditId).ToHashSet(StringComparer.Ordinal);
        var correlations = matching.Select(entry => entry.CorrelationId).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
        var references = matching.SelectMany(entry => new[] { Metadata(entry, "manualOverrideId"), Metadata(entry, "overrideId"), Metadata(entry, "escalationId") }).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
        return (entries.Where(entry => direct.Contains(entry.AuditId) ||
            (string.IsNullOrWhiteSpace(entry.OrderId) &&
             !HasConflictingContext(matching.Append(entry)) &&
             ((!string.IsNullOrWhiteSpace(entry.CorrelationId) && correlations.Contains(entry.CorrelationId)) ||
              references.Contains(entry.ObjectId) ||
              references.Contains(Metadata(entry, "overrideId")) ||
              references.Contains(Metadata(entry, "escalationId"))))), status);
    }

    private static bool HasConflictingContext(IEnumerable<AuditTrailTimelineEntryDto> entries)
    {
        var rows = entries.ToArray();
        return rows.Select(entry => entry.BrokerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any() ||
            new[] { "sessionId", "brokerAccountId", "fundAccountId" }.Any(name => rows.Select(entry => Metadata(entry, name)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).Skip(1).Any());
    }

    private static string? Metadata(AuditTrailTimelineEntryDto entry, string name)
        => entry.Metadata?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

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
            Contains(entry.ActionLedgerSource, needle) ||
            Contains(entry.PreviousActionHash, needle) ||
            Contains(entry.CurrentActionHash, needle) ||
            Contains(entry.ActionLedgerStatus, needle) ||
            (entry.RelatedObjectIds?.Any(related => Contains(related, needle)) ?? false) ||
            (entry.Metadata?.Any(pair => Contains(pair.Key, needle) || Contains(pair.Value, needle)) ?? false);
    }

    private static bool Contains(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static AuditTrailTimelineEntryDto MapExecution(ExecutionAuditEntry entry, int sequence)
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
            EvidenceRoute: BuildEvidenceRoute(entry),
            ActionLedgerSource: "ExecutionAuditTrail",
            ActionLedgerSequence: sequence,
            CurrentActionHash: BuildExecutionActionHash(entry),
            ActionLedgerStatus: "WalRetained",
            OrderId: FirstNonBlank(GetMetadata(entry, "clientOrderId"), entry.OrderId),
            BrokerName: entry.BrokerName);
    }

    private static AuditTrailTimelineEntryDto MapOperations(OperationsTimelineEntryDto entry, int sequence)
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
            ["toState"] = entry.ToState.ToString()
        };

        AddIfPresent(metadata, "previousHash", entry.PreviousHash);
        AddIfPresent(metadata, "currentHash", entry.CurrentHash);

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
            EvidenceRoute: evidenceRoute,
            ActionLedgerSource: "OperationsContinuityTimeline",
            ActionLedgerSequence: sequence,
            PreviousActionHash: entry.PreviousHash,
            CurrentActionHash: entry.CurrentHash,
            ActionLedgerStatus: string.IsNullOrWhiteSpace(entry.CurrentHash) ? "AppendOnly" : "HashChained");
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

    private static string BuildExecutionActionHash(ExecutionAuditEntry entry)
    {
        var builder = new StringBuilder();
        AppendHashField(builder, "auditId", entry.AuditId);
        AppendHashField(builder, "occurredAt", entry.OccurredAt.ToUniversalTime().ToString("O"));
        AppendHashField(builder, "category", entry.Category);
        AppendHashField(builder, "action", entry.Action);
        AppendHashField(builder, "outcome", entry.Outcome);
        AppendHashField(builder, "actor", entry.Actor);
        AppendHashField(builder, "brokerName", entry.BrokerName);
        AppendHashField(builder, "orderId", entry.OrderId);
        AppendHashField(builder, "runId", entry.RunId);
        AppendHashField(builder, "symbol", entry.Symbol);
        AppendHashField(builder, "correlationId", entry.CorrelationId);
        AppendHashField(builder, "message", entry.Message);
        AppendHashField(builder, "reason", entry.Reason);
        AppendHashField(builder, "scope", entry.Scope);

        if (entry.Metadata is not null)
        {
            foreach (var pair in entry.Metadata.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                AppendHashField(builder, $"metadata.{pair.Key}", pair.Value);
            }
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static void AppendHashField(StringBuilder builder, string key, string? value)
        => builder.Append(key).Append('=').Append(value ?? string.Empty).Append('\n');
}
