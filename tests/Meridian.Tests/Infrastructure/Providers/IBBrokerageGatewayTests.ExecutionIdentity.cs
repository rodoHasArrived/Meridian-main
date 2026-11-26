using System.Globalization;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Fact]
    public async Task ExecutionCallbacks_CarryTheBrokerExecutionId_ButNotAPerFillQuantity()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.RaiseExecution(new IBExecutionUpdate(11001, "AAPL", "BOT", 2m, 100d,
            2m, 100d, "0000e0d5.6576f4a2.01.01", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
        client.RaiseExecution(new IBExecutionUpdate(11001, "AAPL", "BOT", 2m, 100d,
            4m, 100d, "0000e0d5.6576f4a3.01.01", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fills = new List<ExecutionReport>();
        await ReadUntilAsync(sut.StreamExecutionReportsAsync(timeout.Token), report =>
        {
            if (report.ReportType is ExecutionReportType.PartialFill or ExecutionReportType.Fill)
                fills.Add(report);
            return report.FilledQuantity == 4m;
        }, timeout.Token);
        var first = fills.Single(report => report.FilledQuantity == 2m);
        var second = fills.Single(report => report.FilledQuantity == 4m);

        first.ExecutionId.Should().Be("0000e0d5.6576f4a2.01.01");
        second.ExecutionId.Should().Be("0000e0d5.6576f4a3.01.01");
        // A per-fill quantity would let a restarted host adopt and book this fill for an order it
        // no longer tracks. IB executions are not deduplicated durably by broker id, so that
        // adoption stays off until they are.
        first.LastFillQuantity.Should().BeNull();
        second.LastFillQuantity.Should().BeNull();
    }

    [Theory]
    [InlineData("20261125-15:15:30", "2026-11-25T15:15:30Z")]
    [InlineData("20261125 10:15:30 US/Eastern", "2026-11-25T15:15:30Z")]
    [InlineData("20261125 10:15:30 America/New_York", "2026-11-25T15:15:30Z")]
    [InlineData("20260715 10:15:30 US/Eastern", "2026-07-15T14:15:30Z")]
    public void ExecutionTime_ParsesTheFormatsIbReports(string raw, string expectedUtc)
    {
        IBCanonicalPayloadMapper.TryParseExecutionTime(raw, out var executedAt).Should().BeTrue();

        executedAt.Should().Be(DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture));
        executedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("20261125  10:15:30")]
    [InlineData("20261125 10:15:30 Not/A_Zone")]
    [InlineData("not a time")]
    [InlineData("")]
    [InlineData(null)]
    public void ExecutionTime_RefusesTimesItCannotPlaceOnTheTimeline(string? raw)
        => IBCanonicalPayloadMapper.TryParseExecutionTime(raw, out _).Should().BeFalse();
}
