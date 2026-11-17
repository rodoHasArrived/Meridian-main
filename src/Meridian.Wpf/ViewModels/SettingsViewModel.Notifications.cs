using System;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// The Settings page Notifications card. Each control edits the live desktop notification settings
/// (<see cref="NotificationServiceBase.UpdateSettings"/>), which take effect at once and, through the
/// host preference sync, are saved with the operator's preferences so the browser card shows the
/// same choices. Quiet hours are read in the operator's display time zone.
/// </summary>
public sealed partial class SettingsViewModel
{
    private bool _isLoadingNotificationPreferences;
    private bool _notifyConnectionStatus = true;
    private bool _notifyErrors = true;
    private bool _notifyBackfillComplete = true;
    private bool _notifyDataGaps = true;
    private bool _notifyStorageWarnings = true;
    private bool _isQuietHoursEnabled;
    private string _quietHoursStart = QuietHours.FormatClock(QuietHours.DefaultStart);
    private string _quietHoursEnd = QuietHours.FormatClock(QuietHours.DefaultEnd);
    private string _quietHoursError = string.Empty;
    private string _testNotificationStatus = string.Empty;

    public bool NotifyConnectionStatus
    {
        get => _notifyConnectionStatus;
        set => _ = SetNotificationChoice(ref _notifyConnectionStatus, value, (settings, on) => settings.NotifyConnectionStatus = on);
    }

    public bool NotifyErrors
    {
        get => _notifyErrors;
        set => _ = SetNotificationChoice(ref _notifyErrors, value, (settings, on) => settings.NotifyErrors = on);
    }

    public bool NotifyBackfillComplete
    {
        get => _notifyBackfillComplete;
        set => _ = SetNotificationChoice(ref _notifyBackfillComplete, value, (settings, on) => settings.NotifyBackfillComplete = on);
    }

    public bool NotifyDataGaps
    {
        get => _notifyDataGaps;
        set => _ = SetNotificationChoice(ref _notifyDataGaps, value, (settings, on) => settings.NotifyDataGaps = on);
    }

    public bool NotifyStorageWarnings
    {
        get => _notifyStorageWarnings;
        set => _ = SetNotificationChoice(ref _notifyStorageWarnings, value, (settings, on) => settings.NotifyStorageWarnings = on);
    }

    public bool IsQuietHoursEnabled
    {
        get => _isQuietHoursEnabled;
        set => _ = SetNotificationChoice(ref _isQuietHoursEnabled, value, (settings, on) => settings.QuietHoursEnabled = on);
    }

    /// <summary>Start of quiet hours, <c>HH:mm</c> in the display time zone. Applied once it reads as a valid time.</summary>
    public string QuietHoursStart
    {
        get => _quietHoursStart;
        set
        {
            if (SetProperty(ref _quietHoursStart, value ?? string.Empty))
            {
                ApplyQuietHoursWindow();
            }
        }
    }

    /// <summary>End of quiet hours (exclusive), <c>HH:mm</c> in the display time zone.</summary>
    public string QuietHoursEnd
    {
        get => _quietHoursEnd;
        set
        {
            if (SetProperty(ref _quietHoursEnd, value ?? string.Empty))
            {
                ApplyQuietHoursWindow();
            }
        }
    }

    /// <summary>Why the typed quiet-hours window was not applied, or empty.</summary>
    public string QuietHoursError
    {
        get => _quietHoursError;
        private set => SetProperty(ref _quietHoursError, value);
    }

    /// <summary>Names the zone quiet hours are read in, for example "Times are in Europe/London."</summary>
    public string QuietHoursZoneText => $"Times are in {DisplayZoneName()}, your display time zone.";

    /// <summary>What the last test notification did, for example that quiet hours held it.</summary>
    public string TestNotificationStatus
    {
        get => _testNotificationStatus;
        private set => SetProperty(ref _testNotificationStatus, value);
    }

    /// <summary>Reads the live notification settings into the card. Called when the page loads.</summary>
    public void LoadNotificationPreferences()
    {
        var settings = _notificationService.GetSettings();
        _isLoadingNotificationPreferences = true;
        try
        {
            IsNotificationsEnabled = settings.Enabled;
            NotifyConnectionStatus = settings.NotifyConnectionStatus;
            NotifyErrors = settings.NotifyErrors;
            NotifyBackfillComplete = settings.NotifyBackfillComplete;
            NotifyDataGaps = settings.NotifyDataGaps;
            NotifyStorageWarnings = settings.NotifyStorageWarnings;
            IsQuietHoursEnabled = settings.QuietHoursEnabled;
            QuietHoursStart = QuietHours.FormatClock(settings.QuietHoursStart);
            QuietHoursEnd = QuietHours.FormatClock(settings.QuietHoursEnd);
            QuietHoursError = string.Empty;
            TestNotificationStatus = string.Empty;
            RaisePropertyChanged(nameof(QuietHoursZoneText));
        }
        finally
        {
            _isLoadingNotificationPreferences = false;
        }
    }

    // Returns true when the value changed.
    private bool SetNotificationChoice(ref bool field, bool value, Action<NotificationSettings, bool> apply)
    {
        if (!SetProperty(ref field, value))
        {
            return false;
        }

        if (!_isLoadingNotificationPreferences)
        {
            UpdateNotificationSettings(settings => apply(settings, value));
        }

        return true;
    }

    private void ApplyQuietHoursWindow()
    {
        if (_isLoadingNotificationPreferences)
        {
            return;
        }

        if (!QuietHours.TryParseClock(_quietHoursStart, out var start) || !QuietHours.TryParseClock(_quietHoursEnd, out var end))
        {
            QuietHoursError = "Enter both times as 24-hour HH:mm, such as 22:00 and 07:00.";
            return;
        }

        QuietHoursError = start == end
            ? "Start and end are the same, so quiet hours never hold anything."
            : string.Empty;
        UpdateNotificationSettings(settings =>
        {
            settings.QuietHoursStart = start;
            settings.QuietHoursEnd = end;
        });
    }

    // Settings are replaced, never edited in place, so the service raises SettingsChanged and the
    // host preference sync saves the new set.
    private void UpdateNotificationSettings(Action<NotificationSettings> change)
    {
        var next = _notificationService.GetSettings().Copy();
        change(next);
        _notificationService.UpdateSettings(next);
    }

    private void SendTestNotification()
    {
        var settings = _notificationService.GetSettings();
        if (!settings.Enabled)
        {
            TestNotificationStatus = "Pop-up notifications are off, so the test was not shown.";
            return;
        }

        if (_notificationService.IsQuietHoursNow)
        {
            TestNotificationStatus =
                $"Quiet hours are on until {QuietHours.FormatClock(settings.QuietHoursEnd)} ({DisplayZoneName()}), so the test was held.";
            return;
        }

        TestNotificationStatus = "Test notification sent.";
        _notificationService.ShowNotification("Test Notification", "This is a test notification from Meridian.", NotificationType.Info);
    }

    private static string DisplayZoneName()
    {
        var zone = DisplayTimeZone.Current;
        return DisplayTimeZone.IsUtc(zone) ? "UTC" : zone.Id;
    }
}
