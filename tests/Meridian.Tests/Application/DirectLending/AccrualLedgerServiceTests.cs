using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Application.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Application.DirectLending;

public sealed class AccrualLedgerServiceTests
{
    [Fact]
    public async Task AccrueAsync_ProjectsOriginatingPostingKind()
    {
        var period = BuildOpenPeriod();
        var service = CreateService(period);
        var loanId = Guid.NewGuid();

        var writes = await service.AccrueAsync(
            loanId,
            BuildContract(loanId),
            new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)),
            BuildAccrualEntry(),
            Guid.NewGuid(),
            BuildMetadata());

        writes.Should().ContainSingle();
        writes[0].PostingKind.Should().Be(LedgerPostingKindDto.Originating);
    }

    [Fact]
    public async Task ReverseAccrualAsync_ProjectsAdjustmentPostingKind()
    {
        var period = BuildOpenPeriod();
        var service = CreateService(period);
        var loanId = Guid.NewGuid();

        var writes = await service.ReverseAccrualAsync(
            loanId,
            BuildContract(loanId),
            BuildAccrualEntry(),
            Guid.NewGuid(),
            BuildMetadata());

        writes.Should().ContainSingle();
        writes[0].PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
    }

    [Fact]
    public async Task ReverseAccrualAsync_PreservesAdjustmentApprovalMetadata()
    {
        var period = BuildOpenPeriod();
        var service = CreateService(period);
        var loanId = Guid.NewGuid();
        var approval = BuildApprovedAdjustmentApproval();

        var writes = await service.ReverseAccrualAsync(
            loanId,
            BuildContract(loanId),
            BuildAccrualEntry(),
            Guid.NewGuid(),
            BuildMetadata(),
            adjustmentApproval: approval);

        writes.Should().ContainSingle();
        writes[0].AdjustmentApproval.Should().BeEquivalentTo(approval);
    }

    private static AccrualLedgerService CreateService(LedgerAccountingPeriod period)
    {
        var projector = new LoanAccountingProjector(
            new PeriodOnlyLedgerJournalStore(period),
            new AccountingPolicyService());
        return new AccrualLedgerService(projector);
    }

    private static LedgerAccountingPeriod BuildOpenPeriod() =>
        new(
            PeriodId: Guid.NewGuid(),
            LedgerBookId: null,
            FiscalYear: 2026,
            PeriodNo: 3,
            Label: "2026-P03",
            StartDate: new DateOnly(2026, 3, 1),
            EndDate: new DateOnly(2026, 3, 31),
            Status: "Open",
            OpenedAt: DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
            ClosedAt: null,
            Version: 1);

    private static DailyAccrualEntryDto BuildAccrualEntry() =>
        new(
            AccrualEntryId: Guid.NewGuid(),
            AccrualDate: new DateOnly(2026, 3, 24),
            InterestAmount: 12.50m,
            CommitmentFeeAmount: 3.25m,
            PenaltyAmount: 0m,
            AnnualRateApplied: 0.08m,
            RecordedAt: DateTimeOffset.Parse("2026-03-24T12:00:00Z"));

    private static DirectLendingEventWriteMetadata BuildMetadata() =>
        new(
            CausationId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CommandId: Guid.NewGuid(),
            SourceSystem: "unit-test",
            ReplayFlag: false);

    private static LedgerAdjustmentApprovalMetadataDto BuildApprovedAdjustmentApproval() =>
        new(
            ApprovalId: "approval-direct-lending-reversal-1",
            Status: LedgerAdjustmentApprovalStatusDto.Approved,
            ApprovedBy: "fund-controller",
            ApprovedAt: DateTimeOffset.Parse("2026-03-24T13:00:00Z"),
            ReasonCode: "accrual-reversal",
            GovernanceCaseId: "case-direct-lending-reversal-1",
            EvidenceLink: "evidence://ledger/direct-lending/reversal-approval-1");

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

    private sealed class PeriodOnlyLedgerJournalStore(LedgerAccountingPeriod period) : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<LedgerAccountingPeriod?>(period.PeriodId == periodId ? period : null);

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([period]);

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) =>
            Task.FromResult<LedgerBookRecord?>(null);

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerBookRecord>>([]);

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
