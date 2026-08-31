using System.Globalization;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;
using Meridian.Contracts.Api;
using ContractSlaStatus = Meridian.Contracts.Api.BackfillRemediationSlaStatusDto;
using ContractSlaTier = Meridian.Contracts.Api.BackfillRemediationSlaTierDto;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Projects application-owned execution history into a stable workstation contract. Legacy
/// warning metadata remains readable, but all callers receive the same typed SLA shape.
/// </summary>
public static class BackfillExecutionContractProjection
{
    private static readonly TimeSpan DefaultDueSoonWindow = TimeSpan.FromHours(1);

    public static BackfillExecutionHistoryResponse Build(
        IReadOnlyList<BackfillExecutionLog> executions,
        string? defaultProvider,
        DateTimeOffset? nowUtc = null,
        TimeSpan? dueSoonWindow = null)
    {
        ArgumentNullException.ThrowIfNull(executions);

        var evaluatedAt = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var effectiveDefaultProvider = NormalizeProvider(defaultProvider);
        var rows = executions
            .Select(execution => ProjectExecution(
                execution,
                effectiveDefaultProvider,
                evaluatedAt,
                dueSoonWindow ?? DefaultDueSoonWindow))
            .ToArray();
        var remediationRows = executions
            .Where(IsAutoRemediation)
            .ToArray();

        return new BackfillExecutionHistoryResponse
        {
            Executions = rows,
            Total = rows.Length,
            AutoRemediation = new BackfillAutoRemediationSummary
            {
                Total = remediationRows.Length,
                WithReason = remediationRows.Count(static execution =>
                    !string.IsNullOrWhiteSpace(execution.AutoRemediationTriggerReason)),
                LastOutcome = remediationRows.FirstOrDefault()?.AutoRemediationLastOutcome,
                DefaultProvider = effectiveDefaultProvider
            },
            Timestamp = evaluatedAt
        };
    }

    private static BackfillExecution ProjectExecution(
        BackfillExecutionLog execution,
        string defaultProvider,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonWindow)
    {
        var totalSymbols = execution.Statistics.TotalSymbols > 0
            ? execution.Statistics.TotalSymbols
            : execution.Symbols.Count;
        var bars = Math.Clamp(execution.Statistics.TotalBarsRetrieved, 0, int.MaxValue);

        return new BackfillExecution
        {
            Id = execution.ExecutionId,
            ScheduleId = execution.ScheduleId,
            ScheduleName = execution.ScheduleName,
            Trigger = execution.Trigger.ToString(),
            Status = execution.Status.ToString(),
            StartedAt = (execution.StartedAt ?? execution.ScheduledAt).UtcDateTime,
            CompletedAt = execution.CompletedAt?.UtcDateTime,
            SymbolsProcessed = totalSymbols,
            BarsDownloaded = (int)bars,
            ErrorMessage = execution.ErrorMessage,
            FromDate = execution.FromDate,
            ToDate = execution.ToDate,
            Symbols = execution.Symbols.ToArray(),
            AutoRemediationTriggerReason = execution.AutoRemediationTriggerReason,
            AutoRemediationAttemptCount = execution.AutoRemediationAttemptCount,
            AutoRemediationLastOutcome = execution.AutoRemediationLastOutcome,
            AutoRemediationIdempotencyKey = execution.AutoRemediationIdempotencyKey,
            AutoRemediationSla = ProjectSla(execution, defaultProvider, evaluatedAt, dueSoonWindow)
        };
    }

    private static BackfillRemediationSlaDto? ProjectSla(
        BackfillExecutionLog execution,
        string defaultProvider,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonWindow)
    {
        if (execution.AutoRemediationSla is { } typed)
        {
            return new BackfillRemediationSlaDto
            {
                Tier = typed.Tier == BackfillRemediationSlaTier.SameBusinessDay
                    ? ContractSlaTier.SameBusinessDay
                    : ContractSlaTier.Standard,
                Status = ResolveStatus(execution, typed.DueAtUtc, evaluatedAt, dueSoonWindow),
                DueAtUtc = typed.DueAtUtc,
                RequiresOwnerAssignment = typed.RequiresOwnerAssignment,
                DownstreamWorkflow = typed.DownstreamWorkflow,
                ReasonCode = typed.ReasonCode,
                Provider = string.IsNullOrWhiteSpace(typed.Provider)
                    ? defaultProvider
                    : NormalizeProvider(typed.Provider),
                TriggerSource = typed.TriggerSource.ToString(),
                IsCompatibilityDerived = false
            };
        }

        var metadata = ParseWarningMetadata(execution.Warnings);
        if (!metadata.TryGetValue("sla-due-utc", out var dueText) ||
            !DateTimeOffset.TryParse(
                dueText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dueAt))
        {
            return null;
        }

        var tier = metadata.TryGetValue("sla-tier", out var tierText) &&
                   Enum.TryParse<ContractSlaTier>(tierText, ignoreCase: true, out var parsedTier)
            ? parsedTier
            : ContractSlaTier.Standard;
        var requiresOwner = metadata.TryGetValue("sla-requires-owner", out var ownerText) &&
                            bool.TryParse(ownerText, out var parsedOwner) &&
                            parsedOwner;

        return new BackfillRemediationSlaDto
        {
            Tier = tier,
            Status = ResolveStatus(execution, dueAt, evaluatedAt, dueSoonWindow),
            DueAtUtc = dueAt,
            RequiresOwnerAssignment = requiresOwner,
            DownstreamWorkflow = GetMetadata(metadata, "downstream-workflow", "unassigned"),
            ReasonCode = GetMetadata(metadata, "sla-reason", "Unknown"),
            Provider = NormalizeProvider(GetMetadata(metadata, "provider", defaultProvider)),
            TriggerSource = null,
            IsCompatibilityDerived = true
        };
    }

    private static ContractSlaStatus ResolveStatus(
        BackfillExecutionLog execution,
        DateTimeOffset dueAt,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonWindow)
    {
        if (execution.Status == ExecutionStatus.Completed ||
            string.Equals(
                execution.AutoRemediationLastOutcome,
                AutoRemediationOutcome.Completed.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return ContractSlaStatus.Completed;
        }

        if (execution.Status is ExecutionStatus.Failed or ExecutionStatus.Cancelled or ExecutionStatus.Skipped)
            return ContractSlaStatus.Failed;

        if (evaluatedAt >= dueAt)
            return ContractSlaStatus.Overdue;

        return dueAt - evaluatedAt <= dueSoonWindow
            ? ContractSlaStatus.DueSoon
            : ContractSlaStatus.Open;
    }

    private static bool IsAutoRemediation(BackfillExecutionLog execution) =>
        execution.Trigger == ExecutionTrigger.AutoRemediation ||
        execution.AutoRemediationSla is not null ||
        !string.IsNullOrWhiteSpace(execution.AutoRemediationTriggerReason) ||
        execution.Warnings.Any(static warning => warning.StartsWith("sla-", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> ParseWarningMetadata(IEnumerable<string> warnings)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var warning in warnings)
        {
            var separator = warning.IndexOf('=');
            if (separator <= 0 || separator >= warning.Length - 1)
                continue;

            metadata[warning[..separator].Trim()] = warning[(separator + 1)..].Trim();
        }

        return metadata;
    }

    private static string GetMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string fallback) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string NormalizeProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider)
            ? "stooq"
            : provider.Trim().ToLowerInvariant();
}
