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
    public async Task TerminalPrincipal_PositiveEarnedCouponClearsAfterPhysicalClosure_AndCashInverseRebooks()
        => await TerminalCouponPersistenceAsync(1, deferred: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task TerminalPrincipal_DeferredNegativeEarnedCouponClearsAfterPhysicalClosure_AndCashInverseRebooks()
        => await TerminalCouponPersistenceAsync(-1, deferred: true);

    private static async Task TerminalCouponPersistenceAsync(int sign, bool deferred)
    {
        await using var fixture = await TerminalCouponFixture.CreateAsync(sign, deferred);
        var earned = await fixture.RecognizeAsync("earned-before-principal");
        earned.Instruction.Cash.Should().BeEmpty();
        earned.Instruction.CumulativeEarned.Should().Be(sign * 1.24m);
        var frozenIncome = earned.Instruction.IncomeInstruction.BondIncome!;
        frozenIncome.CurrentProjection.CashFlows
            .Single(flow => flow.Interest != 0m).Interest.Should().Be(sign * 1.235m);
        frozenIncome.CouponCashAllocations!.Single().Lots.Single()
            .CashAmount.Should().Be(sign * 1.24m);

        var principal = fixture.BindPrincipal(earned);
        var closed = await fixture.Physical.Store.AppendAssetPostingAsync(principal);
        var zero = closed.MutatedLots.Single();
        zero.OpenQuantity.Should().Be(0m);
        zero.CurrentFactor.Should().Be(0m);
        zero.ToOpenLot().OpenTransactionCostBasis.Should().Be(0m);
        closed.Journal.Entry.Lines.Should().HaveCount(2);
        var postedPrincipal = closed.FactorPaydown!;
        postedPrincipal.PrincipalPaydown.Should().Be(1_140_000m);
        postedPrincipal.ScheduledPrincipal!.TerminalCouponReceipts.Should().ContainSingle()
            .Which.ReceiptId.Should().Be(earned.ReceiptId);
        (await fixture.Physical.Restart().AppendAssetPostingAsync(principal)).IsExactReplay.Should().BeTrue();

        fixture.Physical.IncomeAuthority!.ClearReceivedCalls();
        var cashRequest = fixture.ObserveCash("cash-after-principal");
        var preview = await fixture.Service.PreviewAsync(fixture.Scope, cashRequest);
        preview.EarnedDelta.Should().Be(0m);
        preview.RemainingSignedBalance.Should().Be(0m);
        var terminal = preview.Instruction.TerminalCashAuthority!;
        terminal.Mode.Should().Be("CashOnly");
        terminal.PrincipalMutationBatchId.Should().Be(principal.MutationBatchId);
        terminal.EarnedReceipt.Should().BeEquivalentTo(earned);
        preview.Instruction.IncomeInstruction.ExpectedLot.OpenQuantity.Should().Be(0m);
        preview.Instruction.Lines.Should().NotContain(line => line.AccountId == "TerminalIncome");
        await fixture.Physical.IncomeAuthority!.DidNotReceive().PreviewAsync(Arg.Any<BondIncomePreviewRequestDto>(), Arg.Any<CancellationToken>());
        var cashWrite = await fixture.PostAsync(preview.Instruction);
        var cashReceipt = (await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, zero.TaxLotRecordId))!;
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(sign * 1.24m);
        var afterCash = await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId);
        var cashJournal = afterCash.Single(row => row.Entry.JournalEntryId == cashWrite.Entry.JournalEntryId);
        var cashLine = cashJournal.Entry.Lines.Single(line => line.Account.Name == "TerminalCash");
        (cashLine.Debit - cashLine.Credit).Should().Be(sign * 1.24m);

        // An exact run replay cannot consume the source cash or append a second journal.
        (await fixture.Service.PreviewAsync(fixture.Scope, cashRequest)).Fingerprint.Should().Be(cashReceipt.ReceiptId);
        await fixture.Store.AppendAsync(cashWrite);
        (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Should().HaveCount(afterCash.Count);
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(sign * 1.24m);
        await fixture.Service.Invoking(service => service.PreviewAsync(fixture.Scope,
            cashRequest with { RunId = "duplicate-payment", KnownAt = DateTimeOffset.UtcNow }))
            .Should().ThrowAsync<InvalidOperationException>();

        var inverseRequest = cashRequest with
        {
            RunId = "inverse-closed-cash", KnownAt = DateTimeOffset.UtcNow, CashAllocations = [],
            ReversesReceiptId = cashReceipt.ReceiptId, CorrectionRationale = "Correct the observed terminal coupon allocation"
        };
        var inverse = await fixture.Service.PreviewAsync(fixture.Scope, inverseRequest);
        inverse.Instruction.Lines.Select(line => (line.AccountId, line.Debit, line.Credit)).Should()
            .Equal(cashReceipt.Instruction.Lines.Select(line => (line.AccountId, line.Credit, line.Debit)));
        await fixture.PostAsync(inverse.Instruction);
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(0m);
        var rebook = await fixture.Service.PreviewAsync(fixture.Scope,
            cashRequest with { RunId = "rebook-closed-cash", KnownAt = DateTimeOffset.UtcNow });
        rebook.EarnedDelta.Should().Be(0m);
        rebook.Instruction.TerminalCashAuthority!.EarnedReceipt!.ReceiptId.Should().Be(earned.ReceiptId);
        await fixture.PostAsync(rebook.Instruction);
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(sign * 1.24m);
        var finalLot = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId, [zero.TaxLotRecordId])).Single();
        finalLot.Should().BeEquivalentTo(zero, "coupon clearing and correction must preserve the actual closed-lot CAS outcome");
        (await fixture.Store.GetCouponRecognitionReceiptAsync(fixture.Scope, zero.TaxLotRecordId, earned.ReceiptId))
            .Should().BeEquivalentTo(earned, "the principal-bound earned source remains immutable after cash correction");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task TerminalPrincipal_MissingOrSupersededEarnedReceiptRefusesWithoutAnyLotOrJournalMutation()
    {
        await using var fixture = await TerminalCouponFixture.CreateAsync(1, deferred: true);
        var initial = fixture.Physical.Lot;
        var count = (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Count;
        await fixture.Physical.Store.Invoking(store => store.AppendAssetPostingAsync(fixture.Principal))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*final earned coupon target*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(fixture.Principal.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Should().HaveCount(count);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId, [initial.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(initial);

        var earned = await fixture.RecognizeAsync("earned-before-stale-preparation");
        var pinned = fixture.BindPrincipal(earned);
        var inverse = await fixture.Service.PreviewAsync(fixture.Scope, fixture.Request("inverse-before-principal") with
        { ReversesReceiptId = earned.ReceiptId, CorrectionRationale = "Correct the final earned target before principal" });
        await fixture.PostAsync(inverse.Instruction);
        var beforeRefusal = (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Count;
        await fixture.Physical.Store.Invoking(store => store.AppendAssetPostingAsync(pinned))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*latest posted coupon target or entitlement differs*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(pinned.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Should().HaveCount(beforeRefusal);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId, [initial.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(initial);

        var rebooked = await fixture.RecognizeAsync("reconciled-earned-target");
        var refreshed = fixture.BindPrincipal(rebooked);
        (await fixture.Physical.Store.AppendAssetPostingAsync(refreshed)).MutatedLots.Single().OpenQuantity.Should().Be(0m);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task TerminalPrincipal_PurchasedAccruedOnlyPinsActualBirth_AndFirstSignedCashNeedsNoZeroIncomeJournal()
    {
        foreach (var sign in new[] { 1, -1 })
        {
            await using var fixture = await TerminalCouponFixture.CreateAsync(sign, deferred: true, purchasedOnly: true);
            var preview = await fixture.Service.PreviewAsync(fixture.Scope, fixture.Request("zero-earned-opening"));
            preview.HasChanges.Should().BeFalse();
            preview.EarnedDelta.Should().Be(0m);
            preview.Instruction.Lines.Should().BeEmpty();
            preview.Instruction.IncomeInstruction.BondIncome!.AcquisitionAccruedInterest.Should().Be(sign * 1.24m);
            (await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, fixture.Physical.Lot.TaxLotRecordId)).Should().BeNull();
            var before = (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Count;
            var opening = BondIncomeTerminalCoupon.Opening(preview.Instruction.IncomeInstruction,
                fixture.Physical.Lot.OriginatingMutationBatchId!.Value, fixture.Physical.Lot.SourceJournalEntryId!.Value);
            await fixture.Service.VerifyTerminalOpeningAsync(fixture.Scope, preview.Instruction.IncomeInstruction,
                opening, fixture.Physical.Period.PeriodId);
            await fixture.Service.Invoking(service => service.VerifyTerminalOpeningAsync(fixture.Scope,
                preview.Instruction.IncomeInstruction, opening with { AcquisitionJournalEntryId = Guid.NewGuid() },
                fixture.Physical.Period.PeriodId)).Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*original acquisition changed*");
            var principal = fixture.BindOpening(opening);
            var wrong = fixture.BindOpening(opening with { AcquisitionJournalEntryId = Guid.NewGuid() });
            await fixture.Physical.Store.Invoking(store => store.AppendAssetPostingAsync(wrong)).Should()
                .ThrowAsync<ArgumentException>().WithMessage("*acquisition-opening coupon authority is absent or stale*");
            (await fixture.Store.GetAtomicTaxLotPostingAsync(wrong.MutationBatchId)).Should().BeNull();
            (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Should().HaveCount(before);
            (await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId, [fixture.Physical.Lot.TaxLotRecordId])).Single()
                .Should().BeEquivalentTo(fixture.Physical.Lot);

            var closed = (await fixture.Physical.Store.AppendAssetPostingAsync(principal)).MutatedLots.Single();
            closed.OpenQuantity.Should().Be(0m);
            closed.ToOpenLot().OpenTransactionCostBasis.Should().Be(0m);
            (await fixture.Store.GetByPeriodAsync(fixture.Physical.Period.PeriodId)).Should().HaveCount(before + 1,
                "the terminal principal is the only new journal; a zero earned receipt must not be invented");
            (await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, closed.TaxLotRecordId)).Should().BeNull();
            var request = fixture.ObserveCash("first-purchased-cash-after-principal");
            var cash = await fixture.Service.PreviewAsync(fixture.Scope, request);
            cash.EarnedDelta.Should().Be(0m);
            cash.RemainingSignedBalance.Should().Be(0m);
            cash.Instruction.TerminalCashAuthority!.EarnedReceipt.Should().BeNull();
            cash.Instruction.TerminalCashAuthority.Opening.Should().Be(opening);
            cash.Instruction.Lines.Should().NotContain(line => line.AccountId == "TerminalIncome");
            await fixture.PostAsync(cash.Instruction);
            var receipt = (await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, closed.TaxLotRecordId))!;
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(sign * 1.24m);
            var inverse = await fixture.Service.PreviewAsync(fixture.Scope, request with
            {
                RunId = "inverse-purchased-cash", KnownAt = DateTimeOffset.UtcNow, CashAllocations = [],
                ReversesReceiptId = receipt.ReceiptId, CorrectionRationale = "Correct the seller accrued cash allocation"
            });
            await fixture.PostAsync(inverse.Instruction);
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(0m);
            var rebook = await fixture.Service.PreviewAsync(fixture.Scope, request with
            { RunId = "rebook-purchased-cash", KnownAt = DateTimeOffset.UtcNow });
            rebook.EarnedDelta.Should().Be(0m);
            await fixture.PostAsync(rebook.Instruction);
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(sign * 1.24m);
            (await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId, [closed.TaxLotRecordId])).Single()
                .Should().BeEquivalentTo(closed);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CouponNativeHistory_ManualCashAfterEarnedReceiptRequiresReconciliation_ForOpenAndClosedLots()
    {
        foreach (var closePrincipal in new[] { false, true })
        {
            await using var fixture = await TerminalCouponFixture.CreateAsync(1, deferred: false);
            var earned = await fixture.RecognizeAsync("earned-before-ordinary-journal");
            if (closePrincipal)
                await fixture.Physical.Store.AppendAssetPostingAsync(fixture.BindPrincipal(earned));
            var before = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId,
                [fixture.Physical.Lot.TaxLotRecordId])).Single();

            var request = fixture.ObserveCash("manual-other-lot");
            var unrelated = await fixture.Service.PreviewAsync(fixture.Scope, request);
            await fixture.PostAsync(unrelated.Instruction, manual: true, otherLotId: "different-explicit-lot");
            var selected = await fixture.Service.PreviewAsync(fixture.Scope, request with
            { RunId = "manual-same-lot", KnownAt = DateTimeOffset.UtcNow });
            selected.RemainingSignedBalance.Should().Be(0m, "exact other-lot accounting dimensions prove unrelated history");
            await fixture.PostAsync(selected.Instruction, manual: true);

            await fixture.Service.Invoking(service => service.PreviewAsync(fixture.Scope, request with
            { RunId = "native-after-manual-cash", KnownAt = DateTimeOffset.UtcNow }))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicit retained opening reconciliation*");
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "terminal-bank-coupon")).Should().Be(0m,
                "ordinary cash journal rows must never masquerade as native allocation claims");
            (await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, before.TaxLotRecordId))
                .Should().BeEquivalentTo(earned);
            (await fixture.Store.GetTaxLotsByIdsAsync(fixture.Physical.BookId, [before.TaxLotRecordId])).Single()
                .Should().BeEquivalentTo(before);
        }
    }

    private sealed class TerminalCouponFixture : IAsyncDisposable
    {
        private static readonly BondCouponBookAccountsDto Paths = new("TerminalReceivable", "TerminalPayable", "TerminalIncome",
            "TerminalCash", "TerminalTax", "TerminalFees");
        private static readonly BondCouponRecognitionAccountDto[] Accounts =
        [new(Paths.Receivable, Paths.Receivable, "Asset"), new(Paths.Payable, Paths.Payable, "Liability"),
         new(Paths.Income, Paths.Income, "Revenue"), new(Paths.Cash, Paths.Cash, "Asset"),
         new(Paths.Tax, Paths.Tax, "Asset"), new(Paths.Fees, Paths.Fees, "Expense")];
        public AmortFixture Physical { get; }
        public PostgresLedgerJournalStore Store { get; }
        public AccountCashForecastScopeDto Scope { get; }
        public AtomicTaxLotJournalCommand Principal { get; private set; } = null!;
        public BondCouponRecognitionService Service { get; private set; } = null!;
        private readonly IBondMonitoringService _monitoring = Substitute.For<IBondMonitoringService>();
        private readonly IBondCouponRecognitionService _sourceReview = Substitute.For<IBondCouponRecognitionService>();
        private readonly int _sign;
        private readonly DateOnly _couponDate;
        private readonly DateOnly _couponStart;
        private readonly DateOnly _couponEnd;
        private readonly decimal _rawCoupon;
        private readonly bool _purchasedOnly;

        private TerminalCouponFixture(AmortFixture physical, int sign, bool deferred, bool purchasedOnly)
        {
            Physical = physical;
            _sign = sign;
            _purchasedOnly = purchasedOnly;
            _rawCoupon = sign * (purchasedOnly ? 1.24m : 1.235m);
            _couponDate = deferred ? FirstPaydown.AddDays(2) : FirstPaydown;
            _couponStart = purchasedOnly ? AmortAcquired.AddMonths(-6) : AmortAcquired;
            _couponEnd = purchasedOnly ? AmortAcquired : FirstPaydown;
            Scope = new("terminal-tenant", "terminal-company", physical.IncomeAccountId.ToString("D"), physical.BookId.ToString("D"), "USD");
            // Only the external retained-document review gate is substituted. Calculations,
            // coupon receipts, physical lots, reference locks and both financial writers are real.
            _sourceReview.VerifyPreparedSourceAsync(Arg.Any<BondCouponRecognitionInstructionDto>(),
                Arg.Any<RetainedEvidenceIdentityDto>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Store = new(physical.Options, backfillSecurityMaster: () => physical.Securities,
                backfillPositions: () => physical.Positions, bondIncomeAuthority: () => physical.IncomeAuthority,
                couponRecognitionAuthority: () => _sourceReview);
        }

        public static async Task<TerminalCouponFixture> CreateAsync(int sign, bool deferred, bool purchasedOnly = false)
        {
            var physical = await AmortFixture.CreateAsync(false, AccountingBasisKindDto.Gaap, factorPool: true, incomeIndexRatio: 1.2m,
                incomePurchasedAccrued: purchasedOnly ? sign * 1.24m : 0m);
            try
            {
                var fixture = new TerminalCouponFixture(physical, sign, deferred, purchasedOnly);
                await fixture.InitializeAsync(deferred);
                return fixture;
            }
            catch { await physical.DisposeAsync(); throw; }
        }

        private async Task InitializeAsync(bool deferred)
        {
            var legacy = await FactorPaydownCommandAsync(Physical, 0m, FirstPaydown);
            var original = legacy.FactorPaydown!;
            var lot = original.Lots.Single().ExpectedLot;
            var cutoff = DateTimeOffset.UtcNow;
            var coupon = new BondWorkflowCashFlow(_couponDate, _couponStart, _couponEnd, _rawCoupon,
                0m, 0m, 1.2m, false, []) { PeriodUnits = 1m, AccrualFraction = 1m };
            var flows = new[] { coupon, new BondWorkflowCashFlow(FirstPaydown, FirstPaydown, FirstPaydown,
                0m, 1_140_000m, 0m, 1.2m, false, []) };
            var projection = new BondWorkflowResult(lot.SecurityId, "USD", "bond-workflow-v1",
                new(cutoff, lot.AcquiredDate, cutoff), 1_000_000m, 950_000m, 0m, null, null, 0m, null,
                null, null, null, null, null, null, flows,
                [new("SecurityTerms", lot.SecurityId.ToString("D"), Physical.Security.Version, new string('a', 64))], [], []);
            var native = BondIncomeCouponRounding.Allocate(_couponDate, coupon.AccrualStart, coupon.AccrualEnd, "USD",
                [new(lot.TaxLotRecordId, lot.Version, lot.OriginalQuantity, coupon.Interest, 0m)]) with
            { HolderSnapshotDate = deferred ? FirstPaydown : null };
            var income = new BondIncomeInstructionDto(Physical.BookId, "amort-policy", "1",
                new(BondIncomeCalculator.ModelVersion, BondIncomeFloaterPolicy.AcquisitionIndex, true, true, true),
                lot.AcquiredDate, FirstPaydown, cutoff, projection, projection, projection, [], [], [],
                Physical.IncomePurchasedAccrued, 0m, 1.2m, 1.2m, 950_000m, lot.Acquisition.BondIncome!.Evidence, Physical.IncomeAccountId)
            {
                CouponCashAllocations = [native],
                TerminalEntitlement = deferred ? new(BondIncomeCouponRounding.TerminalEntitlementModelVersion, FirstPaydown) : null,
                PurchasedCouponTail = _purchasedOnly ? new(BondIncomePurchasedCouponTail.ModelVersion,
                    Physical.Lot.OriginatingMutationBatchId!.Value, Physical.Lot.SourceJournalEntryId!.Value,
                    _couponDate, _couponStart, _couponEnd, Physical.IncomePurchasedAccrued) : null
            };
            var projectedIncome = BondIncomeCalculator.Project(lot, income);
            income = income with { BookedTarget = projectedIncome.BookedTarget };
            var inputs = new OpenLotAmortizationInstructionDto(lot, Physical.Security, AmortSecurityEvidence(Physical.Security),
                original.Scope.ExpectedBookPositionVersion, FirstPaydown, BondIncomeCalculator.ModelVersion) { BondIncome = income };
            OpenLotAmortization.Project(inputs).TransactionMovement.Should().Be(0m);
            var receipt = BuildEvidence("terminal-principal-cash", 'e') with
            { EffectiveDate = FirstPaydown, SubjectType = "BondPrincipalCashReceipt", SubjectId = lot.TaxLotRecordId.ToString("D") };
            var evidence = legacy.RetainedEvidence.Append(receipt).ToArray();
            var economic = original.EconomicEvent with { RetainedEvidence = original.EconomicEvent.RetainedEvidence.Append(receipt).ToArray() };
            var instruction = original with
            {
                Scope = original.Scope with { TenantId = Scope.TenantId, CompanyId = Scope.CompanyId },
                EconomicEvent = economic,
                Lineage = original.Lineage with { TriggerEvent = economic, ModelVersion = BondIncomeScheduledPrincipal.ModelVersion },
                PrincipalPaydown = 1_140_000m,
                ScheduledPrincipal = new(BondIncomeScheduledPrincipal.ModelVersion, [inputs])
            };
            instruction = instruction with { Lots = BondIncomeScheduledPrincipal.Allocate(instruction, instruction.ScheduledPrincipal) };
            var old = legacy.Journal.Entry;
            var dimensions = Physical.Dimensions(lot.SecurityId, lot.BookPositionId);
            var lines = new LedgerEntry[]
            {
                new(Guid.NewGuid(), old.JournalEntryId, old.Timestamp, LedgerAccounts.CashAccount(Scope.AccountId), 1_140_000m, 0m,
                    old.Description, dimensions, new("USD", "USD", 1_140_000m, 0m, 1m)),
                new(Guid.NewGuid(), old.JournalEntryId, old.Timestamp, Physical.Lot.Account, 0m, 1_140_000m,
                    old.Description, dimensions, new("USD", "USD", 0m, 1_140_000m, 1m))
            };
            var posting = legacy.Journal.PostingCommand! with
            { LotFactorPaydown = instruction, EconomicEvent = economic, ProjectionLineage = instruction.Lineage,
                Evidence = evidence.Select(PostingEvidence).ToArray() };
            Principal = ReplacePrincipal(legacy with { RetainedEvidence = evidence,
                Journal = legacy.Journal with { Entry = new(old.JournalEntryId, old.Timestamp, old.Description, lines, old.Metadata), PostingCommand = posting } }, instruction);
            var authority = Substitute.For<IBondIncomeService>();
            authority.PreviewAsync(Arg.Any<BondIncomePreviewRequestDto>(), Arg.Any<CancellationToken>()).Returns(new BondIncomePreviewDto(
                Physical.BookId, lot.TaxLotRecordId, "retained", inputs, projectedIncome,
                new(AssetLotMutationIntentDto.Amortize, Amortization: inputs)));
            Physical.IncomeAuthority = authority;
            var policies = Substitute.For<IAccountingPolicyService>();
            var adopted = JsonSerializer.Serialize(new { bondCouponRecognition = new { modelVersion = BondCouponRecognitionCalculator.ModelVersion, accounts = Paths } });
            policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(
                new AccountingPolicyDto("amort-policy", AccountingBasisKindDto.Gaap, "1", "Synthetic terminal coupon", AmortAcquired, null, false, adopted, cutoff));
            var configuration = Substitute.For<IAccountingConfigurationService>();
            configuration.GetWorkspaceAsync("amort-fund", Physical.BookId, Arg.Any<CancellationToken>(), Scope.TenantId, Scope.CompanyId).Returns(
                new AccountingConfigurationWorkspaceDto("amort-fund", Physical.BookId, default, "1", cutoff, [],
                    Accounts.Select((account, i) => new ChartOfAccountsNodeDto(i.ToString(), account.Path, account.LedgerName, account.AccountType)).ToArray(), [], [], [], []));
            _monitoring.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns(Array.Empty<BondMonitoringSnapshotDto>());
            var evidenceVerifier = Substitute.For<IBondCouponRecognitionEvidenceVerifier>();
            Service = new(authority, Store, Store, Physical.Positions, policies, configuration, _monitoring, evidenceVerifier);
            _sourceReview.VerifyTerminalOpeningAsync(Arg.Any<AccountCashForecastScopeDto>(), Arg.Any<OpenLotAmortizationInstructionDto>(),
                Arg.Any<BondIncomeTerminalCouponOpeningDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                Service.VerifyTerminalOpeningAsync(call.ArgAt<AccountCashForecastScopeDto>(0), call.ArgAt<OpenLotAmortizationInstructionDto>(1),
                    call.ArgAt<BondIncomeTerminalCouponOpeningDto>(2), call.ArgAt<Guid>(3), call.ArgAt<CancellationToken>(4)));
            Physical.CouponAuthority = _sourceReview;
        }

        public BondCouponRecognitionRequest Request(string runId)
            => new(runId, Physical.Lot.TaxLotRecordId, Physical.Period.PeriodId, FirstPaydown, DateTimeOffset.UtcNow, []);

        public async Task<BondCouponRecognitionReceiptDto> RecognizeAsync(string runId)
        {
            var preview = await Service.PreviewAsync(Scope, Request(runId));
            await PostAsync(preview.Instruction);
            return (await Store.GetLatestCouponRecognitionAsync(Scope, Physical.Lot.TaxLotRecordId))!;
        }

        public AtomicTaxLotJournalCommand BindPrincipal(BondCouponRecognitionReceiptDto receipt)
        {
            var instruction = Principal.FactorPaydown!;
            var proof = BondIncomeTerminalCoupon.Reconcile(instruction.ScheduledPrincipal!.IncomeInstructions.Single(), Scope, receipt)!;
            return ReplacePrincipal(Principal, instruction with
            { ScheduledPrincipal = instruction.ScheduledPrincipal with { TerminalCouponReceipts = [proof] } });
        }

        public AtomicTaxLotJournalCommand BindOpening(BondIncomeTerminalCouponOpeningDto opening)
        {
            var instruction = Principal.FactorPaydown!;
            return ReplacePrincipal(Principal, instruction with
            { ScheduledPrincipal = instruction.ScheduledPrincipal! with { TerminalCouponOpenings = [opening] } });
        }

        public BondCouponRecognitionRequest ObserveCash(string runId)
        {
            var received = _couponDate.AddDays(1);
            var now = DateTimeOffset.UtcNow;
            var evidence = BuildEvidence("terminal-bank-coupon", 'b') with
            { EffectiveDate = received, SubjectType = "AccountCashFlow", SubjectId = "terminal-bank-coupon", ReviewedAtUtc = now.AddSeconds(-2), RetainedAtUtc = now.AddSeconds(-1) };
            var provenance = new AccountCashProvenanceDto("Synthetic", "terminal-bank-coupon", evidence.ContentHashSha256, AccountCashEvidenceRole.Observation, evidence);
            var deductions = provenance with { SourceEntityId = "terminal-allocation", Evidence = evidence with
            { EvidenceId = "terminal-deductions", SubjectType = "AccountCashAllocation", SubjectId = "terminal-allocation" } };
            var amount = _sign * 1.24m;
            var payment = new AccountCashFlowDto("terminal-bank-coupon", "terminal-bank-v1", Scope.AccountId, Scope.BookId,
                AccountCashFlowStatus.Actual, "Coupon", amount, "USD", _couponDate, received, received, provenance, Physical.Security.SecurityId);
            var obligation = AccountBondIncomePlanningService.ObligationIdentity(Scope, Physical.Security.SecurityId, "Coupon", _couponDate, _couponStart);
            var allocation = new AccountCashPaymentAllocationDto("terminal-allocation", obligation, payment.VersionId, amount, amount, DeductionEvidence: deductions);
            var capture = new BondMonitoringCaptureRequest("terminal-monitor", Scope, now, [], [payment], [allocation], [], []);
            _monitoring.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns(new[] { new BondMonitoringSnapshotDto("synthetic", "bond-monitoring-v1", capture, [], [], []) });
            return new(runId, Physical.Lot.TaxLotRecordId, Physical.Period.PeriodId, received, DateTimeOffset.UtcNow,
                [new(allocation.AllocationId, payment.VersionId, amount, amount, DeductionEvidence: deductions)]);
        }

        public async Task<LedgerJournalEntryWrite> PostAsync(BondCouponRecognitionInstructionDto instruction,
            bool manual = false, string? otherLotId = null)
        {
            var lot = instruction.IncomeInstruction.ExpectedLot;
            var date = instruction.Request.ThroughDate;
            var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero);
            var hash = BondCouponRecognitionCalculator.Fingerprint(instruction);
            var source = BuildEvidence("terminal-coupon-" + instruction.Request.RunId, 'd') with
            { EffectiveDate = date, SubjectType = AssetAccountingEvidenceSubjects.Event, SubjectId = instruction.EventId.ToString("D"),
                ContentHashSha256 = hash, ReviewedAtUtc = at.AddMinutes(-10), RetainedAtUtc = at.AddMinutes(-5) };
            var retained = lot.Acquisition.Evidence.Append(source).Distinct().ToArray();
            var economic = new EconomicEventReferenceDto(instruction.EventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Income),
                1, date, source.RetainedAtUtc, "BondCouponRecognition", source.SourceReference, SourceContentHash: hash)
            { SecurityId = lot.SecurityId, BookPositionId = lot.BookPositionId, RetainedEvidence = retained };
            var lineage = new ProjectionLineageDto(instruction.EventId, instruction.EventId, BondCouponRecognitionCalculator.ModelVersion,
                BondCouponRecognitionCalculator.ModelVersion, BondCouponRecognitionCalculator.ModelVersion, "ObservedCouponRecognition", date,
                at, "BondCouponRecognition", source.SourceReference, economic) { BookPositionId = lot.BookPositionId, RetainedEvidence = retained };
            var position = (await Physical.Positions.GetBookPositionAsync(lot.BookPositionId))!;
            var target = instruction.ReversalTarget;
            var posting = new AccountingPostingCommandDto(Guid.NewGuid(), Physical.BookId, Physical.Period.PeriodId, date, at,
                "terminal-coupon:" + instruction.Request.RunId, target is null ? AccountingPostingIntentDto.Originating : AccountingPostingIntentDto.Adjustment,
                instruction.EventId, SourceJournalEntryId: target?.JournalEntryId, ExpectedVersion: Physical.Period.Version,
                SourceEventType: economic.EventType, ApprovalState: AccountingPostingApprovalStateDto.Approved,
                ApprovalId: "independent-terminal-coupon-review", Evidence: retained.Select(PostingEvidence).ToArray(), LedgerBookId: Physical.BookId)
            { Actor = "terminal-coupon-controller", BookContext = position.BookContext with { PeriodId = Physical.Period.PeriodId },
                BookPositionId = lot.BookPositionId, EconomicEvent = economic, ProjectionLineage = lineage,
                RulePackReference = new("amort-policy", "1"), BondCouponRecognition = instruction };
            if (manual)
            {
                economic = economic with { SourceDomain = "OrdinaryCashJournal", RetainedEvidence = [source] };
                lineage = lineage with { ModelKey = "ordinary-cash", SourceDomain = economic.SourceDomain,
                    TriggerEvent = economic, RetainedEvidence = [source] };
                posting = posting with { BondCouponRecognition = null, EconomicEvent = economic,
                    ProjectionLineage = lineage, Evidence = [PostingEvidence(source)] };
            }
            var id = Guid.NewGuid();
            var dimensions = new LedgerLineDimensionSet(BookId: Scope.BookId, AccountId: Scope.AccountId, InstrumentId: lot.SecurityId,
                TaxLotId: otherLotId ?? lot.LotId)
            { PositionId = lot.BookPositionId };
            var lines = instruction.Lines.Select(line =>
            {
                var account = instruction.Accounts.Single(account => account.Path == line.AccountId);
                return new LedgerEntry(Guid.NewGuid(), id, at, new(account.LedgerName, Enum.Parse<LedgerAccountType>(account.AccountType), FinancialAccountId: Scope.AccountId),
                    line.Debit, line.Credit, "Terminal coupon income/clearing", dimensions, new("USD", "USD", line.Debit, line.Credit, 1m));
            }).ToArray();
            var write = new LedgerJournalEntryWrite(new(id, at, "Terminal coupon income/clearing", lines,
                new(SecurityId: lot.SecurityId, EffectiveDate: date, IdempotencyKey: posting.IdempotencyKey, Tags: SecurityMasterLineageTags(lot.SecurityId))),
                Physical.BookId, Physical.Period.PeriodId, SourceEventId: instruction.EventId, SourceJournalEntryId: target?.JournalEntryId,
                AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1",
                PostingKind: target is null ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment, LedgerBookId: Physical.BookId, PostingCommand: posting);
            await Store.AppendAsync(write);
            return write;
        }

        private static AtomicTaxLotJournalCommand ReplacePrincipal(AtomicTaxLotJournalCommand command, OpenLotFactorPaydownInstructionDto principal)
        {
            var old = command.Journal.Entry;
            var tags = new Dictionary<string, string>(old.Metadata.Tags!) { [OpenLotFactorPaydown.JournalFingerprintTag] = OpenLotFactorPaydown.Fingerprint(principal) };
            return (command with { FactorPaydown = principal, Journal = command.Journal with
            { Entry = new(old.JournalEntryId, old.Timestamp, old.Description, old.Lines, old.Metadata with { Tags = tags }),
                PostingCommand = command.Journal.PostingCommand! with { LotFactorPaydown = principal } } }).WithComputedFingerprint();
        }

        private static AccountingPostingEvidenceReferenceDto PostingEvidence(RetainedEvidenceIdentityDto item)
            => new(item.EvidenceId, item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference, Reviewer: item.ReviewedBy,
                ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate, EvidenceVersion: item.EvidenceVersion,
                ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType);

        public ValueTask DisposeAsync() => Physical.DisposeAsync();
    }
}
