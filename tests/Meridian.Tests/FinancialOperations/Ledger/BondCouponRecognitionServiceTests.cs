using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.FixedIncome;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondCouponRecognitionServiceTests
{
    [Fact]
    public async Task NegativeCouponPreparesIncomeEvenWhenLotCarryingValueDoesNotMove()
    {
        using var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Scope, f.Request);
        Assert.True(preview.HasChanges);
        Assert.Equal(-1m, preview.EarnedDelta);
        Assert.Equal(1m, preview.Instruction.Lines.Single(l => l.AccountId == "Payable").Credit);
        var prepared = await f.Service.PrepareAsync(f.Scope, f.Request, preview.Fingerprint, "operator", f.At);
        Assert.Equal(AssetAccountingEventKindDto.Income, prepared.Projection.EventKind);
        Assert.Equal(1m, prepared.Projection.EventAmount);
        Assert.NotNull(prepared.Projection.BondCouponRecognition);
        await f.Ledger.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LaterAccrualUsesPostedCumulativeRecognitionInsteadOfPostingFullBalanceAgain()
    {
        using var f = new Fixture();
        var first = await f.Service.PreviewAsync(f.Scope, f.Request);
        f.Receipts.GetLatestCouponRecognitionAsync(f.Scope, f.Lot.TaxLotRecordId, Arg.Any<CancellationToken>())
            .Returns(new BondCouponRecognitionReceiptDto(first.Fingerprint, Guid.NewGuid(), f.At, first.Instruction));
        f.Earned = -1.5m;
        var next = await f.Service.PreviewAsync(f.Scope, f.Request with { RunId = "next", ThroughDate = f.Request.ThroughDate.AddDays(1) });
        Assert.Equal(-.5m, next.EarnedDelta);
        Assert.Equal(.5m, next.Instruction.Lines.Single(l => l.AccountId == "Payable").Credit);
        Assert.Equal(-1.5m, next.RemainingSignedBalance);
    }

    [Fact]
    public async Task PostedRunReplaysItsExactInputAndRejectsDifferentContent()
    {
        using var f = new Fixture();
        var first = await f.Service.PreviewAsync(f.Scope, f.Request);
        f.Receipts.GetCouponRecognitionRunAsync(f.Scope, f.Lot.TaxLotRecordId, f.Request.RunId, Arg.Any<CancellationToken>())
            .Returns(new BondCouponRecognitionReceiptDto(first.Fingerprint, Guid.NewGuid(), f.At, first.Instruction));
        Assert.Equal(first.Fingerprint, (await f.Service.PreviewAsync(f.Scope, f.Request)).Fingerprint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PreviewAsync(f.Scope,
            f.Request with { ThroughDate = f.Request.ThroughDate.AddDays(1) }));
    }

    [Fact]
    public async Task TamperedAccountsOrChangedPostedStateInvalidatePreparation()
    {
        using var f = new Fixture();
        var first = await f.Service.PreviewAsync(f.Scope, f.Request);
        var forged = first.Instruction with { Lines = first.Instruction.Lines.Select(l => l with { AccountId = "CallerCash" }).ToArray() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.VerifyAsync(forged));
        f.Earned = -2m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PrepareAsync(f.Scope, f.Request,
            first.Fingerprint, "operator", f.At));
    }

    [Fact]
    public async Task SignedSettlementRechecksCurrentSourceEvidenceAndCannotReuseReversedCash()
    {
        using var f = new Fixture();
        var request = f.AddObservedPayment();
        var preview = await f.Service.PreviewAsync(f.Scope, request);
        Assert.Equal(-1m, preview.SettledGross);
        Assert.Equal(0m, preview.RemainingSignedBalance);
        Assert.Equal(1.1m, preview.Instruction.Lines.Single(l => l.AccountId == "Cash").Credit);
        await f.Service.VerifyAsync(preview.Instruction);
        await f.EvidenceVerifier.Received(2).VerifyAsync(f.Scope, Arg.Any<IReadOnlyList<BondCouponRecognizedCashDto>>(), Arg.Any<CancellationToken>());
        f.Receipts.GetConsumedCouponCashAsync(f.Scope, "bank-stable", Arg.Any<CancellationToken>()).Returns(1m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PreviewAsync(f.Scope, request));
    }

    [Fact]
    public async Task DifferentSignedCouponsRemainDistinctAndOutgoingPaymentLeavesPriorReceivable()
    {
        using var f = new Fixture();
        f.Earned = 10m;
        f.Flows.Add(f.Flow(f.Request.ThroughDate.AddDays(-1), 10m));
        var first = await f.Service.PreviewAsync(f.Scope, f.Request);
        f.Record(first);
        f.Earned = 5m;
        var nextDate = f.Request.ThroughDate.AddDays(1);
        f.Flows.Add(f.Flow(nextDate, -5m));
        var request = f.AddObservedPayment(-2m, 0m, nextDate) with { RunId = "next", ThroughDate = nextDate };
        var second = await f.Service.PreviewAsync(f.Scope, request);
        Assert.Equal(-5m, second.EarnedDelta);
        Assert.Equal(10m, second.RemainingReceivable);
        Assert.Equal(3m, second.RemainingPayable);
        Assert.Equal(3m, second.Instruction.Lines.Single(l => l.AccountId == "Payable").Credit);
        Assert.DoesNotContain(second.Instruction.Lines, l => l.AccountId == "Receivable");
        Assert.Equal(2m, second.Instruction.Lines.Single(l => l.AccountId == "Cash").Credit);
        Assert.Equal(second.Instruction.Lines.Sum(l => l.Debit), second.Instruction.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PurchasedSellerAccruedClearsAlongsideEarnedIncomeWithoutRecognizingItAgain()
    {
        using var f = new Fixture(purchasedAccrued: 60m);
        f.Earned = 40m;
        f.Flows.Add(f.Flow(f.Request.ThroughDate, 100m));
        var preview = await f.Service.PreviewAsync(f.Scope, f.AddObservedPayment(100m, 0m));
        Assert.Equal(40m, preview.EarnedDelta);
        Assert.Equal(0m, preview.RemainingReceivable);
        Assert.Equal(40m, preview.Instruction.Lines.Single(l => l.AccountId == "Income").Credit);
        Assert.Equal(60m, preview.Instruction.Lines.Single(l => l.AccountId == "Receivable").Credit);
    }

    [Theory]
    [InlineData(1.235, 1.24)]
    [InlineData(-1.235, -1.24)]
    public async Task NativePaymentBoundaryUsesRetainedRoundedEntitlement(double raw, double payable)
    {
        using var f = new Fixture();
        f.Earned = (decimal)payable;
        f.Flows.Add(f.Flow(f.Request.ThroughDate, (decimal)raw));
        var request = f.AddObservedPayment((decimal)payable, 0m);
        var preview = await f.Service.PreviewAsync(f.Scope, request);
        Assert.Equal((decimal)payable, preview.EarnedDelta);
        Assert.Equal(0m, preview.RemainingSignedBalance);
        Assert.Equal((decimal)raw, preview.Instruction.IncomeInstruction.BondIncome!.CurrentProjection.CashFlows.Single().Interest);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task SubJournalPrecisionAccrualRetainsRawTargetsAndPostsCumulativeBookedDeltas(int sign)
    {
        using var f = new Fixture();
        f.Earned = sign * 1.234567890123m;
        var first = await f.Service.PreviewAsync(f.Scope, f.Request);
        Assert.Equal(sign * 1.2345678901m, first.EarnedDelta);
        Assert.Equal(sign * 1.234567890123m, first.Instruction.IncomeInstruction.BondIncome!.CurrentAccruedInterest);
        var targets = first.Instruction.BookedTargets!;
        Assert.Equal(BondIncomeBookedPrecision.PolicyVersion, targets.PolicyVersion);
        Assert.Equal(.000000000023m, sign > 0 ? targets.ReceivableRoundingResidual : targets.PayableRoundingResidual);
        Assert.All(first.Instruction.Lines, line =>
        {
            Assert.Equal(line.Debit, BondIncomeBookedPrecision.RoundTarget(line.Debit));
            Assert.Equal(line.Credit, BondIncomeBookedPrecision.RoundTarget(line.Credit));
        });
        f.Record(first);
        f.Earned = sign * 1.234567890151m;
        var next = await f.Service.PreviewAsync(f.Scope, f.Request with { RunId = "next-quantum", ThroughDate = f.Request.ThroughDate.AddDays(1) });
        Assert.Equal(sign * .0000000001m, next.EarnedDelta);
        Assert.Equal(sign * 1.2345678902m, first.EarnedDelta + next.EarnedDelta);
    }

    [Fact]
    public async Task LotCanClaimBoundedShareOfFullRetainedHolderAllocation()
    {
        using var f = new Fixture();
        f.Earned = 40m;
        f.Flows.Add(f.Flow(f.Request.ThroughDate, 40m));
        var full = f.AddObservedPayment(100m, 0m);
        var request = full with { CashAllocations = [full.CashAllocations[0] with { NetAmount = 40m, GrossAmount = 40m }] };
        var preview = await f.Service.PreviewAsync(f.Scope, request);
        Assert.Equal(0m, preview.RemainingReceivable);
        Assert.Equal(100m, preview.Instruction.Cash.Single().RetainedAllocation.GrossAmount);
        Assert.Equal(40m, preview.Instruction.Cash.Single().Allocation.GrossAmount);
        f.Receipts.GetConsumedCouponAllocationAsync(f.Scope, "allocation", Arg.Any<CancellationToken>()).Returns(new BondCouponAllocationConsumptionDto(70m, 70m, 0m, 0m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PreviewAsync(f.Scope, request));
    }

    [Fact]
    public async Task RevisedSettledCouponCannotHideItsSignFlipInsideOtherPositiveCoupons()
    {
        using var f = new Fixture();
        var firstDate = f.Request.ThroughDate.AddDays(-1);
        var nextDate = f.Request.ThroughDate.AddDays(1);
        f.Earned = 10m;
        f.Flows.Add(f.Flow(firstDate, 10m));
        var first = await f.Service.PreviewAsync(f.Scope, f.AddObservedPayment(5m, 0m, firstDate));
        f.Record(first);
        var settledId = first.Instruction.Cash.Single().RetainedAllocation.ExpectedFlowId;
        f.Receipts.GetConsumedCouponObligationsAsync(f.Scope, f.Lot.TaxLotRecordId, Arg.Any<CancellationToken>())
            .Returns(new[] { new BondCouponObligationConsumptionDto(settledId, 5m) });
        f.Flows.Clear();
        f.Flows.Add(f.Flow(firstDate, -5m));
        f.Flows.Add(f.Flow(nextDate, 20m));
        f.Earned = 15m;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PreviewAsync(f.Scope,
            f.Request with { RunId = "changed", ThroughDate = nextDate }));
        Assert.Contains("reversal/rebook", error.Message);
    }

    [Fact]
    public async Task ReversalUsesExactInverseAndAllowsCorrectedSameDateRebook()
    {
        using var f = new Fixture();
        var original = await f.Service.PreviewAsync(f.Scope, f.AddObservedPayment());
        var posted = f.Record(original);
        var reverseRequest = f.Request with { RunId = "reverse", ReversesReceiptId = posted.ReceiptId, CorrectionRationale = "Corrected coupon fixing" };
        var reverse = await f.Service.PreviewAsync(f.Scope, reverseRequest);
        Assert.Equal(original.Instruction.Lines.Select(l => (l.AccountId, l.Debit, l.Credit)),
            reverse.Instruction.Lines.Select(l => (l.AccountId, l.Credit, l.Debit)));
        Assert.Equal(0m, reverse.RemainingPayable);
        Assert.Equal(1m, reverse.SettledGross);
        var reversed = f.Record(reverse);
        f.Receipts.GetCouponRecognitionRunAsync(f.Scope, f.Lot.TaxLotRecordId, reverseRequest.RunId, Arg.Any<CancellationToken>()).Returns(reversed);
        Assert.Equal(reverse.Fingerprint, (await f.Service.PreviewAsync(f.Scope, reverseRequest)).Fingerprint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PreviewAsync(f.Scope,
            reverseRequest with { RunId = "double-reverse" }));
        f.Earned = -1.2m;
        f.Flows.Clear();
        var corrected = f.AddObservedPayment(-1.2m, 0m) with { RunId = "rebook" };
        var rebook = await f.Service.PreviewAsync(f.Scope, corrected);
        Assert.Equal(reversed.ReceiptId, rebook.Instruction.PreviousReceiptId);
        Assert.Equal(-1.2m, rebook.EarnedDelta);
        Assert.Equal(0m, rebook.RemainingPayable);
    }

    [Fact]
    public async Task ReversingLaterRecognitionThenEarlierRecognitionTraversesImmutableInverseNodes()
    {
        using var f = new Fixture();
        var a = f.Record(await f.Service.PreviewAsync(f.Scope, f.Request));
        f.Earned = -2m;
        var b = f.Record(await f.Service.PreviewAsync(f.Scope, f.Request with { RunId = "b", ThroughDate = f.Request.ThroughDate.AddDays(1) }));
        var reverseB = f.Record(await f.Service.PreviewAsync(f.Scope, f.Request with
        { RunId = "reverse-b", ReversesReceiptId = b.ReceiptId, CorrectionRationale = "Unwind later recognition" }));
        var reverseA = await f.Service.PreviewAsync(f.Scope, f.Request with
        { RunId = "reverse-a", ReversesReceiptId = a.ReceiptId, CorrectionRationale = "Correct original recognition" });
        Assert.Equal(reverseB.ReceiptId, reverseA.Instruction.PreviousReceiptId);
        Assert.Equal(0m, reverseA.Instruction.CumulativeEarned);
        Assert.Equal(1m, reverseA.EarnedDelta);
    }

    private sealed class Fixture : IDisposable
    {
        public IBondIncomeService Income { get; } = Substitute.For<IBondIncomeService>();
        public IBondCouponRecognitionStore Receipts { get; } = Substitute.For<IBondCouponRecognitionStore>();
        public ILedgerJournalStore Ledger { get; } = Substitute.For<ILedgerJournalStore>();
        public IInstrumentPositionProjectionStore Positions { get; } = Substitute.For<IInstrumentPositionProjectionStore>();
        public IAccountingPolicyService Policies { get; } = Substitute.For<IAccountingPolicyService>();
        public IAccountingConfigurationService Configuration { get; } = Substitute.For<IAccountingConfigurationService>();
        public IBondMonitoringService Monitoring { get; } = Substitute.For<IBondMonitoringService>();
        public IBondCouponRecognitionEvidenceVerifier EvidenceVerifier { get; } = Substitute.For<IBondCouponRecognitionEvidenceVerifier>();
        public DateTimeOffset At { get; } = new(2026, 1, 16, 0, 0, 0, TimeSpan.Zero);
        public AccountCashForecastScopeDto Scope { get; }
        public BondCouponRecognitionRequest Request { get; }
        public OpenLotDto Lot { get; }
        public List<BondWorkflowCashFlow> Flows { get; } = [];
        public decimal Earned { get; set; } = -1m;
        public BondCouponRecognitionService Service { get; }
        private readonly string _evidenceRoot = Path.Combine(Path.GetTempPath(), "meridian-coupon-test-" + Guid.NewGuid().ToString("N"));
        private readonly List<LedgerJournalEntryRecord> _history = [];
        private readonly Dictionary<string, BondCouponRecognitionReceiptDto> _posted = new(StringComparer.Ordinal);

        public Fixture(decimal purchasedAccrued = 0m)
        {
            var book = Guid.NewGuid();
            var account = Guid.NewGuid();
            var period = Guid.NewGuid();
            var position = Guid.NewGuid();
            var securityId = Guid.NewGuid();
            Scope = new("tenant", "company", account.ToString("D"), book.ToString("D"), "USD");
            Request = new("first", Guid.NewGuid(), period, new(2026, 1, 15), At, []);
            var acquired = new DateOnly(2025, 1, 1);
            var evidence = new RetainedEvidenceIdentityDto("synthetic", "urn:synthetic", new string('a', 64), "Synthetic", "synthetic",
                "Accepted", "reviewer", At.AddYears(-1), acquired, 1, At.AddYears(-1), "reviewer", "OpenLotAcquisition", Request.TaxLotRecordId.ToString("D"));
            var acq = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, 100m, 100m, acquired,
                new(100m, 1m, BondAmortizationMethod.NoAmortization, null), [evidence])
            { BondIncome = new(At.AddYears(-1), 100m, purchasedAccrued, 1m, [], [evidence]) };
            var birthId = Guid.NewGuid();
            var birthJournalId = Guid.NewGuid();
            var physical = new LedgerTaxLotRecord(Request.TaxLotRecordId, book,
                new("Holding", LedgerAccountType.Asset, FinancialAccountId: Scope.AccountId), "synthetic-lot", acquired,
                1m, 1m, 100m, "USD", At.AddYears(-1), At, birthJournalId, Version: 1,
                OriginatingMutationBatchId: birthId, LastMutationBatchId: birthId, SecurityId: securityId,
                BookPositionId: position, OriginalFace: 100m, BookedFactor: 1m, ParBasis: 100m, Acquisition: acq);
            Lot = physical.ToOpenLot();
            var sec = new SecurityProjectionRecord(securityId, "Bond", SecurityStatusDto.Active, "Synthetic", "USD", "ISIN", "SYNTHETIC",
                JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
                1, At.AddYears(-1), null, [], []);
            var termsEvidence = evidence with { SubjectType = "SecurityMasterProjection", SubjectId = securityId.ToString("D"), ContentHashSha256 = OpenLotAmortization.SecurityHash(sec) };
            var context = new AccountingBookContextDto(book, "org", account, FundStructureNodeKindDto.Account, "Synthetic", "USD",
                AccountingBasisKindDto.Gaap, "policy", "v1");
            var projection = new BondWorkflowResult(securityId, "USD", "bond-workflow-v1", new(At, Request.ThroughDate, At), 100m, 100m,
                -1m, null, null, 0m, null, null, null, null, null, null, null, [], [new("Terms", "synthetic")], [], []);
            var incomeInputs = new BondIncomeInstructionDto(book, "policy", "v1", new(BondIncomeCalculator.ModelVersion,
                BondIncomeFloaterPolicy.AcquisitionIndex, true, true, true), acquired, Request.ThroughDate, At,
                projection, projection, projection, [], [], [], purchasedAccrued, -1m, 1m, 1m, 100m, [evidence], account);
            var instruction = new OpenLotAmortizationInstructionDto(Lot, sec, termsEvidence, 1, Request.ThroughDate,
                BondIncomeCalculator.ModelVersion)
            { BondIncome = incomeInputs };
            Income.PreviewAsync(Arg.Any<BondIncomePreviewRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.ArgAt<BondIncomePreviewRequestDto>(0);
                var completed = Flows.Where(f => f.PaymentDate <= request.ThroughDate).ToArray();
                var couponAllocations = completed.Select(flow => BondIncomeCouponRounding.Allocate(flow.PaymentDate,
                    flow.AccrualStart, flow.AccrualEnd, "USD", [new(Lot.TaxLotRecordId, Lot.Version, 100m, flow.Interest, 0m)])).ToArray();
                var accrued = Earned + purchasedAccrued - couponAllocations.Sum(a => a.Lots.Single().CashAmount);
                var dynamicInstruction = instruction with
                {
                    AsOfDate = request.ThroughDate,
                    BondIncome = incomeInputs with
                    {
                        ThroughDate = request.ThroughDate,
                        CurrentAccruedInterest = accrued,
                        CouponCashAllocations = couponAllocations,
                        CurrentProjection = projection with
                        { CashFlows = Flows.ToArray(), AccruedInterest = accrued }
                    }
                };
                return new BondIncomePreviewDto(book,
                Lot.TaxLotRecordId, "income", dynamicInstruction, new(BondIncomeCalculator.ModelVersion, BondIncomeFloaterPolicy.AcquisitionIndex,
                    -.01m, 100m, 100m, Earned, 0m, 0m, 0m, 0m, 0m, 0m, 0m, -Earned, []),
                new(AssetLotMutationIntentDto.Amortize, Amortization: dynamicInstruction));
            });
            Ledger.GetTaxLotsByIdsAsync(book, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(new[] { physical });
            var birthDimensions = new LedgerLineDimensionSet(InstrumentId: securityId, TaxLotId: Lot.LotId,
                BookId: Scope.BookId, AccountId: Scope.AccountId)
            { PositionId = position };
            var birthLines = new List<LedgerEntry>
            {
                new(Guid.NewGuid(), birthJournalId, At.AddYears(-1), physical.Account, 100m, 0m, "Acquisition", birthDimensions),
                new(Guid.NewGuid(), birthJournalId, At.AddYears(-1), new("Cash", LedgerAccountType.Asset, FinancialAccountId: Scope.AccountId),
                    0m, 100m + purchasedAccrued, "Acquisition", birthDimensions)
            };
            if (purchasedAccrued > 0m)
                birthLines.Add(new(Guid.NewGuid(), birthJournalId, At.AddYears(-1),
                new("Receivable", LedgerAccountType.Asset, FinancialAccountId: Scope.AccountId), purchasedAccrued, 0m, "Acquisition", birthDimensions));
            var birthJournal = new LedgerJournalEntryRecord(new(birthJournalId, At.AddYears(-1), "Acquisition", birthLines),
                book, period, null, null, 1, At.AddYears(-1), AccountingBasisKindDto.Gaap);
            var mutation = new LedgerTaxLotMutationRecord(Guid.NewGuid(), birthId, AtomicTaxLotMutationKind.Acquisition,
                physical.TaxLotRecordId, physical.LotId, 0, 0m, 1m, 1m, 100m, 100m, 0, 1, "synthetic", birthJournalId,
                Guid.NewGuid(), [evidence], At.AddYears(-1), null, physical, SecurityId: securityId, BookPositionId: position);
            Ledger.GetAtomicTaxLotPostingAsync(birthId, Arg.Any<CancellationToken>()).Returns(new AtomicTaxLotJournalResult(
                birthId, AtomicTaxLotMutationKind.Acquisition, "synthetic", false, birthJournal, [physical], [mutation], [evidence]));
            _history.Add(birthJournal);
            Ledger.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var query = call.ArgAt<LedgerJournalEntryQuery>(0);
                return _history.Where(row => query.JournalEntryId is null || row.Entry.JournalEntryId == query.JournalEntryId).ToArray();
            });
            Receipts.GetCouponRecognitionReceiptAsync(Scope, Lot.TaxLotRecordId, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => _posted.GetValueOrDefault(call.ArgAt<string>(2)));
            Ledger.GetLedgerBookAsync(book, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(book, "org", account,
                FundStructureNodeKindDto.Account, "Synthetic", "USD", At.AddYears(-1), At, AccountingBasis: AccountingBasisKindDto.Gaap,
                AccountingPolicyId: "policy", AccountingPolicyVersion: "v1"));
            Ledger.GetPeriodAsync(period, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(period, book, 2026, 1, "January",
                new(2026, 1, 1), new(2026, 1, 31), "Open", At.AddDays(-15), null, 1));
            Positions.GetBookPositionAsync(position, Arg.Any<CancellationToken>()).Returns(new BookPositionDto(position, securityId,
                Guid.NewGuid(), context, "Long", "Active", acquired));
            var adopted = JsonSerializer.Serialize(new
            {
                bondCouponRecognition = new
                { modelVersion = BondCouponRecognitionCalculator.ModelVersion, accounts = new BondCouponBookAccountsDto("Receivable", "Payable", "Income", "Cash", "Tax", "Fees") }
            });
            Policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(new AccountingPolicyDto("policy",
                AccountingBasisKindDto.Gaap, "v1", "Synthetic", acquired, null, false, adopted, At));
            Configuration.GetWorkspaceAsync("org", book, Arg.Any<CancellationToken>(), "tenant", "company").Returns(
                new AccountingConfigurationWorkspaceDto("org", book, default, "v1", At, [],
                    [new("r", "Receivable", "Receivable", "Asset"), new("p", "Payable", "Payable", "Liability"),
                     new("i", "Income", "Income", "Revenue"), new("c", "Cash", "Cash", "Asset"),
                     new("t", "Tax", "Tax", "Asset"), new("f", "Fees", "Fees", "Expense")], [], [], [], []));
            Monitoring.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns(Array.Empty<BondMonitoringSnapshotDto>());
            Service = new(Income, Receipts, Ledger, Positions, Policies, Configuration, Monitoring, EvidenceVerifier,
                new FileBondIncomePreparationEvidenceStore(_evidenceRoot));
        }

        public BondWorkflowCashFlow Flow(DateOnly due, decimal interest) => new(due, due.AddMonths(-6), due, interest, 0m, 100m, 1m, false, []);

        public BondCouponRecognitionReceiptDto Record(BondCouponRecognitionPreviewDto preview)
        {
            var journalId = Guid.NewGuid();
            var instruction = preview.Instruction;
            var lines = instruction.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), journalId, At,
                new(line.AccountId, Enum.Parse<LedgerAccountType>(instruction.Accounts.Single(a => a.Path == line.AccountId).AccountType),
                    FinancialAccountId: Scope.AccountId), line.Debit, line.Credit, "Coupon",
                new LedgerLineDimensionSet(BookId: line.Dimensions?.BookId, AccountId: line.Dimensions?.AccountId,
                    InstrumentId: line.Dimensions?.InstrumentId, TaxLotId: line.Dimensions?.TaxLotId)
                { PositionId = line.Dimensions?.PositionId })).ToArray();
            _history.Add(new(new(journalId, At, "Coupon", lines, new(EffectiveDate: instruction.Request.ThroughDate)),
                Lot.LedgerBookId, instruction.Request.PeriodId, null, null, _history.Count + 1, At,
                AccountingBasisKindDto.Gaap, SourceEventId: instruction.EventId));
            var receipt = new BondCouponRecognitionReceiptDto(preview.Fingerprint, journalId, At, instruction);
            _posted[receipt.ReceiptId] = receipt;
            Receipts.GetLatestCouponRecognitionAsync(Scope, Lot.TaxLotRecordId, Arg.Any<CancellationToken>()).Returns(receipt);
            return receipt;
        }

        public BondCouponRecognitionRequest AddObservedPayment(decimal gross = -1m, decimal fees = .1m, DateOnly? due = null)
        {
            var date = due ?? Request.ThroughDate;
            if (!Flows.Any(f => f.PaymentDate == date && Math.Sign(f.Interest) == Math.Sign(gross)))
                Flows.Add(Flow(date, gross));
            var evidence = new RetainedEvidenceIdentityDto("bank", "urn:synthetic:bank", new string('b', 64), "Synthetic", "bank",
                "Accepted", "reviewer", At, Request.ThroughDate, 1, At, "reviewer", "AccountCashFlow", "bank-stable");
            var provenance = new AccountCashProvenanceDto("Synthetic", "bank", evidence.ContentHashSha256, AccountCashEvidenceRole.Observation, evidence);
            var deduction = provenance with
            {
                SourceEntityId = "deduction",
                Evidence = evidence with
                { EvidenceId = "deduction", SourceReference = "deduction", SubjectType = "AccountCashAllocation", SubjectId = "allocation" }
            };
            var payment = new AccountCashFlowDto("bank-stable", "bank-v1", Scope.AccountId, Scope.BookId, AccountCashFlowStatus.Actual,
                "Coupon", gross - fees, "USD", date, date, date, provenance, Lot.SecurityId);
            var obligation = Flows.Single(f => f.PaymentDate == date && Math.Sign(f.Interest) == Math.Sign(gross));
            var obligationId = AccountBondIncomePlanningService.ObligationIdentity(Scope, Lot.SecurityId, "Coupon", date, obligation.AccrualStart);
            var allocation = new AccountCashPaymentAllocationDto("allocation", obligationId, "bank-v1", gross - fees, gross, Fees: fees, DeductionEvidence: deduction);
            var capture = new BondMonitoringCaptureRequest("monitor", Scope, At, [], [payment], [allocation], [], []);
            Monitoring.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns(new[]
                { new BondMonitoringSnapshotDto("synthetic", "bond-monitoring-v1", capture, [], [], []) });
            return Request with { CashAllocations = [new("allocation", "bank-v1", gross - fees, gross, Fees: fees, DeductionEvidence: deduction)] };
        }

        public void Dispose() { if (Directory.Exists(_evidenceRoot)) Directory.Delete(_evidenceRoot, true); }
    }
}
