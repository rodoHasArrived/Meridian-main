using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

public sealed class DirectLendingServiceTests
{
    [Theory]
    [InlineData(CurrencyCode.Other)]
    [InlineData((CurrencyCode)99)]
    public async Task PortfolioSummary_ShouldWithholdTotalsWithoutKnownDenomination(CurrencyCode currency)
    {
        var service = new InMemoryDirectLendingService();
        foreach (var commitment in new[] { 1_000_000m, 2_000_000m })
        {
            var request = BuildCreateRequest();
            var loan = await service.CreateLoanAsync(request with
            {
                Terms = request.Terms with { BaseCurrency = currency, CommitmentAmount = commitment }
            });
            await service.AddCollateralAsync(loan.LoanId,
                new AddCollateralRequest(CollateralType.Equipment, "Unspecified appraisal", commitment,
                    currency, new DateOnly(2026, 3, 22)));
        }
        var summary = await service.GetPortfolioSummaryAsync();
        summary.TotalCurrency.Should().BeNull();
        summary.TotalCommitment.Should().BeNull();
        summary.TotalCollateralValue.Should().BeNull();
        summary.CurrencyTotals.Should().ContainSingle().Which.Should().Be(
            new LoanPortfolioCurrencyTotalsDto(currency, null, null, null, null, null, null));
    }

    [Fact]
    public async Task PortfolioSummary_ShouldKeepLoanAndAppraisalCurrenciesSeparateAcrossCollateralChanges()
    {
        var service = new InMemoryDirectLendingService();
        var usd = await service.CreateLoanAsync(BuildCreateRequest());
        var eurRequest = BuildCreateRequest();
        var eur = await service.CreateLoanAsync(eurRequest with
        {
            Terms = eurRequest.Terms with { BaseCurrency = CurrencyCode.EUR, CommitmentAmount = 2_000_000m }
        });
        var drawDate = new DateOnly(2026, 3, 23);
        await service.ActivateLoanAsync(usd.LoanId, new ActivateLoanRequest(drawDate));
        await service.ActivateLoanAsync(eur.LoanId, new ActivateLoanRequest(drawDate));
        await service.BookDrawdownAsync(usd.LoanId, new BookDrawdownRequest(100_000m, drawDate, drawDate, "USD wire"));
        await service.BookDrawdownAsync(eur.LoanId, new BookDrawdownRequest(200_000m, drawDate, drawDate, "EUR wire"));
        var state = await service.AddCollateralAsync(usd.LoanId,
            new AddCollateralRequest(CollateralType.Equipment, "EUR appraisal", 300_000m,
                CurrencyCode.EUR, new DateOnly(2026, 3, 22)));
        var collateralId = state!.Collateral!.Single().CollateralId;

        var summary = await service.GetPortfolioSummaryAsync();
        summary.TotalCurrency.Should().BeNull();
        summary.TotalCommitment.Should().BeNull();
        summary.TotalPrincipalOutstanding.Should().BeNull();
        summary.TotalInterestAccruedUnpaid.Should().BeNull();
        summary.TotalPenaltyAccruedUnpaid.Should().BeNull();
        summary.TotalAvailableToDraw.Should().BeNull();
        summary.TotalCollateralValue.Should().BeNull();
        var usdTotals = summary.CurrencyTotals!.Single(item => item.Currency == CurrencyCode.USD);
        var eurTotals = summary.CurrencyTotals!.Single(item => item.Currency == CurrencyCode.EUR);
        usdTotals.TotalCommitment.Should().Be(1_000_000m);
        usdTotals.TotalPrincipalOutstanding.Should().Be(100_000m);
        usdTotals.TotalAvailableToDraw.Should().Be(900_000m);
        usdTotals.TotalCollateralValue.Should().Be(0m);
        eurTotals.TotalCommitment.Should().Be(2_000_000m);
        eurTotals.TotalPrincipalOutstanding.Should().Be(200_000m);
        eurTotals.TotalAvailableToDraw.Should().Be(1_800_000m);
        eurTotals.TotalCollateralValue.Should().Be(300_000m);

        await service.UpdateCollateralValueAsync(usd.LoanId,
            new UpdateCollateralValueRequest(collateralId, 350_000m, new DateOnly(2026, 3, 23)));
        (await service.GetPortfolioSummaryAsync()).CurrencyTotals!
            .Single(item => item.Currency == CurrencyCode.EUR).TotalCollateralValue.Should().Be(350_000m);

        await service.RemoveCollateralAsync(usd.LoanId, new RemoveCollateralRequest(collateralId, "Released"));
        (await service.GetPortfolioSummaryAsync()).CurrencyTotals!
            .Single(item => item.Currency == CurrencyCode.EUR).TotalCollateralValue.Should().Be(0m);
    }

    [Fact]
    public async Task PortfolioSummary_ShouldRetainScalarCurrencyOnlyForSingleDenomination()
    {
        var service = new InMemoryDirectLendingService();
        var empty = await service.GetPortfolioSummaryAsync();
        empty.TotalCommitment.Should().Be(0m);
        empty.TotalCurrency.Should().BeNull();
        empty.CurrencyTotals.Should().BeEmpty();
        var request = BuildCreateRequest();
        await service.CreateLoanAsync(request with { Terms = request.Terms with { BaseCurrency = CurrencyCode.EUR } });

        var summary = await service.GetPortfolioSummaryAsync();
        summary.TotalCurrency.Should().Be(CurrencyCode.EUR);
        summary.TotalCommitment.Should().Be(1_000_000m);
        summary.TotalCollateralValue.Should().Be(0m);
    }

    [Fact]
    public async Task CreateLoanAsync_ShouldCreateLoanWithInitialTermsVersionAndSchemaVersionedHistory()
    {
        var service = new InMemoryDirectLendingService();
        var commandId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var detail = await service.CreateLoanAsync(
            BuildCreateRequest(),
            new DirectLendingCommandMetadataDto(
                CommandId: commandId,
                CorrelationId: correlationId,
                CausationId: causationId,
                SourceSystem: "unit-test",
                ReplayFlag: false));

        var history = await service.GetHistoryAsync(detail.LoanId);
        var rebuilt = await service.RebuildStateFromHistoryAsync(detail.LoanId);

        detail.Status.Should().Be(LoanStatus.Draft);
        detail.CurrentTermsVersion.Should().Be(1);
        history.Should().ContainSingle();
        history[0].EventType.Should().Be("loan.created");
        history[0].EventSchemaVersion.Should().Be(1);
        history[0].CommandId.Should().Be(commandId);
        history[0].CorrelationId.Should().Be(correlationId);
        history[0].CausationId.Should().Be(causationId);
        rebuilt.Should().NotBeNull();
        rebuilt!.AggregateVersion.Should().Be(1);
    }

    [Fact]
    public async Task CreateLoanAsync_ShouldThrowTypedValidationFailure_WhenTermsAreInvalid()
    {
        var service = new InMemoryDirectLendingService();
        var invalid = BuildCreateRequest() with
        {
            Terms = BuildTerms(commitmentAmount: 0m)
        };

        var act = () => service.CreateLoanAsync(invalid);

        var exception = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        exception.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        exception.Error.Message.Should().Contain("Commitment amount");
    }

    private static CreateLoanRequest BuildCreateRequest(RateTypeKind rateTypeKind = RateTypeKind.Fixed) =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Northwind Senior Term Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Northwind Holdings", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: BuildTerms(rateTypeKind: rateTypeKind));

    private static DirectLendingTermsDto BuildTerms(
        decimal commitmentAmount = 1_000_000m,
        RateTypeKind rateTypeKind = RateTypeKind.Fixed) =>
        new(
            OriginationDate: new DateOnly(2026, 3, 22),
            MaturityDate: new DateOnly(2029, 3, 22),
            CommitmentAmount: commitmentAmount,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: rateTypeKind,
            FixedAnnualRate: rateTypeKind == RateTypeKind.Fixed ? 0.08m : null,
            InterestIndexName: rateTypeKind == RateTypeKind.Floating ? "SOFR" : null,
            SpreadBps: rateTypeKind == RateTypeKind.Floating ? 150m : null,
            FloorRate: rateTypeKind == RateTypeKind.Floating ? 0.05m : null,
            CapRate: rateTypeKind == RateTypeKind.Floating ? 0.12m : null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0.03m,
            DefaultRateSpreadBps: 200m,
            PrepaymentAllowed: true,
            CovenantsJson: "{\"leverage\": \"<= 4.5x\"}");
}
