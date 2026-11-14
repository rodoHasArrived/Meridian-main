using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.Calendars;
using Meridian.Instruments.FixedIncome;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondIncomeServiceTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task CompletedCouponAcrossTwoLotsAcceptsActualResolvedCurvePinsWithoutChangingTheAdoptedWorkflow(int coupon)
    {
        var f = new RealCurveIncomeFixture(coupon);
        var preview = await f.Service.PreviewAsync(f.Request);
        var peerRequest = f.PeerRequest!;
        var peerProjection = f.PeerProjection!;
        peerRequest.Context.Curves.Should().BeNull("the account service did not choose a caller-authored curve");
        peerProjection.Context.Curves.Should().ContainSingle().Which.Should().Be(f.Pin);
        peerProjection.Context.Should().NotBe(peerRequest.Context, "the actual workflow enriches its resolved context");
        peerProjection.ModelVersion.Should().Be("bond-workflow-v2");
        peerProjection.Inputs.Should().Contain(input => input.Kind == "Curve:Discount"
            && input.VersionId == f.Version.CurveVersionId && input.ContentSha256 == f.Version.ContentSha256);
        var inputs = preview.Instruction.BondIncome!;
        var allocation = inputs.CouponCashAllocations.Should().ContainSingle().Which;
        allocation.Lots.Should().HaveCount(2);
        allocation.Lots.Sum(row => row.CashAmount).Should().Be(2m * coupon);
        allocation.Lots.Should().OnlyContain(row => row.CashAmount == coupon && row.RawAmount == coupon);
        allocation.Lots.Single(row => row.TaxLotRecordId == f.Peer.TaxLotRecordId).EntitlementInputs.Should()
            .Contain(input => input.Kind == "Curve:Discount" && input.ContentSha256 == f.Version.ContentSha256);
        inputs.AcquisitionProjection.ModelVersion.Should().Be("bond-workflow-v2");
        inputs.CurrentProjection.ModelVersion.Should().Be("bond-workflow-v2");
        inputs.AmortizationProjection.ModelVersion.Should().Be("bond-workflow-v2");
        await f.Curves.Received().GetDefinitionAsync(f.Definition.CurveId, Arg.Any<CancellationToken>());
        await f.Curves.Received().GetVersionByIdAsync(f.Definition.CurveId, f.Version.CurveVersionId, Arg.Any<CancellationToken>());
        // Adding a definition after the retained knowledge cutoff cannot reinterpret preparation.
        f.FutureDefinition = f.Definition with { CurveId = Guid.NewGuid(), CurveKey = "USD-FUTURE", CreatedAt = f.Request.KnownAt.AddDays(1) };
        (await f.Service.PrepareAsync(new(f.Request, preview.Fingerprint))).Fingerprint.Should().Be(preview.Fingerprint);
        await f.Base.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("settlement")]
    [InlineData("security-version")]
    [InlineData("observed-selection")]
    [InlineData("economic-evidence")]
    [InlineData("knowledge-evidence")]
    [InlineData("unbacked-pin")]
    [InlineData("content-hash")]
    [InlineData("curve-type")]
    [InlineData("definition-admission")]
    [InlineData("freeze")]
    [InlineData("request-fee")]
    public async Task PeerCurveEnrichmentCannotSubstituteScopeCutoffsOrRetainedAuthority(string defect)
    {
        var f = new RealCurveIncomeFixture(5);
        f.MutatePeer = projection => defect switch
        {
            "settlement" => projection with { Context = projection.Context with { SettlementDate = f.Peer.AcquiredDate.AddDays(1) } },
            "security-version" => projection with { Context = projection.Context with { SecurityVersion = f.Base.Security.Version + 1 } },
            "observed-selection" => projection with { Context = projection.Context with { ObservedFixingReceiptIds = [Guid.NewGuid()] } },
            "economic-evidence" => projection with
            {
                Inputs = projection.Inputs.Select(input => input.Kind == "Curve:Discount"
                ? input with { AsOf = projection.Context.AsOf.AddDays(1) } : input).ToArray()
            },
            "knowledge-evidence" => projection with
            {
                Inputs = projection.Inputs.Select(input => input.Kind == "Curve:Discount"
                ? input with { RecordedAt = f.Request.KnownAt.AddTicks(1) } : input).ToArray()
            },
            "unbacked-pin" => projection with
            {
                Context = projection.Context with
                { Curves = [.. projection.Context.Curves!, new("Forward:UNBACKED", Guid.NewGuid(), Guid.NewGuid())] }
            },
            "content-hash" => projection with
            {
                Inputs = projection.Inputs.Select(input => input.Kind == "Curve:Discount"
                ? input with { ContentSha256 = new string('b', 64) } : input).ToArray()
            },
            "curve-type" => WrongCurveRole(projection),
            "definition-admission" => f.ChangeDefinitionAdmission(projection),
            "freeze" => projection with { FrozenIndexAt = f.Request.ThroughDate.AddDays(1) },
            _ => projection with { Request = projection.Request! with { Fees = 1m } }
        };
        await f.Service.Invoking(service => service.PreviewAsync(f.Request)).Should().ThrowAsync<InvalidOperationException>();
        f.PeerCalculations.Should().Be(1, "the rejection must follow the real peer projection, not an earlier fixture refusal");
        await f.Base.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());

        static BondWorkflowResult WrongCurveRole(BondWorkflowResult projection)
        {
            var context = projection.Context with { Curves = projection.Context.Curves!.Select(pin => pin with { Role = "Forward:SOFR" }).ToArray() };
            return projection with
            {
                Context = context,
                Request = projection.Request! with { Context = context },
                Inputs = projection.Inputs.Select(input => input.Kind == "Curve:Discount"
                    ? input with { Kind = "Curve:Forward:SOFR" } : input).ToArray()
            };
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task EarlyPaidCouponKeepsMarketInvoiceAvailableButBookAdvanceTailRefuses(int coupon)
    {
        var f = new Fixture(separatedAccrual: true, declaredCoupon: coupon, earlyPayment: true);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(f.Lot.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(f.Security.ToDetail());
        query.GetByIdAsync(f.Lot.SecurityId, Arg.Any<CancellationToken>()).Returns(f.Security.ToDetail());
        var workflow = new BondWorkflowService(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        var service = new BondIncomeService(f.Lots, f.Positions, f.Securities, f.Policies, workflow);
        var date = new DateOnly(2027, 12, 31);
        var at = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var market = (await workflow.CalculateAsync(new(f.Lot.SecurityId, new(at, date, at))))!;
        market.IsBlocked.Should().BeFalse();
        market.AccruedInterest.Should().Be(0m);
        market.HasPaidActiveCoupon.Should().BeTrue();
        var prepare = () => service.PreviewAsync(f.Request with { ThroughDate = date, KnownAt = at });
        await prepare.Should().ThrowAsync<InvalidOperationException>().WithMessage("*advance-payment accounting*book-income*");
        await f.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task ActualWorkflowLagAndExCouponInvoiceKeepHeldIncomeAndCanonicalParBasisSeparate(int coupon)
    {
        var f = new Fixture(exCoupon: true, separatedAccrual: true, declaredCoupon: coupon);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(f.Lot.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(f.Security.ToDetail());
        query.GetByIdAsync(f.Lot.SecurityId, Arg.Any<CancellationToken>()).Returns(f.Security.ToDetail());
        var workflow = new BondWorkflowService(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        var service = new BondIncomeService(f.Lots, f.Positions, f.Securities, f.Policies, workflow);
        var sameDay = await service.PreviewAsync(f.Request with { ThroughDate = f.Lot.AcquiredDate });
        sameDay.Income.CouponIncome.Should().Be(0m);
        sameDay.Income.BookedTarget!.BookedTransactionTarget.Should().Be(100m);
        var request = f.Request with { ThroughDate = new(2026, 1, 2), KnownAt = new(2026, 1, 6, 0, 0, 0, TimeSpan.Zero) };
        var lag = await service.PreviewAsync(request);
        lag.Instruction.BondIncome!.AcquisitionProjection.HasDetachedCoupon.Should().BeFalse();
        lag.Instruction.BondIncome.AmortizationProjection.HasDetachedCoupon.Should().BeTrue();
        lag.Instruction.BondIncome.CurrentAccruedInterest.Should().BeApproximately(coupon / 365m, 0.000000000001m);
        lag.Income.CouponIncome.Should().BeApproximately(coupon + coupon / 365m, 0.000000000001m);
        lag.Income.BookedTarget!.BookedTransactionTarget.Should().Be(100m);
        OpenLotAmortization.Project(lag.Instruction).TransactionMovement.Should().Be(0m);
        var paid = await service.PreviewAsync(request with { ThroughDate = new(2026, 1, 5) });
        paid.Income.CouponIncome.Should().BeApproximately(coupon + coupon * 4m / 365m, 0.000000000001m);
        paid.Income.BookedTarget!.BookedTransactionTarget.Should().Be(100m);
        BondIncomeCouponRounding.DeferredEarnedFlows(paid.Instruction.BondIncome!).Should().BeEmpty();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task AdoptedSeparatedAccrualPreviewKeepsParBasisAndSignedEarnedDeferredCouponAcrossPayment(int coupon)
    {
        var f = new Fixture(separatedAccrual: true);
        await ConfigureLaggedWorkflowAsync(f, coupon);
        var request = f.Request with { ThroughDate = new(2026, 1, 6), KnownAt = new(2026, 1, 12, 0, 0, 0, TimeSpan.Zero) };
        var lag = await f.Service.PreviewAsync(request);
        lag.Income.CouponIncome.Should().BeApproximately(coupon + coupon * 5m / 365m, 0.000000000001m);
        lag.Instruction.BondIncome!.CurrentAccruedInterest.Should().Be(coupon * 5m / 365m);
        lag.Income.BookedTarget!.BookedTransactionTarget.Should().Be(100m);
        OpenLotAmortization.Project(lag.Instruction).TransactionMovement.Should().Be(0m);
        lag.Instruction.BondIncome.CouponCashAllocations.Should().BeEmpty("no future holder pool is assumed before payment");
        var paid = await f.Service.PreviewAsync(request with { ThroughDate = new(2026, 1, 11) });
        paid.Income.CouponIncome.Should().BeApproximately(coupon + coupon * 10m / 365m, 0.000000000001m);
        paid.Income.BookedTarget!.BookedTransactionTarget.Should().Be(100m);
        BondIncomeCouponRounding.DeferredEarnedFlows(paid.Instruction.BondIncome!).Should().BeEmpty();
        await f.Workflow.Received().CalculateIncomeScheduleAsync(Arg.Is<BondWorkflowRequest>(input =>
            input.ModelVersion == BondIncomeCalculator.SeparatedAccrualWorkflowModelVersion), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await f.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LegacyPaymentLagPreparationRequiresPolicyReview_AndNewBookModeCannotRewriteOldAcquisition()
    {
        var legacy = new Fixture();
        await ConfigureLaggedWorkflowAsync(legacy, 5m);
        var prepare = () => legacy.Service.PreviewAsync(legacy.Request with
        { ThroughDate = new(2026, 1, 6), KnownAt = new(2026, 1, 12, 0, 0, 0, TimeSpan.Zero) });
        await prepare.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Legacy coupon accrual*policy*opening authority*");
        var current = new Fixture(separatedAccrual: true);
        current.Lot = current.Lot with
        {
            Acquisition = current.Lot.Acquisition! with
            { BondIncome = current.Lot.Acquisition.BondIncome! with { AccrualPolicyVersion = null } }
        };
        var mismatch = () => current.Service.PreviewAsync(current.Request);
        await mismatch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable acquisition*opening*policy*");
        current.Workflow.ReceivedCalls().Should().BeEmpty();
        JsonSerializer.Serialize(legacy.Lot.Acquisition!.BondIncome).Should().NotContain("AccrualPolicyVersion");
    }

    private static async Task ConfigureLaggedWorkflowAsync(Fixture f, decimal coupon)
    {
        var seed = (await f.Service.PreviewAsync(f.Request)).Instruction.BondIncome!.AcquisitionProjection;
        var first = seed.CashFlows[0] with { PaymentDate = new(2026, 1, 11), Interest = coupon };
        var second = seed.CashFlows[1] with { PaymentDate = new(2027, 1, 11), Interest = coupon };
        f.Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<BondWorkflowRequest>();
            var date = request.Context.SettlementDate!.Value;
            var part = date >= new DateOnly(2026, 1, 1) && date < new DateOnly(2027, 1, 1)
                ? (date.DayNumber - new DateOnly(2026, 1, 1).DayNumber) / 365m : 0m;
            var deferred = date >= first.AccrualEnd && date < first.PaymentDate ? coupon : 0m;
            var legacy = request.ModelVersion == BondWorkflowModelVersions.Legacy;
            var active = legacy && deferred != 0m ? deferred : coupon * part;
            return seed with
            {
                ModelVersion = request.ModelVersion!,
                Context = request.Context,
                CashFlows = new[] { first, second }.Where(flow => flow.PaymentDate > date).ToArray(),
                AccruedInterest = active + (legacy ? 0m : deferred),
                ContractualAccruedInterest = active,
                DeferredCouponInterest = legacy ? null : deferred,
                SettlementAccrualFraction = part,
                SettlementPeriodAccrualFraction = 1m,
                SettlementAccrualPeriodUnits = part,
                SettlementPeriodUnits = 1m
            };
        });
    }

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
            BookId: book.LedgerBookId.ToString("D"))
        { PositionId = f.Lot.BookPositionId };
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
            BookContext = f.Position.BookContext with { PeriodId = periodId },
            BookPositionId = f.Lot.BookPositionId,
            EconomicEvent = economic,
            ProjectionLineage = lineage,
            RetainedEvidence = evidence,
            RulePackReference = new(pack, "v1", rule, "v1"),
            AssetLotMutation = income.LotMutation
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
            .Returns(call => seed with
            {
                Context = call.Arg<BondWorkflowRequest>().Context,
                AccruedInterest = -0.1m,
                ContractualAccruedInterest = 4.9m
            });
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
                return seed with
                {
                    Context = input.Context,
                    AccruedInterest = held - (detached ? coupon : 0m),
                    ContractualAccruedInterest = held,
                    CashFlows = seed.CashFlows.Where(flow => flow.PaymentDate > settlement).Select(flow => flow with
                    { Interest = detached && flow.PaymentDate == new DateOnly(2026, 1, 1) ? 0m : coupon }).ToArray()
                };
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
        var principal = coupon with
        {
            PaymentDate = terminal,
            AccrualStart = terminal,
            AccrualEnd = terminal,
            Interest = 0m,
            Principal = 100m,
            PeriodUnits = 0m,
            AccrualFraction = 0m
        };
        f.Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<BondWorkflowRequest>();
                return seed with
                {
                    Context = input.Context,
                    CashFlows = [principal, coupon],
                    AccruedInterest = input.Context.SettlementDate == terminal ? raw : 0m,
                    ContractualAccruedInterest = input.Context.SettlementDate == terminal ? raw : 0m
                };
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

    [Fact]
    public async Task UntaggedEraAcquisitionSecurityEvidence_KeepsMatchingAfterTheTermsAreStamped_AndChangedTermsAreRefused()
    {
        var f = new Fixture();
        // The acquisition retained Security Master evidence hashed over the untagged terms; they now read back stamped.
        var retainedHash = OpenLotAmortization.SecurityHash(f.Security);
        SecurityTermsEnvelope.GetDeclaredFormat(f.Security.AssetSpecificTerms).Should().BeNull();
        var stamped = f.Security with { AssetSpecificTerms = SecurityTermsEnvelope.StampDeclaredFormat(f.Security.AssetSpecificTerms) };
        SecurityTermsEnvelope.GetDeclaredFormat(stamped.AssetSpecificTerms).Should().Be(SecurityTermsFormats.Flat);
        OpenLotAmortization.SecurityHash(stamped).Should().NotBe(retainedHash);
        f.Securities.GetProjectionAsync(f.Security.SecurityId, Arg.Any<CancellationToken>()).Returns(stamped);

        var preview = await f.Service.PreviewAsync(f.Request);

        SecurityTermsEnvelope.GetDeclaredFormat(preview.Instruction.Security!.AssetSpecificTerms).Should().Be(SecurityTermsFormats.Flat);
        preview.Instruction.SecurityEvidence!.ContentHashSha256.Should().Be(retainedHash);
        // BondIncomeCalculator.ProjectCanonical binds the same untagged-era evidence to the stamped projection.
        OpenLotAmortization.Project(preview.Instruction).TransactionCostBasis
            .Should().Be(preview.Instruction.BondIncome!.BookedTarget!.BookedTransactionTarget);
        (await f.Service.PrepareAsync(new(f.Request, preview.Fingerprint))).Fingerprint.Should().Be(preview.Fingerprint);

        f.Securities.GetProjectionAsync(f.Security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(stamped with { DisplayName = stamped.DisplayName + " (amended)" });
        await f.Service.Invoking(s => s.PreviewAsync(f.Request)).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*retained evidence matching the current Security Master terms*");
        var amended = preview.Instruction with { Security = preview.Instruction.Security! with { DisplayName = "Amended" } };
        var project = () => OpenLotAmortization.Project(amended);
        project.Should().Throw<ArgumentException>().WithMessage("*hash-bound authoritative Security Master*");
        await f.Lots.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
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

    private sealed class RealCurveIncomeFixture : IBondWorkflowService
    {
        private readonly BondWorkflowService _workflow;
        public Fixture Base { get; }
        public BondIncomeService Service { get; }
        public BondIncomePreviewRequestDto Request { get; }
        public LedgerTaxLotRecord Peer { get; }
        public ISecurityCurveStore Curves { get; } = Substitute.For<ISecurityCurveStore>();
        public SecurityCurveDefinitionDto Definition { get; private set; }
        public SecurityCurveDefinitionDto? FutureDefinition { get; set; }
        public SecurityCurveVersionDto Version { get; }
        public BondPinnedCurveInput Pin => new("Discount", Definition.CurveId, Version.CurveVersionId);
        public BondWorkflowRequest? PeerRequest { get; private set; }
        public BondWorkflowResult? PeerProjection { get; private set; }
        public int PeerCalculations { get; private set; }
        public Func<BondWorkflowResult, BondWorkflowResult>? MutatePeer { get; set; }

        public RealCurveIncomeFixture(decimal coupon)
        {
            Base = new(separatedAccrual: true, declaredCoupon: coupon);
            Request = Base.Request with { ThroughDate = new(2026, 1, 5), KnownAt = new(2026, 1, 6, 0, 0, 0, TimeSpan.Zero) };
            Peer = Base.Lot with { TaxLotRecordId = Guid.NewGuid(), LotId = "real-curve-peer", AcquiredDate = new(2025, 1, 2), Version = 2 };
            Base.Lots.ListOpenTaxLotsByAssetScopeAsync(Base.Book.LedgerBookId, Base.Lot.SecurityId, Base.Lot.BookPositionId,
                Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => (IReadOnlyList<LedgerTaxLotRecord>)[Base.Lot, Peer]);
            var query = Substitute.For<ISecurityMasterQueryService>();
            query.GetRecordedByIdAsOfAsync(Base.Lot.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Base.Security.ToDetail());
            query.GetByIdAsync(Base.Lot.SecurityId, Arg.Any<CancellationToken>()).Returns(Base.Security.ToDetail());
            var recorded = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero);
            Definition = new(Guid.NewGuid(), "USD-INCOME-DISCOUNT", SecurityCurveKind.Discount, "USD", null, null,
                "ACT/365", SecurityCurveInterpolation.Linear, "Synthetic", null, "synthetic-retainer", recorded);
            var asOf = new DateOnly(2025, 1, 1);
            SecurityCurvePointDto[] points = [new(asOf, 1m), new(new(2026, 1, 1), .96m), new(new(2027, 1, 10), .92m)];
            Version = new(Guid.NewGuid(), Definition.CurveId, Definition.CurveKey, 1, asOf, recorded, recorded,
                "Synthetic", "synthetic-retainer", points, SecurityCurveRules.ContentSha256(asOf, points));
            Curves.ListDefinitionsAsync(SecurityCurveKind.Discount, "USD", null, Arg.Any<CancellationToken>())
                .Returns(_ => (IReadOnlyList<SecurityCurveDefinitionDto>)(FutureDefinition is { } future
                    ? new[] { Definition, future } : new[] { Definition }));
            Curves.GetLatestVersionAsOfAsync(Definition.CurveId, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(Version);
            Curves.GetDefinitionAsync(Definition.CurveId, Arg.Any<CancellationToken>()).Returns(_ => Definition);
            Curves.GetVersionByIdAsync(Definition.CurveId, Version.CurveVersionId, Arg.Any<CancellationToken>()).Returns(Version);
            _workflow = new(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), Curves);
            Service = new(Base.Lots, Base.Positions, Base.Securities, Base.Policies, this);
        }

        public Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default)
            => _workflow.CalculateAsync(request, ct);

        public async Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt,
            CancellationToken ct = default)
        {
            var projection = await _workflow.CalculateIncomeScheduleAsync(request, freezeIndexAt, ct);
            if (request.Context.SettlementDate == Peer.AcquiredDate)
            {
                PeerCalculations++;
                PeerRequest = request;
                projection.Should().NotBeNull();
                projection!.IsBlocked.Should().BeFalse(string.Join("; ", projection.Issues.Select(issue => issue.Reason)));
                PeerProjection = projection;
                return MutatePeer?.Invoke(projection) ?? projection;
            }
            return projection;
        }

        public Task<BondWorkflowResult?> ReplayAsync(BondWorkflowResult retained, CancellationToken ct = default)
            => _workflow.ReplayAsync(retained, ct);

        public BondWorkflowResult ChangeDefinitionAdmission(BondWorkflowResult projection)
        {
            Definition = Definition with { CreatedAt = Request.KnownAt.AddTicks(1) };
            return projection;
        }
    }

    private sealed class Fixture
    {
        public ILedgerJournalStore Lots { get; } = Substitute.For<ILedgerJournalStore, ILedgerTaxLotDisposalHistory>();
        public IInstrumentPositionProjectionStore Positions { get; } = Substitute.For<IInstrumentPositionProjectionStore>();
        public ISecurityMasterStore Securities { get; } = Substitute.For<ISecurityMasterStore>();
        public IAccountingPolicyService Policies { get; } = Substitute.For<IAccountingPolicyService>();
        public IBondWorkflowService Workflow { get; } = Substitute.For<IBondWorkflowService>();
        public LedgerBookRecord Book { get; }
        public SecurityProjectionRecord Security { get; }
        public LedgerTaxLotRecord Lot { get; set; }
        public Guid AccountId { get; } = Guid.NewGuid();
        public BookPositionDto Position { get; set; }
        public BondIncomeFloaterPolicy FloaterPolicy { get; set; } = BondIncomeFloaterPolicy.AcquisitionIndex;
        public string? AccrualPolicyVersion { get; }
        public string InputHash { get; set; } = new string('a', 64);
        public BondIncomePreviewRequestDto Request { get; }
        public BondIncomeService Service { get; }

        public Fixture(bool exCoupon = false, decimal initialCost = 100m, bool separatedAccrual = false, decimal? declaredCoupon = null,
            bool earlyPayment = false)
        {
            AccrualPolicyVersion = separatedAccrual ? BondIncomeCalculator.SeparatedAccrualPolicyVersion : null;
            var date = new DateOnly(2025, 1, 1);
            var at = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var securityId = Guid.NewGuid();
            var positionId = Guid.NewGuid();
            var lotId = Guid.NewGuid();
            var security = new SecurityProjectionRecord(securityId, "Bond", SecurityStatusDto.Active, "Synthetic", "USD", "ISIN",
                "SYNTHETIC", JsonSerializer.SerializeToElement(new { paymentFrequency = "Annual", dayCountConvention = "ACT/365F" }),
                exCoupon ? JsonSerializer.SerializeToElement(new { assetClassTermsVersion = 5, bondWorkflow = new { exCouponBusinessDays = 5 } }) : JsonSerializer.SerializeToElement(new { }),
                JsonSerializer.SerializeToElement(new { }), 1, at, null, [], []);
            if (declaredCoupon is { } rate)
                security = security with
                {
                    AssetSpecificTerms = JsonSerializer.SerializeToElement(new
                    {
                        assetClassTermsVersion = exCoupon ? 5 : 2,
                        issueDate = "2025-01-01",
                        maturity = earlyPayment ? "2028-01-01" : "2027-01-01",
                        couponType = "Fixed",
                        couponRate = rate,
                        dayCount = "ACT/365F",
                        par = 100m,
                        paymentFrequency = "Annual",
                        scheduleConventions = new
                        {
                            accrualAdjusted = false,
                            couponRateUnit = "Percent",
                            couponCashBasis = "DayCount",
                            paymentLagBusinessDays = earlyPayment ? 0 : 2,
                            businessDayConvention = earlyPayment ? "Preceding" : "Unadjusted",
                            paymentCalendars = new[] { "WEEKENDS" }
                        },
                        bondWorkflow = new { exCouponBusinessDays = exCoupon ? 5 : 0 }
                    })
                };
            Security = security;
            var lotEvidence = Evidence("lot", "OpenLotAcquisition", lotId.ToString("D"), new string('a', 64));
            var termsEvidence = Evidence("terms", "SecurityMasterProjection", securityId.ToString("D"), OpenLotAmortization.SecurityHash(security));
            var profile = new BondIncomeAcquisitionDto(at, initialCost, 0m, 1m, [], [lotEvidence, termsEvidence])
            { AccrualPolicyVersion = AccrualPolicyVersion };
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
                        { AccrualPolicyVersion = AccrualPolicyVersion }
                    }), at));
            Workflow.CalculateIncomeScheduleAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call => new BondWorkflowResult(securityId, "USD", call.ArgAt<BondWorkflowRequest>(0).ModelVersion ?? BondWorkflowModelVersions.Legacy,
                    call.ArgAt<BondWorkflowRequest>(0).Context, 100m, 100m, 0m, null, null, 0m, null, null, null, null,
                    null, null, null,
                    [new BondWorkflowCashFlow(new(2026, 1, 1), date, new(2026, 1, 1), 5m, 0m, 100m, 1m, false, [])
                        { AccrualFraction = 1m, PeriodUnits = 1m },
                     new BondWorkflowCashFlow(new(2027, 1, 1), new(2026, 1, 1), new(2027, 1, 1), 5m, 100m, 0m, 1m, false, [])
                        { AccrualFraction = 1m, PeriodUnits = 1m }],
                    [new("SecurityTerms", securityId.ToString("D"), 1, InputHash)], [], [])
                {
                    SettlementIndexRatio = 1m,
                    SettlementIndexIsObserved = true,
                    ContractualAccruedInterest = 0m,
                    DeferredCouponInterest = separatedAccrual ? 0m : null,
                    HasDetachedCoupon = separatedAccrual ? false : null
                });
            Request = new(Book.LedgerBookId, lotId, new(2026, 1, 1), new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), AccountId);
            Service = new(Lots, Positions, Securities, Policies, Workflow);

            RetainedEvidenceIdentityDto Evidence(string id, string subjectType, string subjectId, string hash)
                => new(id, "urn:synthetic:" + id, hash, "Synthetic", id, "Accepted", "reviewer", at, date,
                    1, at, "reviewer", subjectType, subjectId);
        }
    }
}
