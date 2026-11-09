using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// <see cref="AsyncHandlerGuard"/> is what keeps an exception in an <c>async void</c> event
/// handler from reaching the dispatcher. These pin its containment rules: ordinary failures are
/// reported and contained, cancellation is silent, and fatal runtime failures still propagate.
/// </summary>
public sealed class AsyncHandlerGuardTests
{
    private sealed class Recorder
    {
        public List<(Exception Exception, string Handler)> Reports { get; } = [];

        public void Report(Exception exception, string handler) => Reports.Add((exception, handler));
    }

    [Fact]
    public async Task RunAsync_WhenTheHandlerCompletes_RunsItAndReportsNothing()
    {
        var recorder = new Recorder();
        var ran = false;

        await AsyncHandlerGuard.RunAsync(
            async () =>
            {
                await Task.Yield();
                ran = true;
            },
            "OnSaveClick",
            recorder.Report);

        ran.Should().BeTrue();
        recorder.Reports.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(NullReferenceException))]      // not on the dispatcher's "recoverable" list
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(InvalidOperationException))]   // on the "recoverable" list
    public async Task RunAsync_WhenTheHandlerThrows_ContainsTheFailureAndReportsItWithTheHandlerName(Type exceptionType)
    {
        var recorder = new Recorder();
        var failure = (Exception)Activator.CreateInstance(exceptionType)!;

        var act = () => AsyncHandlerGuard.RunAsync(
            async () =>
            {
                await Task.Yield();
                throw failure;
            },
            "OnRefreshClick",
            recorder.Report);

        await act.Should().NotThrowAsync("an event handler failure must not reach the dispatcher");
        recorder.Reports.Should().ContainSingle();
        recorder.Reports[0].Exception.Should().BeSameAs(failure);
        recorder.Reports[0].Handler.Should().Be("OnRefreshClick");
    }

    [Fact]
    public async Task RunAsync_WhenTheHandlerThrowsSynchronously_StillContainsAndReportsIt()
    {
        var recorder = new Recorder();

        var act = () => AsyncHandlerGuard.RunAsync(
            () => throw new FormatException("bad input"),
            "OnApplyClick",
            recorder.Report);

        await act.Should().NotThrowAsync();
        recorder.Reports.Should().ContainSingle().Which.Exception.Should().BeOfType<FormatException>();
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task RunAsync_WhenTheHandlerIsCancelled_IsSilent(Type exceptionType)
    {
        var recorder = new Recorder();
        var cancellation = (Exception)Activator.CreateInstance(exceptionType)!;

        var act = () => AsyncHandlerGuard.RunAsync(
            () => Task.FromException(cancellation),
            "OnLoadClick",
            recorder.Report);

        await act.Should().NotThrowAsync();
        recorder.Reports.Should().BeEmpty("a cancelled action is not a failure to show the operator");
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public async Task RunAsync_WhenTheFailureIsFatal_LetsItPropagateUnreported(Type exceptionType)
    {
        var recorder = new Recorder();
        var fatal = (Exception)Activator.CreateInstance(exceptionType)!;

        var act = () => AsyncHandlerGuard.RunAsync(
            () => Task.FromException(fatal),
            "OnExportClick",
            recorder.Report);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(fatal);
        recorder.Reports.Should().BeEmpty();
    }

    [Fact]
    public void IsFatal_ClassifiesOnlyUnrecoverableRuntimeFailures()
    {
        AsyncHandlerGuard.IsFatal(new OutOfMemoryException()).Should().BeTrue();
        AsyncHandlerGuard.IsFatal(new StackOverflowException()).Should().BeTrue();
        AsyncHandlerGuard.IsFatal(new AccessViolationException()).Should().BeTrue();
        AsyncHandlerGuard.IsFatal(new NullReferenceException()).Should().BeFalse();
        AsyncHandlerGuard.IsFatal(new InvalidOperationException()).Should().BeFalse();
    }
}
