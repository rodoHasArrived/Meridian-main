using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public sealed class FundStructurePolicyService : IFundStructurePolicyService
{
    public void EnsureSingleOperatingParent(CreateInvestmentPortfolioRequest request)
    {
        var assignedParents = 0;
        if (request.ClientId.HasValue) assignedParents++;
        if (request.FundId.HasValue) assignedParents++;
        if (request.SleeveId.HasValue) assignedParents++;
        if (request.VehicleId.HasValue) assignedParents++;
        if (request.EntityId.HasValue) assignedParents++;

        if (assignedParents > 1)
        {
            throw new InvalidOperationException("Investment portfolios can only be assigned to one operating parent.");
        }
    }

    public void ValidateCashFlowQuery(GovernanceCashFlowQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.HistoricalDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "HistoricalDays must be greater than or equal to zero.");
        }

        if (query.ForecastDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "ForecastDays must be greater than or equal to zero.");
        }

        if (query.BucketDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "BucketDays must be greater than zero.");
        }
    }
}
