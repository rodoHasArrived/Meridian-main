using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfNotificationService = Meridian.Wpf.Services.NotificationService;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class NotificationCenterViewModelTests
{
    [Fact]
    public void Filters_CombineSearchUnreadAndTypeSelection()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();
            SeedNotification(viewModel, "Order Reject", "AAPL order rejected by guardrail.", NotificationType.Error, isRead: false, historyIndex: 0, minutesAgo: 3);
            SeedNotification(viewModel, "Data Gap", "MSFT feed delayed.", NotificationType.Warning, isRead: true, historyIndex: 1, minutesAgo: 9);
            SeedNotification(viewModel, "Backfill Complete", "AAPL session repaired.", NotificationType.Success, isRead: false, historyIndex: 2, minutesAgo: 15);
            SeedNotification(viewModel, "Research Export", "Notebook snapshot published.", NotificationType.Info, isRead: false, historyIndex: 3, minutesAgo: 21);

            viewModel.ApplyCheckboxFilters(showErrors: true, showWarnings: false, showInfo: false, showSuccess: false);
            viewModel.SearchText = "AAPL";
            viewModel.ShowUnreadOnly = true;

            viewModel.FilteredNotifications.Should().ContainSingle();
            viewModel.FilteredNotifications[0].Title.Should().Be("Order Reject");
            viewModel.TotalCount.Should().Be(1);
            viewModel.HistorySummaryText.Should().Be("Showing 1 of 4 notifications");
        });
    }

    [Fact]
    public void MarkRead_UpdatesUnreadCountersAndBackingHistory()
    {
        WpfTestThread.Run(() =>
        {
            var notificationService = WpfNotificationService.Instance;
            notificationService.UpdateSettings(new NotificationSettings { Enabled = true });
            notificationService.ClearHistory();
            notificationService.ShowNotification("Order rejected", "AAPL order blocked.", NotificationType.Error);
            notificationService.ShowNotification("Backfill complete", "MSFT repair finished.", NotificationType.Success);

            var viewModel = CreateViewModel();
            viewModel.LoadNotifications();

            viewModel.UnreadCount.Should().Be(2);

            var selected = viewModel.AllNotifications[0];
            viewModel.MarkRead(selected);

            selected.IsRead.Should().BeTrue();
            viewModel.UnreadCount.Should().Be(1);
            notificationService.GetHistory()[selected.HistoryIndex].IsRead.Should().BeTrue();
            viewModel.CanMarkAllRead.Should().BeTrue();

            notificationService.ClearHistory();
        });
    }

    [Fact]
    public void EmptyState_WhenFiltersExcludeEverything_ExplainsFilteredResult()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();
            SeedNotification(viewModel, "Backfill Complete", "AAPL session repaired.", NotificationType.Success, isRead: false, historyIndex: 0, minutesAgo: 2);

            viewModel.ApplyCheckboxFilters(showErrors: true, showWarnings: false, showInfo: false, showSuccess: false);

            viewModel.NoNotificationsVisible.Should().BeTrue();
            viewModel.EmptyStateTitle.Should().Be("No notifications match the current filters");
            viewModel.EmptyStateDescription.Should().Contain("widen the filters");
            viewModel.HistorySummaryText.Should().Be("Showing 0 of 1 notification");
        });
    }

    private static NotificationCenterViewModel CreateViewModel()
        => new(WpfNotificationService.Instance, AlertService.Instance);

    private static void SeedNotification(
        NotificationCenterViewModel viewModel,
        string title,
        string message,
        NotificationType type,
        bool isRead,
        int historyIndex,
        int minutesAgo)
    {
        viewModel.AllNotifications.Add(new NotificationItem
        {
            Title = title,
            Message = message,
            Type = type.ToString(),
            NotificationType = type,
            RawTimestamp = DateTime.Now.AddMinutes(-minutesAgo),
            Timestamp = $"{minutesAgo}m ago",
            IsRead = isRead,
            HistoryIndex = historyIndex
        });
    }
}
