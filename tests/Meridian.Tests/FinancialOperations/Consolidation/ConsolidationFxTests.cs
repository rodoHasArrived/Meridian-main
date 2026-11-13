using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed class ConsolidationFxTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 30);
    private static readonly DateOnly TradeDate = new(2026, 6, 12);
    private static readonly Guid Book = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void WholeBookTranslation_UsesClosingAndHistoricalRatesAndBalancesWithReserve()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m), Line("Equity:Capital", "Equity", 0m, 100m) with { HistoricalDate = TradeDate } };
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf), Quote(ConsolidationFxRateKindDto.Historical, 1m, TradeDate));

        var result = ConsolidationFxTranslator.Translate(source, rates, Policy(), AsOf);

        Assert.Empty(result.Blockers);
        Assert.Equal(120m, result.Lines[0].Debit + result.Lines[1].Debit);
        Assert.Equal(-20m, result.TranslationReserveSignedAmount);
        Assert.Equal(0m, result.Lines.Sum(x => x.Debit - x.Credit) + result.TranslationReserveSignedAmount);
        Assert.Equal(-20m, Assert.Single(result.Reserves!).SignedAmount);
    }

    [Fact]
    public void IncomeDefaultsToTransactionDate_NotClosingOrAvailableAverage()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m), Line("Revenue:Fees", "Revenue", 0m, 100m) };
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf), Quote(ConsolidationFxRateKindDto.TransactionDate, 1.1m, TradeDate),
            Quote(ConsolidationFxRateKindDto.PeriodAverage, 1.05m, AsOf) with { PeriodStart = new(2026, 6, 1), PeriodEnd = AsOf });

        var result = ConsolidationFxTranslator.Translate(source, rates, Policy(), AsOf);

        Assert.Empty(result.Blockers);
        Assert.Equal(110m, result.Lines.Single(x => x.Source.AccountType == "Revenue").Credit);
        Assert.Equal(-10m, result.TranslationReserveSignedAmount);
    }

    [Fact]
    public void AverageRequiresExplicitPolicyAndExactApprovedPeriod()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m), Line("Revenue:Fees", "Revenue", 0m, 100m) };
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf),
            Quote(ConsolidationFxRateKindDto.PeriodAverage, 1.05m, AsOf) with { PeriodStart = new(2026, 6, 1), PeriodEnd = AsOf });
        var policy = Policy() with { AccountRules = [new("Revenue:Fees", ConsolidationFxRateKindDto.PeriodAverage, new(2026, 6, 1), AsOf)] };

        var unsupported = ConsolidationFxTranslator.Translate(source, rates, Policy(), AsOf);
        var permitted = ConsolidationFxTranslator.Translate(source, rates, policy, AsOf);

        Assert.NotEmpty(unsupported.Blockers);
        Assert.Equal(0m, unsupported.TranslationReserveSignedAmount);
        Assert.Empty(permitted.Blockers);
        Assert.Equal(105m, permitted.Lines.Single(x => x.Source.AccountType == "Revenue").Credit);
    }

    [Fact]
    public void MissingHistoricalDateDoesNotSubstituteClosingRate()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m), Line("Equity:Capital", "Equity", 0m, 100m) };
        var result = ConsolidationFxTranslator.Translate(source, Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf)), Policy(), AsOf);
        Assert.Contains(result.Blockers, x => x.Contains("Historical date evidence", StringComparison.Ordinal));
        Assert.Equal(0m, result.TranslationReserveSignedAmount);
    }

    [Fact]
    public void RetainedOpeningEquityIsNotRetranslated()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m),
            Line("Equity:Retained Earnings", "Equity", 0m, 100m) with { OpeningTranslatedSignedAmount = -101m } };
        var policy = Policy() with { AccountRules = [new("Equity:Retained Earnings", ConsolidationFxRateKindDto.Historical, RequiresOpeningTranslatedBalance: true)] };
        var result = ConsolidationFxTranslator.Translate(source, Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf)), policy, AsOf);
        Assert.Empty(result.Blockers);
        Assert.Equal(101m, result.Lines.Single(x => x.Source.AccountType == "Equity").Credit);
        Assert.Equal(-19m, result.TranslationReserveSignedAmount);
    }

    [Fact]
    public void RetainedOpeningPlusCurrentIncomeRollForwardWithoutRetranslatingOpening()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m),
            Line("Equity:Retained Earnings", "Equity", 0m, 90m) with { OpeningTranslatedSignedAmount = -95m },
            Line("Revenue:Fees", "Revenue", 0m, 10m) };
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf), Quote(ConsolidationFxRateKindDto.TransactionDate, 1.1m, TradeDate));
        var policy = Policy() with { AccountRules = [new("Equity:Retained Earnings", ConsolidationFxRateKindDto.Historical, RequiresOpeningTranslatedBalance: true)] };
        var result = ConsolidationFxTranslator.Translate(source, rates, policy, AsOf);
        Assert.Empty(result.Blockers);
        Assert.Equal(106m, result.Lines.Where(x => x.Source.AccountType is "Equity" or "Revenue").Sum(x => x.Credit));
        Assert.Equal(-14m, result.TranslationReserveSignedAmount);
    }

    [Fact]
    public void RoundingDifferenceIsDisclosedSeparatelyFromTranslationDifference()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 1m, 0m), Line("Revenue:Fees", "Revenue", 0m, 1m) };
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.005m, AsOf), Quote(ConsolidationFxRateKindDto.TransactionDate, 1m, TradeDate));
        var result = ConsolidationFxTranslator.Translate(source, rates, Policy(), AsOf);
        Assert.Empty(result.Blockers);
        Assert.Equal(-0.005m, result.TranslationDifferenceSignedAmount);
        Assert.Equal(-0.005m, result.RoundingDifferenceSignedAmount);
        Assert.Equal(-0.01m, result.TranslationReserveSignedAmount);
        Assert.Equal(result.TranslationReserveSignedAmount, result.TranslationDifferenceSignedAmount + result.RoundingDifferenceSignedAmount);
    }

    [Fact]
    public void UnbalancedSourceBookCannotBeHiddenInTranslationReserve()
    {
        var result = ConsolidationFxTranslator.Translate([Line("Assets:Cash", "Asset", 100m, 0m)],
            Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf)), Policy(), AsOf);
        Assert.Contains(result.Blockers, x => x.Contains("balanced functional trial balance", StringComparison.Ordinal));
        Assert.Equal(0m, result.TranslationReserveSignedAmount);
    }

    [Theory]
    [InlineData("JPY", 0, 123.456, 123)]
    [InlineData("BYR", 0, 123.456, 123)]
    [InlineData("USD", 2, 123.456, 123.46)]
    [InlineData("KWD", 3, 123.456, 123.456)]
    public void TranslationUsesCurrencyMinorUnits(string currency, int precision, double raw, double expected)
    {
        var source = new[] { Line("Assets:Cash", "Asset", 1m, 0m), Line("Liabilities:Payable", "Liability", 0m, 1m) };
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, (decimal)raw, AsOf) with { ToCurrency = currency });
        var result = ConsolidationFxTranslator.Translate(source, rates, Policy() with { ReportingCurrency = currency, DecimalPlaces = precision }, AsOf);
        Assert.Empty(result.Blockers);
        Assert.All(result.Lines, x => Assert.Equal((decimal)expected, x.Debit + x.Credit));
    }

    [Fact]
    public void PolicyRejectsCallerChosenPrecisionInconsistentWithCurrency()
        => Assert.Throws<ArgumentException>(() => ConsolidationFxValidation.Validate(Policy() with { ReportingCurrency = "JPY", DecimalPlaces = 2 }));

    [Fact]
    public void QuoteInversionMustBeExplicitAndRetainsIdentity()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m), Line("Liabilities:Payable", "Liability", 0m, 100m) };
        var reverse = Quote(ConsolidationFxRateKindDto.Closing, 0.5m, AsOf) with { FromCurrency = "USD", ToCurrency = "EUR" };
        Assert.NotEmpty(ConsolidationFxTranslator.Translate(source, Rates(reverse), Policy(), AsOf).Blockers);
        var result = ConsolidationFxTranslator.Translate(source, Rates(reverse with { AllowInverse = true }), Policy(), AsOf);
        Assert.Empty(result.Blockers);
        Assert.All(result.Lines, x => { Assert.True(x.Inverted); Assert.Equal(reverse.RateId, x.RateId); Assert.Equal(2m, x.Rate); });
    }

    [Fact]
    public void FutureOrAmbiguousRatesCannotProduceACompleteResult()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m), Line("Liabilities:Payable", "Liability", 0m, 100m) };
        var future = Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf.AddDays(1));
        Assert.NotEmpty(ConsolidationFxTranslator.Translate(source, Rates(future), Policy(), AsOf).Blockers);
        var direct = Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf);
        var inverse = Quote(ConsolidationFxRateKindDto.Closing, 0.5m, AsOf) with { FromCurrency = "USD", ToCurrency = "EUR", AllowInverse = true };
        Assert.NotEmpty(ConsolidationFxTranslator.Translate(source, Rates(direct, inverse), Policy(), AsOf).Blockers);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("equity")]
    [InlineData("Unrecognized")]
    public void ExplicitRateRuleCannotAuthorizeMalformedLedgerAccountType(string accountType)
    {
        var source = new[] { Line("Assets:Cash", accountType, 100m, 0m), Line("Liabilities:Payable", "Liability", 0m, 100m) };
        var policy = Policy() with { AccountRules = [new("Assets:Cash", ConsolidationFxRateKindDto.Closing)] };
        var result = ConsolidationFxTranslator.Translate(source,
            Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf)), policy, AsOf);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("canonical ledger account type", StringComparison.Ordinal));
    }

    [Fact]
    public void PriorBusinessDateRequiresExplicitEvidencedSelection()
    {
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m), Line("Liabilities:Payable", "Liability", 0m, 100m) };
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf.AddDays(-1)));
        Assert.NotEmpty(ConsolidationFxTranslator.Translate(source, rates, Policy(), AsOf).Blockers);
        var policy = Policy() with { ClosingRateDates = [new(AsOf, AsOf.AddDays(-1), "vault:approved-calendar-selection")] };
        Assert.Empty(ConsolidationFxTranslator.Translate(source, rates, policy, AsOf).Blockers);
    }

    [Fact]
    public async Task ApprovedRevisionIsImmutableScopedAndIdempotent()
    {
        var store = new ConsolidationFxInMemoryStore();
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf));
        await store.RetainRateSetAsync(rates);
        await store.RetainRateSetAsync(rates);
        var retained = await store.GetRateSetAsync("tenant", "company", rates.RateSetId, 1);
        Assert.NotNull(retained?.ContentHash);
        Assert.Null(await store.GetRateSetAsync("foreign", "company", rates.RateSetId, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetainRateSetAsync(rates with { Rates = [rates.Rates[0] with { Rate = 99m }] }));
        await store.RetainRateSetAsync(rates with { Revision = 2, PreviousHash = retained!.ContentHash, Rates = [rates.Rates[0] with { Rate = 1.3m }] });
        Assert.Equal(1.2m, (await store.GetRateSetAsync("tenant", "company", rates.RateSetId, 1))!.Rates[0].Rate);
        Assert.Equal(2, (await store.ListRateSetsAsync("tenant", "company")).Count);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("XAU")]
    [InlineData("XDR")]
    public async Task GovernedAuthorityRejectsUnknownCurrenciesAndUnitsWithoutDefinedMinorScale(string currency)
    {
        var store = new ConsolidationFxInMemoryStore();
        var quote = Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf);
        var approved = Rates(quote);
        await store.RetainRateSetAsync(approved);
        await Assert.ThrowsAsync<ArgumentException>(() => store.RetainRateSetAsync(
            Rates(quote with { FromCurrency = currency })));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RetainConfigurationAsync(Configuration(approved) with
        {
            Policy = Policy() with { ReportingCurrency = currency, DecimalPlaces = 2 }
        }));
        var source = new[] { Line("Assets:Cash", "Asset", 100m, 0m) with { FunctionalCurrency = currency },
            Line("Liabilities:Payable", "Liability", 0m, 100m) with { FunctionalCurrency = currency } };
        var translated = ConsolidationFxTranslator.Translate(source, approved, Policy(), AsOf);
        Assert.False(translated.IsComplete);
        Assert.Empty(translated.Lines);
    }

    [Fact]
    public async Task ConfigurationsPinExactRateRevisionAndRejectScopeChange()
    {
        var store = new ConsolidationFxInMemoryStore();
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf));
        await store.RetainRateSetAsync(rates);
        var config = Configuration(rates);
        await store.RetainConfigurationAsync(config);
        var retained = await store.GetConfigurationAsync(Book, AsOf);
        Assert.NotNull(retained?.ContentHash);
        var authority = new ConsolidationFxAuthority(store);
        Assert.Equal(1, (await authority.ResolveAsync(Book, AsOf)).RateSet.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetainConfigurationAsync(config with { Revision = 2, PreviousHash = retained!.ContentHash, RateSetRevision = 2 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetainConfigurationAsync(config with { Revision = 2, PreviousHash = retained!.ContentHash, CompanyId = "foreign" }));
    }

    [Fact]
    public async Task PostingAuthorityLeasePreventsConfigurationMutationUntilReleased()
    {
        var store = new ConsolidationFxInMemoryStore();
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf));
        await store.RetainRateSetAsync(rates);
        var lease = await store.AcquireAuthorityLeaseAsync();
        var pending = store.RetainConfigurationAsync(Configuration(rates));
        Assert.False(pending.IsCompleted);
        await lease.DisposeAsync();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(await store.GetConfigurationAsync(Book, AsOf));
    }

    [Fact]
    public async Task FileRestartPreservesExactApprovedAuthority()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-fx-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "fx.json");
            var store = new ConsolidationFxFileStore(path);
            var rates = Rates(Quote(ConsolidationFxRateKindDto.TransactionDate, 1.2m, TradeDate));
            await store.RetainRateSetAsync(rates);
            await store.RetainConfigurationAsync(Configuration(rates));
            var restarted = new ConsolidationFxAuthority(new ConsolidationFxFileStore(path));
            var context = await restarted.ResolveAsync(Book, AsOf);
            var converted = ConsolidationFxTranslator.ConvertAmount(100m, "EUR", context, TradeDate);
            Assert.Equal(120m, converted.Amount);
            Assert.Equal(context.Configuration.ContentHash, converted.ConfigurationHash);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CounterpartConversionRequiresExactDateNotLatestAvailable()
    {
        var store = new ConsolidationFxInMemoryStore();
        var rates = Rates(Quote(ConsolidationFxRateKindDto.TransactionDate, 1.2m, TradeDate));
        await store.RetainRateSetAsync(rates);
        await store.RetainConfigurationAsync(Configuration(rates));
        var context = await new ConsolidationFxAuthority(store).ResolveAsync(Book, AsOf);
        Assert.Throws<InvalidOperationException>(() => ConsolidationFxTranslator.ConvertAmount(100m, "EUR", context, TradeDate.AddDays(1)));
    }

    [Fact]
    public async Task ExpiredReplacementDoesNotReviveAnEarlierOpenEndedConfiguration()
    {
        var store = new ConsolidationFxInMemoryStore();
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf));
        await store.RetainRateSetAsync(rates);
        await store.RetainConfigurationAsync(Configuration(rates));
        var prior = (await store.GetConfigurationAsync(Book, AsOf))!;
        await store.RetainConfigurationAsync(prior with
        {
            Revision = 2,
            PreviousHash = prior.ContentHash,
            ContentHash = null,
            EffectiveFrom = new(2026, 6, 1),
            EffectiveTo = new(2026, 6, 15)
        });
        Assert.Null(await store.GetConfigurationAsync(Book, AsOf));
        Assert.NotNull(await store.GetConfigurationRevisionAsync(Book, 1));
    }

    [Fact]
    public async Task SupersededRatesRequireExplicitConfigurationChangeAndRenewedReview()
    {
        var store = new ConsolidationFxInMemoryStore();
        var rates = Rates(Quote(ConsolidationFxRateKindDto.Closing, 1.2m, AsOf));
        await store.RetainRateSetAsync(rates);
        await store.RetainConfigurationAsync(Configuration(rates));
        var retained = (await store.GetRateSetAsync("tenant", "company", rates.RateSetId, 1))!;
        await store.RetainRateSetAsync(rates with { Revision = 2, PreviousHash = retained.ContentHash, Rates = [rates.Rates[0] with { Rate = 1.3m }] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConsolidationFxAuthority(store).ResolveAsync(Book, AsOf));
        Assert.NotNull(await store.GetRateSetAsync("tenant", "company", rates.RateSetId, 1));
        var prior = (await store.GetConfigurationRevisionAsync(Book, 1))!;
        await store.RetainConfigurationAsync(prior with { Revision = 2, PreviousHash = prior.ContentHash, ContentHash = null, RateSetRevision = 2 });
        Assert.Equal(2, (await new ConsolidationFxAuthority(store).ResolveAsync(Book, AsOf)).RateSet.Revision);
    }

    private static ConsolidationFxSourceLineDto Line(string path, string type, decimal debit, decimal credit)
        => new(Book, Guid.NewGuid(), Guid.NewGuid(), "entity", path, type, "EUR", debit, credit, TradeDate);
    internal static ConsolidationFxRateDto Quote(ConsolidationFxRateKindDto kind, decimal rate, DateOnly date)
        => new(Guid.NewGuid(), "EUR", "USD", date, rate, kind, "vault:synthetic-rate-evidence");
    internal static ConsolidationFxRateSetDto Rates(params ConsolidationFxRateDto[] rates)
        => new(Guid.NewGuid(), 1, "tenant", "company", rates, "vault:synthetic-rate-set", "owner", "owner", new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    internal static ConsolidationFxPolicyDto Policy() => new("current-rate", "v1", "USD", 2);
    private static ConsolidationFxBookConfigurationDto Configuration(ConsolidationFxRateSetDto rates)
        => new(Book, 1, "tenant", "company", new(2026, 1, 1), null, rates.RateSetId, rates.Revision, Policy(), "owner", new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
}
