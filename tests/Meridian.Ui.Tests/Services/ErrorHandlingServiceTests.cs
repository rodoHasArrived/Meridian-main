using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ErrorHandlingService"/> — exception classification,
/// error recording, notification flow, and event raising.
/// </summary>
public sealed class ErrorHandlingServiceTests
{
    private static ErrorHandlingService Svc => ErrorHandlingService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = ErrorHandlingService.Instance;
        var b = ErrorHandlingService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── HandleExceptionAsync ─────────────────────────────────────────

    [Fact]
    public async Task HandleExceptionAsync_ShouldAddToRecentErrors()
    {
        var svc = Svc;
        var initialCount = svc.RecentErrors.Count;

        await svc.HandleExceptionAsync(
            new InvalidOperationException("test"),
            "unit-test-context",
            ErrorHandlingOptions.Silent);

        svc.RecentErrors.Count.Should().BeGreaterThan(initialCount);
    }

    [Fact]
    public async Task HandleExceptionAsync_ShouldRaiseErrorHandledEvent()
    {
        var svc = Svc;
        ErrorHandledEventArgs? received = null;
        svc.ErrorHandled += (_, args) => received = args;

        await svc.HandleExceptionAsync(
            new InvalidOperationException("event-test"),
            "event-context",
            ErrorHandlingOptions.Silent);

        received.Should().NotBeNull();
        received!.Record.Should().NotBeNull();
        received.Record.Context.Should().Be("event-context");
    }

    [Theory]
    [InlineData(typeof(TimeoutException), ErrorSeverity.Warning)]
    [InlineData(typeof(HttpRequestException), ErrorSeverity.Warning)]
    [InlineData(typeof(UnauthorizedAccessException), ErrorSeverity.Error)]
    [InlineData(typeof(InvalidOperationException), ErrorSeverity.Error)]
    [InlineData(typeof(ArgumentException), ErrorSeverity.Error)]
    public async Task HandleExceptionAsync_ShouldClassifyExceptionCorrectly(
        Type exceptionType, ErrorSeverity expectedSeverity)
    {
        var svc = Svc;
        var ex = (Exception)Activator.CreateInstance(exceptionType, "classify-test")!;

        ErrorHandledEventArgs? received = null;
        svc.ErrorHandled += (_, args) => received = args;

        await svc.HandleExceptionAsync(ex, "classify-context", ErrorHandlingOptions.Silent);

        received.Should().NotBeNull();
        received!.Record.Severity.Should().Be(expectedSeverity);
    }

    // ── HandleErrorAsync ─────────────────────────────────────────────

    [Fact]
    public async Task HandleErrorAsync_ShouldRecordErrorMessage()
    {
        var svc = Svc;

        ErrorHandledEventArgs? received = null;
        svc.ErrorHandled += (_, args) => received = args;

        await svc.HandleErrorAsync(
            "Something went wrong",
            "message-context",
            ErrorHandlingOptions.Silent);

        received.Should().NotBeNull();
        received!.Record.ErrorMessage.Should().Be("Something went wrong");
        received.Record.Context.Should().Be("message-context");
    }

    [Fact]
    public async Task HandleErrorAsync_WithCustomSeverity_ShouldUseThatSeverity()
    {
        var svc = Svc;

        ErrorHandledEventArgs? received = null;
        svc.ErrorHandled += (_, args) => received = args;

        await svc.HandleErrorAsync(
            "custom severity test",
            "sev-context",
            new ErrorHandlingOptions { NotifyUser = false, Severity = ErrorSeverity.Critical });

        received.Should().NotBeNull();
        received!.Record.Severity.Should().Be(ErrorSeverity.Critical);
    }

    // ── ExecuteWithErrorHandlingAsync ────────────────────────────────

    [Fact]
    public async Task ExecuteWithErrorHandling_OnSuccess_ShouldReturnResult()
    {
        var svc = Svc;

        var result = await svc.ExecuteWithErrorHandlingAsync(
            _ => Task.FromResult(42),
            "success-context",
            defaultValue: -1,
            options: ErrorHandlingOptions.Silent);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteWithErrorHandling_OnException_ShouldReturnDefault()
    {
        var svc = Svc;

        var result = await svc.ExecuteWithErrorHandlingAsync<int>(
            _ => throw new InvalidOperationException("fail"),
            "fail-context",
            defaultValue: -1,
            options: ErrorHandlingOptions.Silent);

        result.Should().Be(-1);
    }

    [Fact]
    public async Task ExecuteWithErrorHandling_OnCancellation_ShouldRethrow()
    {
        var svc = Svc;

        Func<Task> act = () => svc.ExecuteWithErrorHandlingAsync<int>(
            _ => throw new OperationCanceledException(),
            "cancel-context",
            options: ErrorHandlingOptions.Silent);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteWithErrorHandling_VoidOverload_OnException_ShouldNotThrow()
    {
        var svc = Svc;

        Func<Task> act = () => svc.ExecuteWithErrorHandlingAsync(
            _ => throw new InvalidOperationException("void-fail"),
            "void-context",
            options: ErrorHandlingOptions.Silent);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteWithErrorHandling_VoidOverload_OnCancellation_ShouldRethrow()
    {
        var svc = Svc;

        Func<Task> act = () => svc.ExecuteWithErrorHandlingAsync(
            _ => throw new OperationCanceledException(),
            "void-cancel",
            options: ErrorHandlingOptions.Silent);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── ClearErrors ──────────────────────────────────────────────────

    [Fact]
    public async Task ClearErrors_ShouldEmptyRecentErrors()
    {
        var svc = Svc;

        await svc.HandleExceptionAsync(
            new Exception("clear-test"),
            "clear-context",
            ErrorHandlingOptions.Silent);

        svc.RecentErrors.Count.Should().BeGreaterThan(0);

        svc.ClearErrors();

        svc.RecentErrors.Count.Should().Be(0);
    }

    // ── Notification options ─────────────────────────────────────────

    [Fact]
    public async Task HandleExceptionAsync_WithSilentOption_ShouldNotNotifyUser()
    {
        var svc = Svc;
        ErrorHandledEventArgs? received = null;
        svc.ErrorHandled += (_, args) => received = args;

        await svc.HandleExceptionAsync(
            new Exception("silent-test"),
            "silent-context",
            ErrorHandlingOptions.Silent);

        received.Should().NotBeNull();
        received!.WasNotified.Should().BeFalse();
    }

    // ── ErrorHandlingOptions static presets ───────────────────────────

    [Fact]
    public void ErrorHandlingOptions_Default_ShouldNotifyUser()
    {
        ErrorHandlingOptions.Default.NotifyUser.Should().BeTrue();
        ErrorHandlingOptions.Default.IncludeContext.Should().BeFalse();
    }

    [Fact]
    public void ErrorHandlingOptions_Silent_ShouldNotNotifyUser()
    {
        ErrorHandlingOptions.Silent.NotifyUser.Should().BeFalse();
    }

    [Fact]
    public void ErrorHandlingOptions_Verbose_ShouldIncludeContext()
    {
        ErrorHandlingOptions.Verbose.NotifyUser.Should().BeTrue();
        ErrorHandlingOptions.Verbose.IncludeContext.Should().BeTrue();
    }

    // ── ErrorRecord model ────────────────────────────────────────────

    [Fact]
    public void ErrorRecord_DisplayMessage_ShouldPreferExceptionMessage()
    {
        var record = new ErrorRecord
        {
            Exception = new InvalidOperationException("ex message"),
            ErrorMessage = "fallback"
        };

        record.DisplayMessage.Should().Be("ex message");
    }

    [Fact]
    public void ErrorRecord_DisplayMessage_ShouldFallToErrorMessage()
    {
        var record = new ErrorRecord
        {
            ErrorMessage = "fallback message"
        };

        record.DisplayMessage.Should().Be("fallback message");
    }

    [Fact]
    public void ErrorRecord_DisplayMessage_NullBoth_ShouldReturnUnknown()
    {
        var record = new ErrorRecord();
        record.DisplayMessage.Should().Be("Unknown error");
    }

    // ── ErrorSeverity enum ───────────────────────────────────────────

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Error)]
    [InlineData(ErrorSeverity.Critical)]
    public void ErrorSeverity_AllValues_ShouldBeDefined(ErrorSeverity severity)
    {
        Enum.IsDefined(typeof(ErrorSeverity), severity).Should().BeTrue();
    }
}
