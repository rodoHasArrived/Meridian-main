using Meridian.Wpf.Services;
using Meridian.Ui.Services.Services;
using NotificationType = Meridian.Ui.Services.NotificationType;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for NotificationService singleton service.
/// Validates notification display, history tracking, settings management, and event handling.
/// </summary>
public sealed class NotificationServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = NotificationService.Instance;
        var instance2 = NotificationService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "NotificationService should be a singleton");
    }

    [Fact]
    public void NotificationSettings_Defaults_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var settings = new NotificationSettings();

        // Assert
        settings.Enabled.Should().BeTrue();
        settings.NotifyConnectionStatus.Should().BeTrue();
        settings.NotifyErrors.Should().BeTrue();
        settings.NotifyBackfillComplete.Should().BeTrue();
        settings.NotifyDataGaps.Should().BeTrue();
        settings.NotifyStorageWarnings.Should().BeTrue();
        settings.SoundType.Should().Be("Default");
        settings.QuietHoursEnabled.Should().BeFalse();
        settings.QuietHoursStart.Should().Be(new TimeSpan(22, 0, 0));
        settings.QuietHoursEnd.Should().Be(new TimeSpan(7, 0, 0));
    }

    [Fact]
    public void NotificationHistoryItem_Defaults_ShouldHaveEmptyStrings()
    {
        // Arrange & Act
        var item = new NotificationHistoryItem();

        // Assert
        item.Title.Should().BeEmpty();
        item.Message.Should().BeEmpty();
        item.Type.Should().Be(NotificationType.Info);
        item.Tag.Should().BeEmpty();
        item.IsRead.Should().BeFalse();
    }

    [Fact]
    public void NotificationEventArgs_Properties_ShouldBeSettable()
    {
        // Arrange & Act
        var args = new NotificationEventArgs
        {
            Title = "Test Title",
            Message = "Test Message",
            Type = NotificationType.Warning,
            Tag = "warning",
            DurationMs = 3000
        };

        // Assert
        args.Title.Should().Be("Test Title");
        args.Message.Should().Be("Test Message");
        args.Type.Should().Be(NotificationType.Warning);
        args.Tag.Should().Be("warning");
        args.DurationMs.Should().Be(3000);
    }

    [Fact]
    public void UpdateSettings_WithValidSettings_ShouldUpdateSettings()
    {
        // Arrange
        var service = NotificationService.Instance;
        var newSettings = new NotificationSettings { Enabled = true, SoundType = "Subtle" };

        // Act
        service.UpdateSettings(newSettings);

        // Assert
        var retrieved = service.GetSettings();
        retrieved.SoundType.Should().Be("Subtle");
    }

    [Fact]
    public void GetSettings_ShouldReturnCurrentSettings()
    {
        // Arrange
        var service = NotificationService.Instance;
        var settings = new NotificationSettings { Enabled = true };
        service.UpdateSettings(settings);

        // Act
        var result = service.GetSettings();

        // Assert
        result.Should().NotBeNull();
        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public void GetHistory_ShouldReturnReadOnlyList()
    {
        // Arrange
        var service = NotificationService.Instance;

        // Act
        var history = service.GetHistory();

        // Assert
        history.Should().NotBeNull();
    }

    [Fact]
    public void ClearHistory_ShouldRemoveAllItems()
    {
        // Arrange
        var service = NotificationService.Instance;
        service.UpdateSettings(new NotificationSettings { Enabled = true });
        service.SendTestNotification();

        // Act
        service.ClearHistory();

        // Assert
        var history = service.GetHistory();
        history.Should().BeEmpty();
    }

    [Fact]
    public void GetUnreadCount_Initially_ShouldReturnZero()
    {
        // Arrange
        var service = NotificationService.Instance;
        service.ClearHistory();

        // Act
        var count = service.GetUnreadCount();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void MarkAsRead_AtInvalidIndex_ShouldNotThrow()
    {
        // Arrange
        var service = NotificationService.Instance;
        service.ClearHistory();

        // Act
        var act = () => service.MarkAsRead(999);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void SendTestNotification_ShouldAddToHistory()
    {
        // Arrange
        var service = NotificationService.Instance;
        service.UpdateSettings(new NotificationSettings { Enabled = true });
        service.ClearHistory();

        // Act
        service.SendTestNotification();

        // Assert
        var history = service.GetHistory();
        history.Should().NotBeEmpty();
        history[0].Title.Should().Be("Test Notification");
    }

    [Theory]
    [InlineData(NotificationType.Info)]
    [InlineData(NotificationType.Success)]
    [InlineData(NotificationType.Warning)]
    [InlineData(NotificationType.Error)]
    public void NotificationType_ShouldContainExpectedValues(NotificationType type)
    {
        // Assert
        Enum.IsDefined(typeof(NotificationType), type).Should().BeTrue();
    }

    [Fact]
    public void EventSubscription_ShouldNotThrow()
    {
        // Arrange
        var service = NotificationService.Instance;

        // Act
        var act = () => service.NotificationReceived += (sender, args) => { };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ShowNotification_WithDisabledSettings_ShouldNotAddToHistory()
    {
        // Arrange
        var service = NotificationService.Instance;
        service.UpdateSettings(new NotificationSettings { Enabled = false });
        service.ClearHistory();

        // Act
        service.ShowNotification("Disabled", "Should not appear");

        // Assert
        service.GetHistory().Should().BeEmpty();

        // Cleanup: re-enable for other tests
        service.UpdateSettings(new NotificationSettings { Enabled = true });
    }

    [Fact]
    public void MarkAsRead_AtValidIndex_ShouldSetIsRead()
    {
        // Arrange
        var service = NotificationService.Instance;
        service.UpdateSettings(new NotificationSettings { Enabled = true });
        service.ClearHistory();
        service.SendTestNotification();

        // Act
        service.MarkAsRead(0);

        // Assert
        var history = service.GetHistory();
        history[0].IsRead.Should().BeTrue();
    }
}
