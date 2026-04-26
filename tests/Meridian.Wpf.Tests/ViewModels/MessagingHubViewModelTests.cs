using System.Windows.Media;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class MessagingHubViewModelTests
{
    [Fact]
    public void Start_ProjectsWaitingPostureWhenNoSubscribersOrTraffic()
    {
        WpfTestThread.Run(() =>
        {
            var service = WpfServices.MessagingService.Instance;
            service.ClearSubscriptions();

            using var viewModel = CreateViewModel(service);
            try
            {
                viewModel.Start();

                viewModel.MessageTypes.Should().HaveCount(14);
                viewModel.TotalMessagesText.Should().Be("0");
                viewModel.SubscribersText.Should().Be("0");
                viewModel.MessagingPostureTitle.Should().Be("Waiting for message traffic");
                viewModel.MessagingPostureDetail.Should().Contain("No messages or subscribers");
                viewModel.RecentActivityScopeText.Should().Be("No activity in this session");
                viewModel.NoActivityVisible.Should().BeTrue();
                viewModel.CanClearActivity.Should().BeFalse();
                viewModel.ClearActivityCommand.CanExecute(null).Should().BeFalse();
            }
            finally
            {
                viewModel.Stop();
                service.ClearSubscriptions();
            }
        });
    }

    [Fact]
    public void Start_ProjectsSubscriberReadyPostureFromKnownMessageTypes()
    {
        WpfTestThread.Run(() =>
        {
            var service = WpfServices.MessagingService.Instance;
            service.ClearSubscriptions();

            using var subscription = service.Subscribe(WpfServices.MessageTypes.SymbolsUpdated, _ => { });
            using var viewModel = CreateViewModel(service);
            try
            {
                viewModel.Start();

                viewModel.SubscribersText.Should().Be("1");
                viewModel.MessageTypes.Should().Contain(item =>
                    item.TypeName == "Symbols Updated" &&
                    item.CountText == "1 subscriber");
                viewModel.MessagingPostureTitle.Should().Be("Subscribers are listening");
                viewModel.MessagingPostureDetail.Should().Contain("1 subscriber registered");
                viewModel.ActivityEmptyStateDetail.Should().Be("1 subscriber ready; recent deliveries will appear here.");
            }
            finally
            {
                viewModel.Stop();
                service.ClearSubscriptions();
            }
        });
    }

    [Fact]
    public void ReceivedMessages_UpdateDeliveryPostureAndRetainedActivityScope()
    {
        WpfTestThread.Run(() =>
        {
            var service = WpfServices.MessagingService.Instance;
            service.ClearSubscriptions();

            using var viewModel = CreateViewModel(service);
            try
            {
                viewModel.Start();

                service.Send(WpfServices.MessageTypes.BackfillCompleted);

                viewModel.TotalMessagesText.Should().Be("1");
                viewModel.ActivityItems.Should().ContainSingle();
                viewModel.ActivityItems[0].MessageType.Should().Be("BackfillCompleted");
                viewModel.NoActivityVisible.Should().BeFalse();
                viewModel.MessagingPostureTitle.Should().Be("Messaging is flowing");
                viewModel.MessagingPostureDetail.Should().Contain("1 message observed");
                viewModel.RecentActivityScopeText.Should().Be("1 retained activity row");
                viewModel.CanClearActivity.Should().BeTrue();
                viewModel.ClearActivityCommand.CanExecute(null).Should().BeTrue();
            }
            finally
            {
                viewModel.Stop();
                service.ClearSubscriptions();
            }
        });
    }

    [Fact]
    public void ClearActivityCommand_ResetsSessionCountersAndDisablesClear()
    {
        WpfTestThread.Run(() =>
        {
            var service = WpfServices.MessagingService.Instance;
            service.ClearSubscriptions();

            using var viewModel = CreateViewModel(service);
            try
            {
                viewModel.Start();
                service.Send(WpfServices.MessageTypes.RefreshRequested);

                viewModel.ClearActivityCommand.Execute(null);

                viewModel.ActivityItems.Should().BeEmpty();
                viewModel.TotalMessagesText.Should().Be("0");
                viewModel.FailedMessagesText.Should().Be("0");
                viewModel.MessagingPostureTitle.Should().Be("Waiting for message traffic");
                viewModel.RecentActivityScopeText.Should().Be("No activity in this session");
                viewModel.NoActivityVisible.Should().BeTrue();
                viewModel.CanClearActivity.Should().BeFalse();
                viewModel.ClearActivityCommand.CanExecute(null).Should().BeFalse();
            }
            finally
            {
                viewModel.Stop();
                service.ClearSubscriptions();
            }
        });
    }

    [Fact]
    public void ReceivedMessages_RetainLatestFiftyRows()
    {
        WpfTestThread.Run(() =>
        {
            var service = WpfServices.MessagingService.Instance;
            service.ClearSubscriptions();

            using var viewModel = CreateViewModel(service);
            try
            {
                viewModel.Start();

                for (var i = 0; i < 55; i++)
                {
                    service.Send($"Message-{i:00}");
                }

                viewModel.TotalMessagesText.Should().Be("55");
                viewModel.ActivityItems.Should().HaveCount(50);
                viewModel.ActivityItems[0].MessageType.Should().Be("Message-54");
                viewModel.ActivityItems[^1].MessageType.Should().Be("Message-05");
                viewModel.RecentActivityScopeText.Should().Be("50 retained of 55 messages");
            }
            finally
            {
                viewModel.Stop();
                service.ClearSubscriptions();
            }
        });
    }

    [Fact]
    public void MessagingHubPageSource_BindsPostureScopeAndClearCommand()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\MessagingHubPage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\MessagingHubPage.xaml.cs"));

        xaml.Should().Contain("MessagingHubPostureCard");
        xaml.Should().Contain("MessagingHubPostureTitle");
        xaml.Should().Contain("MessagingHubPostureDetail");
        xaml.Should().Contain("MessagingHubRecentActivityScopeText");
        xaml.Should().Contain("MessagingHubClearActivityButton");
        xaml.Should().Contain("MessagingHubActivityEmptyStateTitle");
        xaml.Should().Contain("MessagingHubActivityEmptyStateDetail");
        xaml.Should().Contain("{Binding MessagingPostureTitle}");
        xaml.Should().Contain("{Binding MessagingPostureDetail}");
        xaml.Should().Contain("{Binding RecentActivityScopeText}");
        xaml.Should().Contain("Command=\"{Binding ClearActivityCommand}\"");
        codeBehind.Should().NotContain("ClearActivity_Click");
    }

    private static MessagingHubViewModel CreateViewModel(WpfServices.MessagingService service) =>
        new(service, Brushes.DodgerBlue, Brushes.ForestGreen, Brushes.IndianRed);

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
