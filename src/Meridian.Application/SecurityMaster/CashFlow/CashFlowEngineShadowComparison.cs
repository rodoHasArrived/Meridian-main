using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>How the two cash-flow engines compare for one security (review B1).</summary>
public enum CashFlowShadowOutcome
{
    Match,
    Differs,
    LegacyOnlyBlocked,
    ContractualOnlyBlocked,
    BothBlocked,
    NotFound
}

/// <summary>One payment date on which the engines disagree.</summary>
public sealed record CashFlowShadowDifference(
    DateOnly Date,
    decimal LegacyInterest,
    decimal ContractualInterest,
    decimal LegacyPrincipal,
    decimal ContractualPrincipal);

/// <summary>The comparison of one security.</summary>
public sealed record CashFlowShadowComparison(
    Guid SecurityId,
    CashFlowShadowOutcome Outcome,
    int LegacyPaymentDates,
    int ContractualPaymentDates,
    IReadOnlyList<CashFlowShadowDifference> Differences,
    string? LegacyBlocker,
    IReadOnlyList<string> ContractualBlockers);

/// <summary>A diff report across securities.</summary>
public sealed record CashFlowShadowReport(
    DateOnly ValuationDate,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CashFlowShadowComparison> Securities)
{
    public int Count(CashFlowShadowOutcome outcome) => Securities.Count(security => security.Outcome == outcome);
}

/// <summary>
/// Review B1 (owner decision 2026-11-25: shadow-compare only). Runs the legacy
/// <c>SecurityMasterCashFlowService</c> and the declared <c>ContractualCashFlowEngine</c> preview over the
/// same securities and reports where they disagree, by payment date, without changing which engine any
/// consumer uses. The decision to route undeclared records through the contractual engine rests on this
/// report. Amounts are compared unsigned per date (interest and principal), at a cent tolerance, from the
/// valuation date forward.
/// </summary>
public sealed class CashFlowEngineShadowComparer
{
    private const decimal Tolerance = 0.01m;

    private readonly ISecurityMasterCashFlowService _legacy;
    private readonly IContractualCashFlowPreviewService _contractual;

    public CashFlowEngineShadowComparer(ISecurityMasterCashFlowService legacy, IContractualCashFlowPreviewService contractual)
    {
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        _contractual = contractual ?? throw new ArgumentNullException(nameof(contractual));
    }

    public async Task<CashFlowShadowReport> CompareAsync(IReadOnlyList<Guid> securityIds, DateOnly valuationDate, CancellationToken ct = default)
    {
        var rows = new List<CashFlowShadowComparison>(securityIds.Count);
        foreach (var securityId in securityIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(await CompareAsync(securityId, valuationDate, ct).ConfigureAwait(false));
        }

        return new CashFlowShadowReport(valuationDate, DateTimeOffset.UtcNow, rows);
    }

    public async Task<CashFlowShadowComparison> CompareAsync(Guid securityId, DateOnly valuationDate, CancellationToken ct = default)
    {
        var legacy = await _legacy.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base, ct).ConfigureAwait(false);
        var contractual = await _contractual.PreviewSecurityAsync(
            securityId, new ContractualCashFlowRequest(valuationDate, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (legacy is null && contractual is null)
            return new(securityId, CashFlowShadowOutcome.NotFound, 0, 0, [], null, []);

        var legacyBlocker = legacy is null ? "No legacy projection."
            : legacy.Blocker?.Reason ?? legacy.BlockedReason;
        var legacyBlocked = legacy is null || legacyBlocker is not null;
        var contractualBlockers = contractual is null
            ? ["No contractual preview."]
            : contractual.Blockers.Select(static blocker => $"{blocker.Code}: {blocker.Message}").ToArray();
        var contractualBlocked = contractualBlockers.Length > 0;

        var legacyByDate = legacyBlocked ? new Dictionary<DateOnly, (decimal Interest, decimal Principal)>()
            : legacy!.Schedule
                .Where(entry => DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime) >= valuationDate)
                .GroupBy(static entry => DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime))
                .ToDictionary(static group => group.Key, static group => (
                    Interest: Math.Abs(group.Sum(static entry => entry.InterestAmount)),
                    Principal: Math.Abs(group.Sum(static entry => entry.PrincipalAmount))));
        var contractualByDate = contractualBlocked ? new Dictionary<DateOnly, (decimal Interest, decimal Principal)>()
            : contractual!.Events
                .Where(@event => @event.Date >= valuationDate)
                .GroupBy(static @event => @event.Date)
                .ToDictionary(static group => group.Key, static group => (
                    Interest: Math.Abs(group.Sum(static e => Signed(e, e.CashInterest))),
                    Principal: Math.Abs(group.Sum(static e => Signed(e, e.ScheduledPrincipal + e.RedeemedPrincipal + e.ForecastPrepayment)))));

        var outcome = (legacyBlocked, contractualBlocked) switch
        {
            (true, true) => CashFlowShadowOutcome.BothBlocked,
            (true, false) => CashFlowShadowOutcome.LegacyOnlyBlocked,
            (false, true) => CashFlowShadowOutcome.ContractualOnlyBlocked,
            _ => CashFlowShadowOutcome.Match
        };

        var differences = new List<CashFlowShadowDifference>();
        if (outcome == CashFlowShadowOutcome.Match)
        {
            foreach (var date in legacyByDate.Keys.Union(contractualByDate.Keys).Order())
            {
                var l = legacyByDate.GetValueOrDefault(date);
                var c = contractualByDate.GetValueOrDefault(date);
                if (Math.Abs(l.Interest - c.Interest) > Tolerance || Math.Abs(l.Principal - c.Principal) > Tolerance)
                    differences.Add(new CashFlowShadowDifference(date, l.Interest, c.Interest, l.Principal, c.Principal));
            }

            if (differences.Count > 0)
                outcome = CashFlowShadowOutcome.Differs;
        }

        return new(securityId, outcome, legacyByDate.Count, contractualByDate.Count, differences,
            legacyBlocked ? legacyBlocker : null, contractualBlockers);
    }

    private static decimal Signed(ContractualCashFlowEvent @event, decimal amount)
        => @event.Direction == CashFlowLegDirection.Pay ? -amount : amount;
}
