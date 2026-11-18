using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Custodian statements for fixed-income holdings state market value and accrued interest
/// explicitly. The fixture is a 100,000-face fixed 4.50% semiannual 30/360 bond quoted at 99.25
/// clean on 2026-05-31: market value 99,250.00 and 75 days of accrual since the 2026-03-15 coupon,
/// 100,000 x 4.5% x 75/360 = 937.50. A statement run must reconcile both against the internal book.
/// </summary>
public sealed partial class StatementRunWorkflowServiceTests
{
    private const string BondFixture = "custodian-bond-accrued-interest.csv";
    private const string BondSymbol = "MER45-2031";
    private static readonly DateOnly BondStatementDate = new(2026, 5, 31);

    [Fact]
    public async Task CreateAsync_BondStatementWithMatchingAccruedInterest_ProducesNoBreaks()
    {
        var workflow = CreateWorkflow(Populations(BondBook(accruedInterest: 937.50m)));

        var result = await workflow.CreateAsync(Request(BondFixturePath()), CancellationToken.None);

        result.Breaks.Should().BeEmpty("face, stated market value and accrued interest all agree");
        result.Cases.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_BondStatementWithDifferentAccruedInterest_RaisesAccruedInterestBreak()
    {
        // 74 days booked instead of 75: one day of accrual, 12.50, is missing from the book.
        var workflow = CreateWorkflow(Populations(BondBook(accruedInterest: 925.00m)));

        var result = await workflow.CreateAsync(Request(BondFixturePath()), CancellationToken.None);

        var accruedBreak = result.Breaks.Should().ContainSingle().Subject;
        accruedBreak.BreakCode.Should().Be(StatementAccruedInterestReconciliation.VarianceBreakCode);
        accruedBreak.Delta.Should().Be(12.50m);
        accruedBreak.ToleranceBreached.Should().BeTrue();
        accruedBreak.Category.Should().Be("position");
        result.Cases.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateAsync_BondStatementWhenBookCarriesNoAccruedInterest_RaisesUnavailableBreak()
    {
        var workflow = CreateWorkflow(Populations(BondBook(accruedInterest: null)));

        var result = await workflow.CreateAsync(Request(BondFixturePath()), CancellationToken.None);

        var accruedBreak = result.Breaks.Should().ContainSingle().Subject;
        accruedBreak.BreakCode.Should().Be(StatementAccruedInterestReconciliation.UnavailableBreakCode);
        accruedBreak.Delta.Should().Be(937.50m);
    }

    [Fact]
    public async Task CreateAsync_BondStatementUsesStatedMarketValueNotFaceTimesPricePer100()
    {
        // Face x price-per-100 would be 9,925,000; only the stated 99,250 may reconcile.
        var overstated = new InternalReconciliationPopulations(
            [new InternalPortfolioPosition("i-bond", "EXT-1", BondSymbol, BondStatementDate, 100_000m, 9_925_000m, "internal:pos:bond")
            {
                AccruedInterest = 937.50m
            }],
            [],
            []);
        var workflow = CreateWorkflow(Populations(overstated));

        var result = await workflow.CreateAsync(Request(BondFixturePath()), CancellationToken.None);

        result.Breaks.Should().NotBeEmpty();
        result.Breaks.Should().NotContain(record => record.BreakCode == StatementAccruedInterestReconciliation.VarianceBreakCode);
    }

    [Fact]
    public async Task CreateAsync_EquityStatementWithoutAccruedColumns_IsUnchanged()
    {
        var path = await WriteStatementAsync(
            "equity-no-accrued.csv",
            "EXT-1,SPY,10,500,5000,position,2026-05-28,,USD,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [new InternalPortfolioPosition("i-spy", "EXT-1", "SPY", new DateOnly(2026, 5, 28), 10m, 5000m, "internal:pos:spy")],
            [],
            [])));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        result.Breaks.Should().BeEmpty("equity rows state no accrued interest, so none is compared");
    }

    private static InternalReconciliationPopulations BondBook(decimal? accruedInterest) => new(
        [new InternalPortfolioPosition("i-bond", "EXT-1", BondSymbol, BondStatementDate, 100_000m, 99_250m, "internal:pos:bond")
        {
            AccruedInterest = accruedInterest
        }],
        [],
        []);

    private static string BondFixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "Meridian.Tests", "Reconciliation", "Fixtures", BondFixture);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate reconciliation fixture '{BondFixture}'.", BondFixture);
    }
}
