using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private static bool LedgerDimensionsEqual(
        LedgerLineDimensionSet? durable,
        LedgerDimensionSetDto? expected)
    {
        if (durable is null && expected is null)
            return true;
        if (durable is null || expected is null)
            return false;
        return string.Equals(durable.FundId, expected.FundId, StringComparison.Ordinal) &&
               string.Equals(durable.EntityId, expected.EntityId, StringComparison.Ordinal) &&
               string.Equals(durable.SleeveId, expected.SleeveId, StringComparison.Ordinal) &&
               string.Equals(durable.StrategyId, expected.StrategyId, StringComparison.Ordinal) &&
               string.Equals(durable.InvestorId, expected.InvestorId, StringComparison.Ordinal) &&
               string.Equals(durable.CapitalAccountId, expected.CapitalAccountId, StringComparison.Ordinal) &&
               durable.InstrumentId == expected.InstrumentId && durable.PositionId == expected.PositionId &&
               durable.IntercompanyAgreementId == expected.IntercompanyAgreementId &&
               durable.IntercompanyAgreementVersion == expected.IntercompanyAgreementVersion &&
               string.Equals(durable.TaxLotId, expected.TaxLotId, StringComparison.Ordinal) &&
               string.Equals(durable.CostCenterId, expected.CostCenterId, StringComparison.Ordinal) &&
               string.Equals(durable.CounterpartyId, expected.CounterpartyId, StringComparison.Ordinal) &&
               string.Equals(durable.OrganizationId, expected.OrganizationId, StringComparison.Ordinal) &&
               string.Equals(durable.PortfolioId, expected.PortfolioId, StringComparison.Ordinal) &&
               string.Equals(durable.BookId, expected.BookId, StringComparison.Ordinal) &&
               string.Equals(durable.AccountId, expected.AccountId, StringComparison.Ordinal) &&
               string.Equals(durable.CustomerId, expected.CustomerId, StringComparison.Ordinal) &&
               string.Equals(durable.VendorId, expected.VendorId, StringComparison.Ordinal) &&
               string.Equals(durable.ProjectId, expected.ProjectId, StringComparison.Ordinal) &&
               string.Equals(durable.AccountingArrangementId, expected.AccountingArrangementId, StringComparison.Ordinal) &&
               string.Equals(durable.AccountingArrangementRevisionId, expected.AccountingArrangementRevisionId, StringComparison.Ordinal) &&
               string.Equals(durable.ChartAccountNodeId, expected.ChartAccountNodeId, StringComparison.Ordinal) &&
               string.Equals(durable.AccountBindingRevisionId, expected.AccountBindingRevisionId, StringComparison.Ordinal) &&
               string.Equals(durable.IntercompanyTransactionId, expected.IntercompanyTransactionId, StringComparison.Ordinal) &&
               durable.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                   .SequenceEqual(expected.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
    }

    private static LedgerDimensionSetDto? ToDimensionDto(LedgerLineDimensionSet? dimensions)
        => dimensions is null
            ? null
            : new LedgerDimensionSetDto(
                dimensions.FundId,
                dimensions.EntityId,
                dimensions.SleeveId,
                dimensions.StrategyId,
                dimensions.InvestorId,
                dimensions.CapitalAccountId,
                dimensions.InstrumentId,
                dimensions.TaxLotId,
                dimensions.CostCenterId,
                dimensions.CounterpartyId,
                dimensions.ExternalGlDimensions,
                dimensions.OrganizationId,
                dimensions.PortfolioId,
                dimensions.BookId,
                dimensions.AccountId,
                dimensions.CustomerId,
                dimensions.VendorId,
                dimensions.ProjectId)
            {
                PositionId = dimensions.PositionId,
                IntercompanyAgreementId = dimensions.IntercompanyAgreementId,
                IntercompanyAgreementVersion = dimensions.IntercompanyAgreementVersion,
                AccountingArrangementId = dimensions.AccountingArrangementId,
                AccountingArrangementRevisionId = dimensions.AccountingArrangementRevisionId,
                ChartAccountNodeId = dimensions.ChartAccountNodeId,
                AccountBindingRevisionId = dimensions.AccountBindingRevisionId,
                IntercompanyTransactionId = dimensions.IntercompanyTransactionId
            };
}
