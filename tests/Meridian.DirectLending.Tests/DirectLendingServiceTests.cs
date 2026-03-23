using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

public sealed class DirectLendingServiceTests
{
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
