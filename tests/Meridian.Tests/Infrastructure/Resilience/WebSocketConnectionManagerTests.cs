using FluentAssertions;
using Meridian.Infrastructure.Resilience;
using Xunit;

namespace Meridian.Tests.Infrastructure.Resilience;

/// <summary>
/// Unit tests for WebSocketConnectionManager lifecycle and resource management.
/// </summary>
public class WebSocketConnectionManagerTests
{
    [Fact]
    public void Constructor_WithValidArgs_CreatesInstance()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        manager.Should().NotBeNull();
        manager.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void StartReceiveLoop_WithoutConnect_ThrowsInvalidOperationException()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        var act = () => manager.StartReceiveLoop(msg => Task.CompletedTask);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task DisposeAsync_WithoutConnect_DoesNotThrow()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        // Should not throw even if never connected
        await manager.DisposeAsync();
    }
}
