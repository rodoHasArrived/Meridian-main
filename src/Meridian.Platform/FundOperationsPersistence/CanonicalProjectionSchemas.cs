namespace Meridian.Platform.FundOperationsPersistence;

public sealed record FundStructureProjectionSchema(Guid OrganizationId, string Code, string Name, string BaseCurrency, DateTimeOffset EffectiveFrom);
public sealed record FundAccountProjectionSchema(Guid AccountId, Guid FundId, string AccountCode, string DisplayName, string AccountType, string BaseCurrency, bool IsActive);
public sealed record DirectLendingProjectionSchema(Guid LoanId, string FacilityCode, string BorrowerName, decimal PrincipalOutstanding, string Currency, DateOnly AsOfDate);
public sealed record BankingProjectionSchema(Guid EntityId, Guid BankTransactionId, string TransactionType, decimal Amount, string Currency, DateOnly EffectiveDate);
public sealed record MoneyMarketProjectionSchema(Guid SecurityId, string DisplayName, string Currency, string? FundFamily, bool IsSweepEligible, int? WeightedAverageMaturityDays);

public sealed class NoOpDomainProjectionReconciliationJob(FundOperationsDomain domain) : IDomainProjectionReconciliationJob
{
    public FundOperationsDomain Domain { get; } = domain;

    public Task<IReadOnlyList<ProjectionDiscrepancy>> ReconcileAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProjectionDiscrepancy>>([]);
}
