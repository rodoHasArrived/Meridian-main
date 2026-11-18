using FluentAssertions;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Ui.Shared.Endpoints;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The trading workspace tells the order ticket whether manual orders can route on the active
/// path, mirroring the OMS: manual live orders are refused until a reviewed manual-intent
/// authority contract exists, so the ticket says so before the operator submits.
/// </summary>
public sealed class WorkstationManualOrderEntryTests
{
    [Fact]
    public void LiveGateway_ManualEntryIsUnavailableWithReason()
    {
        var gateway = Gateway("alpaca", mode: null);

        var entry = WorkstationEndpoints.ResolveManualOrderEntry(gateway, new BrokerageConfiguration { Gateway = "alpaca" });

        entry.Should().NotBeNull();
        entry!.Available.Should().BeFalse();
        entry.ReasonCode.Should().Be(OrderAuthorityReasonCodes.ManualLiveIntentContractNotReviewed);
        entry.Reason.Should().Contain("manual-intent");
    }

    [Fact]
    public void PaperGateway_ManualEntryIsAvailable()
    {
        var entry = WorkstationEndpoints.ResolveManualOrderEntry(Gateway("paper", mode: null), configuration: null);

        entry.Should().BeEquivalentTo(new { Available = true, ReasonCode = (string?)null, Reason = (string?)null });
    }

    [Fact]
    public void GatewayDeclaringPaperMode_IsTreatedAsPaper()
    {
        var entry = WorkstationEndpoints.ResolveManualOrderEntry(
            Gateway("sandbox", mode: ExecutionMode.Paper), new BrokerageConfiguration { Gateway = "sandbox" });

        entry!.Available.Should().BeTrue("the OMS uses the gateway's declared mode");
    }

    [Fact]
    public void NoGateway_LeavesTheTicketToItsOtherChecks()
    {
        WorkstationEndpoints.ResolveManualOrderEntry(gateway: null, configuration: null).Should().BeNull();
    }

    private static IExecutionGateway Gateway(string id, ExecutionMode? mode)
    {
        if (mode is { } declared)
        {
            var withMode = Substitute.For<IExecutionGateway, IExecutionGatewayModeProvider>();
            withMode.GatewayId.Returns(id);
            ((IExecutionGatewayModeProvider)withMode).ExecutionMode.Returns(declared);
            return withMode;
        }

        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns(id);
        return gateway;
    }
}
