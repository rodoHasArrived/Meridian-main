using System.Threading.Channels;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// The Alpaca gateway has two possible sources for its execution-report stream. The REST report
/// channel may only make writers wait when it is the source that is actually read.
/// </summary>
public sealed class AlpacaReportChannelPolicyTests
{
    [Fact]
    public void WithoutATradeUpdatesStream_TheReportChannelWaitsRatherThanEvictingReports()
    {
        AlpacaBrokerageGateway.ResolveReportChannelFullMode(hasTradeUpdatesStream: false)
            .Should().Be(BoundedChannelFullMode.Wait);
    }

    [Fact]
    public void WithATradeUpdatesStream_TheUnreadReportChannelNeverBlocksItsWriters()
    {
        // StreamExecutionReportsAsync reads the trade-updates stream in this mode and nothing
        // drains the REST report channel. A waiting channel would block every submit, cancel and
        // amend once it filled.
        AlpacaBrokerageGateway.ResolveReportChannelFullMode(hasTradeUpdatesStream: true)
            .Should().Be(BoundedChannelFullMode.DropOldest);
    }
}
