namespace Meridian.Contracts.Workstation;

/// <summary>Exact native position and monetary basis facts; missing retained evidence remains unavailable.</summary>
public sealed record StrategyPositionAccountingEvidence(
    string Symbol, string? FinancialAccountId, Guid? SecurityId, bool IsShort,
    decimal? Quantity, decimal? MonetaryBasis, string? QuantityUnit, decimal? Multiplier,
    string? Currency, string? CoverageFailure = null, IReadOnlyList<string>? EvidenceReferences = null);
