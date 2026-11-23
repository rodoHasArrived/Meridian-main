using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

public sealed class DirectLendingRestructuringTests
{
    private static readonly DateOnly RestructuringDate = new(2026, 4, 3);

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 100)]
    [InlineData(25, 75)]
    [InlineData(100, 0)]
    public async Task Restructure_AcceptsNullZeroPartialAndFullHaircuts(int? requestedHaircut, int expectedPrincipal)
    {
        var (service, loan) = await CreateFundedLoanAsync();
        var before = (await service.GetServicingStateAsync(loan.LoanId))!;
        var newTerms = loan.CurrentTerms with { MaturityDate = new DateOnly(2030, 3, 22), CommitmentAmount = 300m };
        var request = new RestructureLoanRequest(RestructuringType.Full, "Retained workout terms", RestructuringDate,
            newTerms, requestedHaircut.HasValue ? (decimal)requestedHaircut.Value : null);

        var result = await service.RestructureLoanAsync(loan.LoanId, request);

        var state = (await service.GetServicingStateAsync(loan.LoanId))!;
        result!.Status.Should().Be(LoanStatus.Workout);
        result.CurrentTerms.Should().Be(newTerms);
        result.CurrentTermsVersion.Should().Be(2);
        state.Status.Should().Be(LoanStatus.Workout);
        state.Balances.PrincipalOutstanding.Should().Be(expectedPrincipal);
        state.DrawdownLots.Sum(lot => lot.RemainingPrincipal).Should().Be(expectedPrincipal);
        state.DrawdownLots.Sum(lot => lot.OriginalPrincipal).Should().Be(100m);
        state.TotalDrawn.Should().Be(before.TotalDrawn);
        state.CurrentCommitment.Should().Be(before.CurrentCommitment);
        state.AvailableToDraw.Should().Be(before.AvailableToDraw);
        state.Balances.Should().Be(before.Balances with { PrincipalOutstanding = expectedPrincipal });
        state.ServicingRevision.Should().Be(before.ServicingRevision + 1);

        var restructuring = (await service.GetHistoryAsync(loan.LoanId)).Last();
        using var payload = JsonDocument.Parse(restructuring.PayloadJson);
        var amount = requestedHaircut ?? 0;
        payload.RootElement.GetProperty("haircutAmount").GetDecimal().Should().Be(amount);
        payload.RootElement.GetProperty("principalHaircut").GetDecimal().Should().Be(amount);
        payload.RootElement.GetProperty("appliedPrincipalHaircut").GetDecimal().Should().Be(amount);
        payload.RootElement.GetProperty("newTermsVersion").GetProperty("recordedAt").GetDateTimeOffset()
            .Should().Be(result.TermsVersions.Single(version => version.VersionNumber == 2).RecordedAt);
        payload.RootElement.GetProperty("servicingRevision").GetProperty("createdAt").GetDateTimeOffset()
            .Should().Be(state.RevisionHistory.First().CreatedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Restructure_RejectsInvalidHaircutBeforeChangingAnyTermsBalancesOrHistory(int haircut)
    {
        var (service, loan) = await CreateFundedLoanAsync();
        var before = await CaptureAsync(service, loan.LoanId);
        var request = new RestructureLoanRequest(RestructuringType.Full, "Invalid haircut", RestructuringDate,
            loan.CurrentTerms with { FixedAnnualRate = 0.04m }, haircut);

        var error = await Assert.ThrowsAsync<DirectLendingCommandException>(
            () => service.RestructureLoanAsync(loan.LoanId, request));

        error.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        (await CaptureAsync(service, loan.LoanId)).Should().Be(before);
    }

    [Fact]
    public async Task Restructure_RejectsInvalidReplacementTermsBeforeApplyingValidHaircut()
    {
        var (service, loan) = await CreateFundedLoanAsync();
        var before = await CaptureAsync(service, loan.LoanId);
        var request = new RestructureLoanRequest(RestructuringType.Full, "Invalid replacement terms", RestructuringDate,
            loan.CurrentTerms with { CommitmentAmount = 0m }, 25m);

        var error = await Assert.ThrowsAsync<DirectLendingCommandException>(
            () => service.RestructureLoanAsync(loan.LoanId, request));

        error.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        (await CaptureAsync(service, loan.LoanId)).Should().Be(before);
    }

    [Fact]
    public async Task Restructure_RejectsNullReasonBeforeCreatingAnUnrebuildableEvent()
    {
        var (service, loan) = await CreateFundedLoanAsync();
        var before = await CaptureAsync(service, loan.LoanId);

        var error = await Assert.ThrowsAsync<DirectLendingCommandException>(() => service.RestructureLoanAsync(loan.LoanId,
            new RestructureLoanRequest(RestructuringType.PrincipalHaircut, null!, RestructuringDate, HaircutAmount: 25m)));

        error.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        (await CaptureAsync(service, loan.LoanId)).Should().Be(before);
    }

    [Fact]
    public async Task Restructure_ReducesEarliestDrawdownFirstEvenWhenBookedLater()
    {
        var (service, loan) = await CreateFundedLoanAsync();
        var before = (await service.GetServicingStateAsync(loan.LoanId))!;

        await service.RestructureLoanAsync(loan.LoanId,
            new RestructureLoanRequest(RestructuringType.PrincipalHaircut, "Reduce principal", RestructuringDate, HaircutAmount: 50m));

        var state = (await service.GetServicingStateAsync(loan.LoanId))!;
        state.Balances.PrincipalOutstanding.Should().Be(50m);
        state.DrawdownLots.Single(lot => lot.ExternalRef == "earlier-drawdown").RemainingPrincipal.Should().Be(0m);
        state.DrawdownLots.Single(lot => lot.ExternalRef == "later-drawdown").RemainingPrincipal.Should().Be(50m);
        state.DrawdownLots.Select(lot => (lot.LotId, lot.OriginalPrincipal))
            .Should().Equal(before.DrawdownLots.Select(lot => (lot.LotId, lot.OriginalPrincipal)));
        state.DrawdownLots.Sum(lot => lot.RemainingPrincipal).Should().Be(state.Balances.PrincipalOutstanding);
        state.TotalDrawn.Should().Be(100m);
        state.AvailableToDraw.Should().Be(before.AvailableToDraw);
    }

    [Fact]
    public async Task Restructure_FullHaircutRetryReturnsRetainedContractWithoutAnotherEffect()
    {
        var (service, loan) = await CreateFundedLoanAsync();
        var request = new RestructureLoanRequest(RestructuringType.Full, "Full principal restructuring", RestructuringDate,
            loan.CurrentTerms with { MaturityDate = new DateOnly(2030, 3, 22) }, 100m);
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-restructuring-test", false);
        var committed = await service.RestructureLoanAsync(loan.LoanId, request, metadata);
        var beforeRetry = await CaptureAsync(service, loan.LoanId);

        var retry = await service.RestructureLoanAsync(loan.LoanId, request, metadata);

        retry.Should().BeEquivalentTo(committed);
        (await CaptureAsync(service, loan.LoanId)).Should().Be(beforeRetry);
        (await service.GetHistoryAsync(loan.LoanId)).Count(entry => entry.CommandId == metadata.CommandId).Should().Be(1);
        (await service.GetServicingStateAsync(loan.LoanId))!.Balances.PrincipalOutstanding.Should().Be(0m);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("terms")]
    [InlineData("date")]
    [InlineData("reason")]
    [InlineData("type")]
    public async Task Restructure_CommandIdentityRejectsChangedRequestWithoutMutation(string changedField)
    {
        var (service, loan) = await CreateFundedLoanAsync();
        var request = new RestructureLoanRequest(RestructuringType.Full, "Full principal restructuring", RestructuringDate,
            loan.CurrentTerms with { MaturityDate = new DateOnly(2030, 3, 22) }, 100m);
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-restructuring-test", false);
        await service.RestructureLoanAsync(loan.LoanId, request, metadata);
        var before = await CaptureAsync(service, loan.LoanId);
        var conflicting = changedField switch
        {
            "amount" => request with { HaircutAmount = 99m },
            "terms" => request with { NewTerms = request.NewTerms! with { FixedAnnualRate = 0.04m } },
            "date" => request with { EffectiveDate = RestructuringDate.AddDays(1) },
            "reason" => request with { Reason = "Changed rationale" },
            "type" => request with { RestructuringType = RestructuringType.PrincipalHaircut },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };

        var error = await Assert.ThrowsAsync<DirectLendingCommandException>(
            () => service.RestructureLoanAsync(loan.LoanId, conflicting, metadata));

        error.Error.Code.Should().Be(DirectLendingErrorCode.Conflict);
        (await CaptureAsync(service, loan.LoanId)).Should().Be(before);
    }

    private static async Task<string> CaptureAsync(InMemoryDirectLendingService service, Guid loanId)
        => JsonSerializer.Serialize(new
        {
            Contract = await service.GetLoanAsync(loanId),
            Servicing = await service.GetServicingStateAsync(loanId),
            History = await service.GetHistoryAsync(loanId)
        });

    private static async Task<(InMemoryDirectLendingService Service, LoanContractDetailDto Loan)> CreateFundedLoanAsync()
    {
        var service = new InMemoryDirectLendingService();
        var terms = new DirectLendingTermsDto(
            OriginationDate: new DateOnly(2026, 3, 22),
            MaturityDate: new DateOnly(2029, 3, 22),
            CommitmentAmount: 200m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.08m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0.03m,
            DefaultRateSpreadBps: 200m,
            PrepaymentAllowed: true,
            CovenantsJson: "{}");
        var loan = await service.CreateLoanAsync(new CreateLoanRequest(Guid.NewGuid(), "Synthetic workout facility",
            new BorrowerInfoDto(Guid.NewGuid(), "Synthetic borrower", Guid.NewGuid()), terms.OriginationDate, terms));
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(terms.OriginationDate));
        var later = new DateOnly(2026, 4, 2);
        var earlier = new DateOnly(2026, 4, 1);
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(60m, later, later, "later-drawdown"));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(40m, earlier, earlier, "earlier-drawdown"));
        return (service, (await service.GetLoanAsync(loan.LoanId))!);
    }
}
