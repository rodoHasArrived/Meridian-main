using Meridian.Ui.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class TradingHoursViewModelTests
{
    [Fact]
    public void ApplyMarketStatusForTests_WhenRegularSessionOpen_ProjectsLiveRiskBriefing()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateSubject();

            viewModel.ApplyMarketStatusForTests("Open", "Regular trading session");

            viewModel.MarketStatusText.Should().Be("Open");
            viewModel.NyseRegularStatusText.Should().Be("Open");
            viewModel.NasdaqRegularStatusText.Should().Be("Open");
            viewModel.SessionBriefingToneLabel.Should().Be("Live risk window");
            viewModel.SessionBriefingTitle.Should().Be("Regular session is active");
            viewModel.SessionBriefingDetail.Should().Contain("Monitor blotter");
            viewModel.SessionBriefingTargetText.Should().Be("Regular session: 9:30 AM - 4:00 PM ET");
        });
    }

    [Fact]
    public void ApplyMarketStatusForTests_WhenPremarket_ProjectsStagingBriefingAndClosedRegularRows()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateSubject();

            viewModel.ApplyMarketStatusForTests("PreMarket", "Pre-market session");

            viewModel.MarketStatusText.Should().Be("Pre-Market");
            viewModel.NyseRegularStatusText.Should().Be("Closed");
            viewModel.NasdaqRegularStatusText.Should().Be("Closed");
            viewModel.SessionBriefingToneLabel.Should().Be("Pre-market staging");
            viewModel.SessionBriefingTitle.Should().Be("Extended session is active before the open");
            viewModel.SessionBriefingTargetText.Should().Be("Pre-market: 4:00 AM - 9:30 AM ET");
        });
    }

    [Fact]
    public void ApplyMarketStatusForTests_WhenClosedWithNextSession_ProjectsNextOpenBriefing()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateSubject();
            var nextSession = new DateTimeOffset(2026, 4, 27, 13, 30, 0, TimeSpan.Zero);

            viewModel.ApplyMarketStatusForTests("Closed", "Outside trading hours", nextSession);

            viewModel.MarketStatusText.Should().Be("Closed");
            viewModel.NextSessionText.Should().Be("Next session: Mon, Apr 27 at 9:30 AM ET");
            viewModel.SessionBriefingToneLabel.Should().Be("Closed planning");
            viewModel.SessionBriefingDetail.Should().Contain("Next session: Mon, Apr 27 at 9:30 AM ET");
            viewModel.SessionBriefingDetail.Should().Contain("staged orders");
        });
    }

    [Fact]
    public void ApplyMarketStatusForTests_WhenUnknownState_UsesClosedPlanningFallback()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateSubject();

            viewModel.ApplyMarketStatusForTests("Halted", "Calendar API returned an unknown state");

            viewModel.MarketStatusText.Should().Be("Closed");
            viewModel.SessionBriefingToneLabel.Should().Be("Closed planning");
            viewModel.SessionBriefingDetail.Should().Contain("Calendar API returned an unknown state");
        });
    }

    [Fact]
    public void ApplyHolidayCalendarForTests_WhenRowsExist_HidesHolidayEmptyState()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateSubject();

            viewModel.ApplyHolidayCalendarForTests(
                [
                    new TradingHoursViewModel.HolidayDisplayItem
                    {
                        DateText = "Jan 1 (Thu)",
                        Name = "New Year's Day"
                    }
                ],
                2026);

            viewModel.HolidaysHeaderText.Should().Be("US Market Holidays (2026)");
            viewModel.HasHolidays.Should().BeTrue();
            viewModel.IsHolidayEmptyStateVisible.Should().BeFalse();
            viewModel.Holidays.Should().ContainSingle(item => item.Name == "New Year's Day");
        });
    }

    [Fact]
    public void ApplyHolidayCalendarForTests_WhenNoRows_ShowsCoverageGuidance()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateSubject();

            viewModel.ApplyHolidayCalendarForTests([], 2026);

            viewModel.HasHolidays.Should().BeFalse();
            viewModel.IsHolidayEmptyStateVisible.Should().BeTrue();
            viewModel.HolidayEmptyStateTitle.Should().Be("No US market holidays loaded");
            viewModel.HolidayEmptyStateDetail.Should().Contain("2026 closure rows");
            viewModel.HolidayEmptyStateDetail.Should().Contain("exchange-holiday coverage");
        });
    }

    [Fact]
    public void ApplyHolidayCalendarUnavailableForTests_ShowsFallbackGuidance()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateSubject();

            viewModel.ApplyHolidayCalendarUnavailableForTests(2026);

            viewModel.HasHolidays.Should().BeFalse();
            viewModel.IsHolidayEmptyStateVisible.Should().BeTrue();
            viewModel.HolidayEmptyStateTitle.Should().Be("Holiday calendar unavailable");
            viewModel.HolidayEmptyStateDetail.Should().Contain("session status above");
            viewModel.Holidays.Should().BeEmpty();
        });
    }

    [Fact]
    public void TradingHoursPageSource_BindsSessionBriefing()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingHoursPage.xaml"));

        xaml.Should().Contain("TradingHoursMarketStatusCard");
        xaml.Should().Contain("TradingHoursSessionBriefingTone");
        xaml.Should().Contain("TradingHoursSessionBriefingTitle");
        xaml.Should().Contain("TradingHoursSessionBriefingDetail");
        xaml.Should().Contain("TradingHoursSessionBriefingTarget");
        xaml.Should().Contain("TradingHoursHolidayRows");
        xaml.Should().Contain("TradingHoursHolidayEmptyState");
        xaml.Should().Contain("TradingHoursHolidayEmptyStateTitle");
        xaml.Should().Contain("TradingHoursHolidayEmptyStateDetail");
        xaml.Should().Contain("{Binding SessionBriefingToneLabel}");
        xaml.Should().Contain("{Binding SessionBriefingTitle}");
        xaml.Should().Contain("{Binding SessionBriefingDetail}");
        xaml.Should().Contain("{Binding SessionBriefingTargetText}");
        xaml.Should().Contain("{Binding HasHolidays");
        xaml.Should().Contain("{Binding IsHolidayEmptyStateVisible");
        xaml.Should().Contain("{Binding HolidayEmptyStateTitle}");
        xaml.Should().Contain("{Binding HolidayEmptyStateDetail}");
    }

    private static TradingHoursViewModel CreateSubject() =>
        new(ApiClientService.Instance);

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
