using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ScheduleManagerViewModelTests
{
    [Fact]
    public async Task InitializeAsync_LoadsSchedulesTemplatesAndStatusText()
    {
        var client = new FakeScheduleManagerClient
        {
            BackfillSchedules =
            [
                new BackfillSchedule
                {
                    Id = "bf-1",
                    Name = "Daily equities",
                    Provider = "Alpaca",
                    CronDescription = "Weekdays at close",
                    IsEnabled = true
                }
            ],
            MaintenanceSchedules =
            [
                new MaintenanceSchedule
                {
                    Id = "maint-1",
                    Name = "Archive compaction",
                    MaintenanceType = "archive",
                    CronDescription = "Weekly",
                    IsEnabled = true
                }
            ],
            Templates =
            [
                new ScheduleTemplate
                {
                    Id = "tmpl-1",
                    Name = "US equities daily",
                    Category = "Backfill",
                    CronDescription = "Every weekday"
                }
            ]
        };

        var viewModel = new ScheduleManagerViewModel(client);

        await viewModel.InitializeAsync();

        viewModel.BackfillSchedules.Should().ContainSingle();
        viewModel.MaintenanceSchedules.Should().ContainSingle();
        viewModel.ScheduleTemplates.Should().ContainSingle();
        viewModel.BackfillSchedulesStatus.Should().Be("1 backfill schedule found");
        viewModel.MaintenanceSchedulesStatus.Should().Be("1 maintenance schedule found");
        viewModel.TemplatesStatus.Should().Be("1 schedule template found");
    }

    [Fact]
    public async Task InitializeAsync_WithEmptyResponses_ShouldExposeEmptyStates()
    {
        var viewModel = new ScheduleManagerViewModel(new FakeScheduleManagerClient());

        await viewModel.InitializeAsync();

        viewModel.BackfillSchedules.Should().BeEmpty();
        viewModel.MaintenanceSchedules.Should().BeEmpty();
        viewModel.ScheduleTemplates.Should().BeEmpty();
        viewModel.BackfillSchedulesStatus.Should().Be("No backfill schedules configured.");
        viewModel.MaintenanceSchedulesStatus.Should().Be("No maintenance schedules configured.");
        viewModel.TemplatesStatus.Should().Be("No schedule templates available.");
    }

    [Fact]
    public async Task RefreshBackfillSchedulesAsync_WhenServiceFails_ShouldExposeErrorState()
    {
        var client = new FakeScheduleManagerClient
        {
            BackfillException = new InvalidOperationException("host offline")
        };
        var viewModel = new ScheduleManagerViewModel(client);

        await viewModel.RefreshBackfillSchedulesAsync();

        viewModel.BackfillSchedulesStatus.Should().Be("Failed to load backfill schedules: host offline");
        viewModel.IsBackfillSchedulesLoading.Should().BeFalse();
        viewModel.RefreshBackfillSchedulesCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCronAsync_ShouldProjectWarningSuccessAndErrorStates()
    {
        var client = new FakeScheduleManagerClient
        {
            CronResult = new CronValidationResult
            {
                IsValid = true,
                Description = "Every weekday",
                NextRuns =
                [
                    new DateTime(2026, 4, 28, 13, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 29, 13, 0, 0, DateTimeKind.Utc)
                ]
            }
        };
        var viewModel = new ScheduleManagerViewModel(client)
        {
            CronExpression = "   "
        };

        await viewModel.ValidateCronAsync();

        viewModel.CronValidationTone.Should().Be("Warning");
        viewModel.CronValidationText.Should().Be("Enter a cron expression before validating.");

        viewModel.CronExpression = "0 13 * * 1-5";

        await viewModel.ValidateCronCommand.ExecuteAsync(null);

        client.LastCronExpression.Should().Be("0 13 * * 1-5");
        viewModel.CronValidationTone.Should().Be("Success");
        viewModel.CronValidationText.Should().Contain("Valid: Every weekday");
        viewModel.CronValidationText.Should().Contain("2026-04-28 13:00:00 UTC");

        client.CronResult = new CronValidationResult
        {
            IsValid = false,
            ErrorMessage = "Expected five fields"
        };

        await viewModel.ValidateCronAsync();

        viewModel.CronValidationTone.Should().Be("Error");
        viewModel.CronValidationText.Should().Be("Invalid: Expected five fields");
    }

    [Fact]
    public void ScheduleManagerPageSource_ShouldBindLoadingAndCronActionsThroughViewModel()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\ScheduleManagerPage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\ScheduleManagerPage.xaml.cs"));

        xaml.Should().Contain("Command=\"{Binding RefreshBackfillSchedulesCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RefreshMaintenanceSchedulesCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RefreshTemplatesCommand}\"");
        xaml.Should().Contain("Command=\"{Binding ValidateCronCommand}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding BackfillSchedules}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding MaintenanceSchedules}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding ScheduleTemplates}\"");
        xaml.Should().Contain("Text=\"{Binding CronExpression");
        xaml.Should().Contain("{Binding CronValidationText}");
        xaml.Should().Contain("{Binding CronValidationTone}");
        xaml.Should().NotContain("Click=\"RefreshBackfillSchedules_Click\"");
        xaml.Should().NotContain("Click=\"RefreshMaintenanceSchedules_Click\"");
        xaml.Should().NotContain("Click=\"ValidateCron_Click\"");

        codeBehind.Should().Contain("new ScheduleManagerViewModel");
        codeBehind.Should().Contain("DataContext = _viewModel");
        codeBehind.Should().NotContain("ValidateCron_Click");
        codeBehind.Should().NotContain("LoadBackfillSchedulesAsync");
        codeBehind.Should().NotContain("CronValidationResult.Text");
    }

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

    private sealed class FakeScheduleManagerClient : IScheduleManagerClient
    {
        public List<BackfillSchedule>? BackfillSchedules { get; set; } = [];

        public List<MaintenanceSchedule>? MaintenanceSchedules { get; set; } = [];

        public List<ScheduleTemplate>? Templates { get; set; } = [];

        public CronValidationResult? CronResult { get; set; }

        public Exception? BackfillException { get; set; }

        public string? LastCronExpression { get; private set; }

        public Task<List<BackfillSchedule>?> GetBackfillSchedulesAsync(CancellationToken ct = default)
        {
            if (BackfillException is not null)
            {
                throw BackfillException;
            }

            return Task.FromResult(BackfillSchedules);
        }

        public Task<List<MaintenanceSchedule>?> GetMaintenanceSchedulesAsync(CancellationToken ct = default) =>
            Task.FromResult(MaintenanceSchedules);

        public Task<List<ScheduleTemplate>?> GetBackfillTemplatesAsync(CancellationToken ct = default) =>
            Task.FromResult(Templates);

        public Task<CronValidationResult?> ValidateCronExpressionAsync(string cronExpression, CancellationToken ct = default)
        {
            LastCronExpression = cronExpression;
            return Task.FromResult(CronResult);
        }
    }
}
