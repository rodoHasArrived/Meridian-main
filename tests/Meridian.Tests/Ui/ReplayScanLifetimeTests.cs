using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ReplayScanLifetimeTests
{
    [Fact]
    public async Task HostStop_CancelsReadersAndWaitsForTheirCleanup()
    {
        using var stopping = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stopping.Token);
        await using var scans = new ReplayScanLifetime(lifetime);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerReleased = false;
        scans.TryStart(async token =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
            }

            await releaseReader.Task;
            readerReleased = true;
        }).Should().BeTrue();

        try
        {
            stopping.Cancel();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var shutdown = scans.StopAsync(CancellationToken.None);
            shutdown.IsCompleted.Should().BeFalse("the active reader has not released its file yet");
            scans.TryStart(_ => Task.CompletedTask).Should().BeFalse();

            releaseReader.SetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            readerReleased.Should().BeTrue();
        }
        finally
        {
            releaseReader.TrySetResult();
        }
    }
}
