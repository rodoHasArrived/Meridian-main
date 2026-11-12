using System.Text.Json.Serialization;

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
    public Guid? PositionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgreementId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgreementRevisionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChartAccountNodeId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountBindingRevisionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntercompanyTransactionId { get; init; }

    public IReadOnlyDictionary<string, string> ExternalGlDimensions { get; init; } =
        ExternalGlDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
