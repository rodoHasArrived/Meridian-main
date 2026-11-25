using System.IO.Pipes;
using System.Text.Json;
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

    [Fact]
    public async Task RegisteredHostReceivesSessionBoundAcknowledgement()
    {
        var pipeName = $"Meridian.LifecycleSupervisor.Tests.{Guid.NewGuid():N}";
        await using var server = new LifecycleSupervisorPipeServer(
            pipeName, _ => throw new InvalidOperationException("Expected host registration."), _ => { });
        server.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var host = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await host.ConnectAsync(deadline.Token);
        using var reader = new StreamReader(host, leaveOpen: true);
        await using var writer = new StreamWriter(host, leaveOpen: true) { AutoFlush = true };
        var registration = new LifecycleSupervisorMessageDto
        {
            Command = "register-host",
            SessionId = "provider-host-session",
            Lifecycle = new RuntimeLifecycleSnapshotDto
            {
                SessionId = "provider-host-session",
                State = RuntimeLifecycleState.Created,
                Readiness = RuntimeReadinessStatus.Starting,
                StartedAtUtc = DateTimeOffset.UtcNow,
                StateChangedAtUtc = DateTimeOffset.UtcNow,
                ActivePhase = "created",
                AcceptingWork = false,
                ShutdownRequested = false
            }
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(registration,
            LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto));

        var acknowledgement = JsonSerializer.Deserialize((await reader.ReadLineAsync(deadline.Token))!,
            LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto);

        acknowledgement!.Success.Should().BeTrue();
        acknowledgement.Command.Should().Be("host-registered");
        acknowledgement.RequestId.Should().Be(registration.RequestId);
        acknowledgement.SessionId.Should().Be(registration.SessionId);
    }
}
