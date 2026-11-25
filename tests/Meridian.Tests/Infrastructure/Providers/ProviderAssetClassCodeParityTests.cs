using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Pins what each brokerage asset-class site accepted before it moved onto
/// <see cref="ProviderAssetClassCodeCatalog"/>, and holds the table's helpers to the same
/// answers. Each truth table is a literal copy of the site's former switch; a row that changes
/// here is a behaviour change, not a refactor.
/// </summary>
public sealed class ProviderAssetClassCodeParityTests
{
    // Inputs the sites see: codes, aliases, casing and whitespace variants, and near misses.
    // The second column is a literal pin, not derived from any table: face-value
    // (percentage-of-par) sizing applies to exactly treasury, us_treasury,
    // fixed_income_treasury, corporate, bond, corporate_bond and us_corporate, in any case
    // and with any surrounding whitespace.
    public static TheoryData<string, bool> Corpus => new()
    {
        { "us_equity", false }, { "US_EQUITY", false }, { " us_equity ", false }, { "equity", false },
        { "Equity", false }, { "stock", false },
        { "us_option", false }, { "US_Option", false }, { "option", false }, { "OPTION", false },
        { "options", false }, { " option", false },
        { "treasury", true }, { "Treasury", true }, { " treasury", true }, { "TREASURY ", true },
        { "us_treasury", true }, { " US_Treasury", true }, { "fixed_income_treasury", true },
        { "Fixed_Income_Treasury ", true },
        { "corporate", true }, { "CORPORATE", true }, { "bond", true }, { "BOND", true }, { " bond ", true },
        { "corporate_bond", true }, { "Corporate_Bond", true }, { "us_corporate", true }, { " US_CORPORATE ", true },
        { "crypto", false }, { "Crypto", false }, { " crypto", false }, { "fixed_income", false },
        { "treasuries", false }, { "bonds", false }, { "corporate bond", false }, { "govt", false },
        { "futures", false }, { "forex", false },
        { "STK", false }, { "stk", false }, { "OPT", false }, { "opt", false }, { "FOP", false },
        { "FUT", false }, { "CASH", false }, { "GOVT", false }, { "ETF", false }, { "", false }, { "   ", false },
    };

    [Theory]
    [InlineData("equity", "us_equity")]
    [InlineData("us_equity", "us_equity")]
    [InlineData(" US_Equity ", "us_equity")]
    [InlineData("option", "us_option")]
    [InlineData("OPTIONS", "us_option")]
    [InlineData("us_option", "us_option")]
    [InlineData("treasury", "treasury")]
    [InlineData("us_treasury", "treasury")]
    [InlineData("fixed_income_treasury", "treasury")]
    [InlineData("corporate", "corporate")]
    [InlineData("bond", "corporate")]
    [InlineData("corporate_bond", "corporate")]
    [InlineData("us_corporate", "corporate")]
    [InlineData("crypto", "crypto")]
    [InlineData(" Crypto", "crypto")]
    [InlineData("stock", "stock")]
    [InlineData("fixed_income", "fixed_income")]
    [InlineData("Futures", "futures")]
    public void AlpacaNormalizeAssetClass_KeepsItsTruthTable(string input, string expected)
    {
        AlpacaBrokerageGateway.NormalizeAssetClass(input).Should().Be(expected);
        ProviderAssetClassCodeCatalog.NormalizeInput(ProviderAssetClassCodeProviders.Alpaca, input).Should().Be(expected);
    }

    [Theory]
    [InlineData("treasury", true)]
    [InlineData("corporate", true)]
    [InlineData("Treasury", false)]
    [InlineData(" treasury", false)]
    [InlineData("us_treasury", false)]
    [InlineData("bond", false)]
    [InlineData("us_equity", false)]
    [InlineData(null, false)]
    public void AlpacaIsFixedIncomeAssetClass_IsAnOrdinalCodeMatch(string? payloadCode, bool expected)
        => AlpacaBrokerageGateway.IsFixedIncomeAssetClass(payloadCode).Should().Be(expected);

    [Theory]
    [MemberData(nameof(Corpus))]
    public void PaperGateway_HasTheLiveGatewaysFixedIncomeSet(string assetClass, bool expectedFaceValue)
    {
        var paper = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "912797AB1",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 100_000m,
            LimitPrice = 101.25m,
            Metadata = new Dictionary<string, string> { ["asset_class"] = assetClass }
        };

        var live = !string.IsNullOrWhiteSpace(assetClass)
            && AlpacaBrokerageGateway.IsFixedIncomeAssetClass(AlpacaBrokerageGateway.NormalizeAssetClass(assetClass));

        // Pin each side to the literal column first, so a new face-value alias in the table
        // fails here rather than moving paper, live and the table together.
        paper.UsesFaceValuePercentageOfPar(request).Should().Be(expectedFaceValue, $"paper must size '{assetClass}' as pinned");
        live.Should().Be(expectedFaceValue, $"live must size '{assetClass}' as pinned");
        ProviderAssetClassCodeCatalog.IsFaceValueFixedIncomeInput(ProviderAssetClassCodeProviders.Alpaca, assetClass)
            .Should().Be(expectedFaceValue);
    }

    [Theory]
    [InlineData("us_equity", true)]
    [InlineData(" US_EQUITY ", true)]
    [InlineData("equity", true)]
    [InlineData("Equity", true)]
    [InlineData("crypto", true)]
    [InlineData("Crypto", true)]
    [InlineData("option", false)]
    [InlineData("us_option", false)]
    [InlineData("treasury", false)]
    [InlineData("corporate", false)]
    [InlineData("bond", false)]
    [InlineData("stock", false)]
    [InlineData("STK", false)]
    [InlineData("ETF", false)]
    [InlineData("futures", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBookableFromReportAlone_KeepsItsTruthTable(string? assetClass, bool expected)
    {
        OrderManagementSystem.IsBookableFromReportAlone(assetClass).Should().Be(expected);
        ProviderAssetClassCodeCatalog.IsBookableFromReportAlone(assetClass).Should().Be(expected);
    }

    [Theory]
    [InlineData("option", true)]
    [InlineData("OPTION", true)]
    [InlineData("Option", true)]
    [InlineData(" option", false)]
    [InlineData("options", false)]
    [InlineData("us_option", false)]
    [InlineData("equity", false)]
    public void RobinhoodIsOptionOrderRequest_MatchesOnlyTheOptionCode(string assetClass, bool expected)
    {
        RobinhoodBrokerageGateway.IsOptionOrderRequest(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            Metadata = new Dictionary<string, string> { ["asset_class"] = assetClass }
        }).Should().Be(expected);

        ProviderAssetClassCodeCatalog.IsCodeIgnoreCase(assetClass, SecurityAssetClassNames.Option, ProviderAssetClassCodeProviders.Robinhood)
            .Should().Be(expected);
    }

    [Fact]
    public void RobinhoodIsOptionOrderRequest_StillHonoursTheInstrumentUrl()
        => RobinhoodBrokerageGateway.IsOptionOrderRequest(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            Metadata = new Dictionary<string, string> { ["option_instrument_url"] = "https://api.robinhood.com/options/instruments/x/" }
        }).Should().BeTrue();

    [Theory]
    [InlineData("STK", "equity")]
    [InlineData("stk", "equity")]
    [InlineData(" STK ", "equity")]
    [InlineData("ETF", "equity")]
    [InlineData("OPT", "option")]
    [InlineData("FOP", "option")]
    [InlineData("FUT", "futures")]
    [InlineData("CASH", "forex")]
    [InlineData("BOND", "bond")]
    [InlineData("GOVT", "bond")]
    [InlineData("CRYPTO", "equity")]
    [InlineData(null, "equity")]
    public void IbMapAssetClass_KeepsItsTruthTable(string? secType, string expected)
        => IBCanonicalPayloadMapper.MapAssetClass(secType).Should().Be(expected);

    [Theory]
    [InlineData("OPT", true)]
    [InlineData("opt", true)]
    [InlineData("FOP", true)]
    [InlineData("Fop", true)]
    [InlineData("STK", false)]
    [InlineData("FUT", false)]
    [InlineData(" OPT", false)]
    public void IbOptionContractParse_GatesOnTheOptionSecTypes(string secType, bool expected)
    {
        var parsed = IBBrokerageGateway.TryParseOptionContract(new Dictionary<string, string>
        {
            ["sec_type"] = secType,
            ["right"] = "C",
            ["strike"] = "100"
        });

        (parsed is not null).Should().Be(expected);
        ProviderAssetClassCodeCatalog.IsCodeIgnoreCase(secType, SecurityAssetClassNames.Option, ProviderAssetClassCodeProviders.InteractiveBrokers)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("option", true)]
    [InlineData("OPTION", true)]
    [InlineData("us_option", false)]
    [InlineData("equity", false)]
    [InlineData("OPT", false)]
    public void ExecutionEndpointPositionEffect_IsDecidedByRobinhoodAndMeridianOptionCodesOnly(string assetClass, bool carriesPositionEffect)
        => ProviderAssetClassCodeCatalog.IsCodeIgnoreCase(
                assetClass,
                SecurityAssetClassNames.Option,
                ProviderAssetClassCodeProviders.Robinhood,
                ProviderAssetClassCodeProviders.Meridian)
            .Should().Be(carriesPositionEffect);
}
