using FluentAssertions;
using Meridian.DataIntegration.Monitoring;
using Xunit;

namespace Meridian.Tests.DataIntegration.Monitoring;

public sealed class DataLossAccountingTests : IDisposable
{
    public DataLossAccountingTests()
    {
        DataLossAccounting.Reset();
    }

    [Fact]
    public void GetReconciliationReport_WhenEveryReceivedEventIsAccountedFor_ReturnsFullyReconciledReport()
    {
        DataLossAccounting.IncReceived("alpaca");
        DataLossAccounting.IncReceived("alpaca");
        DataLossAccounting.IncReceived("alpaca");
        DataLossAccounting.IncReceived("alpaca");
        DataLossAccounting.IncReceived("alpaca");
        DataLossAccounting.IncReceivedDuplicate("alpaca");
        DataLossAccounting.IncRejected();
        DataLossAccounting.IncPipelineDropped();
        DataLossAccounting.IncStoreFailed();
        DataLossAccounting.IncStored();

        var report = DataLossAccounting.GetReconciliationReport();

        report.Received.Should().Be(5);
        report.Unaccounted.Should().Be(0);
        report.ReconciliationRate.Should().Be(1.0);
        report.IsFullyReconciled.Should().BeTrue();
        report.ProviderBreakdown["alpaca"].Received.Should().Be(5);
        report.ProviderBreakdown["alpaca"].Duplicates.Should().Be(1);
    }

    [Fact]
    public void GetReconciliationReport_WhenReceivedEventsAreMissingTerminalOutcomes_ReturnsUnaccountedDelta()
    {
        DataLossAccounting.IncReceived("polygon");
        DataLossAccounting.IncReceived("polygon");

        var report = DataLossAccounting.GetReconciliationReport();

        report.Received.Should().Be(2);
        report.Unaccounted.Should().Be(2);
        report.ReconciliationRate.Should().Be(0.0);
        report.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsGlobalAndProviderCounters()
    {
        DataLossAccounting.IncReceived("alpaca");
        DataLossAccounting.IncReceivedDuplicate("alpaca");
        DataLossAccounting.IncStored();

        DataLossAccounting.Reset();

        DataLossAccounting.Received.Should().Be(0);
        DataLossAccounting.ReceivedDuplicates.Should().Be(0);
        DataLossAccounting.Stored.Should().Be(0);
        DataLossAccounting.GetReconciliationReport().ProviderBreakdown.Should().BeEmpty();
    }

    public void Dispose()
    {
        DataLossAccounting.Reset();
    }
}
