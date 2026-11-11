using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

/// <summary>
/// Security-master review D10, remediation slice 4: one declared rate yields identical money in the
/// Security Master cash-flow projection, the contractual cash-flow preview and the accounting accrual,
/// because all three read the rate through <see cref="RateUnitNormalizer"/>. An undeclared legacy rate is
/// flagged by every reader and converted by none.
/// </summary>
public sealed class DeclaredRateUnitParityTests
{
    private const decimal Par = 1_000_000m;
    private static readonly DateOnly Issue = new(2041, 1, 1);
    private static readonly DateOnly Maturity = new(2042, 1, 1);
    private static readonly DateTimeOffset Cutoff = new(2041, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<decimal, string, decimal> DeclaredCoupons => new()
    {
        // raw, declared couponRateUnit, expected fraction
        { 0.5m, "Percent", 0.005m },
        { 0.5m, "Fraction", 0.5m },
        { 4.25m, "Percent", 0.0425m },
        { 0.0425m, "Fraction", 0.0425m },
        { 0m, "Percent", 0m }
    };

    [Theory]
    [MemberData(nameof(DeclaredCoupons))]
    public async Task ADeclaredCoupon_ProducesIdenticalQuarterlyInterest_InEveryReader(decimal raw, string unit, decimal fraction)
    {
        var expectedQuarter = decimal.Round(Par * fraction * 0.25m, 2);
        var security = DeclaredBond(raw, unit);

        var projection = await ProjectAsync(security);
        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Should().HaveCount(4);
        projection.Schedule.Select(static row => decimal.Round(row.InterestAmount, 2))
            .Should().AllBeEquivalentTo(expectedQuarter, "the cash-flow projection reads the declared unit");

        var preview = await PreviewAsync(security);
        preview.Blockers.Should().BeEmpty();
        if (fraction == 0m)
        {
            preview.Events.Sum(static e => e.CashInterest).Should().Be(0m);
        }
        else
        {
            preview.Events.Where(static e => e.AccrualStart is not null).Select(static e => decimal.Round(e.CashInterest, 2))
                .Should().HaveCount(4).And.AllBeEquivalentTo(expectedQuarter, "the preview reads the same declared unit");
        }

        var accounting = Account(raw, RateUnitNormalizer.ParseCouponRateUnit(unit));
        accounting.Issues.Should().NotContain(static issue => issue.Code.StartsWith("SM_COUPON_RATE", StringComparison.Ordinal));
        if (fraction == 0m)
        {
            accounting.ExpectedEvents.Should().BeEmpty("a zero coupon accrues nothing");
        }
        else
        {
            accounting.AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(expectedQuarter);
            accounting.ExpectedEvents.Where(static e => e.EventKind == ExpectedAccountingEventKindDto.ReceiveCashInterest)
                .Should().ContainSingle().Which.ExpectedAmount.Should().Be(expectedQuarter);
        }
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(0.5)]
    [InlineData(4.25)]
    public async Task AnUndeclaredLegacyCoupon_IsFlaggedByEveryReader_AndConvertedByNone(double rawValue)
    {
        // The retired heuristic read 0.5 as fifty percent and divided 5.0 by 100. Neither happens now.
        var raw = (decimal)rawValue;
        var security = UndeclaredBond(raw);

        var projection = await ProjectAsync(security);
        projection.Schedule.Should().BeEmpty();
        projection.BlockedReason.Should().Contain("couponRate").And.Contain("has no declared unit");

        var preview = await PreviewAsync(security);
        preview.Events.Should().BeEmpty();
        preview.Blockers.Should().ContainSingle().Which.Code.Should().Be(RateUnitNormalizer.UndeclaredCode);

        var accounting = Account(raw, recordUnit: null);
        accounting.AccrualCalculations.Should().BeEmpty();
        accounting.ExpectedEvents.Should().BeEmpty();
        accounting.Issues.Should().ContainSingle(static issue => issue.Code == "SM_COUPON_RATE_UNIT_UNDECLARED")
            .Which.Severity.Should().Be(ReconciliationBreakSeverity.High);
    }

    [Fact]
    public async Task AnUnsupportedNegativeCoupon_IsRefusedConsistently()
    {
        // Bond coupons declare no negative-rate support; every reader refuses rather than posting.
        var security = DeclaredBond(-0.25m, "Percent");

        (await ProjectAsync(security)).BlockedReason.Should().Contain("does not permit a negative rate");
        (await PreviewAsync(security)).Blockers.Should().ContainSingle().Which.Code.Should().Be(RateUnitNormalizer.OutOfRangeCode);
        Account(-0.25m, RateUnit.Percent).Issues.Should().ContainSingle(static issue => issue.Code == "SM_COUPON_RATE_OUT_OF_RANGE");
    }

    [Fact]
    public async Task ASupportedNegativeDepositRate_ResolvesToTheSameFraction_InBothCashFlowReaders()
    {
        // A negative Deposit rate is a valid retained fact. The calculation-only preview supports
        // signed interest; the unsigned cash-flow/posting reader reports a typed capability refusal.
        var security = Deposit(-0.005m);

        var projection = await ProjectAsync(security);
        projection.Blocker.Should().Be(SecurityInterestSupport.NegativeInterestBlocker);
        projection.BlockedReason.Should().Be(SecurityInterestSupport.NegativeInterestReason);
        projection.Schedule.Should().BeEmpty();

        var preview = await PreviewAsync(security);
        preview.Blockers.Should().BeEmpty();
        preview.Events.Where(static e => e.AccrualStart is not null).Select(static e => decimal.Round(e.CashInterest, 2))
            .Should().AllBeEquivalentTo(decimal.Round(Par * -0.005m * 0.25m, 2));
    }

    [Fact]
    public async Task ABasisPointSpread_IsBasisPointsInBothCashFlowReaders()
    {
        // A single floating swap leg with a zero observed fixing: zero needs no unit, and the 125 bp
        // spread is basis points by name in both readers.
        var security = FloatingSwap(spreadBps: 125m);
        var expectedQuarter = decimal.Round(Par * 0.0125m * 0.25m, 2);

        var projection = await ProjectAsync(security);
        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => decimal.Round(row.InterestAmount, 2)).Should().AllBeEquivalentTo(expectedQuarter);

        RateUnitNormalizer.Normalize("Swap", "legs.spreadBps", 125m).TryGetFraction(out var spread).Should().BeTrue();
        spread.Should().Be(0.0125m);
    }

    [Fact]
    public async Task V2NegativeDepositReceipt_ReplaysItsRetainedZeroFloor_WhileV3BlocksNewCalculations()
    {
        var security = Deposit(-0.005m);
        var source = new SecurityCashFlowSourceDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            Cutoff, false, null, null);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(source);
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetRecordedByIdAsOfAsync(security.SecurityId, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var receipts = Substitute.For<ISecurityCashFlowReceiptStore>();
        var retained = new Dictionary<string, SecurityCashFlowCalculationReceipt>();
        receipts.RetainAsync(Arg.Any<SecurityCashFlowCalculationReceipt>(), Arg.Any<CancellationToken>())
            .Returns(call => { var receipt = call.Arg<SecurityCashFlowCalculationReceipt>(); retained[receipt.ContentSha256] = receipt; return Task.CompletedTask; });
        receipts.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(retained.GetValueOrDefault(call.Arg<string>())));
        var service = new SecurityMasterCashFlowService(store, [], query,
            NullLogger<SecurityMasterCashFlowService>.Instance, receiptStore: receipts);
        var current = (await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base,
            new BondCalculationContext(Cutoff, KnownAt: Cutoff)))!;
        current.ModelVersion.Should().Be("bond-cash-flow-context-v3");
        current.Projection.Blocker.Should().Be(SecurityInterestSupport.NegativeInterestBlocker);
        current.Projection.Schedule.Should().BeEmpty();

        // Exact v2 output: four contractual dates, principal at maturity, and the old zero floor.
        var oldProjection = current.Projection with
        {
            Schedule = Enumerable.Range(1, 4).Select(quarter => new StructuredCashFlowScheduleEntry(
                new DateTimeOffset(Issue.AddMonths(quarter * 3).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                quarter == 4 ? Par : 0m, 0m, quarter == 4 ? 0m : 1m)).ToArray(),
            BlockedReason = null,
            Blocker = null,
            InputManifest = current.Projection.InputManifest!.Select(input => input.Kind == "CalculationModel"
                ? input with { Key = SecurityMasterCashFlowService.PreviousContextCalculationVersion } : input).ToArray()
        };
        var old = current with
        {
            ModelVersion = SecurityMasterCashFlowService.PreviousContextCalculationVersion,
            Projection = oldProjection,
            ContentSha256 = string.Empty
        };
        old = old with { ContentSha256 = SecurityMasterCashFlowService.ReceiptHash(old) };
        retained[old.ContentSha256] = old;

        var replay = (await service.ReplayProjectionAsync(old.ContentSha256))!;
        replay.BlockedReason.Should().BeNull();
        replay.Blocker.Should().BeNull();
        replay.Schedule.Should().Equal(oldProjection.Schedule);
    }

    [Fact]
    public void NegativeFixing_WithAContractualSpreadOrFloor_UsesTheEffectiveCoupon()
    {
        var evidence = new BondFloatingRateEvidence(Issue, false, "SOFR", -0.005m, 0.01m,
            null, null, Par, 0.25m, SecurityCouponRateUnit.Fraction, "synthetic");

        evidence.TryCoupon(0m, out var positive, out _, out var blocker).Should().BeTrue();
        positive.Should().Be(1250m);
        blocker.Should().BeNull();
        evidence.TryCoupon(-0.01m, out _, out _, out blocker).Should().BeFalse();
        blocker.Should().Be(SecurityInterestSupport.NegativeInterestBlocker);
        (evidence with { Floor = 0m }).TryCoupon(-0.01m, out var floored, out _, out blocker).Should().BeTrue();
        floored.Should().Be(0m, "an explicit contractual floor is applied before the signed-interest guard");
        blocker.Should().BeNull();
    }

    [Fact]
    public async Task ANegativeFloatingCoupon_BlocksAllLegs_InsteadOfReturningAZeroCoupon()
    {
        var projection = await ProjectAsync(FloatingSwap(spreadBps: -125m));

        projection.Blocker.Should().Be(SecurityInterestSupport.NegativeInterestBlocker);
        projection.Schedule.Should().BeEmpty();
        projection.LegSchedules.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ANegativeFixedSwapCoupon_IsAValidRateButAnUnsupportedInterestModel()
    {
        var swap = FloatingSwap(0m);
        var terms = JsonNode.Parse(swap.AssetSpecificTerms.GetRawText())!.AsObject();
        var leg = terms["legs"]![0]!.AsObject();
        leg["legType"] = "Fixed";
        leg["fixedRate"] = -0.005m;
        terms["assetClassTermsVersion"] = 4;
        terms["rateUnit"] = "Fraction";
        swap = swap with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };

        var projection = await ProjectAsync(swap);

        projection.Blocker.Should().Be(SecurityInterestSupport.NegativeInterestBlocker);
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public void AccountingWithASignedCouponField_ReportsACapabilityIssueAndNoAccrual()
    {
        var id = Guid.NewGuid();
        var security = new SecurityMasterAccountingSecurity(id, "SIGNED", "Bond", "USD",
            new SecurityFixedIncomeTerms(-0.005m, "Fixed", "30/360", 4,
                IssueDate: Issue, MaturityDate: Maturity)
            { TermsAssetClass = "Deposit" }, new SecurityAccountingRule("AvailableForSale", "GAAP"));
        var result = new SecurityMasterAccountingEventService().Generate(new SecurityMasterAccountingEventRequest(
            "negative-rate", Issue, Issue.AddMonths(3), [security],
            [new SecurityMasterAccountingPosition("SIGNED", id, "account", Par)]));

        result.Issues.Should().ContainSingle(issue => issue.Code == SecurityInterestSupport.NegativeInterestUnsupported);
        result.AccrualCalculations.Should().BeEmpty();
        result.ExpectedEvents.Should().BeEmpty();
        result.JournalPreviews.Should().BeEmpty();
    }

    [Fact]
    public void AStructuredNote_CannotReachBondPosting_ThroughItsFixedIncomeTaxonomy()
    {
        // The adapter offers type name, sub-type, then taxonomy. Before slice 4 the FixedIncome
        // taxonomy fallback resolved StructuredNote to Bond; it is now explicitly outside the slice.
        var accountingClass = SecurityAssetClassCatalog.ResolveAccountingInstrumentClass("StructuredNote", "StructuredNote", "FixedIncome");
        accountingClass.Should().BeNull();

        var securityId = Guid.NewGuid();
        var note = new SecurityMasterAccountingSecurity(securityId, "NOTE", "StructuredNote", "USD",
            new SecurityFixedIncomeTerms(0.05m, "Fixed", "30/360", 4, IssueDate: Issue, NextCouponDate: new DateOnly(2041, 3, 31), MaturityDate: Maturity)
            {
                CouponRateUnit = RateUnit.Fraction
            },
            new SecurityAccountingRule("AvailableForSale", "GAAP"));
        var result = new SecurityMasterAccountingEventService().Generate(new SecurityMasterAccountingEventRequest(
            "note-run", Issue, new DateOnly(2041, 4, 1), [note],
            [new SecurityMasterAccountingPosition("NOTE", securityId, "acct-1", Par)]));

        result.ExpectedEvents.Should().BeEmpty();
        result.JournalPreviews.Should().BeEmpty();
        result.Issues.Should().ContainSingle().Which.Code.Should().Be("SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT");
    }

    private static SecurityDetailDto DeclaredBond(decimal coupon, string unit)
        => Bond(coupon, new JsonObject { ["accrualAdjusted"] = false, ["couponRateUnit"] = unit });

    private static SecurityDetailDto UndeclaredBond(decimal coupon) => Bond(coupon, block: null);

    private static SecurityDetailDto Bond(decimal coupon, JsonObject? block)
    {
        var terms = new JsonObject
        {
            ["issueDate"] = "2041-01-01",
            ["maturity"] = "2042-01-01",
            ["couponType"] = "Fixed",
            ["couponRate"] = coupon,
            ["dayCount"] = "30/360",
            ["paymentFrequency"] = "Quarterly",
            ["isCallable"] = false,
            ["subclass"] = "Corporate",
            ["par"] = Par
        };
        if (block is not null)
        {
            terms["assetClassTermsVersion"] = 2;
            terms["scheduleConventions"] = block;
        }

        return Security("Bond", terms);
    }

    private static SecurityDetailDto Deposit(decimal rate)
        => Security("Deposit", new JsonObject
        {
            ["depositType"] = "Term",
            ["institutionName"] = "Meridian Bank",
            ["issueDate"] = "2041-01-01",
            ["maturity"] = "2042-01-01",
            ["interestRate"] = rate,
            ["dayCount"] = "30/360",
            ["paymentFrequency"] = "Quarterly",
            ["par"] = Par,
            ["isCallable"] = false
        });

    private static SecurityDetailDto FloatingSwap(decimal spreadBps)
        => Security("Swap", new JsonObject
        {
            ["effectiveDate"] = "2041-01-01",
            ["issueDate"] = "2041-01-01",
            ["maturityDate"] = "2042-01-01",
            ["maturity"] = "2042-01-01",
            ["legs"] = new JsonArray(new JsonObject
            {
                ["legId"] = "float",
                ["legType"] = "Floating",
                ["currency"] = "USD",
                ["direction"] = "Receive",
                ["index"] = "SOFR",
                ["spreadBps"] = spreadBps,
                ["currentIndexRate"] = 0m,
                ["notional"] = Par,
                ["paymentFrequency"] = "Quarterly",
                ["dayCount"] = "30/360",
                ["exchangesPrincipal"] = false
            })
        });

    private static SecurityDetailDto Security(string assetClass, JsonObject terms)
        => new(Guid.NewGuid(), assetClass, SecurityStatusDto.Active, "Rate parity " + assetClass, "USD",
            JsonSerializer.SerializeToElement(new { displayName = "Rate parity", currency = "USD" }),
            JsonSerializer.SerializeToElement(terms), [], [], 1, Cutoff.AddYears(-1), null);

    private static async Task<StructuredCashFlowProjectionDto> ProjectAsync(SecurityDetailDto security)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = new SecurityMasterCashFlowService(store, Array.Empty<IStructuredCashFlowProvider>(), query,
            NullLogger<SecurityMasterCashFlowService>.Instance);
        var projection = await service.GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base);
        projection.Should().NotBeNull();
        return projection!;
    }

    private static async Task<ContractualCashFlowResult> PreviewAsync(SecurityDetailDto security)
    {
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetRecordedByIdAsOfAsync(security.SecurityId, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var preview = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(security.SecurityId, new ContractualCashFlowRequest(Issue, Cutoff));
        preview.Should().NotBeNull();
        return preview!;
    }

    private static SecurityMasterAccountingEventResult Account(decimal coupon, RateUnit? recordUnit)
    {
        var securityId = Guid.NewGuid();
        var security = new SecurityMasterAccountingSecurity(
            securityId,
            "PARITY",
            SecurityAccountingInstrumentClasses.Bond,
            "USD",
            new SecurityFixedIncomeTerms(
                CouponRate: coupon,
                CouponType: "Fixed",
                DayCountConvention: "30/360",
                PaymentFrequencyPerYear: 4,
                IssueDate: Issue,
                NextCouponDate: new DateOnly(2041, 3, 31),
                MaturityDate: Maturity)
            {
                CouponRateUnit = recordUnit,
                TermsAssetClass = "Bond"
            },
            new SecurityAccountingRule("AvailableForSale", "GAAP"));
        var request = new SecurityMasterAccountingEventRequest(
            "parity-run",
            Issue,
            new DateOnly(2041, 4, 1),
            [security],
            [new SecurityMasterAccountingPosition("PARITY", securityId, "acct-1", Par)]);
        return new SecurityMasterAccountingEventService().Generate(request);
    }
}
