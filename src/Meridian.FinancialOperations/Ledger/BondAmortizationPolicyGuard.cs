using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Live policy admission belongs above the immutable journal/replay arithmetic.</summary>
internal static class BondAmortizationPolicyGuard
{
    public static async Task RequireAsync(IBondCalculationPolicy? policy, SecurityProjectionRecord security, CancellationToken ct)
    {
        if (policy is null) return;
        var detail = new SecurityDetailDto(security.SecurityId, security.AssetClass, security.Status,
            security.DisplayName, security.Currency, security.CommonTerms, security.AssetSpecificTerms,
            security.Identifiers, security.Aliases, security.Version, security.EffectiveFrom, security.EffectiveTo);
        if (await policy.GetBlockerAsync(detail, ct).ConfigureAwait(false) is { } blocker)
            throw new InvalidOperationException(blocker);
    }
}
