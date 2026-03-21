using System.Net.Http;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="InfoBarService"/> — notification display,
/// severity-based duration, error details creation, and event raising.
/// </summary>
public sealed class InfoBarServiceTests
{
    private static InfoBarService Svc => InfoBarService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = InfoBarService.Instance;
        var b = InfoBarService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── ShowAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_ShouldRaiseNotificationRequestedEvent()
    {
        var svc = Svc;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        InfoBarNotificationEventArgs? received = null;
        svc.NotificationRequested += (_, args) => received = args;

        try
        {
            await svc.ShowAsync(InfoBarSeverity.Informational, "Test", "message", cts.Token);
        }
        catch (OperationCanceledException) { }

        received.Should().NotBeNull();
        received!.Severity.Should().Be(InfoBarSeverity.Informational);
        received.Title.Should().Be("Test");
        received.Message.Should().Be("message");
        received.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task ShowAsync_WithCustomDuration_ShouldUseProvidedDuration()
    {
        var svc = Svc;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        InfoBarNotificationEventArgs? received = null;
        svc.NotificationRequested += (_, args) =>
        {
            if (args.IsOpen)
                received = args;
        };

        try
        {
            await svc.ShowAsync(InfoBarSeverity.Warning, "Custom", "msg", 2000, cts.Token);
        }
        catch (OperationCanceledException) { }

        received.Should().NotBeNull();
        received!.DurationMs.Should().Be(2000);
    }

    [Fact]
    public async Task ShowAsync_ShouldHandleCancellationGracefully()
    {
        var svc = Svc;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should not throw — cancellation is caught internally
        Func<Task> act = () => svc.ShowAsync(
            InfoBarSeverity.Error, "Cancel", "test", cts.Token);

        await act.Should().NotThrowAsync();
    }

    // ── ShowErrorAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ShowErrorAsync_ShouldIncludeContextAndRemedy()
    {
        var svc = Svc;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        InfoBarNotificationEventArgs? received = null;
        svc.NotificationRequested += (_, args) =>
        {
            if (args.IsOpen)
                received = args;
        };

        try
        {
            await svc.ShowErrorAsync("Error Title", "Base message", "extra context", "try this", cts.Token);
        }
        catch (OperationCanceledException) { }

        received.Should().NotBeNull();
        received!.Severity.Should().Be(InfoBarSeverity.Error);
        received.Message.Should().Contain("Base message");
        received.Message.Should().Contain("Context: extra context");
        received.Message.Should().Contain("Suggestion: try this");
    }

    [Fact]
    public async Task ShowErrorAsync_NullContextAndRemedy_ShouldOnlyShowMessage()
    {
        var svc = Svc;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        InfoBarNotificationEventArgs? received = null;
        svc.NotificationRequested += (_, args) =>
        {
            if (args.IsOpen)
                received = args;
        };

        try
        {
            await svc.ShowErrorAsync("Title", "Plain message", null, null, cts.Token);
        }
        catch (OperationCanceledException) { }

        received.Should().NotBeNull();
        received!.Message.Should().Be("Plain message");
        received.Message.Should().NotContain("Context:");
        received.Message.Should().NotContain("Suggestion:");
    }

    // ── GetDurationForSeverity ───────────────────────────────────────

    [Theory]
    [InlineData(InfoBarSeverity.Informational, 4000)]
    [InlineData(InfoBarSeverity.Success, 3000)]
    [InlineData(InfoBarSeverity.Warning, 6000)]
    [InlineData(InfoBarSeverity.Error, 10000)]
    public void GetDurationForSeverity_ShouldReturnExpectedDuration(
        InfoBarSeverity severity, int expected)
    {
        InfoBarService.GetDurationForSeverity(severity).Should().Be(expected);
    }

    // ── Durations constants ──────────────────────────────────────────

    [Fact]
    public void Durations_ShouldMatchInfoBarConstants()
    {
        InfoBarService.Durations.Info.Should().Be(4000);
        InfoBarService.Durations.Success.Should().Be(3000);
        InfoBarService.Durations.Warning.Should().Be(6000);
        InfoBarService.Durations.Error.Should().Be(10000);
        InfoBarService.Durations.Critical.Should().Be(0); // no auto-dismiss
    }

    // ── CreateErrorDetails ───────────────────────────────────────────

    [Fact]
    public void CreateErrorDetails_TimeoutException_ShouldMapCorrectly()
    {
        var details = InfoBarService.CreateErrorDetails(
            new TimeoutException("timed out"), "loading data");

        details.Title.Should().Contain("Timeout");
        details.Severity.Should().Be(InfoBarSeverity.Error);
        details.Message.Should().Contain("loading data");
        details.Remedy.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateErrorDetails_OperationCancelled_ShouldMapToWarning()
    {
        var details = InfoBarService.CreateErrorDetails(
            new OperationCanceledException(), "fetching status");

        details.Title.Should().Contain("Cancelled");
        details.Severity.Should().Be(InfoBarSeverity.Warning);
    }

    [Fact]
    public void CreateErrorDetails_HttpRequestException_ShouldMapToConnectionError()
    {
        var details = InfoBarService.CreateErrorDetails(
            new HttpRequestException("connection refused"), "connecting");

        details.Title.Should().Contain("Connection");
        details.Severity.Should().Be(InfoBarSeverity.Error);
    }

    [Fact]
    public void CreateErrorDetails_UnauthorizedAccess_ShouldMapToAccessDenied()
    {
        var details = InfoBarService.CreateErrorDetails(
            new UnauthorizedAccessException("no access"), "saving config");

        details.Title.Should().Contain("Access");
        details.Severity.Should().Be(InfoBarSeverity.Error);
    }

    [Fact]
    public void CreateErrorDetails_GenericException_ShouldMapToUnexpected()
    {
        var details = InfoBarService.CreateErrorDetails(
            new Exception("unknown"), "doing something");

        details.Title.Should().Contain("Unexpected");
        details.Severity.Should().Be(InfoBarSeverity.Error);
    }

    [Fact]
    public void CreateErrorDetails_ArgumentException_ShouldMapToInvalidInput()
    {
        var details = InfoBarService.CreateErrorDetails(
            new ArgumentException("bad arg"), "validating");

        details.Title.Should().Contain("Invalid");
        details.Severity.Should().Be(InfoBarSeverity.Warning);
    }

    // ── ErrorDetails model ───────────────────────────────────────────

    [Fact]
    public void ErrorDetails_ShouldHaveDefaults()
    {
        var details = new ErrorDetails();
        details.Title.Should().Be("Error");
        details.Message.Should().BeEmpty();
        details.Context.Should().BeNull();
        details.Remedy.Should().BeNull();
        details.Severity.Should().Be(InfoBarSeverity.Error);
    }

    [Fact]
    public void ErrorDetails_GetFormattedMessage_ShouldIncludeAllParts()
    {
        var details = new ErrorDetails
        {
            Message = "Something failed",
            Context = "During export",
            Remedy = "Try again later"
        };

        var formatted = details.GetFormattedMessage();
        formatted.Should().Contain("Something failed");
        formatted.Should().Contain("Details: During export");
        formatted.Should().Contain("Suggestion: Try again later");
    }

    [Fact]
    public void ErrorDetails_GetFormattedMessage_WithoutContext_ShouldOnlyShowMessage()
    {
        var details = new ErrorDetails
        {
            Message = "Simple error"
        };

        details.GetFormattedMessage().Should().Be("Simple error");
    }

    // ── InfoBarNotificationEventArgs model ───────────────────────────

    [Fact]
    public void InfoBarNotificationEventArgs_ShouldHaveDefaults()
    {
        var args = new InfoBarNotificationEventArgs();
        args.Title.Should().BeEmpty();
        args.Message.Should().BeEmpty();
        args.DurationMs.Should().Be(0);
        args.IsOpen.Should().BeFalse();
    }

    // ── InfoBarSeverity enum ─────────────────────────────────────────

    [Theory]
    [InlineData(InfoBarSeverity.Informational)]
    [InlineData(InfoBarSeverity.Success)]
    [InlineData(InfoBarSeverity.Warning)]
    [InlineData(InfoBarSeverity.Error)]
    public void InfoBarSeverity_AllValues_ShouldBeDefined(InfoBarSeverity severity)
    {
        Enum.IsDefined(typeof(InfoBarSeverity), severity).Should().BeTrue();
    }
}
