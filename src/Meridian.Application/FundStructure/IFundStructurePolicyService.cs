using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public interface IFundStructurePolicyService
{
    void EnsureSingleOperatingParent(CreateInvestmentPortfolioRequest request);
    void ValidateCashFlowQuery(GovernanceCashFlowQuery query);
}
