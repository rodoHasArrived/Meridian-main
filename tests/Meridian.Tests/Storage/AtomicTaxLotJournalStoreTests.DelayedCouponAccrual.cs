using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.FixedIncome;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DelayedCouponAccrual_V2PositiveRawEarnedCouponRoundsOnlyAtPayment_AndInverseRebooks()
        => await DelayedCouponPersistenceAsync(1);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DelayedCouponAccrual_V2NegativeRawEarnedCouponRoundsOnlyAtPayment_AndInverseRebooks()
        => await DelayedCouponPersistenceAsync(-1);

    private static async Task DelayedCouponPersistenceAsync(int sign)
    {
        await using var fixture = await DelayedCouponFixture.CreateAsync(sign);
        var originalLot = fixture.Physical.Lot;
        originalLot.Acquisition.Should().NotBeNull("the actual acquisition must retain the adopted accounting facts");
        originalLot.Acquisition!.BondIncome!.AccrualPolicyVersion.Should().Be(BondIncomeCalculator.SeparatedAccrualPolicyVersion);
        var lagRequest = fixture.Request("earned-unpaid-lag", DelayedCouponFixture.AccrualEnd.AddDays(1));
        var lag = await fixture.Service.PreviewAsync(fixture.Scope, lagRequest);
        var lagEarned = BondIncomeBookedPrecision.RoundTarget(fixture.RawEarned(lagRequest.ThroughDate));
        lag.EarnedDelta.Should().Be(lagEarned);
        lag.Instruction.Cash.Should().BeEmpty();
        lag.Instruction.CumulativeEarned.Should().Be(lagEarned);
        var lagIncome = lag.Instruction.IncomeInstruction.BondIncome!;
        lagIncome.TerminalEntitlement.Should().BeNull("contractual principal remains due in the following period");
        lagIncome.CurrentAccruedInterest.Should().Be(fixture.ActiveAccrued(lagRequest.ThroughDate));
        lagIncome.CurrentProjection.DeferredCouponInterest.Should().Be(sign * 1.235m);
        lagIncome.CurrentProjection.ContractualAccruedInterest.Should().Be(fixture.ActiveAccrued(lagRequest.ThroughDate));
        var lagResult = BondIncomeCalculator.Project(originalLot.ToOpenLot(), lagIncome);
        lagResult.CouponIncome.Should().Be(fixture.RawEarned(lagRequest.ThroughDate));
        lagResult.PremiumDiscountIncome.Should().BeApproximately(0m, .00000000001m);
        lagResult.CouponCashRoundingAdjustment.Should().Be(0m);
        lagResult.ClosingCarryingBasis.Should().BeApproximately(originalLot.Acquisition.TransactionCostBasis, .00000000001m);
        lagResult.BookedTarget!.BookedTransactionTarget.Should().Be(originalLot.Acquisition.TransactionCostBasis);
        await fixture.PostAsync(lag.Instruction);
        var lagReceipt = (await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, originalLot.TaxLotRecordId))!;
        var rawTargets = lagReceipt.Instruction.BookedTargets!;
        (rawTargets.RawReceivableEarned - rawTargets.RawPayableEarned).Should().Be(fixture.RawEarned(lagRequest.ThroughDate));
        (rawTargets.BookedReceivableEarned - rawTargets.BookedPayableEarned).Should().Be(lagEarned);
        fixture.Store = fixture.Restart();

        var paymentRequest = fixture.ObserveCash("actual-payment");
        var payment = await fixture.Service.PreviewAsync(fixture.Scope, paymentRequest);
        payment.Instruction.TerminalCashAuthority.Should().BeNull();
        var paidEarned = BondIncomeBookedPrecision.RoundTarget(fixture.RawEarned(paymentRequest.ThroughDate));
        var activeAccrualDelta = BondIncomeBookedPrecision.RoundTarget(sign * 1.235m
            + fixture.ActiveAccrued(paymentRequest.ThroughDate)) - lagEarned;
        payment.EarnedDelta.Should().Be(paidEarned - lagEarned);
        (payment.EarnedDelta - activeAccrualDelta).Should().Be(sign * .005m,
            "native cash rounding is an explicit signed income delta, separate from the next period's accrual");
        payment.SettledGross.Should().Be(sign * 1.24m);
        payment.RemainingSignedBalance.Should().Be(paidEarned - sign * 1.24m);
        payment.Instruction.CumulativeEarned.Should().Be(paidEarned);
        payment.Instruction.IncomeInstruction.BondIncome!.CurrentProjection.DeferredCouponInterest.Should().Be(0m);
        var paidResult = BondIncomeCalculator.Project(originalLot.ToOpenLot(), payment.Instruction.IncomeInstruction.BondIncome!);
        paidResult.CouponIncome.Should().Be(fixture.RawEarned(paymentRequest.ThroughDate));
        paidResult.PremiumDiscountIncome.Should().BeApproximately(0m, .00000000001m);
        paidResult.CouponCashRoundingAdjustment.Should().Be(sign * .005m);
        paidResult.ClosingCarryingBasis.Should().BeApproximately(lagResult.ClosingCarryingBasis, .00000000001m);
        paidResult.BookedTarget!.BookedTransactionTarget.Should().Be(originalLot.Acquisition.TransactionCostBasis);
        var write = await fixture.PostAsync(payment.Instruction);
        var paidReceipt = (await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, originalLot.TaxLotRecordId))!;
        await fixture.AssertBalancesAsync(paidEarned, paidEarned - sign * 1.24m, sign * 1.24m);
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "lag-bank-coupon")).Should().Be(sign * 1.24m);
        using (var target = new DurableLedgerPostingTarget(fixture.Store))
            (await target.PostAsync(write)).WasAppended.Should().BeFalse();
        (await fixture.Service.PreviewAsync(fixture.Scope, paymentRequest)).Fingerprint.Should().Be(paidReceipt.ReceiptId);

        var inverse = await fixture.Service.PreviewAsync(fixture.Scope, paymentRequest with
        {
            RunId = "inverse-payment",
            KnownAt = DateTimeOffset.UtcNow,
            CashAllocations = [],
            ReversesReceiptId = paidReceipt.ReceiptId,
            CorrectionRationale = "Correct the retained synthetic payment allocation"
        });
        inverse.Instruction.Lines.Select(line => (line.AccountId, line.Debit, line.Credit)).Should()
            .Equal(paidReceipt.Instruction.Lines.Select(line => (line.AccountId, line.Credit, line.Debit)));
        await fixture.PostAsync(inverse.Instruction);
        await fixture.AssertBalancesAsync(lagEarned, lagEarned, 0m);
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "lag-bank-coupon")).Should().Be(0m);
        var rebook = await fixture.Service.PreviewAsync(fixture.Scope, paymentRequest with
        { RunId = "rebook-payment", KnownAt = DateTimeOffset.UtcNow });
        rebook.EarnedDelta.Should().Be(payment.EarnedDelta);
        await fixture.PostAsync(rebook.Instruction);
        await fixture.AssertBalancesAsync(paidEarned, paidEarned - sign * 1.24m, sign * 1.24m);
        AssertRetainedJsonUnchanged(await fixture.Store.GetCouponRecognitionReceiptAsync(fixture.Scope,
            originalLot.TaxLotRecordId, lagReceipt.ReceiptId), lagReceipt, "the raw deferred earned receipt remains immutable");
        AssertRetainedJsonUnchanged((await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId,
            [originalLot.TaxLotRecordId])).Single(), originalLot, "coupon rounding, clearing and inverse never mutate economic carrying basis");
    }

    private sealed class DelayedCouponFixture : IAsyncDisposable
    {
        private static readonly BondCouponBookAccountsDto Paths = new("LagReceivable", "LagPayable", "LagIncome", "LagCash", "LagTax", "LagFees");
        private static readonly BondCouponRecognitionAccountDto[] Accounts =
        [new(Paths.Receivable, Paths.Receivable, "Asset"), new(Paths.Payable, Paths.Payable, "Liability"),
            new(Paths.Income, Paths.Income, "Revenue"), new(Paths.Cash, Paths.Cash, "Asset"),
            new(Paths.Tax, Paths.Tax, "Asset"), new(Paths.Fees, Paths.Fees, "Expense")];
        public AmortFixture Physical { get; }
        public PostgresLedgerJournalStore Store { get; set; }
        public BondCouponRecognitionService Service { get; private set; } = null!;
        public AccountCashForecastScopeDto Scope { get; }
        private readonly int _sign;
        private readonly IBondMonitoringService _monitoring = Substitute.For<IBondMonitoringService>();
        private readonly IBondCouponRecognitionService _sourceReview = Substitute.For<IBondCouponRecognitionService>();
        private readonly IAccountingPolicyService _policies = Substitute.For<IAccountingPolicyService>();
        private readonly IAccountingConfigurationService _configuration = Substitute.For<IAccountingConfigurationService>();
        private readonly IBondCouponRecognitionEvidenceVerifier _cashReview = Substitute.For<IBondCouponRecognitionEvidenceVerifier>();
        private readonly List<string> _postedRuns = [];
        private long _positionVersion;
        public static DateOnly AccrualEnd => AmortAcquired.AddYears(1);
        private static DateOnly PaymentDate => AccrualEnd.AddDays(2);

        private DelayedCouponFixture(AmortFixture physical, int sign)
        {
            Physical = physical;
            _sign = sign;
            Scope = new("lag-tenant", "lag-company", physical.IncomeAccountId.ToString("D"), physical.BookId.ToString("D"), "USD");
            _sourceReview.VerifyPreparedSourceAsync(Arg.Any<BondCouponRecognitionInstructionDto>(),
                Arg.Any<RetainedEvidenceIdentityDto>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Store = Restart();
        }

        public static async Task<DelayedCouponFixture> CreateAsync(int sign)
        {
            var physical = await AmortFixture.CreateAsync(false, AccountingBasisKindDto.Gaap, factorPool: true,
                incomeIndexRatio: 1.2m, incomeAccrualPolicyVersion: BondIncomeCalculator.SeparatedAccrualPolicyVersion);
            var fixture = new DelayedCouponFixture(physical, sign);
            fixture._positionVersion = (await physical.Positions.GetBookPositionAsync(physical.Lot.BookPositionId))!.Version;
            var now = DateTimeOffset.UtcNow;
            physical.IncomeAuthority!.PreviewAsync(Arg.Any<BondIncomePreviewRequestDto>(), Arg.Any<CancellationToken>())
                .Returns(call => fixture.Calculate(call.ArgAt<BondIncomePreviewRequestDto>(0)));
            var policy = fixture.Policy;
            _ = BondIncomeCalculator.ReadPolicy(new("amort-policy", AccountingBasisKindDto.Gaap, "1", "Synthetic lag policy",
                AmortAcquired, null, false, JsonSerializer.Serialize(new { bondIncome = policy }), now));
            fixture._policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(
                new AccountingPolicyDto("amort-policy", AccountingBasisKindDto.Gaap, "1", "Synthetic lag policy", AmortAcquired,
                    null, false, JsonSerializer.Serialize(new
                    {
                        bondIncome = policy,
                        bondCouponRecognition = new { modelVersion = BondCouponRecognitionCalculator.ModelVersion, accounts = Paths }
                    }), now));
            fixture._configuration.GetWorkspaceAsync("amort-fund", physical.BookId, Arg.Any<CancellationToken>(), fixture.Scope.TenantId, fixture.Scope.CompanyId)
                .Returns(new AccountingConfigurationWorkspaceDto("amort-fund", physical.BookId, default, "1", now, [],
                    Accounts.Select((account, i) => new ChartOfAccountsNodeDto(i.ToString(), account.Path, account.LedgerName, account.AccountType)).ToArray(), [], [], [], []));
            fixture._monitoring.GetHistoryAsync(fixture.Scope, Arg.Any<CancellationToken>()).Returns(Array.Empty<BondMonitoringSnapshotDto>());
            fixture.BindService();
            return fixture;
        }

        private BondIncomePolicyDto Policy => new(BondIncomeCalculator.ModelVersion, BondIncomeFloaterPolicy.AcquisitionIndex,
            true, true, true)
        { AccrualPolicyVersion = BondIncomeCalculator.SeparatedAccrualPolicyVersion };

        public PostgresLedgerJournalStore Restart()
        {
            var store = new PostgresLedgerJournalStore(Physical.Options, backfillSecurityMaster: () => Physical.Securities,
                backfillPositions: () => Physical.Positions, bondIncomeAuthority: () => Physical.IncomeAuthority,
                couponRecognitionAuthority: () => _sourceReview,
                postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);
            Store = store;
            if (Service is not null)
                BindService();
            return store;
        }

        private void BindService() => Service = new(Physical.IncomeAuthority!, Store, Store, Physical.Positions,
            _policies, _configuration, _monitoring, _cashReview);

        private BondIncomePreviewDto Calculate(BondIncomePreviewRequestDto request)
        {
            var lot = Physical.Lot.ToOpenLot();
            var maturity = AccrualEnd.AddYears(1);
            var coupon = new BondWorkflowCashFlow(PaymentDate, AmortAcquired, AccrualEnd, _sign * 1.235m,
                0m, lot.Acquisition.TransactionCostBasis, 1.2m, false, [])
            { PeriodUnits = 1m, AccrualFraction = 1m };
            var nextCoupon = new BondWorkflowCashFlow(maturity.AddDays(2), AccrualEnd, maturity, _sign * 1.235m,
                lot.Acquisition.TransactionCostBasis, 0m, 1.2m, false, [])
            { PeriodUnits = 1m, AccrualFraction = 1m };
            var flows = new[] { coupon, nextCoupon };
            BondWorkflowResult Projection(DateOnly date) => new(lot.SecurityId, "USD", BondIncomeCalculator.SeparatedAccrualWorkflowModelVersion,
                new(request.KnownAt, date, request.KnownAt), 1_000_000m, 950_000m, 0m, null, null, 0m, null,
                null, null, null, null, null, null, flows,
                [new("SecurityTerms", lot.SecurityId.ToString("D"), Physical.Security.Version, new string('a', 64))], [], [])
            {
                AccruedInterest = ActiveAccrued(date) + (date >= AccrualEnd && date < PaymentDate ? _sign * 1.235m : 0m),
                ContractualAccruedInterest = ActiveAccrued(date),
                DeferredCouponInterest = date >= AccrualEnd && date < PaymentDate ? _sign * 1.235m : 0m,
                SettlementAccrualFraction = date > AccrualEnd ? (date.DayNumber - AccrualEnd.DayNumber) / 365m : 0m,
                SettlementPeriodAccrualFraction = 1m,
                SettlementAccrualPeriodUnits = date > AccrualEnd ? (date.DayNumber - AccrualEnd.DayNumber) / 365m : 0m,
                SettlementPeriodUnits = 1m
            };
            var allocation = BondIncomeCouponRounding.Allocate(PaymentDate, AmortAcquired, AccrualEnd, "USD",
                [new(lot.TaxLotRecordId, lot.Version, lot.OriginalQuantity, coupon.Interest, 0m)]);
            var acquisition = Projection(AmortAcquired);
            var current = Projection(request.ThroughDate);
            var instruction = new BondIncomeInstructionDto(Physical.BookId, "amort-policy", "1", Policy,
                AmortAcquired, request.ThroughDate, request.KnownAt, acquisition, current, current, [], [], [],
                0m, ActiveAccrued(request.ThroughDate), 1.2m, 1.2m, lot.OriginalQuantity, lot.Acquisition.BondIncome!.Evidence, Physical.IncomeAccountId)
            { CouponCashAllocations = request.ThroughDate >= PaymentDate ? [allocation] : [] };
            var result = BondIncomeCalculator.Project(lot, instruction);
            instruction = instruction with { BookedTarget = result.BookedTarget, AccountingYieldCertificate = result.AccountingYieldCertificate };
            var inputs = new OpenLotAmortizationInstructionDto(lot, Physical.Security, AmortSecurityEvidence(Physical.Security),
                _positionVersion,
                request.ThroughDate, BondIncomeCalculator.ModelVersion)
            { BondIncome = instruction };
            return new(Physical.BookId, lot.TaxLotRecordId, "retained-lag-inputs", inputs, result,
                new(AssetLotMutationIntentDto.Amortize, Amortization: inputs));
        }

        public BondCouponRecognitionRequest Request(string runId, DateOnly through)
            => new(runId, Physical.Lot.TaxLotRecordId, Physical.Period.PeriodId, through, DateTimeOffset.UtcNow, []);

        public decimal ActiveAccrued(DateOnly date) => date <= AccrualEnd ? 0m : _sign * 1.235m * (date.DayNumber - AccrualEnd.DayNumber) / 365m;
        public decimal RawEarned(DateOnly date) => decimal.Round((date < PaymentDate ? _sign * 1.235m : _sign * 1.24m)
            + ActiveAccrued(date), 12, MidpointRounding.ToEven);

        public BondCouponRecognitionRequest ObserveCash(string runId)
        {
            var now = DateTimeOffset.UtcNow;
            var evidence = BuildEvidence("lag-bank-coupon", 'b') with
            {
                EffectiveDate = PaymentDate,
                SubjectType = "AccountCashFlow",
                SubjectId = "lag-bank-coupon",
                ReviewedAtUtc = now.AddSeconds(-2),
                RetainedAtUtc = now.AddSeconds(-1)
            };
            var provenance = new AccountCashProvenanceDto("Synthetic", "lag-bank-coupon", evidence.ContentHashSha256,
                AccountCashEvidenceRole.Observation, evidence);
            var deductions = provenance with
            {
                SourceEntityId = "lag-allocation",
                Evidence = evidence with
                { EvidenceId = "lag-deductions", SubjectType = "AccountCashAllocation", SubjectId = "lag-allocation" }
            };
            var amount = _sign * 1.24m;
            var payment = new AccountCashFlowDto("lag-bank-coupon", "lag-bank-v1", Scope.AccountId, Scope.BookId,
                AccountCashFlowStatus.Actual, "Coupon", amount, "USD", PaymentDate, PaymentDate, PaymentDate, provenance, Physical.Security.SecurityId);
            var obligation = AccountBondIncomePlanningService.ObligationIdentity(Scope, Physical.Security.SecurityId, "Coupon", PaymentDate, AmortAcquired);
            var allocation = new AccountCashPaymentAllocationDto("lag-allocation", obligation, payment.VersionId, amount, amount, DeductionEvidence: deductions);
            var capture = new BondMonitoringCaptureRequest("lag-monitor", Scope, now, [], [payment], [allocation], [], []);
            _monitoring.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns(
                new[] { new BondMonitoringSnapshotDto("synthetic", "bond-monitoring-v1", capture, [], [], []) });
            return Request(runId, PaymentDate) with
            { CashAllocations = [new(allocation.AllocationId, payment.VersionId, amount, amount, DeductionEvidence: deductions)] };
        }

        public async Task<LedgerJournalEntryWrite> PostAsync(BondCouponRecognitionInstructionDto instruction)
        {
            var lot = instruction.IncomeInstruction.ExpectedLot;
            var date = instruction.Request.ThroughDate;
            var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero);
            var hash = BondCouponRecognitionCalculator.Fingerprint(instruction);
            var source = BuildEvidence("lag-coupon-" + instruction.Request.RunId, 'd') with
            {
                EffectiveDate = date,
                SubjectType = AssetAccountingEvidenceSubjects.Event,
                SubjectId = instruction.EventId.ToString("D"),
                ContentHashSha256 = hash,
                ReviewedAtUtc = at.AddMinutes(-10),
                RetainedAtUtc = at.AddMinutes(-5)
            };
            var retained = lot.Acquisition.Evidence.Append(source).Distinct().ToArray();
            var economic = new EconomicEventReferenceDto(instruction.EventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Income),
                1, date, source.RetainedAtUtc, "BondCouponRecognition", source.SourceReference, SourceContentHash: hash)
            { SecurityId = lot.SecurityId, BookPositionId = lot.BookPositionId, RetainedEvidence = retained };
            var lineage = new ProjectionLineageDto(instruction.EventId, instruction.EventId, BondCouponRecognitionCalculator.ModelVersion,
                BondCouponRecognitionCalculator.ModelVersion, BondCouponRecognitionCalculator.ModelVersion, "ObservedCouponRecognition",
                date, at, "BondCouponRecognition", source.SourceReference, economic)
            { BookPositionId = lot.BookPositionId, RetainedEvidence = retained };
            var position = (await Physical.Positions.GetBookPositionAsync(lot.BookPositionId))!;
            var inverse = instruction.ReversalTarget;
            var posting = new AccountingPostingCommandDto(Guid.NewGuid(), Physical.BookId, Physical.Period.PeriodId, date, at,
                "lag-coupon:" + instruction.Request.RunId, inverse is null ? AccountingPostingIntentDto.Originating : AccountingPostingIntentDto.Adjustment,
                instruction.EventId, SourceJournalEntryId: inverse?.JournalEntryId, ExpectedVersion: Physical.Period.Version,
                SourceEventType: economic.EventType, ApprovalState: AccountingPostingApprovalStateDto.Approved,
                ApprovalId: "independent-lag-coupon-review", Evidence: retained.Select(LagPostingEvidence).ToArray(), LedgerBookId: Physical.BookId)
            {
                Actor = "lag-coupon-controller",
                BookContext = position.BookContext with { PeriodId = Physical.Period.PeriodId },
                BookPositionId = lot.BookPositionId,
                EconomicEvent = economic,
                ProjectionLineage = lineage,
                RulePackReference = new("amort-policy", "1"),
                BondCouponRecognition = instruction
            };
            var id = Guid.NewGuid();
            const string description = "Delayed coupon income/clearing";
            var dimensions = new LedgerLineDimensionSet(BookId: Scope.BookId, AccountId: Scope.AccountId, InstrumentId: lot.SecurityId,
                TaxLotId: lot.LotId)
            { PositionId = lot.BookPositionId };
            var lines = instruction.Lines.Select(line =>
            {
                var account = instruction.Accounts.Single(account => account.Path == line.AccountId);
                return new LedgerEntry(Guid.NewGuid(), id, at, new(account.LedgerName, Enum.Parse<LedgerAccountType>(account.AccountType),
                    FinancialAccountId: Scope.AccountId), line.Debit, line.Credit, description, dimensions,
                    new("USD", "USD", line.Debit, line.Credit, 1m));
            }).ToArray();
            var write = new LedgerJournalEntryWrite(new(id, at, description, lines,
                new(SecurityId: lot.SecurityId, EffectiveDate: date, IdempotencyKey: posting.IdempotencyKey,
                    Tags: SecurityMasterLineageTags(lot.SecurityId))), Physical.BookId, Physical.Period.PeriodId,
                SourceEventId: instruction.EventId, SourceJournalEntryId: inverse?.JournalEntryId,
                AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1",
                PostingKind: inverse is null ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment,
                LedgerBookId: Physical.BookId, PostingCommand: posting);
            await Store.AppendAsync(write);
            _postedRuns.Add(instruction.Request.RunId);
            return write;
        }

        public async Task AssertBalancesAsync(decimal income, decimal signedOutstanding, decimal cash)
        {
            var journalIds = new HashSet<Guid>();
            foreach (var run in _postedRuns)
            {
                var receipt = await Store.GetCouponRecognitionRunAsync(Scope, Physical.Lot.TaxLotRecordId, run);
                receipt.Should().NotBeNull("each balance contribution must have a durable native coupon receipt");
                journalIds.Add(receipt!.JournalEntryId).Should().BeTrue("each native run appends its own immutable journal");
            }
            var journals = (await Store.GetByPeriodAsync(Physical.Period.PeriodId))
                .Where(row => journalIds.Contains(row.Entry.JournalEntryId)).ToArray();
            journals.Should().HaveCount(_postedRuns.Count, "durable coupon receipts must resolve to actual ledger journals");
            var entries = journals.SelectMany(row => row.Entry.Lines).ToArray();
            entries.Where(line => line.Account.Name == Paths.Income).Sum(line => line.Credit - line.Debit).Should().Be(income);
            entries.Where(line => line.Account.Name == Paths.Receivable || line.Account.Name == Paths.Payable)
                .Sum(line => line.Debit - line.Credit).Should().Be(signedOutstanding);
            entries.Where(line => line.Account.Name == Paths.Cash).Sum(line => line.Debit - line.Credit).Should().Be(cash);
        }

        private static AccountingPostingEvidenceReferenceDto LagPostingEvidence(RetainedEvidenceIdentityDto item)
            => new(item.EvidenceId, item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference, Reviewer: item.ReviewedBy,
                ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate, EvidenceVersion: item.EvidenceVersion,
                ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType);

        public ValueTask DisposeAsync() => Physical.DisposeAsync();
    }
}
