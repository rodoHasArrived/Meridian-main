using System.Net;
using FluentAssertions;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class DesktopLaunchTicketServiceTests
{
    [Fact]
    public void Redeem_LoopbackTicket_ReturnsClaimExactlyOnce()
    {
        var service = new DesktopLaunchTicketService();
        var token = service.Issue("local-owner", "Portfolio");

        var first = service.Redeem(IPAddress.Loopback, token);
        var second = service.Redeem(IPAddress.Loopback, token);

        first.Should().NotBeNull();
        first!.Username.Should().Be("local-owner");
        first.Page.Should().Be("Portfolio");
        second.Should().BeNull();
    }

    [Fact]
    public void Redeem_NonLoopbackRequest_DoesNotConsumeTicket()
    {
        var service = new DesktopLaunchTicketService();
        var token = service.Issue("local-owner", "Accounting");

        service.Redeem(IPAddress.Parse("192.0.2.10"), token).Should().BeNull();
        service.Redeem(IPAddress.Loopback, token).Should().NotBeNull();
    }
}
