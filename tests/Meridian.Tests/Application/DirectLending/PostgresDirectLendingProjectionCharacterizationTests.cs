using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.DirectLending;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// Characterization pins for the Postgres direct-lending projection, recorded against base 7d072638
/// before the schedule kernel (ADR-025) folds direct lending in behind the <c>dl-engine-v2</c> fence.
/// The pins drive the public <see cref="PostgresDirectLendingCommandService.RequestProjectionAsync"/>
/// seam with the substitute harness used by <c>PostgresDirectLendingCommandServiceTests</c> and read
/// the flows handed to <see cref="IDirectLendingOperationsStore.SaveProjectionRunAsync"/>.
/// </summary>
/// <remarks>
/// Tests tagged <c>Category=KnownDefect</c> assert TODAY'S defective behavior from the registry in
/// <c>tests/fixtures/schedules/KNOWN-DEFECTS.md</c> and name the TARGET behavior in a comment. When a
/// fix lands the pin fails; the fixing PR flips the assertion and updates the registry in the same
/// change. Loans that stay on <c>dl-engine-v1</c> keep this behavior.
/// </remarks>
public sealed class PostgresDirectLendingProjectionCharacterizationTests
{
    private static readonly DateOnly ProjectionAsOf = new(2026, 1, 31);
    private static readonly DateOnly Maturity = new(2026, 12, 31);

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task FixedMonthlyIsdaLoan_CurrentBehavior_CompoundsFromAsOfAndDividesBy360_SCH_DEF_005()
    {
        // SCH-DEF-005. TARGET (dl-engine-v2): periods come from the contractual schedule and the
        // basis is honoured, so row 1 (28 days) under Actual/Actual ISDA is
        // 1,000,000 x 0.08 x 28 / 365 = 6,136.99.
        var flows = await ProjectAsync(BuildTerms(RateTypeKind.Fixed, fixedAnnualRate: 0.08m));

        var interest = flows.Where(static flow => flow.FlowType == "Interest").ToArray();
        interest.Select(static flow => flow.DueDate).Should().Equal(
            new[]
            {
                new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 28), new DateOnly(2026, 4, 28),
                new DateOnly(2026, 5, 28), new DateOnly(2026, 6, 28), new DateOnly(2026, 7, 28),
                new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 28),
                new DateOnly(2026, 11, 28), new DateOnly(2026, 12, 28), new DateOnly(2026, 12, 31)
            },
            "CURRENT defective behavior: each due date is the previous one plus a month, compounded from the as-of");
        interest[0].AccrualStartDate.Should().Be(ProjectionAsOf);
        interest[0].Amount.Should().Be(6_222.22m,
            "CURRENT defective behavior: every basis except Act/365F divides 28 actual days by 360");
        interest[^1].AccrualStartDate.Should().Be(new DateOnly(2026, 12, 28));
        interest[^1].Amount.Should().Be(666.67m);

        var principal = flows.Should().ContainSingle(static flow => flow.FlowType == "Principal").Subject;
        principal.DueDate.Should().Be(Maturity);
        principal.Amount.Should().Be(1_000_000m);
    }

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task FloatingLoanWithoutRateReset_CurrentBehavior_ProjectsZeroInterest_SCH_DEF_005()
    {
        // SCH-DEF-005. TARGET (dl-engine-v2): a floating loan with no rate reset is blocked from
        // projection instead of projecting zero interest.
        var flows = await ProjectAsync(BuildTerms(RateTypeKind.Floating, fixedAnnualRate: null));

        var interest = flows.Where(static flow => flow.FlowType == "Interest").ToArray();
        interest.Should().HaveCount(12);
        interest.Should().OnlyContain(static flow => flow.Amount == 0.00m && flow.AnnualRate == 0m,
            "CURRENT defective behavior: a missing fixing falls back to a 0% rate");
        flows.Should().ContainSingle(static flow => flow.FlowType == "Principal")
            .Which.Amount.Should().Be(1_000_000m);
    }

    private static async Task<IReadOnlyList<ProjectedCashFlowDto>> ProjectAsync(DirectLendingTermsDto terms)
    {
        var loanId = Guid.Parse("5c4ed005-0000-4000-8000-000000000005");
        IReadOnlyList<ProjectedCashFlowDto>? captured = null;
        var operations = Substitute.For<IDirectLendingOperationsStore>();
        operations.GetProjectionRunsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        operations.SaveProjectionRunAsync(Arg.Any<ProjectionRunDto>(), Arg.Any<IReadOnlyList<ProjectedCashFlowDto>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.ArgAt<IReadOnlyList<ProjectedCashFlowDto>>(1);
                return call.ArgAt<ProjectionRunDto>(0);
            });
        var query = Substitute.For<IDirectLendingQueryService>();
        query.LoadAggregateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns(new PersistedDirectLendingState(loanId, 3, BuildContract(loanId, terms), BuildServicing(loanId)));
        query.GetHistoryAsync(loanId, Arg.Any<CancellationToken>()).Returns([
            new LoanEventLineageDto(Guid.Parse("5c4ed005-0000-4000-8000-0000000000e1"), 3, "loan.activated", 3,
                ProjectionAsOf, DateTimeOffset.Parse("2026-01-31T00:00:00Z"), "{}", null, null, null, "test", false)]);
        var service = new PostgresDirectLendingCommandService(
            Substitute.For<IDirectLendingStateStore>(),
            operations,
            query,
            new LoanAccountingProjector(null, new AccountingPolicyService()),
            new DirectLendingOptions());

        var result = await service.RequestProjectionAsync(
            loanId,
            ProjectionAsOf,
            new DirectLendingCommandMetadataDto(Guid.Parse("5c4ed005-0000-4000-8000-0000000000c1"), null, null, "test", false));

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        return captured!;
    }

    private static DirectLendingTermsDto BuildTerms(RateTypeKind rateTypeKind, decimal? fixedAnnualRate)
        => new(
            OriginationDate: ProjectionAsOf,
            MaturityDate: Maturity,
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: rateTypeKind,
            FixedAnnualRate: fixedAnnualRate,
            InterestIndexName: rateTypeKind == RateTypeKind.Floating ? "SOFR" : null,
            SpreadBps: rateTypeKind == RateTypeKind.Floating ? 300m : null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.ActualActualISDA,
            PaymentFrequency: PaymentFrequency.Monthly,
            AmortizationType: AmortizationType.Bullet,
            CommitmentFeeRate: null,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null);

    private static LoanContractDetailDto BuildContract(Guid loanId, DirectLendingTermsDto terms)
        => new(
            LoanId: loanId,
            FacilityName: "Schedule Characterization Term Loan",
            Borrower: new BorrowerInfoDto(Guid.Parse("5c4ed005-0000-4000-8000-0000000000b0"), "Characterization Borrower", null),
            Status: LoanStatus.Active,
            EffectiveDate: ProjectionAsOf,
            ActivationDate: ProjectionAsOf,
            CloseDate: null,
            CurrentTermsVersion: 1,
            CurrentTerms: terms,
            TermsVersions:
            [
                new LoanTermsVersionDto(1, "terms-v1", terms, "loan.created", null, DateTimeOffset.Parse("2026-01-31T00:00:00Z"))
            ]);

    private static LoanServicingStateDto BuildServicing(Guid loanId)
        => new(
            LoanId: loanId,
            Status: LoanStatus.Active,
            CurrentCommitment: 1_000_000m,
            TotalDrawn: 1_000_000m,
            AvailableToDraw: 0m,
            Balances: new OutstandingBalancesDto(
                PrincipalOutstanding: 1_000_000m,
                InterestAccruedUnpaid: 0m,
                CommitmentFeeAccruedUnpaid: 0m,
                FeesAccruedUnpaid: 0m,
                PenaltyAccruedUnpaid: 0m),
            DrawdownLots: [],
            CurrentRateReset: null,
            LastAccrualDate: null,
            LastPaymentDate: null,
            ServicingRevision: 3,
            RevisionHistory: [],
            AccrualEntries: []);
}
