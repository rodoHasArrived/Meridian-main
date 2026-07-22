using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Field-for-field conversion between the core <see cref="LedgerLineDimensionSet"/> and the
/// contract <see cref="LedgerDimensionSetDto"/>. The two records are structurally identical;
/// this keeps the mapping in one place so automated journal drafts can carry per-line
/// dimensional scope across the core/contract boundary without drift.
/// </summary>
internal static class LedgerDimensionMapper
{
    public static LedgerDimensionSetDto? ToDto(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
            return null;

        return new LedgerDimensionSetDto(
            FundId: dimensions.FundId,
            EntityId: dimensions.EntityId,
            SleeveId: dimensions.SleeveId,
            StrategyId: dimensions.StrategyId,
            InvestorId: dimensions.InvestorId,
            CapitalAccountId: dimensions.CapitalAccountId,
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: dimensions.TaxLotId,
            CostCenterId: dimensions.CostCenterId,
            CounterpartyId: dimensions.CounterpartyId,
            ExternalGlDimensions: dimensions.ExternalGlDimensions.Count == 0 ? null : dimensions.ExternalGlDimensions,
            OrganizationId: dimensions.OrganizationId,
            PortfolioId: dimensions.PortfolioId,
            BookId: dimensions.BookId,
            AccountId: dimensions.AccountId,
            CustomerId: dimensions.CustomerId,
            VendorId: dimensions.VendorId,
            ProjectId: dimensions.ProjectId)
        {
            PositionId = dimensions.PositionId
        };
    }

    public static LedgerLineDimensionSet? ToDomain(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
            return null;

        return new LedgerLineDimensionSet(
            FundId: dimensions.FundId,
            EntityId: dimensions.EntityId,
            SleeveId: dimensions.SleeveId,
            StrategyId: dimensions.StrategyId,
            InvestorId: dimensions.InvestorId,
            CapitalAccountId: dimensions.CapitalAccountId,
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: dimensions.TaxLotId,
            CostCenterId: dimensions.CostCenterId,
            CounterpartyId: dimensions.CounterpartyId,
            ExternalGlDimensions: dimensions.ExternalGlDimensions.Count == 0 ? null : dimensions.ExternalGlDimensions,
            OrganizationId: dimensions.OrganizationId,
            PortfolioId: dimensions.PortfolioId,
            BookId: dimensions.BookId,
            AccountId: dimensions.AccountId,
            CustomerId: dimensions.CustomerId,
            VendorId: dimensions.VendorId,
            ProjectId: dimensions.ProjectId)
        {
            PositionId = dimensions.PositionId
        };
    }
}
