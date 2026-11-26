using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ui.Shared.Services;

/// <summary>The class admission check shared by recurring execution and passive diagnostics.</summary>
internal static class RecurringFixedIncomeAccrualEligibility
{
    internal static SecurityCashFlowBlockerDto? GetClassBlocker(SecurityDetailDto security, string label)
        => SecurityAssetClassCatalog.TryGet(security.AssetClass, out var descriptor)
            && descriptor.RecurringAccrualPolicy is { } policy
            ? SecurityRecurringAccrualPolicies.EvaluateTerms(security, policy)
            : new("RecurringAccrualClassUnsupported",
                $"{label} is classified as '{security.AssetClass}'; its asset module has no registered recurring fixed-income accrual policy.");
}
