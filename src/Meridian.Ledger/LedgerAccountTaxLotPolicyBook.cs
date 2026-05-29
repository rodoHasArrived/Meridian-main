namespace Meridian.Ledger;

/// <summary>
/// Resolves tax-lot relief methods at the ledger-account level.
/// </summary>
public sealed class LedgerAccountTaxLotPolicyBook
{
    private readonly Dictionary<LedgerAccount, LedgerAccountTaxLotPolicy> _policies = [];

    /// <summary>Default relief method used when no account-specific policy exists.</summary>
    public LedgerTaxLotReliefMethod DefaultReliefMethod { get; }

    /// <summary>All registered account-level policies sorted by account identity.</summary>
    public IReadOnlyList<LedgerAccountTaxLotPolicy> Policies =>
        _policies.Values
            .OrderBy(policy => policy.Account.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(policy => policy.Account.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(policy => policy.Account.FinancialAccountId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public LedgerAccountTaxLotPolicyBook(LedgerTaxLotReliefMethod defaultReliefMethod = LedgerTaxLotReliefMethod.Fifo)
    {
        DefaultReliefMethod = defaultReliefMethod;
    }

    /// <summary>Registers or replaces the relief policy for a specific ledger account.</summary>
    public LedgerAccountTaxLotPolicy Register(
        LedgerAccount account,
        LedgerTaxLotReliefMethod reliefMethod,
        string policyId,
        DateOnly effectiveDate,
        string? rationale = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);

        var normalizedPolicy = new LedgerAccountTaxLotPolicy(
            account,
            reliefMethod,
            policyId.Trim(),
            effectiveDate,
            string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim());

        _policies[account] = normalizedPolicy;
        return normalizedPolicy;
    }

    /// <summary>Returns the effective policy for <paramref name="account"/>, or a default policy when none is registered.</summary>
    public LedgerAccountTaxLotPolicy Resolve(LedgerAccount account, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (_policies.TryGetValue(account, out var policy))
        {
            if (policy.EffectiveDate <= asOf)
                return policy;

            throw new InvalidOperationException(
                $"Tax-lot policy '{policy.PolicyId}' for account '{account}' is not effective until {policy.EffectiveDate:yyyy-MM-dd}.");
        }

        return new LedgerAccountTaxLotPolicy(
            account,
            DefaultReliefMethod,
            "default",
            DateOnly.MinValue,
            "Default ledger tax-lot relief method.");
    }

    /// <summary>Returns true when an account-specific policy exists.</summary>
    public bool HasPolicy(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _policies.ContainsKey(account);
    }
}

