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

    [Fact]
    public void ForeignCurrencyReceipt_RequiresASettlementRate_AndRefusesARateWithoutABookCurrency()
    {
        var eur = Receipt() with { Currency = "EUR", FunctionalCurrency = "USD", SettlementFxRateToFunctional = 1.0875m };
        BondSettlementConsideration.Validate(eur).Should().BeEmpty();
        eur.IsForeignCurrency.Should().BeTrue();
        eur.ToFunctional(82_225m).Should().Be(89_419.69m, "82,225 × 1.0875 = 89,419.6875 rounds once to cents");

        BondSettlementConsideration.Validate(eur with { SettlementFxRateToFunctional = null }).Should().ContainSingle().Which.Should().Contain("positive settlement-date FX rate");
        BondSettlementConsideration.Validate(eur with { SettlementFxRateToFunctional = 0m }).Should().ContainSingle().Which.Should().Contain("positive settlement-date FX rate");
        BondSettlementConsideration.Validate(Receipt() with { SettlementFxRateToFunctional = 1.1m }).Should().ContainSingle().Which.Should().Contain("requires the functional currency");
        BondSettlementConsideration.Validate(Receipt() with { FunctionalCurrency = "USD", SettlementFxRateToFunctional = 1.1m }).Should().ContainSingle().Which.Should().Contain("same-currency");
        BondSettlementConsideration.Validate(Receipt() with { FunctionalCurrency = "usd" }).Should().ContainSingle().Which.Should().Contain("upper-case");
        BondSettlementConsideration.Validate(Receipt() with { FunctionalCurrency = "USD", SettlementFxRateToFunctional = 1m }).Should().BeEmpty();
    }

    [Fact]
    public async Task Preparation_RetainsTheBookCurrencyAndSettlementRateOnTheReceipt()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>()).Returns(Analytics() with { Currency = "EUR" });
        var result = await new BondSettlementConsiderationService(analytics).PrepareAsync(new(new(SecurityId, Context, CleanPrice: 101.25m),
            100_000m, 0.8m, 25m, 82_225m, "cash-source", "Assets/Cash", "Assets/PurchasedInterest", "Expenses/Fees", "usd", 1.0875m));

        result.IsBlocked.Should().BeFalse();
        result.Settlement!.Currency.Should().Be("EUR");
        result.Settlement.FunctionalCurrency.Should().Be("USD");
        result.Settlement.SettlementFxRateToFunctional.Should().Be(1.0875m);
        result.Settlement.DirtyConsideration.Should().Be(82_225m, "consideration stays in the bond's currency");
    }

    [Fact]
    public void PostingGuard_TranslatesEachComponentOnceAtTheSettlementRate_AndRefusesOtherRatesOrAmounts()
    {
        var eur = Receipt() with { Currency = "EUR", FunctionalCurrency = "USD", SettlementFxRateToFunctional = 1.0875m };
        var acquisition = Acquisition() with { SettlementConsideration = eur };
        var (candidate, journal) = ForeignJournal("EUR", "USD", 1.0875m, eur);
        BondSettlementPostingGuard.RequireJournal(acquisition, candidate, journal);

        var otherRate = () => BondSettlementPostingGuard.RequireJournal(acquisition, candidate, ForeignJournal("EUR", "USD", 1.0876m, eur).Journal);
        otherRate.Should().Throw<InvalidOperationException>().WithMessage("*does not match its reviewed consideration component*");
        var sameCurrencyJournal = () => BondSettlementPostingGuard.RequireJournal(acquisition, candidate, Journal().Journal);
        sameCurrencyJournal.Should().Throw<InvalidOperationException>().WithMessage("*does not match its reviewed consideration component*");
        var truncated = () => BondSettlementPostingGuard.RequireJournal(acquisition, candidate, ForeignJournal("EUR", "USD", 1.0875m, eur, functionalRounding: amount => decimal.Round(amount, 2, MidpointRounding.ToZero)).Journal);
        truncated.Should().Throw<InvalidOperationException>().WithMessage("*does not match its reviewed consideration component*");
    }

    [Fact]
    public void PostingGuard_JpyBondInUsdBook_RoundsWholeYenComponentsToCentsOnce()
    {
        // JPY has no minor units: per-100 quotes round to whole yen; each yen component translates once to cents.
        var jpy = Receipt() with
        {
            Currency = "JPY",
            FunctionalCurrency = "USD",
            SettlementFxRateToFunctional = 0.0066m,
            OriginalFace = 10_000_000m,
            BookedFactor = 1m,
            CleanPricePer100 = 100.5m,
            AccruedInterestPer100 = 0.1234m,
            CleanConsideration = 10_050_000m,
            PurchasedAccruedInterest = 12_340m,
            Fees = 1_500m,
            DirtyConsideration = 10_063_840m,
            ObservedDirtyCash = 10_063_840m
        };
        BondSettlementConsideration.Validate(jpy).Should().BeEmpty();
        jpy.ToFunctional(12_340m).Should().Be(81.44m);
        var acquisition = Acquisition() with { SettlementConsideration = jpy, Quantity = 100_000m, UnitCost = 100.5m, OriginalFace = 10_000_000m, BookedFactor = 1m };
        var (candidate, journal) = ForeignJournal("JPY", "USD", 0.0066m, jpy);
        BondSettlementPostingGuard.RequireJournal(acquisition, candidate, journal);
        journal.Lines.Single(line => line.Account.Name == "Assets/Cash").Credit.Should().Be(66_421.34m);
    }

    [Fact]
    public void SameCurrencyReceipt_SerializesWithoutDerivedFxMembers_SoRetainedFingerprintsAreUnchanged()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Receipt(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        json.Should().NotContain("functionalCurrency").And.NotContain("settlementFxRateToFunctional")
            .And.NotContain("isForeignCurrency").And.NotContain("effectiveFunctionalCurrency").And.NotContain("effectiveFxRateToFunctional");
        var foreign = System.Text.Json.JsonSerializer.Serialize(Receipt() with { Currency = "EUR", FunctionalCurrency = "USD", SettlementFxRateToFunctional = 1.0875m },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        foreign.Should().Contain("\"functionalCurrency\":\"USD\"").And.NotContain("effective").And.NotContain("isForeignCurrency");
    }

    [Fact]
    public void PostingGuard_CashCreditIsTheSumOfTranslatedComponents_SoRoundingNeverUnbalancesTheJournal()
    {
        // 81,000 / 1,200 / 25 EUR at 1.08753: the rounded components sum to 89,422.16 while the dirty total
        // translated on its own would round to 89,422.15. The cash credit follows the components.
        var eur = Receipt() with { Currency = "EUR", FunctionalCurrency = "USD", SettlementFxRateToFunctional = 1.08753m };
        var acquisition = Acquisition() with { SettlementConsideration = eur };
        var expectedCash = eur.ToFunctional(81_000m) + eur.ToFunctional(1_200m) + eur.ToFunctional(25m);
        expectedCash.Should().Be(89_422.16m);
        eur.ToFunctional(82_225m).Should().Be(89_422.15m, "translating the dirty total alone loses a cent");

        var (candidate, journal) = ForeignJournal("EUR", "USD", 1.08753m, eur, cashFunctional: expectedCash);
        BondSettlementPostingGuard.RequireJournal(acquisition, candidate, journal);
        journal.Lines.Sum(line => line.Debit).Should().Be(journal.Lines.Sum(line => line.Credit));

        var dirtyTranslatedAlone = () => BondSettlementPostingGuard.RequireJournal(acquisition, candidate, ForeignJournal("EUR", "USD", 1.08753m, eur).Journal);
        dirtyTranslatedAlone.Should().Throw<InvalidOperationException>().WithMessage("*does not match its reviewed consideration component*");
    }

    [Fact]
    public void PostingGuard_UsdBondInJpyBook_StatesWholeYenAndBalances()
    {
        var usd = Receipt() with { FunctionalCurrency = "JPY", SettlementFxRateToFunctional = 150.123m };
        BondSettlementConsideration.Validate(usd).Should().BeEmpty();
        usd.ToFunctional(1_200m).Should().Be(180_148m);
        var acquisition = Acquisition() with { SettlementConsideration = usd };
        var cash = usd.ToFunctional(81_000m) + usd.ToFunctional(1_200m) + usd.ToFunctional(25m);
        var (candidate, journal) = ForeignJournal("USD", "JPY", 150.123m, usd, functionalRounding: amount => decimal.Round(amount, 0, MidpointRounding.AwayFromZero), cashFunctional: cash);
        BondSettlementPostingGuard.RequireJournal(acquisition, candidate, journal);
        journal.Lines.Single(line => line.Account.Name == "Assets/Cash").Credit.Should().Be(cash);
    }

    private static (PostingRuleJournalCandidateResultDto Candidate, JournalEntry Journal) ForeignJournal(string transactionCurrency, string functionalCurrency, decimal fx,
        BondSettlementConsiderationDto receipt, Func<decimal, decimal>? functionalRounding = null, decimal? cashFunctional = null)
    {
        functionalRounding ??= amount => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var id = Guid.NewGuid();
        var values = new[]
        {
            ("Assets/Bonds", receipt.CleanConsideration, 0m, LedgerAccountType.Asset),
            ("Assets/PurchasedInterest", receipt.PurchasedAccruedInterest, 0m, LedgerAccountType.Asset),
            ("Expenses/Fees", receipt.Fees, 0m, LedgerAccountType.Expense),
            ("Assets/Cash", 0m, receipt.DirtyConsideration, LedgerAccountType.Asset)
        };
        var lines = values.Select(value => new LedgerEntry(Guid.NewGuid(), id, AsOf,
            new LedgerAccount(value.Item1, value.Item4), functionalRounding(value.Item2 * fx),
            value.Item3 > 0m && cashFunctional is { } cash ? cash : functionalRounding(value.Item3 * fx), "Bond settlement",
            currency: new LedgerEntryCurrency(transactionCurrency, functionalCurrency, value.Item2, value.Item3, fx))).ToArray();
        var generated = values.Select((value, index) => new GeneratedPostingLineDto(index.ToString(), value.Item1,
            value.Item2 > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
            "retained", value.Item2 + value.Item3, transactionCurrency)).ToArray();
        var candidate = new PostingRuleJournalCandidateResultDto(null!, null, null, generated, null, null,
            receipt.DirtyConsideration, receipt.DirtyConsideration, 0m, true, false, true, false, [], []);
        return (candidate, new JournalEntry(id, AsOf, "Bond settlement", lines));
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
            new LedgerAccount(value.Item1, value.Item4), value.Item2, value.Item3, "Bond settlement",
            currency: new LedgerEntryCurrency("USD", "USD", value.Item2, value.Item3, 1m))).ToArray();
        var generated = values.Select((value, index) => new GeneratedPostingLineDto(index.ToString(), value.Item1,
            value.Item2 > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
            "retained", value.Item2 + value.Item3, "USD")).ToArray();
        var candidate = new PostingRuleJournalCandidateResultDto(null!, null, null, generated, null, null,
            82_225m, 82_225m, 0m, true, false, true, false, [], []);
        return (candidate, new JournalEntry(id, AsOf, "Bond settlement", lines));
    }
}
