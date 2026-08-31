using FluentAssertions;
using Meridian.Contracts.Lifecycle;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class LifecycleSupervisorPipeTests
{
    [Fact]
    public async Task CurrentUserPipe_RoundTripsTypedStatusCommand()
    {
        var pipeName = $"Meridian.LifecycleSupervisor.Tests.{Guid.NewGuid():N}";
        await using var server = new LifecycleSupervisorPipeServer(
            pipeName,
            request => Task.FromResult(new LifecycleSupervisorMessageDto
            {
                Command = "status-result",
                RequestId = request.RequestId,
                Success = true,
                Status = new LifecycleSupervisorStatusDto
                {
                    Running = true,
                    PipeName = pipeName,
                    ManifestPath = "service/lifecycle-supervisor.json"
                }
            }),
            _ => { });
        server.Start();

        var response = await LifecycleSupervisorClient.SendAsync(
            pipeName,
            new LifecycleSupervisorMessageDto { Command = "status" },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Status!.Running.Should().BeTrue();
        response.Status.PipeName.Should().Be(pipeName);
    }
}
