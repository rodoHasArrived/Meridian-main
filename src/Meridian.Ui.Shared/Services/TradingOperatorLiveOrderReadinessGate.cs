using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface ITradingOperatorReadinessProvider
{
    Task<TradingOperatorReadinessDto> GetAsync(Guid? fundAccountId = null, CancellationToken ct = default);
}

/// <summary>
/// Adapts the shared W7 operator-readiness projection into the execution-layer live order gate.
/// </summary>
public sealed class TradingOperatorLiveOrderReadinessGate : ILiveOrderReadinessGate
{
    private readonly ITradingOperatorReadinessProvider _readinessProvider;
    private readonly ILogger<TradingOperatorLiveOrderReadinessGate> _logger;

    public TradingOperatorLiveOrderReadinessGate(
        ITradingOperatorReadinessProvider readinessProvider,
        ILogger<TradingOperatorLiveOrderReadinessGate> logger)
    {
        _readinessProvider = readinessProvider ?? throw new ArgumentNullException(nameof(readinessProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LiveOrderReadinessDecision> EvaluateAsync(
        LiveOrderReadinessRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedRunId = Normalize(request.RunId);

        if (!request.FundAccountId.HasValue)
        {
            return LiveOrderReadinessDecision.Rejected(
                $"Live order run '{request.RunId}' requires fundAccountId for account-scoped W7 broker reconciliation.");
        }

        var readiness = await _readinessProvider.GetAsync(request.FundAccountId.Value, ct).ConfigureAwait(false);
        var promotion = readiness.Promotion;
        var targetRunId = Normalize(promotion?.TargetRunId);

        if (targetRunId is null || !string.Equals(targetRunId, requestedRunId, StringComparison.Ordinal))
        {
            return LiveOrderReadinessDecision.Rejected(
                $"Live order run '{request.RunId}' does not match approved live promotion target '{targetRunId ?? "none"}'.");
        }

        if (string.IsNullOrWhiteSpace(promotion?.AuditReference))
        {
            return LiveOrderReadinessDecision.Rejected(
                $"Live order run '{request.RunId}' is missing retained live-promotion audit evidence.");
        }

        var blockers = BuildBlockers(readiness);
        if (blockers.Count > 0)
        {
            return LiveOrderReadinessDecision.Rejected(
                $"Live order readiness is blocked for run '{request.RunId}': {BuildBlockerSummary(blockers)}.");
        }

        if (string.IsNullOrWhiteSpace(readiness.SnapshotVersion))
        {
            return LiveOrderReadinessDecision.Rejected(
                $"Live order run '{request.RunId}' is missing a retained readiness snapshot version.");
        }

        var evidenceReference = $"live-readiness:{promotion.AuditReference};snapshot:{readiness.SnapshotVersion}";
        _logger.LogInformation(
            "Approved live order readiness for run {RunId} using evidence {EvidenceReference}.",
            request.RunId,
            evidenceReference);

        return LiveOrderReadinessDecision.Approved(evidenceReference);
    }

    private static List<string> BuildBlockers(TradingOperatorReadinessDto readiness)
    {
        var blockers = new List<string>();
        if (!readiness.ReadyForLiveOperation)
        {
            blockers.AddRange(readiness.LiveOperationBlockers.Where(static blocker => !string.IsNullOrWhiteSpace(blocker)));
        }

        blockers.AddRange(
            readiness.LiveOperationRequirements
                .Where(static requirement => requirement.Status != TradingAcceptanceGateStatusDto.Ready)
                .Select(static requirement =>
                    !string.IsNullOrWhiteSpace(requirement.BlockerCode)
                        ? requirement.BlockerCode!
                        : requirement.RequirementId));

        return blockers
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildBlockerSummary(IReadOnlyList<string> blockers)
    {
        const int maxBlockers = 12;
        var displayed = blockers.Take(maxBlockers).ToArray();
        var summary = string.Join(", ", displayed);
        return blockers.Count <= maxBlockers
            ? summary
            : $"{summary}, and {blockers.Count - maxBlockers} more";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
