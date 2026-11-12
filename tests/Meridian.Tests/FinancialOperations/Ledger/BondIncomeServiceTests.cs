using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondIncomeServiceTests
{
    [Fact]
    public async Task AdoptedIncomeCandidateKeepsActualOwnedCarryingAccountAndLotScopeBeforeReview()
    {
        var f = new Fixture(initialCost: 101m);
        var income = await f.Service.PreviewAsync(f.Request);
        var amount = Math.Abs(income.Income.BookedCarryingBasisMovement!.Value);
        var at = f.Request.KnownAt;
        var periodId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        const string pack = "bond-income-test-pack", rule = "bond-income-test-rule";
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization);
        var policyService = new AccountingPolicyService();
        await policyService.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Gaap,
            f.Book.AccountingPolicyId, "v1", "Adopted income candidate", f.Lot.AcquiredDate,
            RulePack: new AccountingPolicyRulePackDto(pack, "v1",
                [new AccountingPolicyRuleDto(rule, AccountingTreatmentKindDto.Amortization, "v1", eventType)])));
        var book = new LedgerBookDto(f.Book.LedgerBookId, f.Book.FundProfileId, f.AccountId,
            FundStructureNodeKindDto.Account, "Owned bond book", "USD", at, at,
            AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: f.Book.AccountingPolicyId, AccountingPolicyVersion: "v1");
        var period = new LedgerPeriodDto(periodId, book.LedgerBookId, 2026, 1, "January", new(2026, 1, 1), new(2026, 1, 31),
            LedgerPeriodStatusDto.Open, at, null, 1, AccountingBasisKindDto.Gaap, book.AccountingPolicyId, "v1");
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([period]);
        var dimensions = new LedgerDimensionSetDto(FundId: book.FundProfileId, InstrumentId: f.Lot.SecurityId,
            BookId: book.LedgerBookId.ToString("D")) { PositionId = f.Lot.BookPositionId };
        var economic = new EconomicEventReferenceDto(eventId, eventType, 1, f.Request.ThroughDate, at,
            "Meridian.BondIncome", "candidate", SourceContentHash: income.Fingerprint)
        { SecurityId = f.Lot.SecurityId, BookPositionId = f.Lot.BookPositionId };
        var evidence = f.Lot.Acquisition!.Evidence;
        var links = evidence.Select(item => item.EvidenceUri).ToArray();
        economic = economic with { EvidenceLinks = links, RetainedEvidence = evidence };
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "bond-income-amortization", BondIncomeCalculator.ModelVersion,
            "1", "AcquisitionIndex", f.Request.ThroughDate, at, "Meridian.BondIncome", "candidate", economic)
        { BookPositionId = f.Lot.BookPositionId, EvidenceLinks = links, RetainedEvidence = evidence };
        var generated = new GeneratedPostingLineDto[]
        {
            new("carrying", "Assets:Bonds", AccountingTemplateLineSideDto.Credit, "carrying", amount, "USD", dimensions),
            new("income", "Income:BondAmortization", AccountingTemplateLineSideDto.Debit, "income", amount, "USD", dimensions)
        };
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>()).Returns(
            new RuleDryRunResultDto(book.FundProfileId, book.LedgerBookId, eventType, f.Request.ThroughDate,
                amount, "USD", true, rule, [new AccountingRuleDryRunMatchDto(rule, "Bond income", "v1", 100, true, [], [])], [], [], generated));
        configuration.GetWorkspaceAsync(book.FundProfileId, book.LedgerBookId, Arg.Any<CancellationToken>(), null, null).Returns(
            new AccountingConfigurationWorkspaceDto(book.FundProfileId, book.LedgerBookId, AccountingConfigurationStatusDto.Active,
                "v1", at, [book], [new("carrying", "Assets:Bonds", "Assets:Bonds", "Asset"),
                    new("income", "Income:BondAmortization", "Bond amortization", "Revenue")], [], [], [], []));
        var candidateRequest = new PostingRuleJournalCandidateRequestDto(book.FundProfileId, eventType, amount, "USD",
            f.Request.ThroughDate, "preparer", book.LedgerBookId, periodId, at, "Adopted bond income",
            AccountingBasisKindDto.Gaap, book.LedgerBookId, dimensions, SourceEventId: eventId,
            PolicyId: book.AccountingPolicyId, TreatmentKind: AccountingTreatmentKindDto.Amortization, EvidenceLinks: links)
        {
            BookContext = f.Position.BookContext with { PeriodId = periodId }, BookPositionId = f.Lot.BookPositionId,
            EconomicEvent = economic, ProjectionLineage = lineage, RetainedEvidence = evidence,
            RulePackReference = new(pack, "v1", rule, "v1"), AssetLotMutation = income.LotMutation
        };
        var authority = new AssetAccountingCandidateAuthorityContext(eventId, 1, 2, 3, income.Fingerprint,
            AssetAccountingEventKindDto.DepreciationAmortization, f.Lot.SecurityId, f.Lot.BookPositionId,
            f.Position.Version, book.LedgerBookId, periodId, 1, pack, "v1", income.Instruction.Security.Version);
        var builder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policyService, new AccountingBasisProjectionService(policyService)),
            books, policyService, taxLotStore: f.Lots);
        var built = await builder.BuildAuthoritativeCandidateWriteAsync(candidateRequest, authority);
        built.Write.Should().NotBeNull(string.Join("; ", built.Candidate.Issues.Select(issue => issue.Code + ":" + issue.Message)));
        built.Write!.Entry.Lines.Single(line => line.Account.AccountType == LedgerAccountType.Asset).Account.Should().Be(f.Lot.Account);
        built.Write.Entry.Lines.Should().OnlyContain(line => line.Account.FinancialAccountId == f.AccountId.ToString("D")
            && line.Dimensions!.AccountId == f.AccountId.ToString("D") && line.Dimensions.BookId == book.LedgerBookId.ToString("D")
            && line.Dimensions.InstrumentId == f.Lot.SecurityId && line.Dimensions.PositionId == f.Lot.BookPositionId
            && line.Dimensions.TaxLotId == f.Lot.LotId);
        built.Candidate.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.TaxLotId == f.Lot.LotId);
        built.Write.Entry.Lines.Should().OnlyContain(line => line.Currency != null
            && line.Currency.TransactionCurrency == "USD" && line.Currency.FunctionalCurrency == "USD"
            && line.Currency.FxRateToFunctional == 1m && line.Currency.TransactionDebit == line.Debit
            && line.Currency.TransactionCredit == line.Credit);
    }

    [Fact]
    public async Task AcquisitionInsideExCouponWindowRequiresActualOpeningEntitlementReconciliation()
    {
        var f = new Fixture(exCoupon: true);
        var seed = (await f.Service.PreviewAsync(f.Request)).Instruction.BondIncome!.AcquisitionProjection;
        f.Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => seed with { Context = call.Arg<BondWorkflowRequest>().Context,
                AccruedInterest = -0.1m, ContractualAccruedInterest = 4.9m });
        var action = () => f.Service.PreviewAsync(f.Request);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*acquisition inside its ex-coupon window*opening entitlement reconciliation*");
        await f.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("5")]
    [InlineData("-5")]
    public async Task ExistingHolderEnteringExCouponWindowUsesHeldAccrual_AndKeepsParBasisThroughPayment(string couponText)
    {
        var coupon = decimal.Parse(couponText, System.Globalization.CultureInfo.InvariantCulture);
        var f = new Fixture(exCoupon: true);
        var seed = (await f.Service.PreviewAsync(f.Request)).Instruction.BondIncome!.AcquisitionProjection;
        f.Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<BondWorkflowRequest>();
                var settlement = input.Context.SettlementDate!.Value;
                var inFirstPeriod = settlement > new DateOnly(2025, 1, 1) && settlement < new DateOnly(2026, 1, 1);
                var held = inFirstPeriod ? coupon * (settlement.DayNumber - new DateOnly(2025, 1, 1).DayNumber) / 365m : 0m;
                var detached = settlement >= new DateOnly(2025, 12, 22) && settlement < new DateOnly(2026, 1, 1);
                return seed with { Context = input.Context, AccruedInterest = held - (detached ? coupon : 0m),
                    ContractualAccruedInterest = held,
                    CashFlows = seed.CashFlows.Where(flow => flow.PaymentDate > settlement).Select(flow => flow with
                    { Interest = detached && flow.PaymentDate == new DateOnly(2026, 1, 1) ? 0m : coupon }).ToArray() };
            });
        var through = new DateOnly(2025, 12, 30);
        var preview = await f.Service.PreviewAsync(f.Request with { ThroughDate = through });
        var heldExpected = coupon * 363m / 365m;
        preview.Income.CouponIncome.Should().BeApproximately(heldExpected, 0.000000000001m);
        preview.Income.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
        preview.Instruction.BondIncome!.CurrentAccruedInterest.Should().Be(heldExpected);
        preview.Instruction.BondIncome.AmortizationProjection.AccruedInterest.Should().Be(heldExpected - coupon);
        preview.Instruction.BondIncome.AmortizationProjection.ContractualAccruedInterest.Should().Be(heldExpected);
        var paid = await f.Service.PreviewAsync(f.Request);
        paid.Income.CouponIncome.Should().Be(coupon);
        paid.Income.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
        paid.Income.InterestReceivable.Should().Be(0m);
        paid.Income.InterestPayable.Should().Be(0m);
    }

    [Theory]
    [InlineData("1.235")]
    [InlineData("-1.235")]
    public async Task DeferredFinalEntitlementFreezesActualHolderPoolAtPrincipalDate_AndDoesNotDoubleAccrual(string rawText)
    {
        var raw = decimal.Parse(rawText, System.Globalization.CultureInfo.InvariantCulture);
        var f = new Fixture();
        var seed = (await f.Service.PreviewAsync(f.Request)).Instruction.BondIncome!.AcquisitionProjection;
        var terminal = f.Request.ThroughDate;
        var due = terminal.AddDays(2);
        var coupon = seed.CashFlows[0] with { PaymentDate = due, Interest = raw, Principal = 0m, OutstandingPrincipal = 0m };
        var principal = coupon with { PaymentDate = terminal, AccrualStart = terminal, AccrualEnd = terminal,
            Interest = 0m, Principal = 100m, PeriodUnits = 0m, AccrualFraction = 0m };
        f.Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<BondWorkflowRequest>();
                return seed with { Context = input.Context, CashFlows = [principal, coupon],
                    AccruedInterest = input.Context.SettlementDate == terminal ? raw : 0m,
                    ContractualAccruedInterest = input.Context.SettlementDate == terminal ? raw : 0m };
            });
        var preview = await f.Service.PreviewAsync(f.Request);
        preview.Income.CouponIncome.Should().Be(raw > 0m ? 1.24m : -1.24m);
        preview.Instruction.BondIncome!.TerminalEntitlement!.PrincipalDate.Should().Be(terminal);
        preview.Instruction.BondIncome.CouponCashAllocations!.Single().HolderSnapshotDate.Should().Be(terminal);
        await f.Lots.Received().ListOpenTaxLotsByAssetScopeAsync(f.Book.LedgerBookId, f.Lot.SecurityId,
            f.Lot.BookPositionId, terminal, Arg.Any<CancellationToken>());
        await f.Lots.DidNotReceive().ListOpenTaxLotsByAssetScopeAsync(f.Book.LedgerBookId, f.Lot.SecurityId,
            f.Lot.BookPositionId, due, Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task CanonicalIncomeBooksCumulativeTenDecimalTargets_AndRetainsRawModelResidual()
    {
        var f = new Fixture(initialCost: 101m);
        var preview = await f.Service.PreviewAsync(f.Request);
        var raw = preview.Income.ClosingCarryingBasis;
        var proof = preview.Instruction.BondIncome!.BookedTarget!;
        proof.PolicyVersion.Should().Be(BondIncomeBookedPrecision.PolicyVersion);
        proof.RawTransactionTarget.Should().Be(raw);
        proof.BookedTransactionTarget.Should().Be(decimal.Round(raw, 10, MidpointRounding.ToEven));
        proof.TransactionRoundingResidual.Should().Be(raw - proof.BookedTransactionTarget);
        var canonical = OpenLotAmortization.Project(preview.Instruction);
        canonical.TransactionCostBasis.Should().Be(proof.BookedTransactionTarget);
        canonical.FunctionalCostBasis.Should().Be(proof.BookedFunctionalTarget);
        BondIncomeBookedPrecision.RoundTarget(canonical.FunctionalMovement).Should().Be(canonical.FunctionalMovement);
        var altered = preview.Instruction with
        {
            BondIncome = preview.Instruction.BondIncome with
            { BookedTarget = proof with { BookedTransactionTarget = proof.BookedTransactionTarget + 0.0000000001m } }
        };
        var action = () => OpenLotAmortization.Project(altered);
        action.Should().Throw<ArgumentException>().WithMessage("*exact versioned raw/booked target*");
    }
    [Fact]
    public async Task PreviewResolvesBookAndLotAuthority_AndPreparesCanonicalVersionWithoutJournalWrites()
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Request);
        preview.Instruction.CalculationVersion.Should().Be(BondIncomeCalculator.ModelVersion);
        preview.Instruction.ExpectedLot.Should().BeEquivalentTo(f.Lot.ToOpenLot());
        preview.Income.CouponIncome.Should().Be(5m);
        preview.LotMutation.Intent.Should().Be(AssetLotMutationIntentDto.Amortize);
        preview.LotMutation.AssetAccountId.Should().Be("Assets:Bonds");
        preview.Instruction.BondIncome!.AccountingPolicyId.Should().Be(f.Book.AccountingPolicyId);
        await f.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
        await f.Workflow.Received().CalculateIncomeScheduleAsync(Arg.Is<BondWorkflowRequest>(r =>
            r.Context.KnownAt == f.Lot.Acquisition!.BondIncome!.KnownAt), f.Lot.AcquiredDate, Arg.Any<CancellationToken>());
        (await f.Service.PrepareAsync(new(f.Request, preview.Fingerprint))).Fingerprint.Should().Be(preview.Fingerprint);
    }

    [Theory]
    [InlineData("lot")]
    [InlineData("policy")]
    [InlineData("observation")]
    public async Task PreparationRefusesChangedAuthoritiesOrSourceInputs(string changed)
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Request);
        if (changed == "lot")
            f.Lot = f.Lot with { Version = f.Lot.Version + 1 };
        if (changed == "policy")
            f.FloaterPolicy = BondIncomeFloaterPolicy.ProspectiveReset;
        if (changed == "observation")
            f.InputHash = new string('b', 64);
        var action = () => f.Service.PrepareAsync(new(f.Request, preview.Fingerprint));
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
        await f.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HolderCashAllocationRetainsAllHistoricalLotVersions_AndStalesWhenPeerLotChanges()
    {
        var f = new Fixture();
        var peer = f.Lot with { TaxLotRecordId = Guid.NewGuid(), LotId = "peer", Version = 3 };
        f.Lots.ListOpenTaxLotsByAssetScopeAsync(f.Book.LedgerBookId, f.Lot.SecurityId, f.Lot.BookPositionId,
            Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => (IReadOnlyList<LedgerTaxLotRecord>)[f.Lot, peer]);
        var preview = await f.Service.PreviewAsync(f.Request);
        preview.Instruction.BondIncome!.CouponCashAllocations.Should().ContainSingle();
        var allocation = preview.Instruction.BondIncome.CouponCashAllocations![0];
        allocation.Lots.Should().HaveCount(2);
        allocation.Lots.Sum(l => l.CashAmount).Should().Be(10m);
        peer = peer with { Version = peer.Version + 1 };
        await f.Service.Invoking(s => s.PrepareAsync(new(f.Request, preview.Fingerprint))).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
    }

    [Fact]
    public async Task ExCouponPeerUsesItsOwnAcquisitionSettlementEntitlement_AndReceivesNoRoundingCent()
    {
        var f = new Fixture(exCoupon: true);
        var seed = (await f.Service.PreviewAsync(f.Request)).Instruction.BondIncome!.CurrentProjection;
        var peer = f.Lot with
        {
            TaxLotRecordId = Guid.NewGuid(),
            LotId = "ex-coupon-peer",
            AcquiredDate = new(2025, 12, 30),
            Version = 2
        };
        f.Lots.ListOpenTaxLotsByAssetScopeAsync(f.Book.LedgerBookId, f.Lot.SecurityId, f.Lot.BookPositionId,
            Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => (IReadOnlyList<LedgerTaxLotRecord>)[f.Lot, peer]);
        f.Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<BondWorkflowRequest>();
                return seed with
                {
                    Context = input.Context,
                    CashFlows = seed.CashFlows.Select(flow => flow with
                    { Interest = input.Context.SettlementDate == peer.AcquiredDate && flow.PaymentDate == f.Request.ThroughDate ? 0m : 1.235m }).ToArray()
                };
            });
        var preview = await f.Service.PreviewAsync(f.Request);
        var allocation = preview.Instruction.BondIncome!.CouponCashAllocations!.Single();
        allocation.Lots.Single(l => l.TaxLotRecordId == peer.TaxLotRecordId).RawAmount.Should().Be(0m);
        allocation.Lots.Single(l => l.TaxLotRecordId == peer.TaxLotRecordId).CashAmount.Should().Be(0m);
        allocation.Lots.Single(l => l.TaxLotRecordId == peer.TaxLotRecordId).AcquisitionSettlementDate.Should().Be(peer.AcquiredDate);
        allocation.Lots.Single(l => l.TaxLotRecordId == f.Lot.TaxLotRecordId).CashAmount.Should().Be(1.24m);
        preview.Income.CouponIncome.Should().Be(1.24m);
        await f.Workflow.Received().CalculateIncomeScheduleAsync(Arg.Is<BondWorkflowRequest>(r =>
            r.Context.SettlementDate == peer.AcquiredDate), f.Request.ThroughDate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExCouponPoolRefusesDetachedSoldPeerRightsUntilEntitlementHistoryIsRetained()
    {
        var f = new Fixture(exCoupon: true);
        var journalId = Guid.NewGuid();
        var at = f.Request.KnownAt;
        var description = "Synthetic peer disposal";
        var entry = new JournalEntry(journalId, at, description,
            [new(Guid.NewGuid(), journalId, at, new("Cash", LedgerAccountType.Asset), 1m, 0m, description),
             new(Guid.NewGuid(), journalId, at, f.Lot.Account, 0m, 1m, description)],
            new(EffectiveDate: new(2025, 12, 30)));
        var row = new LedgerJournalEntryRecord(entry, f.Book.LedgerBookId, Guid.NewGuid(), null, null, 1, at);
        f.Lots.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns([row]);
        var soldPeer = f.Lot.ToOpenLot() with { TaxLotRecordId = Guid.NewGuid(), LotId = "sold-peer" };
        ((ILedgerTaxLotDisposalHistory)f.Lots).GetTaxLotDisposalHistoryAsync(f.Book.LedgerBookId,
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(
                [new(Guid.NewGuid(), journalId, f.Lot.Account, LedgerTaxLotReliefMethod.Fifo, [], [], 0m,
                    CanonicalLots: [soldPeer])]);
        await f.Service.Invoking(s => s.PreviewAsync(f.Request)).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*detached-entitlement history*");
    }

    [Fact]
    public async Task MissingAdoptedAcquisitionFactsRemainAnExplicitBlocker()
    {
        var f = new Fixture();
        f.Lot = f.Lot with { Acquisition = f.Lot.Acquisition! with { BondIncome = null } };
        await f.Service.Invoking(s => s.PreviewAsync(f.Request)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*reviewed bond-income acquisition*");
        f.Workflow.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ForeignBookLotAndChangedPositionPolicyAreRefused()
    {
        var f = new Fixture();
        f.Lot = f.Lot with { LedgerBookId = Guid.NewGuid() };
        await f.Service.Invoking(s => s.PreviewAsync(f.Request)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*this book/date scope*");
    }

    [Fact]
    public async Task SameBookLotFromAnotherFinancialAccountIsRefusedBeforeProjection()
    {
        var f = new Fixture();
        f.Lot = f.Lot with { Account = f.Lot.Account with { FinancialAccountId = Guid.NewGuid().ToString("D") } };
        await f.Service.Invoking(s => s.PreviewAsync(f.Request)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*requested financial account*");
        f.Workflow.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PositionWithDifferentOwnerScopeIsRefusedEvenWhenBookIdMatches()
    {
        var f = new Fixture();
        f.Position = f.Position with { BookContext = f.Position.BookContext with { FundStructureNodeId = Guid.NewGuid() } };
        await f.Service.Invoking(s => s.PreviewAsync(f.Request)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*position or book policy*");
        f.Workflow.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PreparedCalculationEvidenceBindsActualFingerprintReview_AndPreservesOriginalActorOnRetry()
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Request);
        var path = Path.Combine(Path.GetTempPath(), "meridian-income-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileBondIncomePreparationEvidenceStore(path);
            var artifact = new BondIncomePreparationArtifactDto("tenant", "company", f.AccountId, f.Book.LedgerBookId,
                Guid.NewGuid(), Guid.NewGuid(), 1, preview.Fingerprint, preview.Instruction);
            var source = await store.RetainAsync(artifact, "preparing-operator", f.Request.KnownAt, false);
            source.ReviewedBy.Should().Be("preparing-operator");
            source.SubjectType.Should().Be(AssetAccountingEvidenceSubjects.Event);
            var retry = await store.RetainAsync(artifact, "second-operator", f.Request.KnownAt.AddHours(1), true);
            retry.Should().Be(source, "reopening a prepared calculation cannot reattribute its original fingerprint review");
            await store.VerifyAsync(source, preview.Instruction, artifact.PeriodId, artifact.EventId, artifact.EventVersion);
            await store.Invoking(s => s.VerifyAsync(source with { ReviewedBy = "invented-reviewer" },
                preview.Instruction, artifact.PeriodId, artifact.EventId, artifact.EventVersion)).Should()
                .ThrowAsync<InvalidOperationException>().WithMessage("*intake proof changed*");
            await store.Invoking(s => s.VerifyAsync(source, preview.Instruction, Guid.NewGuid(),
                artifact.EventId, artifact.EventVersion)).Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*exact book, period, lot and instruction*");
            var json = await File.ReadAllTextAsync(Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Single());
            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("Artifact").GetProperty("Kind").GetString().Should().Be("SystemDerivedPrepared");
            document.RootElement.GetProperty("OperatorFingerprintReview").GetProperty("PreviewFingerprint").GetString().Should().Be(preview.Fingerprint);
            document.RootElement.TryGetProperty("ApprovalId", out _).Should().BeFalse();
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task MissingOrTamperedPreparedEvidenceCannotBeSilentlyRecreatedForAnExistingEvent()
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Request);
        var path = Path.Combine(Path.GetTempPath(), "meridian-income-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileBondIncomePreparationEvidenceStore(path);
            var artifact = new BondIncomePreparationArtifactDto("tenant", "company", f.AccountId, f.Book.LedgerBookId,
                Guid.NewGuid(), Guid.NewGuid(), 1, preview.Fingerprint, preview.Instruction);
            await store.RetainAsync(artifact, "operator", f.Request.KnownAt, false);
            var file = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Single();
            var original = await File.ReadAllTextAsync(file);
            var altered = System.Text.Json.Nodes.JsonNode.Parse(original)!;
            altered["OperatorFingerprintReview"]!["Actor"] = "invented-reviewer";
            await File.WriteAllTextAsync(file, altered.ToJsonString());
            await store.Invoking(s => s.RetainAsync(artifact, "operator", f.Request.KnownAt, true)).Should()
                .ThrowAsync<InvalidOperationException>().WithMessage("*changed or conflicts*");
            File.Delete(file);
            await store.Invoking(s => s.RetainAsync(artifact, "operator", f.Request.KnownAt, true)).Should()
                .ThrowAsync<InvalidOperationException>().WithMessage("*missing*");
            await store.Invoking(s => s.RetainAsync(artifact with { PreviewFingerprint = new string('c', 64) },
                "operator", f.Request.KnownAt, false)).Should().ThrowAsync<ArgumentException>().WithMessage("*exact prepared instruction fingerprint*");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task ExplicitPrepareRetainsCalculationAndHandsOffCanonicalMutationToGovernedDraft()
    {
        var f = new Fixture();
        f.Lot = f.Lot with
        {
            UnitCost = 101m,
            Acquisition = f.Lot.Acquisition! with
            {
                TransactionCostBasis = 101m,
                FunctionalCostBasis = 101m,
                BondIncome = f.Lot.Acquisition!.BondIncome! with { CleanConsideration = 101m }
            }
        };
        var preview = await f.Service.PreviewAsync(f.Request);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), f.Book.LedgerBookId, 2026, 1, "Synthetic",
            new(2026, 1, 1), new(2026, 12, 31), "Open", f.Request.KnownAt, null, 1);
        f.Lots.ListPeriodsAsync(f.Book.LedgerBookId, ct: Arg.Any<CancellationToken>()).Returns([period]);
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        ProjectAssetAccountingEventRequestDto? projected = null;
        AssetAccountingPostingCandidateRequestDto? draft = null;
        AssetAccountingEventSpineDto? capturedSpine = null;
        spine.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            projected = call.Arg<ProjectAssetAccountingEventRequestDto>();
            capturedSpine = new(projected.EconomicEvent.EventId, projected.EventKind, projected.EconomicEvent.EventVersion,
                2, projected.EconomicEvent.EffectiveDate, projected.EventAmount, projected.Currency, projected.Scope,
                projected.EconomicEvent, projected.ProjectionLineage, projected.RetainedEvidence, [], projected.ProjectedEffect);
            return new AssetAccountingEventSpineAppendResultDto(capturedSpine, false);
        });
        spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            draft = call.Arg<AssetAccountingPostingCandidateRequestDto>();
            return new AssetAccountingPostingCandidateDto(capturedSpine!, null!);
        });
        var path = Path.Combine(Path.GetTempPath(), "meridian-income-preparation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var evidence = new FileBondIncomePreparationEvidenceStore(path);
            var adapter = new BondIncomePreparationService(f.Service, f.Lots, f.Positions, f.Policies, spine, evidence);
            var prepared = await adapter.PrepareAsync(new(f.Request, preview.Fingerprint, period.PeriodId),
                "tenant", "company", "operator", f.Request.KnownAt);
            prepared.PreparedCandidate.Should().NotBeNull();
            prepared.PreparedArtifact!.Instruction.Should().BeEquivalentTo(preview.Instruction);
            draft!.LotMutation.Should().BeEquivalentTo(preview.LotMutation);
            draft.ExpectedSpineVersion.Should().Be(2);
            projected!.ProjectedEffect.Lines.Should().HaveCount(2);
            projected.ProjectedEffect.Lines.Should().OnlyContain(line => line.Dimensions!.TaxLotId == f.Lot.LotId);
            var source = projected.RetainedEvidence.Single(e => e.SourceSystem == "Meridian.BondIncome");
            source.ReviewedBy.Should().Be("operator");
            await evidence.VerifyAsync(source, preview.Instruction, period.PeriodId,
                prepared.PreparedArtifact.EventId, prepared.PreparedArtifact.EventVersion);
            await f.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    private sealed class Fixture
    {
        public ILedgerJournalStore Lots { get; } = Substitute.For<ILedgerJournalStore, ILedgerTaxLotDisposalHistory>();
        public IInstrumentPositionProjectionStore Positions { get; } = Substitute.For<IInstrumentPositionProjectionStore>();
        public ISecurityMasterStore Securities { get; } = Substitute.For<ISecurityMasterStore>();
        public IAccountingPolicyService Policies { get; } = Substitute.For<IAccountingPolicyService>();
        public IBondWorkflowService Workflow { get; } = Substitute.For<IBondWorkflowService>();
        public LedgerBookRecord Book { get; }
        public LedgerTaxLotRecord Lot { get; set; }
        public Guid AccountId { get; } = Guid.NewGuid();
        public BookPositionDto Position { get; set; }
        public BondIncomeFloaterPolicy FloaterPolicy { get; set; } = BondIncomeFloaterPolicy.AcquisitionIndex;
        public string InputHash { get; set; } = new string('a', 64);
        public BondIncomePreviewRequestDto Request { get; }
        public BondIncomeService Service { get; }

        public Fixture(bool exCoupon = false, decimal initialCost = 100m)
        {
            var date = new DateOnly(2025, 1, 1);
            var at = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var securityId = Guid.NewGuid();
            var positionId = Guid.NewGuid();
            var lotId = Guid.NewGuid();
            var security = new SecurityProjectionRecord(securityId, "Bond", SecurityStatusDto.Active, "Synthetic", "USD", "ISIN",
                "SYNTHETIC", JsonSerializer.SerializeToElement(new { paymentFrequency = "Annual", dayCountConvention = "ACT/365F" }),
                exCoupon ? JsonSerializer.SerializeToElement(new { assetClassTermsVersion = 5, bondWorkflow = new { exCouponBusinessDays = 5 } }) : JsonSerializer.SerializeToElement(new { }),
                JsonSerializer.SerializeToElement(new { }), 1, at, null, [], []);
            var lotEvidence = Evidence("lot", "OpenLotAcquisition", lotId.ToString("D"), new string('a', 64));
            var termsEvidence = Evidence("terms", "SecurityMasterProjection", securityId.ToString("D"), OpenLotAmortization.SecurityHash(security));
            var profile = new BondIncomeAcquisitionDto(at, initialCost, 0m, 1m, [], [lotEvidence, termsEvidence]);
            var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, initialCost, initialCost, date,
                new(100m, 1m, BondAmortizationMethod.NoAmortization, null), [lotEvidence])
            { BondIncome = profile };
            Book = new(Guid.NewGuid(), "org", AccountId, FundStructureNodeKindDto.Account, "Synthetic book", "USD", at, at,
                AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: "gaap-income", AccountingPolicyVersion: "v1");
            Lot = new(lotId, Book.LedgerBookId, new("Assets:Bonds", LedgerAccountType.Asset, "SYN", AccountId.ToString("D")), "lot", date,
                1m, 1m, initialCost, "USD", at, at, Version: 1, SecurityId: securityId, BookPositionId: positionId,
                OriginalFace: 100m, BookedFactor: 1m, ParBasis: 100m, Acquisition: acquisition);
            var context = new AccountingBookContextDto(Book.LedgerBookId, Book.FundProfileId, Book.FundStructureNodeId,
                Book.FundStructureNodeKind, Book.DisplayName, Book.BaseCurrency, Book.AccountingBasis,
                Book.AccountingPolicyId, Book.AccountingPolicyVersion);
            Position = new BookPositionDto(positionId, securityId, Guid.NewGuid(), context, "Long", "Active", date,
                PrimaryAccountId: "Assets:Bonds");
            Lots.GetLedgerBookAsync(Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(Book);
            Lots.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns([]);
            ((ILedgerTaxLotDisposalHistory)Lots).GetTaxLotDisposalHistoryAsync(Book.LedgerBookId,
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
            Lots.GetTaxLotsByIdsAsync(Book.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(_ => (IReadOnlyList<LedgerTaxLotRecord>)[Lot]);
            Lots.ListOpenTaxLotsByAssetScopeAsync(Book.LedgerBookId, securityId, positionId,
                Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => (IReadOnlyList<LedgerTaxLotRecord>)[Lot]);
            Positions.GetBookPositionAsync(positionId, Arg.Any<CancellationToken>()).Returns(_ => Position);
            Securities.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(security);
            Policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
                new AccountingPolicyDto(Book.AccountingPolicyId, Book.AccountingBasis, Book.AccountingPolicyVersion,
                    "Adopted", date, null, false, JsonSerializer.Serialize(new
                    {
                        bondIncome = new BondIncomePolicyDto(BondIncomeCalculator.ModelVersion, FloaterPolicy, true, true, true,
                        "Income:BondAmortization")
                    }), at));
            Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call => new BondWorkflowResult(securityId, "USD", "bond-workflow-v1",
                    call.ArgAt<BondWorkflowRequest>(0).Context, 100m, 100m, 0m, null, null, 0m, null, null, null, null,
                    null, null, null,
                    [new BondWorkflowCashFlow(new(2026, 1, 1), date, new(2026, 1, 1), 5m, 0m, 100m, 1m, false, [])
                        { AccrualFraction = 1m, PeriodUnits = 1m },
                     new BondWorkflowCashFlow(new(2027, 1, 1), new(2026, 1, 1), new(2027, 1, 1), 5m, 100m, 0m, 1m, false, [])
                        { AccrualFraction = 1m, PeriodUnits = 1m }],
                    [new("SecurityTerms", securityId.ToString("D"), 1, InputHash)], [], [])
                { SettlementIndexRatio = 1m, SettlementIndexIsObserved = true, ContractualAccruedInterest = 0m });
            Request = new(Book.LedgerBookId, lotId, new(2026, 1, 1), new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), AccountId);
            Service = new(Lots, Positions, Securities, Policies, Workflow);

            RetainedEvidenceIdentityDto Evidence(string id, string subjectType, string subjectId, string hash)
                => new(id, "urn:synthetic:" + id, hash, "Synthetic", id, "Accepted", "reviewer", at, date,
                    1, at, "reviewer", subjectType, subjectId);
        }
    }
}
