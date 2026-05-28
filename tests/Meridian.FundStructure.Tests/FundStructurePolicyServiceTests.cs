using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;

namespace Meridian.FundStructure.Tests;

public sealed class FundStructurePolicyServiceTests
{
    [Fact]
    public void ValidateCashFlowQuery_ThrowsWhenHistoricalDaysExceedsMaximum()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(historicalDays: FundStructurePolicyService.MaxCashFlowWindowDays + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.ValidateCashFlowQuery(query));
    }

    [Fact]
    public void ValidateCashFlowQuery_ThrowsWhenForecastDaysExceedsMaximum()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(forecastDays: FundStructurePolicyService.MaxCashFlowWindowDays + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.ValidateCashFlowQuery(query));
    }

    [Fact]
    public void ValidateCashFlowQuery_AllowsBoundaryValues()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(
            historicalDays: FundStructurePolicyService.MaxCashFlowWindowDays,
            forecastDays: FundStructurePolicyService.MaxCashFlowWindowDays,
            bucketDays: FundStructurePolicyService.MaxCashFlowBucketDays);

        service.ValidateCashFlowQuery(query);
    }

    [Fact]
    public void ValidateCashFlowQuery_ThrowsWhenBucketDaysExceedsMaximum()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(bucketDays: FundStructurePolicyService.MaxCashFlowBucketDays + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.ValidateCashFlowQuery(query));
    }

    private static GovernanceCashFlowQuery CreateQuery(
        int historicalDays = 7,
        int forecastDays = 7,
        int bucketDays = 7)
        => new(
            GovernanceScopeKind.Account,
            AccountId: Guid.NewGuid(),
            HistoricalDays: historicalDays,
            ForecastDays: forecastDays,
            BucketDays: bucketDays);
}
