using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// The Settings page Notifications card edits the live desktop notification settings. These tests
/// change the shared notification service, so they run beside the other tests that use it one at a time.
/// </summary>
[Collection("NotificationCenterSerialCollection")]
public sealed class SettingsViewModelNotificationTests
{
    private static SettingsViewModel CreateViewModel() => new(
        ConfigService.Instance,
        NotificationService.Instance,
        StatusService.Instance);

    private static void WithDefaultNotifications(Action<SettingsViewModel, NotificationService> test)
    {
        var service = NotificationService.Instance;
        var original = service.GetSettings();
        service.UpdateSettings(new NotificationSettings());
        try
        {
            WpfTestThread.Run(() =>
            {
                var viewModel = CreateViewModel();
                viewModel.LoadNotificationPreferences();
                test(viewModel, service);
            });
        }
        finally
        {
            service.UpdateSettings(original);
        }
    }

    [Fact]
    public void Toggles_ApplyToTheLiveSettingsAndRaiseSettingsChanged()
    {
        WithDefaultNotifications((viewModel, service) =>
        {
            var changes = 0;
            void OnChanged(object? sender, EventArgs e) => changes++;
            service.SettingsChanged += OnChanged;
            try
            {
                viewModel.NotifyBackfillComplete = false;
                viewModel.NotifyDataGaps = false;
                viewModel.IsNotificationsEnabled = false;

                var settings = service.GetSettings();
                settings.NotifyBackfillComplete.Should().BeFalse();
                settings.NotifyDataGaps.Should().BeFalse();
                settings.Enabled.Should().BeFalse();
                settings.NotifyErrors.Should().BeTrue();
                changes.Should().Be(3, "each change replaces the settings so the host sync saves it");
                viewModel.HasUnsavedChanges.Should().BeFalse("notification choices are not part of the configuration file");
            }
            finally
            {
                service.SettingsChanged -= OnChanged;
            }
        });
    }

    [Fact]
    public void QuietHours_ApplyOnlyAValidWindow()
    {
        WithDefaultNotifications((viewModel, service) =>
        {
            viewModel.IsQuietHoursEnabled = true;
            viewModel.QuietHoursStart = "23:30";
            service.GetSettings().QuietHoursStart.Should().Be(new TimeSpan(23, 30, 0));
            viewModel.QuietHoursError.Should().BeEmpty();

            viewModel.QuietHoursEnd = "6am";
            viewModel.QuietHoursError.Should().Contain("HH:mm");
            service.GetSettings().QuietHoursEnd.Should().Be(new TimeSpan(7, 0, 0), "an unreadable time is not applied");

            viewModel.QuietHoursEnd = "06:15";
            viewModel.QuietHoursError.Should().BeEmpty();
            service.GetSettings().QuietHoursEnabled.Should().BeTrue();
            service.GetSettings().QuietHoursEnd.Should().Be(new TimeSpan(6, 15, 0));
            viewModel.QuietHoursZoneText.Should().Contain("UTC");
        });
    }

    [Fact]
    public void LoadNotificationPreferences_ShowsTheLiveSettingsWithoutChangingThem()
    {
        WithDefaultNotifications((viewModel, service) =>
        {
            service.UpdateSettings(new NotificationSettings
            {
                NotifyStorageWarnings = false,
                QuietHoursEnabled = true,
                QuietHoursStart = new TimeSpan(21, 0, 0)
            });
            var applied = service.GetSettings();

            viewModel.LoadNotificationPreferences();

            viewModel.NotifyStorageWarnings.Should().BeFalse();
            viewModel.IsQuietHoursEnabled.Should().BeTrue();
            viewModel.QuietHoursStart.Should().Be("21:00");
            service.GetSettings().Should().BeSameAs(applied, "loading the card is not a change");
        });
    }

    [Fact]
    public void TestNotification_ExplainsWhenPopUpsAreOff()
    {
        WithDefaultNotifications((viewModel, _) =>
        {
            viewModel.IsNotificationsEnabled = false;
            viewModel.SendTestNotificationCommand.Execute(null);

            viewModel.TestNotificationStatus.Should().Contain("off");
        });
    }
}
