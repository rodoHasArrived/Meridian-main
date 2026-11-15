namespace Meridian.Contracts.Ledger;

/// <summary>Resolves a configured account without inferring classification from its name.</summary>
public interface IConfiguredLedgerAccountResolver
{
    Task<ChartOfAccountsNodeDto> ResolveAsync(ConfiguredLedgerAccountRequestDto request, CancellationToken ct = default);
}

public sealed record ConfiguredLedgerAccountRequestDto(
    string TenantId, string? CompanyId, string? FundProfileId, Guid LedgerBookId,
    string AccountPath, string ExpectedAccountType, string? ConfigurationVersion = null,
    string? Symbol = null, string? FinancialAccountId = null);

/// <summary>Checks exact effective policy rules before an automatic journal is prepared.</summary>
public interface IAccountingAutomaticPostingPolicyValidator
{
    Task<IAsyncDisposable> AcquireRetainedAuthorityAsync(AccountingPolicyDto snapshot, DateOnly effectiveDate,
        IReadOnlyList<string> sourceEventTypes, CancellationToken ct = default);

    Task<IAsyncDisposable> AcquireAuthorityAsync(string policyId, string policyVersion, AccountingBasisKindDto basis,
        DateOnly effectiveDate, IReadOnlyList<string> sourceEventTypes, CancellationToken ct = default);

    Task<AccountingPolicyRuleDto> ResolveRuleAsync(string policyId, string policyVersion, AccountingBasisKindDto basis,
        DateOnly effectiveDate, string sourceEventType, CancellationToken ct = default);

    Task ValidateAsync(string policyId, string policyVersion, AccountingBasisKindDto basis,
        DateOnly effectiveDate, IReadOnlyList<string> sourceEventTypes, CancellationToken ct = default);
}
