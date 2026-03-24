using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="NotificationService"/> — singleton lifecycle,
/// default no-op behavior, notification type enum, and method contracts.
/// </summary>
public sealed class NotificationServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        NotificationService.Instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = NotificationService.Instance;
        var b = NotificationService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public async Task Instance_ThreadSafety_ShouldReturnSameInstance()
    {
        NotificationService? i1 = null, i2 = null;
        var t1 = Task.Run(() => i1 = NotificationService.Instance);
        var t2 = Task.Run(() => i2 = NotificationService.Instance);
        await Task.WhenAll(t1, t2);

        i1.Should().NotBeNull();
        i1.Should().BeSameAs(i2);
    }

    // ── NotifyErrorAsync ─────────────────────────────────────────────

    [Fact]
    public async Task NotifyErrorAsync_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyErrorAsync("Error Title", "Error message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyErrorAsync_WithException_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var ex = new InvalidOperationException("test");
        var act = async () => await svc.NotifyErrorAsync("Error", "msg", ex);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyErrorAsync_WithNullException_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyErrorAsync("Error", "msg", null);
        await act.Should().NotThrowAsync();
    }

    // ── NotifyWarningAsync ───────────────────────────────────────────

    [Fact]
    public async Task NotifyWarningAsync_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyWarningAsync("Warning Title", "Warning message");
        await act.Should().NotThrowAsync();
    }

    // ── NotifyAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task NotifyAsync_WithDefaultType_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyAsync("Title", "Message");
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(NotificationType.Info)]
    [InlineData(NotificationType.Success)]
    [InlineData(NotificationType.Warning)]
    [InlineData(NotificationType.Error)]
    public async Task NotifyAsync_AllTypes_ShouldCompleteWithoutException(NotificationType type)
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyAsync("Title", "Message", type);
        await act.Should().NotThrowAsync();
    }

    // ── NotifyBackfillCompleteAsync ──────────────────────────────────

    [Fact]
    public async Task NotifyBackfillCompleteAsync_Success_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyBackfillCompleteAsync(
            success: true, symbolCount: 5, barsWritten: 1250, duration: TimeSpan.FromMinutes(3));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyBackfillCompleteAsync_Failure_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyBackfillCompleteAsync(
            success: false, symbolCount: 0, barsWritten: 0, duration: TimeSpan.Zero);
        await act.Should().NotThrowAsync();
    }

    // ── NotifyScheduledJobAsync ──────────────────────────────────────

    [Fact]
    public async Task NotifyScheduledJobAsync_Started_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyScheduledJobAsync("DailyBackfill", started: true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyScheduledJobAsync_CompletedSuccess_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyScheduledJobAsync("DailyBackfill", started: false, success: true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyScheduledJobAsync_CompletedFailure_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyScheduledJobAsync("DailyBackfill", started: false, success: false);
        await act.Should().NotThrowAsync();
    }

    // ── NotifyStorageWarningAsync ────────────────────────────────────

    [Fact]
    public async Task NotifyStorageWarningAsync_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyStorageWarningAsync(92.5, 500_000_000);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyStorageWarningAsync_ZeroValues_ShouldCompleteWithoutException()
    {
        var svc = NotificationService.Instance;
        var act = async () => await svc.NotifyStorageWarningAsync(0, 0);
        await act.Should().NotThrowAsync();
    }

    // ── NotificationType enum ────────────────────────────────────────

    [Theory]
    [InlineData(NotificationType.Info)]
    [InlineData(NotificationType.Success)]
    [InlineData(NotificationType.Warning)]
    [InlineData(NotificationType.Error)]
    public void NotificationType_AllValues_ShouldBeDefined(NotificationType type)
    {
        Enum.IsDefined(typeof(NotificationType), type).Should().BeTrue();
    }

    [Fact]
    public void NotificationType_ShouldHaveFourValues()
    {
        Enum.GetValues<NotificationType>().Should().HaveCount(4);
    }
}
