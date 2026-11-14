using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed class ConfiguredLedgerAccountResolver(IAccountingConfigurationService configuration) : IConfiguredLedgerAccountResolver
{
    public async Task<ChartOfAccountsNodeDto> ResolveAsync(ConfiguredLedgerAccountRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccountPath);
        var workspace = await configuration.GetWorkspaceAsync(request.FundProfileId, request.LedgerBookId, ct,
            request.TenantId, request.CompanyId).ConfigureAwait(false);
        if (workspace.LedgerBookId != request.LedgerBookId || workspace.TenantId != request.TenantId
            || workspace.CompanyId != request.CompanyId
            || (request.ConfigurationVersion is not null && workspace.ConfigurationVersion != request.ConfigurationVersion))
            throw new InvalidOperationException("Accounting mapping scope or configuration revision differs from the retained posting authority.");
        var matches = workspace.ChartOfAccounts.Where(node => string.Equals(node.Path.Trim(), request.AccountPath.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].IsArchived)
            throw new InvalidOperationException($"Account mapping '{request.AccountPath}' must identify exactly one active chart account.");
        var node = matches[0];
        if (!new[] { "Asset", "Liability", "Equity", "Revenue", "Expense" }.Contains(node.AccountType, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(node.AccountType, request.ExpectedAccountType, StringComparison.OrdinalIgnoreCase)
            || (node.Symbol is not null && !string.Equals(node.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
            || (node.FinancialAccountId is not null && !string.Equals(node.FinancialAccountId, request.FinancialAccountId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Account mapping '{request.AccountPath}' has an incompatible type, instrument, or financial-account binding.");
        return node;
    }
}

public sealed class AccountingAutomaticPostingPolicyValidator(IAccountingPolicyService policies) : IAccountingAutomaticPostingPolicyValidator
{
    public async Task<IAsyncDisposable> AcquireRetainedAuthorityAsync(AccountingPolicyDto snapshot, DateOnly effectiveDate,
        IReadOnlyList<string> sourceEventTypes, CancellationToken ct = default)
    {
        var lease = await policies.AcquireRetainedPolicyAuthorityLeaseAsync(snapshot, ct).ConfigureAwait(false);
        try
        {
            if (snapshot.EffectiveFrom > effectiveDate || snapshot.EffectiveTo is { } end && end < effectiveDate)
                throw new InvalidOperationException("The retained automatic policy is not effective for this posting date.");
            foreach (var source in sourceEventTypes.Distinct(StringComparer.Ordinal))
            {
                var rules = snapshot.RulePack?.Rules.Where(rule => rule.SourceEventType == source).ToArray() ?? [];
                if (rules.Length != 1 || rules[0].RequiresApproval || !rules[0].AllowsAutoPosting || !rules[0].RequiresEvidence)
                    throw new InvalidOperationException($"Source '{source}' has no unique evidence-requiring rule authorizing automatic posting.");
            }
            return lease;
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<IAsyncDisposable> AcquireAuthorityAsync(string policyId, string policyVersion, AccountingBasisKindDto basis,
        DateOnly effectiveDate, IReadOnlyList<string> sourceEventTypes, CancellationToken ct = default)
    {
        var lease = await policies.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false);
        try
        {
            await ValidateAsync(policyId, policyVersion, basis, effectiveDate, sourceEventTypes, ct).ConfigureAwait(false);
            return lease;
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task ValidateAsync(string policyId, string policyVersion, AccountingBasisKindDto basis,
        DateOnly effectiveDate, IReadOnlyList<string> sourceEventTypes, CancellationToken ct = default)
    {
        foreach (var source in sourceEventTypes.Distinct(StringComparer.Ordinal))
            await ResolveRuleAsync(policyId, policyVersion, basis, effectiveDate, source, ct).ConfigureAwait(false);
    }

    public async Task<AccountingPolicyRuleDto> ResolveRuleAsync(string policyId, string policyVersion, AccountingBasisKindDto basis,
        DateOnly effectiveDate, string sourceEventType, CancellationToken ct = default)
    {
        var policy = await policies.ResolvePolicyAsync(new AccountingPolicyQuery(basis, effectiveDate,
            PolicyId: policyId, PolicyVersion: policyVersion), ct).ConfigureAwait(false);
        if (policy.PolicyId != policyId || policy.Version != policyVersion || policy.AccountingBasis != basis)
            throw new InvalidOperationException("The exact automatic accounting policy is unavailable.");
        var rules = policy.RulePack?.Rules.Where(rule => rule.SourceEventType == sourceEventType).ToArray() ?? [];
        if (rules.Length != 1 || rules[0].RequiresApproval || !rules[0].AllowsAutoPosting || !rules[0].RequiresEvidence)
            throw new InvalidOperationException($"Source '{sourceEventType}' has no unique evidence-requiring rule authorizing automatic posting.");
        return rules[0];
    }
}
