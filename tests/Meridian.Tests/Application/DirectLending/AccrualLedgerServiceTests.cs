using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using NSubstitute;

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

    [Fact]
    public async Task ReverseAccrualAsync_UsesSoftClosedPeriodForApprovedAdjustment()
    {
        var period = BuildOpenPeriod() with { Status = "SoftClosed" };
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
        writes[0].PeriodId.Should().Be(period.PeriodId);
        writes[0].PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
        writes[0].AdjustmentApproval.Should().BeEquivalentTo(approval);
    }

    [Fact]
    public async Task AccrueAsync_RejectsOriginatingAccrualWhenOnlyMatchingPeriodIsSoftClosed()
    {
        var period = BuildOpenPeriod() with { Status = "SoftClosed" };
        var service = CreateService(period);
        var loanId = Guid.NewGuid();

        var act = () => service.AccrueAsync(
            loanId,
            BuildContract(loanId),
            new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)),
            BuildAccrualEntry(),
            Guid.NewGuid(),
            BuildMetadata());

        await act.Should().ThrowAsync<DirectLendingCommandException>()
            .Where(ex => ex.Error.Code == DirectLendingErrorCode.Validation)
            .WithMessage("*No ledger accounting period accepts Originating posting date '2026-03-24'*");
    }

    [Fact]
    public async Task AccrueAsync_PostsDeferredPikToPikReceivable_UnderCapitalizationPolicy()
    {
        var service = CreateService(BuildOpenPeriod());
        var loanId = Guid.NewGuid();
        var accrual = BuildAccrualEntry() with
        {
            InterestAmount = 166.66m,
            CommitmentFeeAmount = 0m,
            PikInterestAmount = 55.56m,
            PikCapitalizationDeferred = true
        };

        var writes = await service.AccrueAsync(
            loanId,
            BuildContract(loanId),
            new PostDailyAccrualRequest(accrual.AccrualDate),
            accrual,
            Guid.NewGuid(),
            BuildMetadata());

        var lines = writes.Should().ContainSingle().Subject.Entry.Lines;
        lines.Should().Contain(line => line.Account.Name == "PikInterestReceivable" && line.Debit == 55.56m);
        lines.Should().Contain(line => line.Account.Name == "PikInterestIncome" && line.Credit == 55.56m);
        lines.Should().Contain(line => line.Account.Name == "AccruedInterestReceivable" && line.Debit == 166.66m);
        lines.Should().NotContain(line => line.Account.Name == "LoanPrincipal" && line.Debit > 0m,
            "deferred PIK is not principal until a capitalization date");
    }

    [Fact]
    public async Task AccrueAsync_KeepsLegacyPikToggleCapitalizingIntoPrincipal()
    {
        var service = CreateService(BuildOpenPeriod());
        var loanId = Guid.NewGuid();
        var accrual = BuildAccrualEntry() with { InterestAmount = 0m, CommitmentFeeAmount = 0m, PikInterestAmount = 222.22m };

        var writes = await service.AccrueAsync(
            loanId,
            BuildContract(loanId),
            new PostDailyAccrualRequest(accrual.AccrualDate),
            accrual,
            Guid.NewGuid(),
            BuildMetadata());

        var lines = writes.Should().ContainSingle().Subject.Entry.Lines;
        lines.Should().Contain(line => line.Account.Name == "LoanPrincipal" && line.Debit == 222.22m);
        lines.Should().Contain(line => line.Account.Name == "InterestIncome" && line.Credit == 222.22m);
        lines.Should().NotContain(line => line.Account.Name.StartsWith("PikInterest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CapitalizePikAsync_MovesPikReceivableIntoLoanPrincipal()
    {
        var service = CreateService(BuildOpenPeriod());
        var loanId = Guid.NewGuid();
        var capitalization = new PikCapitalizationDto(
            new DateOnly(2026, 3, 24),
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 24),
            1_333.44m,
            DateTimeOffset.Parse("2026-03-24T23:00:00Z"));

        var writes = await service.CapitalizePikAsync(loanId, BuildContract(loanId), capitalization, Guid.NewGuid(), BuildMetadata());

        var entry = writes.Should().ContainSingle().Subject.Entry;
        entry.IsBalanced.Should().BeTrue();
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Should().Contain(line => line.Account.Name == "LoanPrincipal" && line.Debit == 1_333.44m);
        entry.Lines.Should().Contain(line => line.Account.Name == "PikInterestReceivable" && line.Credit == 1_333.44m);
    }

    // ── PRD-115 increment 5: split journals ─────────────────────────────────

    private const string SplitPaymentJson = """
        {"amount":257000,"effectiveDate":"2026-03-24","resolution":{"breakdown":{"toInterest":1000,"toCommitmentFee":200,"toFees":300,"toPenalty":400,"toPrincipal":500},
         "resolutionBasis":"WaterfallAuto","ruleVersion":"waterfall-v1","unappliedAmount":1600,"excessToPrincipal":250000,"excessToPrepaidInterest":3000,
         "excessRuleApplied":1,"excessRuleNote":null,"prepaymentPenaltyCharged":5000}}
        """;

    [Fact]
    public async Task Projector_MixedPayment_SplitsByAllocationAndBalances()
    {
        // The amounts are illustrative: one payment touching every line the split can post.
        var entry = await ProjectSingleAsync("loan.mixed-payment-applied", SplitPaymentJson);

        entry.IsBalanced.Should().BeTrue();
        entry.Lines.Select(line => (line.Account.Name, line.Debit, line.Credit)).Should().BeEquivalentTo(new[]
        {
            ("Cash", 257_000m, 0m),
            ("AccruedInterestReceivable", 0m, 1_000m),
            ("CommitmentFeeReceivable", 0m, 200m),
            ("FeeReceivable", 0m, 300m),
            ("PenaltyReceivable", 0m, 400m),
            ("LoanPrincipal", 0m, 250_500m),
            ("PrepaidInterestLiability", 0m, 3_000m),
            ("UnappliedCashLiability", 0m, 1_600m),
            ("PenaltyReceivable", 5_000m, 0m),
            ("PenaltyIncome", 0m, 5_000m),
        });
        entry.Lines.Single(line => line.Account.Name == "PrepaidInterestLiability").Account.AccountType.Should().Be(LedgerAccountType.Liability);
        entry.Lines.Single(line => line.Account.Name == "UnappliedCashLiability").Account.AccountType.Should().Be(LedgerAccountType.Liability);
    }

    [Fact]
    public async Task Projector_MixedPayment_RecordedBeforePrd115_KeepsTheClearingPosting()
    {
        var entry = await ProjectSingleAsync(
            "loan.mixed-payment-applied",
            """{"amount":5000,"resolution":{"breakdown":{"toInterest":100,"toCommitmentFee":0,"toFees":0,"toPenalty":0,"toPrincipal":4900},"resolutionBasis":"Manual","ruleVersion":"v1","unappliedAmount":0}}""");

        entry.Lines.Select(line => (line.Account.Name, line.Debit, line.Credit))
            .Should().Equal(("Cash", 5_000m, 0m), ("LoanAndAccrualsClearing", 0m, 5_000m));
    }

    [Fact]
    public async Task Projector_MixedPayment_WhoseResolutionDoesNotSumToTheReceipt_IsRejected()
    {
        var act = () => ProjectSingleAsync("loan.mixed-payment-applied", SplitPaymentJson.Replace("\"amount\":257000", "\"amount\":257001", StringComparison.Ordinal));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*would not balance*");
    }

    [Fact]
    public async Task AccrueAsync_SettlesInterestFromPrepaidInterestFirst()
    {
        var service = CreateService(BuildOpenPeriod());
        var loanId = Guid.NewGuid();
        var accrual = BuildAccrualEntry();

        var entry = (await service.AccrueAsync(
            loanId, BuildContract(loanId), new PostDailyAccrualRequest(accrual.AccrualDate), accrual, Guid.NewGuid(), BuildMetadata(),
            prepaidInterestConsumed: 10m)).Should().ContainSingle().Subject.Entry;

        entry.IsBalanced.Should().BeTrue();
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "PrepaidInterestLiability" && line.Debit == 10m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "AccruedInterestReceivable" && line.Debit == accrual.InterestAmount - 10m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "InterestIncome" && line.Credit == accrual.InterestAmount);
    }

    [Fact]
    public async Task ReverseAccrualAsync_OfInterestSettledFromPrepaidInterest_IsTheExactInverseOfThePosting()
    {
        var service = CreateService(BuildOpenPeriod());
        var loanId = Guid.NewGuid();
        var accrual = BuildAccrualEntry();

        var posted = (await service.AccrueAsync(
            loanId, BuildContract(loanId), new PostDailyAccrualRequest(accrual.AccrualDate), accrual, Guid.NewGuid(), BuildMetadata(),
            prepaidInterestConsumed: 10m)).Should().ContainSingle().Subject.Entry;
        var reversed = (await service.ReverseAccrualAsync(
            loanId, BuildContract(loanId), accrual, Guid.NewGuid(), BuildMetadata(),
            prepaidInterestConsumed: 10m)).Should().ContainSingle().Subject.Entry;

        reversed.IsBalanced.Should().BeTrue();
        static Dictionary<string, decimal> Net(JournalEntry entry)
            => entry.Lines.GroupBy(line => line.Account.Name).ToDictionary(group => group.Key, group => group.Sum(line => line.Debit - line.Credit));
        var postedNet = Net(posted);
        var reversedNet = Net(reversed);
        reversedNet.Keys.Should().BeEquivalentTo(postedNet.Keys);
        foreach (var (account, net) in postedNet)
        {
            reversedNet[account].Should().Be(-net, $"the reversal undoes {account} exactly");
        }

        reversedNet["PrepaidInterestLiability"].Should().Be(-10m, "the prepaid liability is restored");
    }

    [Fact]
    public async Task Projector_FeeAssessed_PostsTheReceivableAndIncome()
    {
        var entry = await ProjectSingleAsync("loan.fee-assessed", """{"feeType":1,"amount":250,"effectiveDate":"2026-03-24"}""");

        entry.IsBalanced.Should().BeTrue();
        entry.Lines.Select(line => (line.Account.Name, line.Debit, line.Credit))
            .Should().BeEquivalentTo(new[] { ("FeeReceivable", 250m, 0m), ("FeeIncome", 0m, 250m) });
    }

    [Fact]
    public async Task Projector_PrepaymentPenaltyCharged_PostsTheReceivableAndIncome()
    {
        var entry = await ProjectSingleAsync("loan.prepayment-penalty-charged", """{"outstandingPrincipal":100000,"penaltyAmount":2000,"effectiveDate":"2026-03-24"}""");

        entry.IsBalanced.Should().BeTrue();
        entry.Lines.Select(line => (line.Account.Name, line.Debit, line.Credit))
            .Should().BeEquivalentTo(new[] { ("PenaltyReceivable", 2_000m, 0m), ("PenaltyIncome", 0m, 2_000m) });
    }

    private async Task<JournalEntry> ProjectSingleAsync(string eventType, string payloadJson)
    {
        var loanId = Guid.NewGuid();
        using var payload = JsonDocument.Parse(payloadJson);
        var writes = await CreateProjector(BuildOpenPeriod()).ProjectAsync(
            loanId, BuildContract(loanId), eventType, new DateOnly(2026, 3, 24), payload, Guid.NewGuid(), BuildMetadata(), CancellationToken.None);
        return writes.Should().ContainSingle().Subject.Entry;
    }

    private static LoanAccountingProjector CreateProjector(LedgerAccountingPeriod period)
        => new(
            new PeriodOnlyLedgerJournalStore(period),
            new AccountingPolicyService(),
            BuildSecurityMasterQueryService());

    private static AccrualLedgerService CreateService(LedgerAccountingPeriod period)
        => new(CreateProjector(period));

    private static Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService BuildSecurityMasterQueryService()
    {
        var service = Substitute.For<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
        service.GetByIdAsync(Guid.Parse("99999999-9999-9999-9999-999999999999"), Arg.Any<CancellationToken>())
            .Returns(BuildSecurityMasterDetail());
        return service;
    }

    private static SecurityDetailDto BuildSecurityMasterDetail() =>
        new(
            SecurityId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            AssetClass: "CustomAsset",
            Status: SecurityStatusDto.Active,
            DisplayName: "Northwind Senior Term Loan",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Northwind Senior Term Loan", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { facility = "Northwind" }),
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.InternalCode,
                    "NWTERM26",
                    IsPrimary: true,
                    ValidFrom: DateTimeOffset.Parse("2026-03-22T00:00:00Z"))
            ],
            Aliases: [],
            Version: 4,
            EffectiveFrom: DateTimeOffset.Parse("2026-03-22T00:00:00Z"),
            EffectiveTo: null);

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
            CovenantsJson: null,
            SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(
                SecurityId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
                Symbol: "NWTERM26",
                Provenance: "source:security-master;definition:loan-facility",
                ApprovalReference: "sm-approval:nwterm26-controller",
                LedgerMappingReference: "ledger-map:nwterm26-direct-lending",
                Status: "Active"));

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
