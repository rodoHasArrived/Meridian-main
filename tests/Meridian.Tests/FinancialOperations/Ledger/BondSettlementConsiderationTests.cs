using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondSettlementConsiderationTests
{
    private static readonly Guid SecurityId = Guid.Parse("d7ad7a66-7cdf-4f79-b07e-a1a5e99156de");
    private static readonly DateTimeOffset AsOf = new(2030, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly BondCalculationContext Context = new(AsOf, new DateOnly(2030, 1, 15));

    [Fact]
    public async Task Preparation_ScalesFaceAndFactorExactlyOnce_AndSeparatesPurchasedInterestAndFees()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>()).Returns(Analytics());
        var service = new BondSettlementConsiderationService(analytics);

        var result = await service.PrepareAsync(new(new(SecurityId, Context, CleanPrice: 101.25m),
            100_000m, 0.8m, 25m, 82_225m, "cash-source", "Assets/Cash", "Assets/PurchasedInterest", "Expenses/Fees"));

        result.IsBlocked.Should().BeFalse();
        result.Settlement!.CleanConsideration.Should().Be(81_000m);
        result.Settlement.PurchasedAccruedInterest.Should().Be(1_200m);
        result.Settlement.Fees.Should().Be(25m);
        result.Settlement.DirtyConsideration.Should().Be(82_225m);
        (await service.VerifyAsync(result.Settlement)).Should().BeEmpty();
    }

    [Fact]
    public async Task ObservedDirtyCashMismatch_IsBlockedWithoutReceipt()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>()).Returns(Analytics());
        var result = await new BondSettlementConsiderationService(analytics).PrepareAsync(new(
            new(SecurityId, Context, CleanPrice: 101.25m), 100_000m, 0.8m, 25m, 82_224.99m,
            "cash-source", "Assets/Cash", "Assets/PurchasedInterest", "Expenses/Fees"));
        result.IsBlocked.Should().BeTrue();
        result.Settlement.Should().BeNull();
        result.Blockers.Should().Contain(blocker => blocker.Reason.Contains("reconcile exactly"));
    }

    [Fact]
    public async Task Verification_RejectsCallerAuthoredMetricsEvenWhenReceiptArithmeticBalances()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>()).Returns(Analytics() with { AccruedInterest = 1.6m });
        var blockers = await new BondSettlementConsiderationService(analytics).VerifyAsync(Receipt());
        blockers.Should().ContainSingle().Which.Code.Should().Be("SettlementInputsChanged");
    }

    [Fact]
    public async Task ReceiptCannotBypassMissingServerAnalyticsVerification()
    {
        var act = () => BondSettlementPostingGuard.RequireVerifiedAsync(Acquisition(), null, default);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*caller-authored*");
    }

    [Fact]
    public void Acquisition_CannotCapitalizePurchasedInterestOrApplyFactorTwice()
    {
        BondSettlementConsideration.ValidateAcquisition(Acquisition()).Should().BeEmpty();
        BondSettlementConsideration.ValidateAcquisition(Acquisition() with { UnitCost = 82.2m })
            .Should().Contain(issue => issue.Contains("separate from clean acquisition"));
        BondSettlementConsideration.Validate(Receipt() with { CleanConsideration = 64_800m })
            .Should().Contain(issue => issue.Contains("reconcile exactly"));
    }

    [Fact]
    public void PostingGuard_RequiresSeparateReceivableExpenseAndObservedCashLines()
    {
        var (candidate, journal) = Journal();
        var act = () => BondSettlementPostingGuard.RequireJournal(Acquisition(), candidate, journal);
        act.Should().NotThrow();
        var changed = candidate with
        {
            GeneratedPostingLines = candidate.GeneratedPostingLines.Select(line =>
                line.AccountPath == "Assets/PurchasedInterest" ? line with { AccountPath = "Revenue/Interest" } : line).ToArray()
        };
        var wrong = () => BondSettlementPostingGuard.RequireJournal(Acquisition(), changed, journal);
        wrong.Should().Throw<InvalidOperationException>().WithMessage("*exactly one retained journal line*");
    }

    [Fact]
    public void PostingGuard_RejectsWrongSecurityVersionAndCurrency()
    {
        var wrongVersion = () => BondSettlementPostingGuard.RequireScope(Acquisition(), SecurityId, 8, "USD");
        wrongVersion.Should().Throw<ArgumentException>();
        var wrongCurrency = () => BondSettlementPostingGuard.RequireScope(Acquisition(), SecurityId, 7, "EUR");
        wrongCurrency.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AbsentSettlementReceipt_PreservesLegacyAcquisitionInstruction()
    {
        var legacy = Acquisition() with { SettlementConsideration = null };
        BondSettlementConsideration.ValidateAcquisition(legacy).Should().BeEmpty();
        var (candidate, journal) = Journal();
        var act = () => BondSettlementPostingGuard.RequireJournal(legacy, candidate, journal);
        act.Should().NotThrow();
    }

    [Fact]
    public void HeldSettlementReceipt_CannotBeReusedForShortOpening()
    {
        var instruction = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Acquire, Acquisition())
        {
            Direction = OpenLotDirection.Short
        };
        var issues = AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.Acquisition,
            instruction, 81_000m, Context.SettlementDate!.Value, []);
        issues.Should().Contain(issue => issue.Contains("held Long acquisitions only"));
    }

    private static BondSettlementConsiderationDto Receipt() => new(SecurityId, "USD", "bond-analytics-v1", Context,
        BondYieldConvention.NominalPeriodic, 100_000m, 0.8m, 101.25m, 1.5m, 81_000m, 1_200m, 25m,
        82_225m, 82_225m, "cash-source", "Assets/Cash", "Assets/PurchasedInterest", "Expenses/Fees", Inputs());

    private static AssetAcquisitionLotDto Acquisition() => new(Guid.NewGuid(), "bond-lot", Context.SettlementDate!.Value,
        1_000m, 81m, "Assets/Bonds", 100_000m, 0.8m, 100m, BondAmortizationMethod.NoAmortization,
        SettlementConsideration: Receipt());

    private static BondCalculationInput[] Inputs() => [new("SecurityTerms", SecurityId.ToString("D"), 7, new string('a', 64))];

    private static BondAnalyticsResult Analytics() => new(SecurityId, "USD", "bond-analytics-v1", Context,
        BondYieldConvention.NominalPeriodic, 101.25m, 102.75m, 1.5m,
        0.05m, 0.049m, 4m, 3.9m, 20m, 0.04m, 0.05m, [], [], Inputs(), [], [], []);

    private static (PostingRuleJournalCandidateResultDto Candidate, JournalEntry Journal) Journal()
    {
        var id = Guid.NewGuid();
        var values = new[]
        {
            ("Assets/Bonds", 81_000m, 0m, LedgerAccountType.Asset),
            ("Assets/PurchasedInterest", 1_200m, 0m, LedgerAccountType.Asset),
            ("Expenses/Fees", 25m, 0m, LedgerAccountType.Expense),
            ("Assets/Cash", 0m, 82_225m, LedgerAccountType.Asset)
        };
        var lines = values.Select(value => new LedgerEntry(Guid.NewGuid(), id, AsOf,
            new LedgerAccount(value.Item1, value.Item4), value.Item2, value.Item3, "Settlement",
            currency: new LedgerEntryCurrency("USD", "USD", value.Item2, value.Item3, 1m))).ToArray();
        var generated = values.Select((value, index) => new GeneratedPostingLineDto(index.ToString(), value.Item1,
            value.Item2 > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
            "retained", value.Item2 + value.Item3, "USD")).ToArray();
        var candidate = new PostingRuleJournalCandidateResultDto(null!, null, null, generated, null, null,
            82_225m, 82_225m, 0m, true, false, true, false, [], []);
        return (candidate, new JournalEntry(id, AsOf, "Bond settlement", lines));
    }
}
