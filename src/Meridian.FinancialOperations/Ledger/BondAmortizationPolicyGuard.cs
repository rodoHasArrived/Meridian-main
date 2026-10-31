using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Live policy admission belongs above the immutable journal/replay arithmetic.</summary>
internal static class BondAmortizationPolicyGuard
{
    public static async Task RequireAsync(IBondCalculationPolicy? policy, SecurityProjectionRecord security, CancellationToken ct)
    {
        if (policy is null)
            return;
        if (await policy.GetBlockerAsync(security.ToDetail(), ct).ConfigureAwait(false) is { } blocker)
            throw new InvalidOperationException(blocker);
    }
}
