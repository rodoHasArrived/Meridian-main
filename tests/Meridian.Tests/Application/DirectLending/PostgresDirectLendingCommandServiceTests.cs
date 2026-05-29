using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Application.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed class PostgresDirectLendingCommandServiceTests
{
    [Fact]
    public async Task PostDailyAccrualAsync_PassesProjectedLedgerEntriesToStateStoreWithEventLineage()
    {
        var loanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var commandId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var correlationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var contract = BuildContract(loanId);
        var servicing = BuildServicing(loanId);
        IReadOnlyList<LedgerJournalEntryWrite>? capturedLedgerEntries = null;
        Guid capturedEventId = Guid.Empty;

        var stateStore = Substitute.For<IDirectLendingStateStore>();
        stateStore.SaveAsync(
                loanId,
                expectedVersion: 7,
                nextVersion: 8,
                Arg.Any<LoanContractDetailDto>(),
                Arg.Any<LoanServicingStateDto>(),
                eventType: "loan.daily-accrual-posted",
                eventSchemaVersion: 3,
                effectiveDate: new DateOnly(2026, 3, 24),
                Arg.Any<JsonDocument>(),
                Arg.Any<DirectLendingEventWriteMetadata>(),
                Arg.Any<DirectLendingPersistenceBatch?>(),
                Arg.Any<IReadOnlyList<LedgerJournalEntryWrite>?>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                capturedLedgerEntries = call.ArgAt<IReadOnlyList<LedgerJournalEntryWrite>?>(11);
                capturedEventId = call.ArgAt<Guid>(12);
            });

        var queryService = Substitute.For<IDirectLendingQueryService>();
        queryService.LoadAggregateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns(new PersistedDirectLendingState(loanId, 7, contract, servicing));

        var service = new PostgresDirectLendingCommandService(
            stateStore,
            Substitute.For<IDirectLendingOperationsStore>(),
            queryService,
            new LoanAccountingProjector(BuildLedgerJournalStore(), new AccountingPolicyService()),
            new DirectLendingOptions { CurrentEventSchemaVersion = 3 });

        var result = await service.PostDailyAccrualAsync(
            loanId,
            new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)),
            new DirectLendingCommandMetadataDto(
                CommandId: commandId,
                CorrelationId: correlationId,
                CausationId: null,
                SourceSystem: "unit-test",
                ReplayFlag: false));

        result.IsSuccess.Should().BeTrue();
        capturedEventId.Should().NotBeEmpty();
        capturedLedgerEntries.Should().ContainSingle();
        var write = capturedLedgerEntries![0];
        write.AggregateId.Should().Be(loanId);
        write.SourceEventId.Should().Be(capturedEventId);
        write.CommandId.Should().Be(commandId);
        write.CorrelationId.Should().Be(correlationId);
        write.PostingKind.Should().Be(LedgerPostingKindDto.Originating);
        write.Entry.IsBalanced.Should().BeTrue();
        write.Entry.Metadata.Tags.Should().ContainKey("sourceEventId")
            .WhoseValue.Should().Be(capturedEventId.ToString("D"));
    }

    private static ILedgerJournalStore BuildLedgerJournalStore()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListPeriodsAsync(
                null,
                null,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new LedgerAccountingPeriod(
                    PeriodId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    LedgerBookId: Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    FiscalYear: 2026,
                    PeriodNo: 3,
                    Label: "2026-P03",
                    StartDate: new DateOnly(2026, 3, 1),
                    EndDate: new DateOnly(2026, 3, 31),
                    Status: "Open",
                    OpenedAt: DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                    ClosedAt: null,
                    Version: 1)
            ]);

        return store;
    }

    private static LoanContractDetailDto BuildContract(Guid loanId)
    {
        var terms = new DirectLendingTermsDto(
            OriginationDate: new DateOnly(2026, 3, 22),
            MaturityDate: new DateOnly(2029, 3, 22),
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.08m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Monthly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0.0025m,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null);

        return new LoanContractDetailDto(
            LoanId: loanId,
            FacilityName: "Northwind Senior Term Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Northwind Holdings", Guid.NewGuid()),
            Status: LoanStatus.Active,
            EffectiveDate: new DateOnly(2026, 3, 22),
            ActivationDate: new DateOnly(2026, 3, 22),
            CloseDate: null,
            CurrentTermsVersion: 1,
            CurrentTerms: terms,
            TermsVersions:
            [
                new LoanTermsVersionDto(
                    VersionNumber: 1,
                    TermsHash: "terms-v1",
                    Terms: terms,
                    SourceAction: "loan.created",
                    AmendmentReason: null,
                    RecordedAt: DateTimeOffset.Parse("2026-03-22T00:00:00Z"))
            ]);
    }

    private static LoanServicingStateDto BuildServicing(Guid loanId) =>
        new(
            LoanId: loanId,
            Status: LoanStatus.Active,
            CurrentCommitment: 1_000_000m,
            TotalDrawn: 500_000m,
            AvailableToDraw: 500_000m,
            Balances: new OutstandingBalancesDto(
                PrincipalOutstanding: 500_000m,
                InterestAccruedUnpaid: 0m,
                CommitmentFeeAccruedUnpaid: 0m,
                FeesAccruedUnpaid: 0m,
                PenaltyAccruedUnpaid: 0m),
            DrawdownLots: [],
            CurrentRateReset: null,
            LastAccrualDate: new DateOnly(2026, 3, 23),
            LastPaymentDate: null,
            ServicingRevision: 7,
            RevisionHistory: [],
            AccrualEntries: []);
}
