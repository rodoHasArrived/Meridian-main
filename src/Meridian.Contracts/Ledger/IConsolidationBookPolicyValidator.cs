namespace Meridian.Contracts.Ledger;

/// <summary>Validates the governed rule behind the narrow additional reporting-book creation path.</summary>
public interface IConsolidationBookPolicyValidator
{
    Task ValidateAsync(string policyId, string policyVersion, CancellationToken ct = default);
}
