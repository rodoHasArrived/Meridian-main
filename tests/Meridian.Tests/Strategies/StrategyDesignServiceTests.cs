using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Guards the multi-symbol rebalance design scenario where a visual builder compiles into a
/// governed QuantScript proof run before a strategy can move toward promotion review.
/// </summary>
public sealed class StrategyDesignServiceTests
{
    [Fact]
    public async Task Scenario_MultiSymbolRebalance_DesignerTemplateValidatesAndRunsGeneratedQuantScript()
    {
        var service = new StrategyDesignService();
        var template = service.GetTemplates().Single(item => item.TemplateId == "equity-momentum-breakout");
        var document = service.Normalize(template.Document);

        var validation = service.Validate(document);
        var compiled = service.Compile(document);
        var runner = BuildRunner();

        var result = await runner.RunAsync(compiled.Source, new Dictionary<string, object?>());

        validation.IsValid.Should().BeTrue(validation.Summary);
        compiled.Source.Should().Contain("Strategy Builder run");
        compiled.FieldRefs.Should().Contain("PRICE");
        compiled.FieldRefs.Should().Contain("MOMENTUM_63D");
        compiled.FieldRefs.Should().Contain("VOLATILITY_20D");
        result.Success.Should().BeTrue(result.RuntimeError);
        result.Metrics.Should().Contain(metric => metric.Key == "Designer cells" && metric.Value == "4");
        result.ConsoleOutput.Should().Contain("Momentum score");
    }

    [Fact]
    public void Validate_PrototypeOnlyAmxField_ShouldKeepFieldVisibleButBlockExecution()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new(
                "score",
                "Prototype score",
                "formula",
                "rank",
                "AMX_PRIVATE_SCORE > 0.8",
                ["AMX_PRIVATE_SCORE"])
        ]));

        var validation = service.Validate(document);
        var field = service.GetFieldCatalog().Single(item => item.FieldId == "AMX_PRIVATE_SCORE");

        field.IsEnabled.Should().BeFalse();
        field.DisabledReason.Should().Contain("No Meridian canonical source");
        validation.IsValid.Should().BeFalse();
        validation.Messages.Should().Contain(message =>
            message.Code == "DisabledField" &&
            message.TargetId == "score" &&
            message.Message.Contains("AMX_PRIVATE_SCORE", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_StrategyWithoutRiskGuard_ShouldRecommendControlCellForPromotionReview()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("filter", "Filter universe", "visual", "filter", "PRICE > 20", ["PRICE"]),
            new("rank", "Rank universe", "formula", "rank", "MOMENTUM_63D", ["MOMENTUM_63D"])
        ]));

        var validation = service.Validate(document);
        var warning = validation.Messages.Single(message => message.Code == "RiskGuardRecommended");

        warning.Message.Should().Be("Add a risk or control cell before promotion review.");
        warning.Message.Should().NotContain("governance cell");
    }

    [Fact]
    public void Validate_BackwardTransitionWithoutLoopGuard_ShouldBlockExecution()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(
            cells:
            [
                new("filter", "Filter universe", "visual", "filter", "PRICE > 20", ["PRICE"]),
                new("rank", "Rank universe", "formula", "rank", "MOMENTUM_63D", ["MOMENTUM_63D"])
            ],
            transitions:
            [
                new("loop", "rank", "filter", "loop", "rebalance")
            ]));

        var validation = service.Validate(document);

        validation.IsValid.Should().BeFalse();
        validation.Messages.Should().Contain(message => message.Code == "LoopGuardRequired");
        validation.Messages.Should().Contain(message => message.Code == "LoopRationaleRequired");
    }

    [Fact]
    public void Validate_StateCellMissingExitCondition_ShouldBlock()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("state-node", "Monitoring", "state", "state", "monitoring phase", [],
                new Dictionary<string, string> { ["stateLabel"] = "Monitoring" }),
            new("risk", "Risk guard", "governance", "risk", "VOLATILITY_20D < 0.3", ["VOLATILITY_20D"])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Code == "StateCellExitRequired");
        result.Messages.Should().NotContain(m => m.Code == "StateCellLabelRequired");
    }

    [Fact]
    public void Validate_TradeCellWithAllRequiredParameters_ShouldPass()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("filter", "Filter universe", "visual", "filter", "PRICE > 20", ["PRICE"]),
            new("trade-cell", "Buy equities", "trade", "trade", "execute buy order", [],
                new Dictionary<string, string>
                {
                    ["instrument"] = "Equity",
                    ["direction"] = "Buy",
                    ["sizingMethod"] = "EqualWeight",
                    ["priceConstraint"] = "VWAP"
                }),
            new("risk", "Risk guard", "governance", "risk", "VOLATILITY_20D < 0.3", ["VOLATILITY_20D"])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeTrue(result.Summary);
        result.Messages.Should().NotContain(m =>
            m.Code == "TradeCellInstrumentRequired" ||
            m.Code == "TradeCellDirectionRequired" ||
            m.Code == "TradeCellSizingRequired");
    }

    [Fact]
    public void Validate_ConcurrentCellWithBadSemantics_ShouldBlock()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("parallel", "Parallel check", "concurrent", "concurrent", "parallel eval", [],
                new Dictionary<string, string>
                {
                    ["branchIds"] = "cell-a,cell-b",
                    ["semantics"] = "bad-value"
                }),
            new("risk", "Risk guard", "governance", "risk", "true", [])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Code == "ConcurrentCellSemanticsRequired");
    }

    [Fact]
    public void Validate_ConcurrentCellWithMissingBranch_ShouldBlock()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("rank", "Rank", "formula", "rank", "MOMENTUM_63D > 0", ["MOMENTUM_63D"]),
            new("parallel", "Parallel check", "concurrent", "concurrent", "parallel eval", [],
                new Dictionary<string, string>
                {
                    ["branchIds"] = "rank,missing-cell",
                    ["semantics"] = "all-pass"
                }),
            new("risk", "Risk guard", "governance", "risk", "true", [])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Code == "ConcurrentCellBranchMissing");
    }

    [Fact]
    public void Validate_UniverseBuilderCellMissingAssetClass_ShouldBlock()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("ub", "Universe", "universe-builder", "universe", "build universe", ["PRICE"],
                new Dictionary<string, string> { ["includeRules"] = "PRICE > 10" }),
            new("risk", "Risk guard", "governance", "risk", "true", [])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Code == "UniverseBuilderAssetClassRequired");
        result.Messages.Should().NotContain(m => m.Code == "UniverseBuilderIncludeRulesRequired");
    }

    [Fact]
    public void Validate_UniverseBuilderInvalidSizeConstraints_ShouldBlock()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("ub", "Universe", "universe-builder", "universe", "build universe", [],
                new Dictionary<string, string>
                {
                    ["assetClass"] = "Equity",
                    ["includeRules"] = "PRICE > 10",
                    ["minSize"] = "101",
                    ["maxSize"] = "100"
                }),
            new("risk", "Risk guard", "governance", "risk", "true", [])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Code == "UniverseBuilderSizeRangeInvalid");
    }

    [Fact]
    public void Validate_UniverseBuilderRulesWithUnknownField_ShouldBlock()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("ub", "Universe", "universe-builder", "universe", "build universe", ["PRICE"],
                new Dictionary<string, string>
                {
                    ["assetClass"] = "Equity",
                    ["includeRules"] = "UNKNOWN_FIELD > 10"
                }),
            new("risk", "Risk guard", "governance", "risk", "true", [])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Code == "UnknownField" && m.TargetId == "ub");
    }

    [Fact]
    public void Validate_TradeCellFixedSizingWithoutNumericValue_ShouldBlock()
    {
        var service = new StrategyDesignService();
        var document = service.Normalize(BuildDocument(cells:
        [
            new("trade-cell", "Buy equities", "trade", "trade", "execute buy order", [],
                new Dictionary<string, string>
                {
                    ["instrument"] = "Equity",
                    ["direction"] = "Buy",
                    ["sizingMethod"] = "FixedShares",
                    ["sizingValue"] = "not-a-number"
                }),
            new("risk", "Risk guard", "governance", "risk", "true", [])
        ]));

        var result = service.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Code == "TradeCellSizingValueRequired");
    }

    [Fact]
    public void Normalize_TradeAndConcurrentParameters_ShouldCanonicalizeEnums()
    {
        var service = new StrategyDesignService();
        var document = BuildDocument(cells:
        [
            new("parallel", "Parallel check", "concurrent", "concurrent", "parallel eval", [],
                new Dictionary<string, string>
                {
                    ["branchIds"] = "rank",
                    ["semantics"] = "ALL-PASS"
                }),
            new("trade-cell", "Buy equities", "trade", "trade", "execute buy order", [],
                new Dictionary<string, string>
                {
                    ["instrument"] = "equity",
                    ["direction"] = "buy",
                    ["sizingMethod"] = "equalweight"
                }),
            new("rank", "Rank", "formula", "rank", "MOMENTUM_63D > 0", ["MOMENTUM_63D"])
        ]);

        var normalized = service.Normalize(document);
        var concurrent = normalized.Cells.Single(cell => cell.CellId == "parallel");
        var trade = normalized.Cells.Single(cell => cell.CellId == "trade-cell");

        concurrent.Parameters!["semantics"].Should().Be("all-pass");
        trade.Parameters!["instrument"].Should().Be("Equity");
        trade.Parameters!["direction"].Should().Be("Buy");
        trade.Parameters!["sizingMethod"].Should().Be("EqualWeight");
    }

    [Fact]
    public void Validate_FullyPopulatedStateCellTemplate_ShouldPass()
    {
        var service = new StrategyDesignService();
        var template = service.GetTemplates().Single(t => t.TemplateId == "state-machine-strategy");
        var document = service.Normalize(template.Document);

        var result = service.Validate(document);

        result.IsValid.Should().BeTrue(result.Summary);
    }

    private static StrategyDesignDocument BuildDocument(
        IReadOnlyList<StrategyDesignCell>? cells = null,
        IReadOnlyList<StrategyDesignTransition>? transitions = null)
        => new(
            "designer-test",
            "Designer test",
            "Test strategy design",
            "1",
            "provider-bars/equities/daily",
            ["SPY", "QQQ"],
            cells ??
            [
                new("filter", "Filter universe", "visual", "filter", "PRICE > 20", ["PRICE"]),
                new("rank", "Rank universe", "formula", "rank", "MOMENTUM_63D", ["MOMENTUM_63D"]),
                new("risk", "Risk guard", "governance", "risk", "VOLATILITY_20D < 0.3", ["VOLATILITY_20D"])
            ],
            transitions ?? [],
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1));

    private static ScriptRunner BuildRunner()
    {
        var compiler = new RoslynScriptCompiler(
            Microsoft.Extensions.Options.Options.Create(new QuantScriptOptions()),
            NullLogger<RoslynScriptCompiler>.Instance);
        return new ScriptRunner(
            compiler,
            new Mock<Meridian.QuantScript.Api.IQuantDataContext>().Object,
            new PlotQueue(),
            Microsoft.Extensions.Options.Options.Create(new QuantScriptOptions { RunTimeoutSeconds = 10 }),
            NullLogger<ScriptRunner>.Instance,
            null);
    }
}
