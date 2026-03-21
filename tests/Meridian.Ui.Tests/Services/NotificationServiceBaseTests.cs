using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Concrete test implementation of NotificationServiceBase.
/// </summary>
internal sealed class TestNotificationService : NotificationServiceBase
{
}

public sealed class NotificationServiceBaseTests
{
    private readonly TestNotificationService _sut = new();

    [Fact]
    public void ShowNotification_AddsToHistory()
    {
        _sut.ShowNotification("Test Title", "Test Message", NotificationType.Info);

        var history = _sut.GetHistory();
        history.Should().HaveCount(1);
        history[0].Title.Should().Be("Test Title");
        history[0].Message.Should().Be("Test Message");
        history[0].Type.Should().Be(NotificationType.Info);
    }

    [Fact]
    public void ShowNotification_RaisesEvent()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        _sut.ShowNotification("Title", "Message", NotificationType.Warning, 3000);

        received.Should().NotBeNull();
        received!.Title.Should().Be("Title");
        received.Type.Should().Be(NotificationType.Warning);
        received.DurationMs.Should().Be(3000);
    }

    [Fact]
    public void ShowNotification_SuppressesDuplicatesWithinWindow()
    {
        _sut.ShowNotification("Dup", "Msg", NotificationType.Info);
        _sut.ShowNotification("Dup", "Msg", NotificationType.Info);
        _sut.ShowNotification("Dup", "Msg", NotificationType.Info);

        var history = _sut.GetHistory();
        history.Should().HaveCount(1, "duplicates within deduplication window should be suppressed");
    }

    [Fact]
    public void ShowNotification_DoesNotSuppressDifferentTitles()
    {
        _sut.ShowNotification("Title A", "Msg", NotificationType.Info);
        _sut.ShowNotification("Title B", "Msg", NotificationType.Info);

        _sut.GetHistory().Should().HaveCount(2);
    }

    [Fact]
    public void ShowNotification_RespectsDisabledSetting()
    {
        _sut.UpdateSettings(new NotificationSettings { Enabled = false });

        _sut.ShowNotification("Title", "Message");

        _sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void NotifySuccess_SetsCorrectType()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        _sut.NotifySuccess("Done", "All complete");

        received.Should().NotBeNull();
        received!.Type.Should().Be(NotificationType.Success);
    }

    [Fact]
    public void NotifyWarning_RespectsNotifyErrorsSetting()
    {
        _sut.UpdateSettings(new NotificationSettings { Enabled = true, NotifyErrors = false });

        _sut.NotifyWarning("Warn", "Something bad");

        _sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void NotifyInfo_CreatesInfoNotification()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        _sut.NotifyInfo("Info Title", "Info Body");

        received.Should().NotBeNull();
        received!.Type.Should().Be(NotificationType.Info);
        received.DurationMs.Should().Be(4000);
    }

    [Fact]
    public async Task NotifyErrorAsync_IncludesExceptionMessage()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        await _sut.NotifyErrorAsync("Error", "Something failed", new InvalidOperationException("inner detail"));

        received.Should().NotBeNull();
        received!.Message.Should().Contain("inner detail");
        received.Type.Should().Be(NotificationType.Error);
    }

    [Fact]
    public async Task NotifyErrorAsync_RespectsDisabledSetting()
    {
        _sut.UpdateSettings(new NotificationSettings { Enabled = true, NotifyErrors = false });

        await _sut.NotifyErrorAsync("Error", "fail");

        _sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyConnectionStatusAsync_Connected()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        await _sut.NotifyConnectionStatusAsync(true, "Alpaca");

        received.Should().NotBeNull();
        received!.Title.Should().Be("Connected");
        received.Message.Should().Contain("Alpaca");
        received.Type.Should().Be(NotificationType.Success);
    }

    [Fact]
    public async Task NotifyConnectionStatusAsync_Disconnected()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        await _sut.NotifyConnectionStatusAsync(false, "Polygon", "timeout");

        received.Should().NotBeNull();
        received!.Title.Should().Be("Connection Lost");
        received.Message.Should().Contain("Polygon");
        received.Message.Should().Contain("timeout");
    }

    [Fact]
    public async Task NotifyBackfillCompleteAsync_Success()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        await _sut.NotifyBackfillCompleteAsync(true, 3, 1500, TimeSpan.FromMinutes(2));

        received.Should().NotBeNull();
        received!.Title.Should().Be("Backfill Complete");
        received.Message.Should().Contain("1,500");
        received.Message.Should().Contain("3");
        received.Type.Should().Be(NotificationType.Success);
    }

    [Fact]
    public async Task NotifyBackfillCompleteAsync_Failure()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        await _sut.NotifyBackfillCompleteAsync(false, 0, 0, TimeSpan.FromSeconds(5));

        received.Should().NotBeNull();
        received!.Title.Should().Be("Backfill Failed");
        received.Type.Should().Be(NotificationType.Error);
    }

    [Fact]
    public async Task NotifyDataGapAsync_ContainsSymbolAndDateRange()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        await _sut.NotifyDataGapAsync("SPY", new DateTime(2025, 1, 1), new DateTime(2025, 1, 5), 4);

        received.Should().NotBeNull();
        received!.Message.Should().Contain("SPY");
        received.Message.Should().Contain("4");
    }

    [Fact]
    public async Task NotifyDataGapAsync_RespectsDataGapSetting()
    {
        _sut.UpdateSettings(new NotificationSettings { Enabled = true, NotifyDataGaps = false });

        await _sut.NotifyDataGapAsync("AAPL", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow, 10);

        _sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void UpdateSettings_ThrowsOnNull()
    {
        var act = () => _sut.UpdateSettings(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetSettings_ReturnsCurrentSettings()
    {
        var settings = new NotificationSettings { Enabled = false, SoundType = "Custom" };
        _sut.UpdateSettings(settings);

        var result = _sut.GetSettings();

        result.Enabled.Should().BeFalse();
        result.SoundType.Should().Be("Custom");
    }

    [Fact]
    public void ClearHistory_RemovesAllItems()
    {
        _sut.ShowNotification("A", "1");
        _sut.GetHistory().Should().NotBeEmpty();

        _sut.ClearHistory();

        _sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void MarkAsRead_SetsIsRead()
    {
        _sut.ShowNotification("A", "1");

        _sut.MarkAsRead(0);

        _sut.GetHistory()[0].IsRead.Should().BeTrue();
    }

    [Fact]
    public void GetUnreadCount_ReturnsCorrectCount()
    {
        _sut.ShowNotification("A", "1", NotificationType.Info);
        // Use different title/type combos to avoid dedup
        _sut.ShowNotification("B", "2", NotificationType.Warning);
        _sut.ShowNotification("C", "3", NotificationType.Error);

        _sut.GetUnreadCount().Should().Be(3);

        _sut.MarkAsRead(0);

        _sut.GetUnreadCount().Should().Be(2);
    }

    [Fact]
    public void SendTestNotification_CreatesInfoNotification()
    {
        NotificationEventArgs? received = null;
        _sut.NotificationReceived += (_, e) => received = e;

        _sut.SendTestNotification();

        received.Should().NotBeNull();
        received!.Title.Should().Be("Test Notification");
    }
}
