using System.Text.Json;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Tests.Helpers;

namespace Meridian.QuantScript.Tests;

public sealed class QuantScriptGlobalsTests
{
    [Fact]
    public void Param_ExactInt64AndDecimalStrings_PreserveValues()
    {
        var globals = CreateGlobals(new Dictionary<string, object?>
        {
            ["large"] = "9223372036854775807",
            ["precise"] = "1234567890.123456789012345678"
        });

        globals.Param<long>("large").Should().Be(long.MaxValue);
        globals.Param<decimal>("precise").Should().Be(1234567890.123456789012345678m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-integer")]
    [InlineData(1.5d)]
    [InlineData("2147483648")]
    public void Param_InvalidOrLossyIntOverride_FailsClosed(object? supplied)
    {
        var globals = CreateGlobals(new Dictionary<string, object?> { ["value"] = supplied });

        var act = () => globals.Param("value", 42);

        act.Should().Throw<ArgumentException>().WithMessage("*failed validation*");
    }

    [Fact]
    public void Param_OutOfDeclaredRange_FailsClosed()
    {
        var globals = CreateGlobals(new Dictionary<string, object?> { ["lookback"] = 500 });

        var act = () => globals.Param("lookback", 20, min: 2, max: 252);

        act.Should().Throw<ArgumentException>().WithMessage("*outside the inclusive range*");
    }

    [Fact]
    public void Param_Int64BoundBeyondDoublePrecision_FailsClosedExactly()
    {
        var globals = CreateGlobals(new Dictionary<string, object?>
        {
            ["value"] = "9007199254740993"
        });

        var act = () => globals.Param<long>("value", max: 9007199254740992d);

        act.Should().Throw<ArgumentException>().WithMessage("*outside the inclusive range*");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Param_NonFiniteTypedOverride_FailsClosed(double supplied)
    {
        var globals = CreateGlobals(new Dictionary<string, object?> { ["value"] = supplied });

        var act = () => globals.Param("value", 0d);

        act.Should().Throw<ArgumentException>().WithMessage("*cannot be represented exactly*");
    }

    [Fact]
    public void Param_ScalarJsonNumber_UsesItsExactLexeme()
    {
        using var document = JsonDocument.Parse("{\"value\":9223372036854775807}");
        var globals = CreateGlobals(new Dictionary<string, object?>
        {
            ["value"] = document.RootElement.GetProperty("value").Clone()
        });

        globals.Param<long>("value").Should().Be(long.MaxValue);
    }

    private static QuantScriptGlobals CreateGlobals(IReadOnlyDictionary<string, object?> parameters)
        => new(
            new DataProxy(new FakeQuantDataContext(), () => CancellationToken.None),
            new BacktestProxy(null, new QuantScriptOptions()),
            () => CancellationToken.None,
            parameters);
}
