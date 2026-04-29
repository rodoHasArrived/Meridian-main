using System.IO;
using System.IO.Pipes;
using System.Text;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public void TryAcquire_WhenPrimaryOwnsLocalMutex_ShouldMakeSecondInstanceSecondary()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceService(
            $"Local\\Meridian.Desktop.Tests.{suffix}",
            $"Meridian.Desktop.Tests.{suffix}.Pipe");
        using var secondary = new SingleInstanceService(
            $"Local\\Meridian.Desktop.Tests.{suffix}",
            $"Meridian.Desktop.Tests.{suffix}.Pipe");

        primary.TryAcquire().Should().BeTrue();
        secondary.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public async Task TrySendArgsToConfiguredPrimary_WhenPipeIsListening_ShouldForwardArgsAndReturnTrue()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"Meridian.Desktop.Tests.{suffix}.Pipe";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var service = new SingleInstanceService(
            $"Local\\Meridian.Desktop.Tests.{suffix}",
            pipeName);

        var readTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync();
        });

        service.TrySendArgsToConfiguredPrimary(["--page=ResearchShell", "--start-collector"]).Should().BeTrue();

        var payload = await readTask.WaitAsync(TimeSpan.FromSeconds(5));
        payload.Should().Be("--page=ResearchShell\n--start-collector");
    }
}
