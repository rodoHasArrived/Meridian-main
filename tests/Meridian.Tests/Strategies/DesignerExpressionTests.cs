using FluentAssertions;
using Meridian.Strategies.Live.Designer;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// The designer grammar is the containment boundary that lets operator-written documents reach
/// live execution without script isolation: anything outside it must fail to parse, operand
/// shapes must be checked before a run activates, and evaluation faults must surface as the
/// bounded exception the strategy turns into a skipped symbol.
/// </summary>
public sealed class DesignerExpressionTests
{
    private static readonly IReadOnlySet<string> Fields =
        new HashSet<string>(StringComparer.Ordinal) { "PRICE", "MOMENTUM_63D", "VOLATILITY_20D", "PORTFOLIO_WEIGHT" };

    private static IReadOnlyDictionary<string, decimal> Values(params (string Field, decimal Value)[] values)
        => values.ToDictionary(item => item.Field, item => item.Value, StringComparer.Ordinal);

    [Theory]
    [InlineData("PRICE + 2 * 3", 26)]
    [InlineData("(PRICE + 2) * 3", 66)]
    [InlineData("-PRICE + 30", 10)]
    [InlineData("PRICE / 4 - 1", 4)]
    [InlineData(".5 * PRICE", 10)]
    public void Arithmetic_FollowsPrecedenceAndParentheses(string source, int expected)
    {
        var expression = DesignerExpression.Parse(source, Fields);

        expression.ResultKind.Should().Be(DesignerResultKind.Number);
        expression.Evaluate(Values(("PRICE", 20m))).Number.Should().Be(expected);
    }

    [Theory]
    [InlineData("PRICE > 20", false)]
    [InlineData("PRICE >= 20", true)]
    [InlineData("PRICE == 20 && MOMENTUM_63D < 0", true)]
    [InlineData("!(PRICE != 20)", true)]
    [InlineData("PRICE < 10 || MOMENTUM_63D <= -1", true)]
    public void Conditions_EvaluateToBooleans(string source, bool expected)
    {
        var expression = DesignerExpression.Parse(source, Fields);

        expression.ResultKind.Should().Be(DesignerResultKind.Boolean);
        expression.EvaluateCondition(Values(("PRICE", 20m), ("MOMENTUM_63D", -1m))).Should().Be(expected);
    }

    [Theory]
    [InlineData("System.IO.File.Delete(\"x\")")]
    [InlineData("PRICE.ToString()")]
    [InlineData("Math.Abs(PRICE)")]
    [InlineData("price > 1")]
    [InlineData("UNKNOWN_FIELD > 1")]
    [InlineData("PRICE = 5")]
    [InlineData("PRICE ? 1 : 2")]
    [InlineData("\"PRICE\" > 1")]
    public void AnythingOutsideTheClosedGrammar_FailsToParse(string source)
    {
        var parse = () => DesignerExpression.Parse(source, Fields);

        parse.Should().Throw<DesignerExpressionException>();
    }

    [Theory]
    [InlineData("45d > PRICE", "unsupported suffix")]
    [InlineData("PRICE > 1 > 0", "Unexpected")]
    [InlineData("PRICE && MOMENTUM_63D > 0", "true/false operands")]
    [InlineData("(PRICE > 1) + 2", "numeric operands")]
    [InlineData("!PRICE", "'!' requires")]
    [InlineData("-(PRICE > 1)", "Unary '-'")]
    [InlineData("(PRICE + 1", "Missing ')'")]
    [InlineData("PRICE +", "ended unexpectedly")]
    [InlineData("   ", "empty")]
    public void MalformedOrMixedShapeSource_IsRefusedWithAnOperatorFacingReason(string source, string reason)
    {
        var parse = () => DesignerExpression.Parse(source, Fields);

        parse.Should().Throw<DesignerExpressionException>().WithMessage($"*{reason}*");
    }

    [Fact]
    public void Limits_BoundParserWorkOnOperatorText()
    {
        var tooLong = string.Join(" + ", Enumerable.Repeat("PRICE", 1_000));
        var tooDeep = new string('(', 40) + "PRICE" + new string(')', 40);
        var deepButAllowed = new string('(', 30) + "PRICE" + new string(')', 30);

        ((Action)(() => DesignerExpression.Parse(tooLong, Fields))).Should().Throw<DesignerExpressionException>().WithMessage("*limit*");
        ((Action)(() => DesignerExpression.Parse(tooDeep, Fields))).Should().Throw<DesignerExpressionException>().WithMessage("*nests deeper*");
        DesignerExpression.Parse(deepButAllowed, Fields).ResultKind.Should().Be(DesignerResultKind.Number);
    }

    [Fact]
    public void TryParse_RequiresTheResultKindTheCellNeeds()
    {
        DesignerExpression.TryParse("PRICE", Fields, DesignerResultKind.Boolean, out var asFilter, out var filterReason)
            .Should().BeFalse();
        asFilter.Should().BeNull();
        filterReason.Should().Contain("true/false condition");

        DesignerExpression.TryParse("PRICE > 20", Fields, DesignerResultKind.Number, out _, out var rankReason)
            .Should().BeFalse();
        rankReason.Should().Contain("numeric score");

        DesignerExpression.TryParse("MOMENTUM_63D - VOLATILITY_20D", Fields, DesignerResultKind.Number, out var rank, out var noReason)
            .Should().BeTrue();
        rank!.ReferencedFields().Should().BeEquivalentTo("MOMENTUM_63D", "VOLATILITY_20D");
        noReason.Should().BeNull();
    }

    [Fact]
    public void ColdField_DivisionByZero_AndOverflow_AreBoundedEvaluationFailures()
    {
        var cold = () => DesignerExpression.Parse("MOMENTUM_63D > 0", Fields).Evaluate(Values(("PRICE", 1m)));
        var divide = () => DesignerExpression.Parse("PRICE / (PRICE - 1)", Fields).Evaluate(Values(("PRICE", 1m)));
        var overflow = () => DesignerExpression.Parse("PRICE * PRICE", Fields).Evaluate(Values(("PRICE", decimal.MaxValue)));

        cold.Should().Throw<DesignerExpressionException>().WithMessage("*no value for this symbol yet*");
        divide.Should().Throw<DesignerExpressionException>().WithMessage("*Division by zero*");
        overflow.Should().Throw<DesignerExpressionException>().WithMessage("*overflow*");
    }

    [Fact]
    public void ShortCircuit_LetsAGuardAnswerWhileItsSecondFieldIsCold()
    {
        var guard = DesignerExpression.Parse("PORTFOLIO_WEIGHT == 0 || MOMENTUM_63D > 0", Fields);
        var both = DesignerExpression.Parse("PORTFOLIO_WEIGHT > 0 && MOMENTUM_63D > 0", Fields);
        var weightOnly = Values(("PORTFOLIO_WEIGHT", 0m));

        guard.EvaluateCondition(weightOnly).Should().BeTrue();
        both.EvaluateCondition(weightOnly).Should().BeFalse();
    }
}
