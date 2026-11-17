using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
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

    /// <summary>Records which projection each call asked for; returns no projection.</summary>
    private class RecordingOperations : IAssetOperationsQueryService
    {
        public List<Guid> CurrentCalls { get; } = [];
        public List<(Guid SecurityId, BondCalculationContext Context)> AsOfCalls { get; } = [];

        public Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default)
        {
            CurrentCalls.Add(securityId);
            return Task.FromResult<AssetOperationsDetailDto?>(null);
        }

        public Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<AssetOperationsReadinessDto?>(null);
    }

    private sealed class HistoricalOperations : RecordingOperations, IAssetOperationsQueryService
    {
        public bool SupportsTermsAsOf => true;

        public Task<AssetOperationsDetailDto?> GetOperationsAsOfAsync(Guid securityId, BondCalculationContext context, CancellationToken ct = default)
        {
            AsOfCalls.Add((securityId, context));
            return Task.FromResult<AssetOperationsDetailDto?>(null);
        }
    }

    private static readonly Guid HeldBond = Guid.Parse("8f5c8f1e-6a0f-4a35-9d8a-3c3b1d1b0a01");

    private static PortfolioCashLadderReadService CreateService(IPortfolioCashBalanceProvider cash, IAssetOperationsQueryService? operations = null)
    {
        var holdings = Substitute.For<IPortfolioHoldingsSource>();
        holdings.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(operations is null
            ? Array.Empty<PortfolioHoldingDto>()
            : [new PortfolioHoldingDto(HeldBond, 100m)]);
        return new PortfolioCashLadderReadService(
            securityMasterQueryService: Substitute.For<ISecurityMasterQueryService>(),
            assetOperationsQueryService: operations ?? Substitute.For<IAssetOperationsQueryService>(),
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
    public async Task ProjectsThePriorLadderFromTheTermsRecordedByThePriorDate()
    {
        var operations = new HistoricalOperations();
        var baselineDay = Today.AddDays(-10);

        var comparison = await CreateService(new HistoricalCash(1_250m, 1_000m), operations)
            .GetCashLadderComparisonAsync(new PortfolioCashLadderQuery(HorizonDays: 28, BucketDays: 7), baselineDay);

        // Today's ladder reads current terms; the prior ladder reads terms known by the end of that day.
        operations.CurrentCalls.Should().Equal(HeldBond);
        var cutoff = new DateTimeOffset(baselineDay.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        operations.AsOfCalls.Should().ContainSingle()
            .Which.Should().Be((HeldBond, new BondCalculationContext(cutoff, KnownAt: cutoff)));
        comparison.Notes.Should().Contain(note => note.Contains("terms recorded by the end of the prior date", StringComparison.Ordinal));
        comparison.Notes.Should().NotContain(note => note.Contains("current terms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsCurrentTermsWhenTheServiceCannotProjectHistoricalTerms()
    {
        var operations = new RecordingOperations();

        var comparison = await CreateService(new HistoricalCash(1_250m, 1_000m), operations)
            .GetCashLadderComparisonAsync(new PortfolioCashLadderQuery(), Today.AddDays(-10));

        operations.CurrentCalls.Should().Equal(HeldBond, HeldBond);
        comparison.Notes.Should().Contain(note => note.Contains("current terms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDefaultAsOfProjectionFallsBackToTheCurrentOne()
    {
        IAssetOperationsQueryService operations = new RecordingOperations();

        operations.SupportsTermsAsOf.Should().BeFalse();
        await operations.GetOperationsAsOfAsync(HeldBond, new BondCalculationContext(DateTimeOffset.UnixEpoch, KnownAt: DateTimeOffset.UnixEpoch));

        ((RecordingOperations)operations).CurrentCalls.Should().Equal(HeldBond);
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
