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
