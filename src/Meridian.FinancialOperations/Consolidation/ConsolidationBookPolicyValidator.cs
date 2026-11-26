using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

public sealed class ConsolidationBookPolicyValidator(IAccountingPolicyService policies) : IConsolidationBookPolicyValidator
{
    public async Task ValidateAsync(string policyId, string policyVersion, CancellationToken ct = default)
    {
        if ((policyId, policyVersion) is not (("consolidation-v1", "w10-v1") or ("consolidation-v2", "w10-v2") or
            ("consolidation-v3", "w10-v3") or ("consolidation-v4", "w10-v4")))
            throw new InvalidOperationException("Only the existing governed consolidation policy/version pairs authorize additional reporting books.");
        var policy = await policies.ResolvePolicyAsync(new(AccountingBasisKindDto.Primary,
            DateOnly.FromDateTime(DateTime.UtcNow), PolicyId: policyId, PolicyVersion: policyVersion), ct).ConfigureAwait(false);
        if (policy.PolicyId != policyId || policy.Version != policyVersion)
            throw new InvalidOperationException("The selected consolidation policy does not resolve exactly.");
        foreach (var id in policyVersion == "w10-v4" ? new[] { ConsolidationService.RuleId, ConsolidationService.RevenueExpenseRuleId } : new[] { ConsolidationService.RuleId })
        {
            var rule = policy.RulePack?.Rules.SingleOrDefault(x => x.RuleId == id);
            if (rule is null || rule.TreatmentKind != AccountingTreatmentKindDto.ConsolidationElimination ||
                rule.RuleVersion != policyVersion || !rule.RequiresApproval || !rule.RequiresEvidence || rule.AllowsAutoPosting)
                throw new InvalidOperationException("Additional reporting books require the exact reviewed consolidation policy treatments with auto-posting disabled.");
        }
    }
}
