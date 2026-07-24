using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Rules shared by the journal-construction paths (governed accounting drafts in
/// <see cref="AccountingJournalDraftService"/> and operations-continuity ledger posting in
/// <c>OperationsLedgerPostingService</c>): the canonical mapping from the shared
/// <see cref="LedgerDimensionSetDto"/> contract onto ledger line dimensions, and the journal
/// debit/credit balance rule. Keeping them here means one edit instead of one per path.
/// </summary>
internal static class LedgerJournalConstruction
{
    /// <summary>
    /// Returns <see langword="true"/> when total debits equal total credits within the shared
    /// ledger balance tolerance.
    /// </summary>
    internal static bool IsBalanced(decimal totalDebits, decimal totalCredits)
        => Math.Abs(totalDebits - totalCredits) <= LedgerToleranceConstants.Balance;

    /// <summary>
    /// Maps a dimension-set DTO onto ledger line dimensions with normalized (trimmed, non-empty)
    /// values and deterministically ordered external GL dimensions. Returns
    /// <see langword="null"/> when the DTO carries no populated dimension so empty scopes do not
    /// masquerade as dimensioned lines.
    /// </summary>
    internal static LedgerLineDimensionSet? ToLedgerLineDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var mapped = new LedgerLineDimensionSet(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions),
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };

        return HasAnyDimension(mapped) ? mapped : null;
    }

    private static bool HasAnyDimension(LedgerLineDimensionSet dimensions)
        => dimensions.FundId is not null
            || dimensions.EntityId is not null
            || dimensions.SleeveId is not null
            || dimensions.StrategyId is not null
            || dimensions.InvestorId is not null
            || dimensions.CapitalAccountId is not null
            || dimensions.InstrumentId is not null
            || dimensions.PositionId is not null
            || dimensions.TaxLotId is not null
            || dimensions.CostCenterId is not null
            || dimensions.CounterpartyId is not null
            || dimensions.ExternalGlDimensions.Count > 0
            || dimensions.OrganizationId is not null
            || dimensions.PortfolioId is not null
            || dimensions.BookId is not null
            || dimensions.AccountId is not null
            || dimensions.CustomerId is not null
            || dimensions.VendorId is not null
            || dimensions.ProjectId is not null;

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(
        IReadOnlyDictionary<string, string>? dimensions)
        => dimensions is null || dimensions.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : NormalizeExternalGlDimensionsCore(dimensions);

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensionsCore(
        IReadOnlyDictionary<string, string> dimensions)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dimensions
                     .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                     .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            normalized[pair.Key.Trim()] = pair.Value.Trim();
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
