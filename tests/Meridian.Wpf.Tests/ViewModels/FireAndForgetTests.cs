using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FireAndForgetTests
{
    [Fact]
    public async Task ObserveAsync_WhenTaskFaults_InvokesCallbackAndDoesNotThrow()
    {
        Exception? captured = null;
        var task = Task.FromException(new InvalidOperationException("boom"));

        await FireAndForget.ObserveAsync(task, NullLogger.Instance, "op", ex => captured = ex);

        captured.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ObserveAsync_WhenCancelled_DoesNotInvokeCallback()
    {
        var called = false;
        var task = Task.FromCanceled(new CancellationToken(canceled: true));

        await FireAndForget.ObserveAsync(task, NullLogger.Instance, "op", _ => called = true);

        called.Should().BeFalse();
    }

    [Fact]
    public async Task ObserveAsync_WhenCallbackThrows_SwallowsCallbackException()
    {
        var task = Task.FromException(new InvalidOperationException("boom"));

        var act = () => FireAndForget.ObserveAsync(
            task, NullLogger.Instance, "op", _ => throw new InvalidOperationException("ui"));

        await act.Should().NotThrowAsync();
    }
}
