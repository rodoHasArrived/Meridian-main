namespace Meridian.Ledger;

/// <summary>
/// Optional dimensional accounting scope attached to one immutable ledger line.
/// </summary>
public sealed record LedgerLineDimensionSet(
    string? FundId = null,
    string? EntityId = null,
    string? SleeveId = null,
    string? StrategyId = null,
    string? InvestorId = null,
    string? CapitalAccountId = null,
    Guid? InstrumentId = null,
    string? TaxLotId = null,
    string? CostCenterId = null,
    string? CounterpartyId = null,
    IReadOnlyDictionary<string, string>? ExternalGlDimensions = null,
    string? OrganizationId = null,
    string? PortfolioId = null,
    string? BookId = null,
    string? AccountId = null,
    string? CustomerId = null,
    string? VendorId = null,
    string? ProjectId = null)
{
    public IReadOnlyDictionary<string, string> ExternalGlDimensions { get; init; } =
        ExternalGlDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
