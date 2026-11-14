using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Workstation;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// The daily worker runs the opt-in servicing schedules in a fixed order per date: scheduled
/// reset, then accrual, then PIK capitalization; a missing fixing fails closed.
/// </summary>
public sealed class DailyAccrualWorkerServicingScheduleTests
{
    private static readonly Guid LoanId = Guid.Parse("5c5c5c5c-0000-4000-8000-000000000011");
    private static readonly DateOnly Origination = new(2026, 1, 15);

    [Fact]
    public async Task RunAccrualBatchAsync_AppliesTheDueResetBeforeThatDaysAccrual()
    {
        var terms = FloatingTerms();
        var servicing = Servicing(lastAccrual: new DateOnly(2026, 4, 13), currentReset: Reset(Origination));
        var (commandService, calls) = RecordingCommandService(servicing);

        await CreateWorker(terms, servicing, commandService).RunAccrualBatchAsync(new DateOnly(2026, 4, 15), CancellationToken.None);

        calls.Should().Equal("accrue:2026-04-14", "reset:2026-04-15", "accrue:2026-04-15");
    }

    [Fact]
    public async Task RunAccrualBatchAsync_StopsAndRaisesAWorkItem_WhenTheFixingIsMissing()
    {
        var terms = FloatingTerms();
        var servicing = Servicing(lastAccrual: new DateOnly(2026, 4, 13), currentReset: Reset(Origination));
        var (commandService, calls) = RecordingCommandService(servicing, fixingMissing: true);
        var inbox = Substitute.For<IOperatorInboxService>();
        OperatorWorkItemDto? workItem = null;
        inbox.When(service => service.UpsertItemAsync(Arg.Any<OperatorWorkItemDto>(), Arg.Any<CancellationToken>()))
            .Do(call => workItem = call.Arg<OperatorWorkItemDto>());

        await CreateWorker(terms, servicing, commandService, inbox).RunAccrualBatchAsync(new DateOnly(2026, 4, 16), CancellationToken.None);

        calls.Should().Equal("accrue:2026-04-14", "reset:2026-04-15");
        workItem.Should().NotBeNull();
        workItem!.WorkItemId.Should().Be($"direct-lending-fixing-missing:{LoanId:D}:20260415");
        workItem.Kind.Should().Be(OperatorWorkItemKindDto.ProviderTrustGate);
        workItem.Scope.Should().Be("DirectLendingRateFixing");
    }

    [Fact]
    public async Task RunAccrualBatchAsync_InitializesTheRateFromTheLatestScheduledReset()
    {
        var terms = FloatingTerms();
        var servicing = Servicing(lastAccrual: null, currentReset: null);
        var (commandService, calls) = RecordingCommandService(servicing);

        await CreateWorker(terms, servicing, commandService).RunAccrualBatchAsync(new DateOnly(2026, 5, 2), CancellationToken.None);

        calls.Should().Equal("reset:2026-04-15", "accrue:2026-05-02");
    }

    [Fact]
    public async Task RunAccrualBatchAsync_CapitalizesPikAfterTheAccrualOnACapitalizationDate()
    {
        var terms = FixedPikTerms();
        var servicing = Servicing(lastAccrual: new DateOnly(2026, 2, 13), currentReset: null);
        var (commandService, calls) = RecordingCommandService(servicing);

        await CreateWorker(terms, servicing, commandService).RunAccrualBatchAsync(new DateOnly(2026, 2, 16), CancellationToken.None);

        calls.Should().Equal("accrue:2026-02-14", "accrue:2026-02-15", "capitalize:2026-02-15", "accrue:2026-02-16");
    }

    [Fact]
    public async Task RunAccrualBatchAsync_DoesNotCapitalizeTwice()
    {
        var terms = FixedPikTerms();
        var servicing = Servicing(lastAccrual: new DateOnly(2026, 2, 14), currentReset: null) with
        {
            LastPikCapitalizationDate = new DateOnly(2026, 2, 15)
        };
        var (commandService, calls) = RecordingCommandService(servicing);

        await CreateWorker(terms, servicing, commandService).RunAccrualBatchAsync(new DateOnly(2026, 2, 15), CancellationToken.None);

        calls.Should().Equal("accrue:2026-02-15");
    }

    [Fact]
    public async Task RunAccrualBatchAsync_LeavesLoansWithoutSchedulesOnTheLegacyPath()
    {
        var terms = FixedPikTerms() with { PikCapitalization = null };
        var servicing = Servicing(lastAccrual: new DateOnly(2026, 2, 14), currentReset: null);
        var (commandService, calls) = RecordingCommandService(servicing);

        await CreateWorker(terms, servicing, commandService).RunAccrualBatchAsync(new DateOnly(2026, 2, 15), CancellationToken.None);

        calls.Should().Equal("accrue:2026-02-15");
    }

    private static (IDirectLendingCommandService Service, List<string> Calls) RecordingCommandService(
        LoanServicingStateDto servicing,
        bool fixingMissing = false)
    {
        var calls = new List<string>();
        var current = servicing;
        var service = Substitute.For<IDirectLendingCommandService>();
        service.PostDailyAccrualAsync(LoanId, Arg.Any<PostDailyAccrualRequest>(), Arg.Any<DirectLendingCommandMetadataDto?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var date = call.Arg<PostDailyAccrualRequest>().AccrualDate;
                calls.Add($"accrue:{date:yyyy-MM-dd}");
                current = current with { LastAccrualDate = date };
                return DirectLendingCommandResult<DailyAccrualEntryDto>.Success(
                    new DailyAccrualEntryDto(Guid.NewGuid(), date, 1m, 0m, 0m, 0.08m, DateTimeOffset.UtcNow));
            });
        service.ApplyScheduledRateResetAsync(LoanId, Arg.Any<ApplyScheduledRateResetRequest>(), Arg.Any<DirectLendingCommandMetadataDto?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var date = call.Arg<ApplyScheduledRateResetRequest>().ResetDate;
                calls.Add($"reset:{date:yyyy-MM-dd}");
                if (fixingMissing)
                {
                    return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.FixingMissing, "No SOFR fixing.");
                }

                current = current with { CurrentRateReset = Reset(date) };
                return DirectLendingCommandResult<LoanServicingStateDto>.Success(current);
            });
        service.CapitalizePikAsync(LoanId, Arg.Any<CapitalizePikRequest>(), Arg.Any<DirectLendingCommandMetadataDto?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var date = call.Arg<CapitalizePikRequest>().CapitalizationDate;
                calls.Add($"capitalize:{date:yyyy-MM-dd}");
                current = current with { LastPikCapitalizationDate = date, PikInterestAccruedUnpaid = 0m };
                return DirectLendingCommandResult<LoanServicingStateDto>.Success(current);
            });
        return (service, calls);
    }

    private static DailyAccrualWorker CreateWorker(
        DirectLendingTermsDto terms,
        LoanServicingStateDto servicing,
        IDirectLendingCommandService commandService,
        IOperatorInboxService? inbox = null)
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        operationsStore.GetLoanIdsAsync(Arg.Any<CancellationToken>()).Returns([LoanId]);

        var contract = new LoanContractDetailDto(
            LoanId,
            "Scheduled Loan",
            new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null),
            LoanStatus.Active,
            Origination,
            Origination,
            null,
            1,
            terms,
            []);
        var queryService = Substitute.For<IDirectLendingQueryService>();
        queryService.GetServicingStateAsync(LoanId, Arg.Any<CancellationToken>()).Returns(servicing);
        queryService.LoadAggregateAsync(LoanId, Arg.Any<CancellationToken>())
            .Returns(new PersistedDirectLendingState(LoanId, 7, contract, servicing));

        return new DailyAccrualWorker(
            operationsStore,
            queryService,
            commandService,
            NullLogger<DailyAccrualWorker>.Instance,
            ledgerJournalStore: null,
            operatorInbox: inbox);
    }

    private static RateResetDto Reset(DateOnly effectiveDate) =>
        new(effectiveDate, "SOFR", 0.04m, 250m, 0.065m, "test") { ResetKind = RateResetKind.Scheduled };

    private static LoanServicingStateDto Servicing(DateOnly? lastAccrual, RateResetDto? currentReset) =>
        new(
            LoanId,
            LoanStatus.Active,
            1_000_000m,
            1_000_000m,
            0m,
            new OutstandingBalancesDto(1_000_000m, 0m, 0m, 0m, 0m),
            [],
            currentReset,
            lastAccrual,
            null,
            3,
            [],
            []);

    private static DirectLendingTermsDto BaseTerms(RateTypeKind rateType) =>
        new(
            OriginationDate: Origination,
            MaturityDate: new DateOnly(2029, 1, 15),
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: rateType,
            FixedAnnualRate: rateType == RateTypeKind.Fixed ? 0.08m : null,
            InterestIndexName: rateType == RateTypeKind.Floating ? "SOFR" : null,
            SpreadBps: rateType == RateTypeKind.Floating ? 250m : null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.Bullet,
            CommitmentFeeRate: null,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null);

    private static DirectLendingTermsDto FloatingTerms() =>
        BaseTerms(RateTypeKind.Floating) with
        {
            RateResetSchedule = new FloatingRateResetSchedule(
                "SOFR", RateResetFrequency.Quarterly, Origination, 2, LookbackDayKind.Business, DeclaredRate.BasisPoints(250m))
        };

    private static DirectLendingTermsDto FixedPikTerms() =>
        BaseTerms(RateTypeKind.Fixed) with
        {
            PikCapitalization = new PikCapitalizationPolicy(
                PikElection.Always, DeclaredRate.Percent(25m), PikCapitalizationFrequency.Monthly, new DateOnly(2026, 2, 15))
        };
}
