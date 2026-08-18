using System.Text.Json;
using FluentAssertions;
using Meridian.QuantScript.Compilation;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Ui;

public sealed class QuantLabEndpointContractTests
{
    [Fact]
    public void Normalize_ParsesKnownInt64AndDecimalStringsExactly()
    {
        using var document = JsonDocument.Parse(
            "{\"large\":\"9223372036854775807\",\"precise\":\"1234567890.123456789012345678\"}");
        var parameters = document.RootElement.EnumerateObject()
            .ToDictionary(static property => property.Name, static property => (object?)property.Value.Clone());
        ParameterDescriptor[] descriptors =
        [
            new("large", "long", "Large", 0L),
            new("precise", "decimal", "Precise", 0m)
        ];

        var normalized = QuantRunParameterParser.Normalize(parameters, descriptors);

        normalized["large"].Should().Be(long.MaxValue);
        normalized["precise"].Should().Be(1234567890.123456789012345678m);
    }

    [Fact]
    public void Normalize_RejectsMalformedKnownValuesAndStructuredJson()
    {
        using var malformedDocument = JsonDocument.Parse("{\"large\":\"9223372036854775808\"}");
        using var structuredDocument = JsonDocument.Parse("{\"unknown\":{\"nested\":true}}");
        var descriptor = new[] { new ParameterDescriptor("large", "long", "Large", 0L) };

        var malformed = () => QuantRunParameterParser.Normalize(
            new Dictionary<string, object?> { ["large"] = malformedDocument.RootElement.GetProperty("large").Clone() },
            descriptor);
        var structured = () => QuantRunParameterParser.Normalize(
            new Dictionary<string, object?> { ["unknown"] = structuredDocument.RootElement.GetProperty("unknown").Clone() },
            Array.Empty<ParameterDescriptor>());

        malformed.Should().Throw<ArgumentException>().WithMessage("*large*");
        structured.Should().Throw<ArgumentException>().WithMessage("*unknown*");
    }

    [Fact]
    public void QuantRunResponse_PropagatesWarningsAndTradeLineage()
    {
        var fillId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var result = new ScriptRunResult(
            Success: true,
            Elapsed: TimeSpan.FromMilliseconds(20),
            CompileTime: TimeSpan.FromMilliseconds(5),
            PeakMemoryBytes: 1024,
            CompilationErrors: [],
            RuntimeDiagnostics: [],
            RuntimeError: null,
            ConsoleOutput: string.Empty,
            Metrics: [],
            Plots: [],
            Trades:
            [
                new ScriptTradeResult(
                    DateTimeOffset.Parse("2026-01-02T12:00:00Z"),
                    "SPY",
                    "Buy",
                    10m,
                    500m,
                    1m,
                    fillId,
                    orderId,
                    2)
            ],
            CapturedBacktests: [],
            RuntimeParameters: [],
            CompilationWarnings: [new ScriptDiagnostic("Warning", "Unused value", 1, 1)]);

        var response = QuantRunResponse.From(result);

        response.CompilationWarnings.Should().ContainSingle();
        response.Trades.Should().ContainSingle().Which.Should().Match<QuantTradeDto>(trade =>
            trade.FillId == fillId.ToString("D") &&
            trade.OrderId == orderId.ToString("D") &&
            trade.BacktestRunIndex == 2);
    }
}
