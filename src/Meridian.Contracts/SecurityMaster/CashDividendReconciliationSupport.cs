namespace Meridian.Contracts.SecurityMaster;

public sealed record CashDividendReconciliationAccountDto(string FinancialAccountId, decimal ExpectedLongQuantity);

/// <summary>Frozen external reconciliation evidence for every account in the complete book inventory.</summary>
public sealed record CashDividendReconciliationSupportDto(string EvidenceHash, string EvidenceJson);

public interface ICorporateActionCashDividendReconciliationSupport
{
    Task<CashDividendReconciliationSupportDto> GetSupportAsync(
        CorporateActionCaseScopeDto scope,
        Guid securityId,
        DateOnly entitlementCutoff,
        IReadOnlyList<CashDividendReconciliationAccountDto> accounts,
        CancellationToken ct = default);
}
