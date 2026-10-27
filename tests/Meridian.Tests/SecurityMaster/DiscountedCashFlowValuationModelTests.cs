using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Valuation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The discounted-cash-flow model on the hand-computed golden cases: the bond (PV 1,014,000.00,
/// price 101.4000, market value 507,000.00), the swap (NPV -162.50; 0.00 undiscounted; +7,500 flat
/// forward) and every blocker that stops a value being invented.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DiscountedCashFlowValuationModelTests
{
    private static readonly DateOnly AsOfDate = ValuationTestSecurities.AsOfDate;
    private static readonly DateTimeOffset AsOf = ValuationTestSecurities.AsOf;

    [Fact]
    public async Task Bond_DiscountsTwoCouponsAndPrincipal_ToTheGoldenValue()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints);
        var model = Build(bond, Curves(definition, version));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(1_014_000.00m, "50,000 x 0.96 + 1,050,000 x 0.92");
        result.Price.Should().Be(101.4000m);
        result.PriceUnit.Should().Be(SecurityPriceUnit.PercentOfPar);
        result.MarketValue.Should().Be(507_000.00m, "500,000 face x 101.4 / 100");
        result.Observability.Should().Be(SecurityValuationObservability.ObservableInputs);
        result.ObservedAsOf.Should().Be(AsOf);
        result.Model.Should().Be(SecurityValuationModelKind.DiscountedCashFlow);
        result.ModelKey.Should().Be("discounted-cash-flow");
        result.Currency.Should().Be("USD");

        var curveInput = result.Inputs.Single(static input => input.Kind == "DiscountCurveVersion");
        curveInput.Reference.Should().Be("USD-DISCOUNT-TEST");
        curveInput.Id.Should().Be(version.CurveVersionId);
        curveInput.Version.Should().Be(1);
        curveInput.ContentSha256.Should().Be(version.ContentSha256);
        result.Inputs.Single(static input => input.Kind == "SecurityTerms").Version.Should().Be(bond.Version);
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("DirtyValue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APaidDownBond_QuotesItsPricePerOutstandingFace_AndAppliesTheFactorOnce()
    {
        // Factor 0.8 on 1,000,000 par: the walk projects two coupons of 40,000 and 800,000 of
        // principal on the 800,000 outstanding. On a unit discount curve the PV is 880,000, which is
        // 110 per 100 of OUTSTANDING face, not 88 per 100 of par: the market value of 500,000 par is
        // 500,000 x 0.8 x 110 / 100 = 440,000, the factor applied once.
        var bond = ValuationTestSecurities.PaidDownBond(0.8m);
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(bond, Curves(definition, version));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(880_000.00m, "40,000 + 40,000 + 800,000 on unit discount factors");
        result.PriceUnit.Should().Be(SecurityPriceUnit.PercentOfPar);
        result.Price.Should().Be(110m, "880,000 per 800,000 of outstanding face");
        result.MarketValue.Should().Be(440_000.00m, "500,000 par x 0.8 x 110 / 100");
    }

    [Fact]
    public async Task AFullyRepaidBond_HasNoPriceBasis_AndSaysSo()
    {
        var bond = ValuationTestSecurities.PaidDownBond(0m);
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(bond, Curves(definition, version));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(0m);
        result.Price.Should().BeNull();
        result.PriceUnit.Should().Be(SecurityPriceUnit.Unspecified);
        result.MarketValue.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("NoPriceBasis", StringComparison.Ordinal)).Which.Should().Contain("fully repaid");
    }

    [Fact]
    public async Task Swap_NetsTheLegsAndDiscounts_ToTheGoldenValue()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.SwapDiscountPoints);
        var model = Build(swap, Curves(definition, version), FlatFourPercentSofr());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf, Quantity: 1_000_000m), swap, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(-162.50m, "-8,043.75 + 9,187.50 - 10,306.25 + 9,000.00");
        result.Price.Should().Be(-0.0001625m, "per unit of the 1,000,000 notional");
        result.PriceUnit.Should().Be(SecurityPriceUnit.CurrencyPerUnit);
        result.MarketValue.Should().Be(-162.50m);
        result.Observability.Should().Be(SecurityValuationObservability.ObservableInputs);
        result.Inputs.Should().ContainSingle(static input => input.Kind == "ForwardCurve")
            .Which.Reference.Should().Be("forwardCurve:SOFR@2025-12-01 (test-curve)");
        result.Diagnostics.Should().NotContain(static d => d.StartsWith("FloatingForecastFlatAtCurrentIndexRate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Swap_UndiscountedNetsToZero_WithTheForwardCurve()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(swap, Curves(definition, version), FlatFourPercentSofr());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf), swap, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().Be(0.00m, "-8,125 + 9,375 - 10,625 + 9,375");
    }

    [Fact]
    public async Task Swap_WithoutAForwardCurve_ProjectsFlatForward_AndSaysSo()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(swap, Curves(definition, version), new InMemorySecurityForwardCurveProvider());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf), swap, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().Be(7_500.00m, "fixed 40,000 against floating 4 x 8,125");
        result.Observability.Should().Be(SecurityValuationObservability.UnobservableInputs);
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("FloatingForecastFlatAtCurrentIndexRate", StringComparison.Ordinal))
            .Which.Should().Contain("pay-floating").And.Contain("SOFR");
        result.Inputs.Should().NotContain(static input => input.Kind == "ForwardCurve");
    }

    [Fact]
    public async Task NoDiscountCurve_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var model = Build(bond, Curves());

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "NoDiscountCurve").Reason.Should().Contain("USD");
    }

    [Fact]
    public async Task TwoDiscountCurvesInTheCurrency_Block_NamingBoth()
    {
        var bond = ValuationTestSecurities.Bond();
        var first = ValuationTestSecurities.DiscountDefinition("USD-OIS");
        var second = ValuationTestSecurities.DiscountDefinition("USD-TREASURY");
        var model = Build(bond, Curves(first, null, second, null));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "AmbiguousDiscountCurve").Reason.Should().Contain("USD-OIS").And.Contain("USD-TREASURY");
    }

    [Fact]
    public async Task APinnedCurveInAnotherCurrency_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var eur = ValuationTestSecurities.DiscountDefinition("EUR-ESTR", "EUR");
        var model = Build(bond, Curves(eur, ValuationTestSecurities.Version(eur, AsOfDate, ValuationTestSecurities.BondDiscountPoints)));

        var result = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, AsOf, Curves: [new SecurityValuationCurveInput("Discount", eur.CurveId)]), bond, CancellationToken.None);

        Blocked(result, "CurrencyMismatch").Reason.Should().Contain("EUR").And.Contain("USD");
    }

    [Fact]
    public async Task APinnedVersion_IsUsedExactly()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var pinned = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints) with { Version = 1 };
        var latest = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints) with { Version = 2 };
        var curves = Curves(definition, latest);
        curves.ListVersionsAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(new[] { pinned, latest });
        var model = Build(bond, curves);

        var result = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, AsOf, Curves: [new SecurityValuationCurveInput("Discount", definition.CurveId, pinned.CurveVersionId)]), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().Be(1_100_000.00m, "undiscounted on the pinned unit curve");
        result.Inputs.Single(static input => input.Kind == "DiscountCurveVersion").Id.Should().Be(pinned.CurveVersionId);
    }

    [Fact]
    public async Task APaymentBeyondTheCurvesLastPillar_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var shortCurve = ValuationTestSecurities.Version(definition, AsOfDate, (AsOfDate, 1.00m), (new DateOnly(2027, 1, 1), 0.96m));
        var model = Build(bond, Curves(definition, shortCurve));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "DiscountCurveCoverage").Reason.Should().Contain("2028-01-01").And.Contain("2027-01-01");
    }

    [Fact]
    public async Task NoCashFlowSource_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(bond, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints)), source: null);

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "NoCashFlowSource");
    }

    [Fact]
    public async Task ABlockedProjection_PassesItsReasonThrough()
    {
        var swap = ValuationTestSecurities.FloatingSwap(currentIndexRate: null);
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(swap, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.SwapDiscountPoints)), new InMemorySecurityForwardCurveProvider());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf), swap, CancellationToken.None);

        Blocked(result, "ProjectionBlocked").Reason.Should().Contain("index fixing");
    }

    [Fact]
    public async Task AForwardRoleOnTheRequest_IsRefused()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(swap, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.SwapDiscountPoints)), FlatFourPercentSofr());

        var result = await model.ValueAsync(
            new SecurityValuationRequest(swap.SecurityId, AsOf, Curves: [new SecurityValuationCurveInput("Forward", Guid.NewGuid())]), swap, CancellationToken.None);

        Blocked(result, "UnsupportedInput").Reason.Should().Contain("Forward");
    }

    private static SecurityValuationBlockerDto Blocked(SecurityValuationResult result, string code)
    {
        result.IsBlocked.Should().BeTrue();
        result.Price.Should().BeNull();
        result.PresentValue.Should().BeNull();
        result.MarketValue.Should().BeNull();
        var blocker = result.Blockers.Should().ContainSingle().Subject;
        blocker.Code.Should().Be(code);
        return blocker;
    }

    private static InMemorySecurityForwardCurveProvider FlatFourPercentSofr()
    {
        var provider = new InMemorySecurityForwardCurveProvider();
        provider.Publish(new SecurityForwardCurve("SOFR", AsOfDate.AddMonths(-1),
            [new(AsOfDate, 0.04m), new(AsOfDate.AddYears(1), 0.04m)], Source: "test-curve"));
        return provider;
    }

    /// <summary>A curve store with the given Discount definitions (and their latest versions, when supplied).</summary>
    private static ISecurityCurveStore Curves(params object?[] definitionsAndVersions)
    {
        var store = Substitute.For<ISecurityCurveStore>();
        var definitions = new List<SecurityCurveDefinitionDto>();
        for (var i = 0; i < definitionsAndVersions.Length; i += 2)
        {
            var definition = (SecurityCurveDefinitionDto)definitionsAndVersions[i]!;
            var version = i + 1 < definitionsAndVersions.Length ? (SecurityCurveVersionDto?)definitionsAndVersions[i + 1] : null;
            definitions.Add(definition);
            store.GetDefinitionAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(definition);
            store.GetLatestVersionAsOfAsync(definition.CurveId, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(version);
            store.ListVersionsAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(version is null ? [] : new[] { version });
        }

        store.ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => definitions
                .Where(d => call.ArgAt<SecurityCurveKind?>(0) is not { } kind || d.Kind == kind)
                .Where(d => call.ArgAt<string?>(1) is not { } currency || string.Equals(d.Currency, currency, StringComparison.OrdinalIgnoreCase))
                .ToList());
        return store;
    }

    private static DiscountedCashFlowValuationModel Build(
        SecurityDetailDto security,
        ISecurityCurveStore curves,
        ISecurityForwardCurveProvider? forwardCurves = null,
        StructuredCashFlowSourceKind? source = StructuredCashFlowSourceKind.CalculatedBullet)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(source is { } kind
                ? new SecurityCashFlowSourceDto(security.SecurityId, kind, DateTimeOffset.UtcNow, false, null, null)
                : null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var cashFlows = new SecurityMasterCashFlowService(
            store, Array.Empty<IStructuredCashFlowProvider>(), query,
            NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: forwardCurves);
        return new DiscountedCashFlowValuationModel(cashFlows, curves, NullLogger<DiscountedCashFlowValuationModel>.Instance);
    }
}
