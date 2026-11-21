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

    // ── Notification throttling ──────────────────────────────────────
    // A timer or polling handler that keeps failing must not notify on every tick.

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2024, 1, 2, 9, 30, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public void Throttle_LetsTheFirstFailureThroughAndHoldsBackRepeatsWithinTheInterval()
    {
        var clock = new ManualTimeProvider();
        var throttle = new HandlerNotificationThrottle(TimeSpan.FromMinutes(1), clock);

        throttle.TryAcquire("BackfillViewModel.OnProgressPollTimerTick", out var first).Should().BeTrue();
        first.Should().Be(0);

        for (var tick = 0; tick < 5; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            throttle.TryAcquire("BackfillViewModel.OnProgressPollTimerTick", out _)
                .Should().BeFalse("a failure on every tick must not notify on every tick");
        }
    }

    [Fact]
    public void Throttle_AfterTheInterval_NotifiesAgainAndReportsHowManyWereHeldBack()
    {
        var clock = new ManualTimeProvider();
        var throttle = new HandlerNotificationThrottle(TimeSpan.FromMinutes(1), clock);

        throttle.TryAcquire("Page.OnTick", out _).Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(10));
        throttle.TryAcquire("Page.OnTick", out _).Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(10));
        throttle.TryAcquire("Page.OnTick", out _).Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(40));
        throttle.TryAcquire("Page.OnTick", out var heldBack).Should().BeTrue();
        heldBack.Should().Be(2);

        // The count starts over after each notification.
        clock.Advance(TimeSpan.FromMinutes(1));
        throttle.TryAcquire("Page.OnTick", out var afterReset).Should().BeTrue();
        afterReset.Should().Be(0);
    }

    [Fact]
    public void Throttle_IsPerHandler_SoOneNoisyHandlerDoesNotHideAnother()
    {
        var throttle = new HandlerNotificationThrottle(TimeSpan.FromMinutes(1), new ManualTimeProvider());

        throttle.TryAcquire("BackfillPage.OnPageLoaded", out _).Should().BeTrue();
        throttle.TryAcquire("BackfillPage.OnPageLoaded", out _).Should().BeFalse();
        throttle.TryAcquire("StoragePage.OnPageLoaded", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("OnPageLoaded", "/src/Meridian.Wpf/Views/BackfillPage.xaml.cs", "BackfillPage.OnPageLoaded")]
    [InlineData("OnTick", @"C:\src\Meridian.Wpf\ViewModels\BackfillViewModel.cs", "BackfillViewModel.OnTick")]
    [InlineData("OnTick", "", "OnTick")]
    public void DescribeHandler_QualifiesTheHandlerWithItsSourceFile(string handler, string callerFile, string expected)
    {
        AsyncHandlerGuard.DescribeHandler(handler, callerFile).Should().Be(expected);
    }

    [Fact]
    public void FormatNotification_MentionsHeldBackRepeatsOnlyWhenThereWereSome()
    {
        AsyncHandlerGuard.FormatNotification("Connection refused", 0).Should().Be("Connection refused");
        AsyncHandlerGuard.FormatNotification("Connection refused", 11)
            .Should().StartWith("Connection refused").And.Contain("11 more");
    }
}
