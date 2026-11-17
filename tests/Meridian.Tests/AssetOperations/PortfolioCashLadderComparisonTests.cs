using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.AssetOperations;

/// <summary>Today's cash ladder against the ladder rebuilt as of a prior date (UX Phase E).</summary>
public sealed class PortfolioCashLadderComparisonTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private sealed class HistoricalCash(decimal today, decimal prior) : IPortfolioCashBalanceProvider, IPortfolioCashBalanceHistoryProvider
    {
        public List<DateOnly> RequestedDays { get; } = [];

        public Task<IReadOnlyList<PortfolioCashBalanceDto>> GetCashBalancesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioCashBalanceDto>>([Balance(today)]);

        public Task<IReadOnlyList<PortfolioCashBalanceDto>> GetCashBalancesAsOfAsync(DateOnly asOf, CancellationToken ct = default)
        {
            RequestedDays.Add(asOf);
            return Task.FromResult<IReadOnlyList<PortfolioCashBalanceDto>>([Balance(prior)]);
        }

        private static PortfolioCashBalanceDto Balance(decimal amount) =>
            new("cash", "Cash", amount, "USD", "Ledger", null);
    }

    private sealed class CurrentCashOnly : IPortfolioCashBalanceProvider
    {
        public Task<IReadOnlyList<PortfolioCashBalanceDto>> GetCashBalancesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioCashBalanceDto>>([new("cash", "Cash", 1_000m, "USD", "Ledger", null)]);
    }

    private static PortfolioCashLadderReadService CreateService(IPortfolioCashBalanceProvider cash)
    {
        var holdings = Substitute.For<IPortfolioHoldingsSource>();
        holdings.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<PortfolioHoldingDto>());
        return new PortfolioCashLadderReadService(
            securityMasterQueryService: Substitute.For<ISecurityMasterQueryService>(),
            assetOperationsQueryService: Substitute.For<IAssetOperationsQueryService>(),
            holdingsSource: holdings,
            cashBalanceProvider: cash);
    }

    [Fact]
    public async Task ComparesTodaysLadderWithTheLadderRebuiltFromPriorCash()
    {
        var cash = new HistoricalCash(today: 1_250m, prior: 1_000m);
        var service = CreateService(cash);

        var comparison = await service.GetCashLadderComparisonAsync(new PortfolioCashLadderQuery(HorizonDays: 28, BucketDays: 7), baselineAsOf: null);

        var expectedBaseline = new DateOnly(Today.Year, Today.Month, 1).AddDays(-1);
        cash.RequestedDays.Should().Equal(expectedBaseline);
        comparison.Status.Should().Be(PeriodComparisonStatus.Ready);
        comparison.Currency.Should().Be("USD");
        comparison.Baseline!.End.Should().Be(expectedBaseline);
        comparison.Lines.Single(line => line.Key == "opening-cash").Should().Be(PeriodComparisonLineDto.Create("opening-cash", "Opening cash", 1_250m, 1_000m));
        comparison.Lines.Single(line => line.Key == "opening-cash").PercentChange.Should().Be(25m);
        var ending = comparison.Lines.Single(line => line.Key == "ending-cash");
        ending.IsTotal.Should().BeTrue();
        ending.Current.Should().Be(1_250m);
        ending.Prior.Should().Be(1_000m);
        comparison.Lines.Should().Contain(line => line.Key == "bucket:1");
        comparison.Notes.Should().Contain(note => note.Contains("current terms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IsUnavailableWhenTheCashProviderCannotReadAnEarlierDate()
    {
        var comparison = await CreateService(new CurrentCashOnly()).GetCashLadderComparisonAsync(new PortfolioCashLadderQuery(), baselineAsOf: null);

        comparison.Status.Should().Be(PeriodComparisonStatus.Unavailable);
        comparison.Reason.Should().Contain("cannot read cash as of an earlier date");
        comparison.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectsAPriorDateThatIsNotBeforeToday()
    {
        var comparison = await CreateService(new HistoricalCash(1m, 1m)).GetCashLadderComparisonAsync(new PortfolioCashLadderQuery(), Today);

        comparison.Status.Should().Be(PeriodComparisonStatus.Incompatible);
    }
}
