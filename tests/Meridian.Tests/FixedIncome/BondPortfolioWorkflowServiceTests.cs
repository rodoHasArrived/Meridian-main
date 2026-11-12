using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

/// <summary>Independent orchestration checks: held-face authority, native currencies and partial coverage.</summary>
public sealed class BondPortfolioWorkflowServiceTests
{
    private static readonly Guid AccountId = Guid.Parse("09d0fbd0-02b2-47ea-b3f4-f5947181a39f");
    private static readonly Guid SecurityId = Guid.Parse("8482abf1-4818-4469-8a8b-0951006012a4");
    private static readonly Guid SecondSecurityId = Guid.Parse("98ed6e2a-a91e-4cf9-8d6e-9de230689610");
    private static readonly Guid BatchId = Guid.Parse("2c2ba2d0-d884-4fcd-b2a5-d5abf212cc52");
    private static readonly DateTimeOffset Cutoff = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static BondCalculationContext Context => new(Cutoff, new DateOnly(2026, 1, 5), Cutoff);
    private static AccountCashForecastScopeDto Scope => new("tenant-synthetic", "company-synthetic",
        AccountId.ToString("D"), "book-synthetic", "USD");

    [Fact]
    public async Task ComparisonUsesCommonSettlementAndCutoffsWithoutDiscardingEachRowsPinnedCurves()
    {
        var models = Substitute.For<IBondWorkflowService>();
        var calls = new List<BondWorkflowRequest>();
        models.CalculateAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
        {
            var request = info.Arg<BondWorkflowRequest>();
            calls.Add(request);
            return Task.FromResult<BondWorkflowResult?>(Model(request));
        });
        var firstPin = new BondPinnedCurveInput("Discount", Guid.NewGuid(), Guid.NewGuid());
        var secondPin = new BondPinnedCurveInput("Discount", Guid.NewGuid(), Guid.NewGuid());
        var first = new BondWorkflowRequest(SecurityId, new(Cutoff.AddDays(-10), new(2025, 12, 25), Cutoff, 7, [firstPin]), 1000m, 99m);
        var second = new BondWorkflowRequest(SecondSecurityId, new(Cutoff.AddDays(-5), new(2025, 12, 29), Cutoff, 11, [secondPin]), 500m, 102m);

        var result = await new BondComparisonService(models).CompareAsync(new("comparison-synthetic", Context, [first, second]));

        result.IsComplete.Should().BeTrue();
        calls.Should().HaveCount(2);
        calls.Should().OnlyContain(row => row.Context.AsOf == Cutoff && row.Context.KnownAt == Cutoff
            && row.Context.SettlementDate == Context.SettlementDate);
        calls[0].Context.Curves.Should().Equal(firstPin);
        calls[1].Context.Curves.Should().Equal(secondPin);
        calls[0].Context.SecurityVersion.Should().Be(7);
        calls[1].Context.SecurityVersion.Should().Be(11);
        calls.Select(row => row.QuoteSettlementDate).Should().Equal(first.QuoteSettlementDate, second.QuoteSettlementDate);
        first.Context.SettlementDate.Should().Be(new DateOnly(2025, 12, 25));
    }

    [Theory]
    [InlineData(100, 40, 50.4)]
    [InlineData(10, 50, -33.6)]
    public async Task RiskNetsOriginalHeldFaceAndAppliesDirectionAndFactorExactlyOnce(int longFace, int shortFace, double expectedValue)
    {
        var calls = new List<BondWorkflowRequest>();
        var service = RiskService([Holding(SecurityId, longFace), Holding(SecurityId, shortFace, isShort: true)], calls);

        var result = await service.CalculateAsync(Scope, new(Context));

        var position = result.Positions.Should().ContainSingle().Which;
        position.SignedOriginalFace.Should().Be(longFace - shortFace);
        position.PresentValue.Should().Be((decimal)expectedValue);
        position.Dv01.Should().Be((longFace - shortFace) * 0.8m * 0.001m);
        calls.Should().HaveCount(5); // Base plus four distinct model scenarios.
        calls.Should().OnlyContain(request => request.OriginalFace == Math.Abs(longFace - shortFace));
        calls.Should().OnlyContain(request => request.AccountId == Scope.AccountId);
        var up = position.Scenarios.Single(row => row.Scenario == "parallel-up");
        up.Change.Should().Be(-(longFace - shortFace) * 0.8m * 0.01m);
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task IssuerAndMaturityBucketsNeverAddDifferentCurrencies()
    {
        var service = RiskService([Holding(SecurityId, 100m, currency: "USD"),
            Holding(SecondSecurityId, 200m, currency: "EUR")], [],
            currencies: new Dictionary<Guid, string> { [SecurityId] = "USD", [SecondSecurityId] = "EUR" });

        var result = await service.CalculateAsync(Scope, new(Context));

        var issuers = result.Buckets.Where(row => row.Dimension == "issuer").ToArray();
        issuers.Should().HaveCount(2);
        issuers.Select(row => row.Key).Should().OnlyContain(key => key == "Synthetic issuer");
        issuers.Single(row => row.Currency == "USD").KnownPresentValue.Should().Be(84m);
        issuers.Single(row => row.Currency == "EUR").KnownPresentValue.Should().Be(168m);
        result.Buckets.Where(row => row.Dimension == "maturity").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("factor")]
    [InlineData("competing-factors")]
    public async Task UnreconciledHoldingEconomicsExcludeBaselineAndEveryScenario(string conflict)
    {
        var rows = conflict switch
        {
            "currency" => new[] { Holding(SecurityId, 100m, currency: "EUR") },
            "factor" => new[] { Holding(SecurityId, 100m, factor: 0.9m) },
            _ => new[] { Holding(SecurityId, 100m), Holding(SecurityId, 100m, factor: 0.9m) }
        };
        var result = await RiskService(rows, []).CalculateAsync(Scope, new(Context));

        var position = result.Positions.Should().ContainSingle().Which;
        position.PresentValue.Should().BeNull();
        position.Dv01.Should().BeNull();
        position.Cs01.Should().BeNull();
        position.Scenarios.Should().HaveCount(4).And.OnlyContain(row => row.PresentValue == null && row.Change == null);
        result.Buckets.Should().OnlyContain(row => row.CoveredHoldings == 0 && row.KnownPresentValue == 0m
            && row.KnownDv01 == 0m && row.KnownCs01 == 0m);
        result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task MissingSpreadSensitivityPreservesKnownValueAndRateRiskWithoutClaimingCompleteCoverage()
    {
        var result = await RiskService([Holding(SecurityId, 100m)], [], missingCs01: true)
            .CalculateAsync(Scope, new(Context));

        var position = result.Positions.Should().ContainSingle().Which;
        position.PresentValue.Should().Be(84m);
        position.Dv01.Should().Be(0.08m);
        position.Cs01.Should().BeNull();
        result.Buckets.Should().OnlyContain(row => row.CoveredHoldings == 0 && row.KnownPresentValue == 84m
            && row.KnownDv01 == 0.08m && row.KnownCs01 == 0m);
        result.IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData("factor")]
    [InlineData("currency")]
    public async Task OffsettingOriginalFaceCannotHideContradictoryHoldingEconomics(string conflict)
    {
        var shortRow = conflict == "factor" ? Holding(SecurityId, 100m, isShort: true, factor: 0.9m)
            : Holding(SecurityId, 100m, isShort: true, currency: "EUR");
        var result = await RiskService([Holding(SecurityId, 100m), shortRow], []).CalculateAsync(Scope, new(Context));
        result.IsComplete.Should().BeFalse();
        (result.Issues.Count > 0 || result.Positions.Any(position => position.Issues.Count > 0)).Should().BeTrue();
    }

    [Fact]
    public async Task DifferentReferenceVersionUsesUnknownMetadataBuckets()
    {
        var result = await RiskService([Holding(SecurityId, 100m)], [], referenceVersion: 8)
            .CalculateAsync(Scope, new(Context));

        var position = result.Positions.Should().ContainSingle().Which;
        position.Issuer.Should().BeNull();
        position.Maturity.Should().BeNull();
        result.Buckets.Should().Contain(row => row.Dimension == "issuer" && row.Key == "Unknown issuer");
        result.Buckets.Should().Contain(row => row.Dimension == "maturity" && row.Key == "Unknown");
        position.Issues.Should().Contain(reason => reason.Contains("retained calculation terms version"));
    }

    [Theory]
    [InlineData("default-economic")]
    [InlineData("offset-economic")]
    [InlineData("offset-known")]
    [InlineData("future-economic")]
    [InlineData("future-known")]
    public async Task ComparisonRejectsInvalidOrFutureTimestampInputs(string invalid)
    {
        var context = InvalidContext(invalid);
        var models = Substitute.For<IBondWorkflowService>();
        var action = () => new BondComparisonService(models).CompareAsync(new("invalid-synthetic", context,
            [new(SecurityId, Context)]));
        await action.Should().ThrowAsync<ArgumentException>();
        await models.DidNotReceive().CalculateAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("default-economic")]
    [InlineData("offset-economic")]
    [InlineData("offset-known")]
    [InlineData("future-economic")]
    [InlineData("future-known")]
    public async Task RiskRejectsInvalidOrFutureTimestampInputs(string invalid)
    {
        var action = () => RiskService([], []).CalculateAsync(Scope, new(InvalidContext(invalid)));
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("book")]
    public async Task RiskRequiresCompleteExactScope(string invalid)
    {
        var scope = invalid switch
        {
            "account" => Scope with { AccountId = "invalid" },
            "tenant" => Scope with { TenantId = "" },
            "company" => Scope with { CompanyId = "" },
            _ => Scope with { BookId = "" }
        };
        var action = () => RiskService([], []).CalculateAsync(scope, new(Context));
        await action.Should().ThrowAsync<ArgumentException>();
    }

    private static BondCalculationContext InvalidContext(string invalid) => invalid switch
    {
        "default-economic" => Context with { AsOf = default },
        "offset-economic" => Context with { AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)) },
        "offset-known" => Context with { KnownAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)) },
        "future-economic" => Context with { AsOf = DateTimeOffset.UtcNow.AddDays(1) },
        _ => Context with { KnownAt = DateTimeOffset.UtcNow.AddDays(1) }
    };

    private static BondPortfolioRiskService RiskService(IReadOnlyList<CustodianPositionLineDto> rows,
        List<BondWorkflowRequest> calls, bool missingCs01 = false, long referenceVersion = 7,
        IReadOnlyDictionary<Guid, string>? currencies = null)
    {
        var source = Substitute.For<IAccountBondHoldingsSource>();
        source.GetHoldingsAsync(AccountId, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new AccountBondHoldingsSnapshotDto(AccountId, new(2026, 1, 1), BatchId,
                new string('a', 64), "synthetic-statement", rows));
        var models = Substitute.For<IBondWorkflowService>();
        models.CalculateAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
        {
            var request = info.Arg<BondWorkflowRequest>();
            calls.Add(request);
            var model = Model(request, currencies?.GetValueOrDefault(request.SecurityId) ?? "USD");
            return Task.FromResult<BondWorkflowResult?>(missingCs01 ? model with { Cs01 = null } : model);
        });
        var refs = Substitute.For<IBondReferenceService>();
        refs.GetReferenceAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(info =>
        {
            var id = info.Arg<Guid>();
            var reference = new BondReferenceDto(id, "Synthetic bond", currencies?.GetValueOrDefault(id) ?? "USD",
                "Synthetic issuer", null, id.ToString("D"),
                new(id, BondLifecycleStat.Active, new(2025, 1, 1), null, new(2030, 1, 1), false, referenceVersion),
                null, referenceVersion);
            return Task.FromResult<BondReferenceDto?>(reference);
        });
        return new(source, models, refs);
    }

    private static BondWorkflowResult Model(BondWorkflowRequest request, string currency = "USD")
    {
        var current = request.OriginalFace * 0.8m;
        var pv = current * 1.05m - current * (request.RateShiftBps + request.SpreadShiftBps) / 10000m;
        return new(request.SecurityId, currency, "synthetic-model", request.Context with { SecurityVersion = 7 },
            request.OriginalFace, current, 0m, pv, pv, 0m, pv, pv, 0.04m, 0.01m,
            current * 0.001m, current * 0.0005m, 1m, [], [], [], [new("Valuation", true)]);
    }

    private static CustodianPositionLineDto Holding(Guid securityId, decimal original, bool isShort = false,
        string currency = "USD", decimal factor = 0.8m)
        => new(Guid.NewGuid(), BatchId, AccountId, new(2026, 1, 1), securityId.ToString("D"), "SecurityId",
            original, original * factor, currency, "Synthetic bond", "Bond", isShort, SecurityId: securityId,
            Bond: new(original, original * factor, factor, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace,
                100m, false, 0m, InvoiceBasis: BondHoldingInvoiceBasis.Nominal));
}
