using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Unit tests for the pure formatting/derivation helpers on
/// <see cref="StatusBarViewModel"/>. The view model itself relies on
/// <c>Application.Current.Dispatcher</c>, so the live refresh loop is not
/// exercised here; these tests cover the two pieces that are easy to break
/// without UI: throughput formatting and status derivation.
/// </summary>
public sealed class StatusBarViewModelTests
{
    // ── FormatThroughput ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0 ev/s")]
    [InlineData(1, "1 ev/s")]
    [InlineData(999, "999 ev/s")]
    [InlineData(1_000, "1.0K ev/s")]
    [InlineData(1_499, "1.5K ev/s")]
    [InlineData(12_345, "12.3K ev/s")]
    [InlineData(999_999, "1000.0K ev/s")]
    [InlineData(1_000_000, "1.0M ev/s")]
    [InlineData(5_500_000, "5.5M ev/s")]
    public void FormatThroughput_FormatsAcrossTiers(double eventsPerSecond, string expected)
    {
        StatusBarViewModel.FormatThroughput(eventsPerSecond).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FormatThroughput_HandlesNonsenseInputs(double bogus)
    {
        StatusBarViewModel.FormatThroughput(bogus).Should().Be("0 ev/s");
    }

    // ── FormatPipelineQueue ───────────────────────────────────────────────

    [Fact]
    public void FormatPipelineQueue_WhenSnapshotIsMissing_ShouldShowUnavailableQueue()
    {
        StatusBarViewModel.FormatPipelineQueue(null).Should().Be("Queue: n/a");
    }

    [Fact]
    public void FormatPipelineQueue_WhenCapacityIsKnown_ShouldShowCurrentCapacityAndPercent()
    {
        var pipeline = new PipelineData
        {
            CurrentQueueSize = 250,
            QueueCapacity = 1_000
        };

        StatusBarViewModel.FormatPipelineQueue(pipeline).Should().Be("Queue: 250/1,000 (25%)");
    }

    [Theory]
    [InlineData(0.72f)]
    [InlineData(72f)]
    public void FormatPipelineQueue_WhenOnlyUtilizationIsKnown_ShouldNormalizePercent(float utilization)
    {
        var pipeline = new PipelineData
        {
            QueueCapacity = 0,
            QueueUtilization = utilization
        };

        StatusBarViewModel.FormatPipelineQueue(pipeline).Should().Be("Queue: 72%");
    }

    [Fact]
    public void DerivePipelineQueueBrush_WhenQueueIsNearCapacity_ShouldEscalateTone()
    {
        var low = StatusBarViewModel.DerivePipelineQueueBrush(new PipelineData
        {
            CurrentQueueSize = 10,
            QueueCapacity = 100
        });
        var high = StatusBarViewModel.DerivePipelineQueueBrush(new PipelineData
        {
            CurrentQueueSize = 95,
            QueueCapacity = 100
        });

        high.Should().NotBeSameAs(low);
        high.Should().NotBeSameAs(Brushes.Transparent);
    }

    // ── DeriveBackendStatus ───────────────────────────────────────────────

    [Fact]
    public void DeriveBackendStatus_WhenStatusIsNull_ReportsDisconnected()
    {
        var (status, brush) = StatusBarViewModel.DeriveBackendStatus(status: null, dropRate: 0);
        status.Should().Be("Disconnected");
        brush.Should().NotBeSameAs(Brushes.Transparent);
    }

    [Fact]
    public void DeriveBackendStatus_WhenStatusIsDisconnected_ReportsDisconnected()
    {
        var status = new StatusResponse { IsConnected = false };
        var (label, _) = StatusBarViewModel.DeriveBackendStatus(status, dropRate: 0);
        label.Should().Be("Disconnected");
    }

    [Fact]
    public void DeriveBackendStatus_WhenConnectedAndDropRateBelowThreshold_ReportsConnected()
    {
        var status = new StatusResponse { IsConnected = true };
        var (label, _) = StatusBarViewModel.DeriveBackendStatus(
            status, dropRate: StatusBarViewModel.DegradedDropRateThreshold);
        label.Should().Be("Connected");
    }

    [Fact]
    public void DeriveBackendStatus_WhenConnectedAndDropRateAboveThreshold_ReportsDegraded()
    {
        var status = new StatusResponse { IsConnected = true };
        var (label, _) = StatusBarViewModel.DeriveBackendStatus(
            status, dropRate: StatusBarViewModel.DegradedDropRateThreshold + 0.001);
        label.Should().Be("Degraded");
    }

    [Fact]
    public void StatusBarControlSource_ShouldBindPipelineQueueIndicator()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\StatusBarControl.xaml"));

        xaml.Should().Contain("StatusBarPipelineQueueLabel");
        xaml.Should().Contain("{Binding PipelineQueueLabel}");
        xaml.Should().Contain("{Binding PipelineQueueBrush}");
        xaml.Should().Contain("AutomationProperties.Name=\"Pipeline queue\"");
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

        throw new DirectoryNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}

public sealed class StatusStripModelTests
{
    [Fact]
    public void ResolveStatusReplacement_WhenIncomingSeverityIsLowerFromDifferentSource_KeepsCurrentMessage()
    {
        var current = new StatusStripMessage(StatusStripSeverity.Error, "Provider offline", "Provider", DateTimeOffset.Now);
        var incoming = new StatusStripMessage(StatusStripSeverity.Info, "Refresh complete", "Refresh", DateTimeOffset.Now.AddSeconds(1));

        StatusBarViewModel.ResolveStatusReplacement(current, incoming).Should().BeSameAs(current);
    }

    [Fact]
    public void ResolveStatusReplacement_WhenIncomingSeverityIsHigher_ReplacesCurrentMessage()
    {
        var current = new StatusStripMessage(StatusStripSeverity.Info, "Refresh complete", "Refresh", DateTimeOffset.Now);
        var incoming = new StatusStripMessage(StatusStripSeverity.Warning, "Provider degraded", "Provider", DateTimeOffset.Now.AddSeconds(1));

        StatusBarViewModel.ResolveStatusReplacement(current, incoming).Should().BeSameAs(incoming);
    }

    [Fact]
    public void ClearStatus_WithMatchingSource_ClearsCurrentMessage()
    {
        using var viewModel = new StatusBarViewModel(new FakeStatusService(), new FakeNotificationService());
        viewModel.ReportStatus(new StatusStripMessage(StatusStripSeverity.Warning, "Validation warning", "Validation", DateTimeOffset.Now));

        viewModel.ClearStatus("Validation");

        viewModel.CurrentStatus.Should().BeNull();
        viewModel.StatusMessage.Should().Be("Ready");
    }

    [Fact]
    public void ReportStatus_WithActionCommand_ExposesExecutableAction()
    {
        var executed = false;
        using var viewModel = new StatusBarViewModel(new FakeStatusService(), new FakeNotificationService());
        var command = new RelayCommand(() => executed = true);

        viewModel.ReportStatus(new StatusStripMessage(
            StatusStripSeverity.Warning,
            "Credentials need validation.",
            "Validation",
            DateTimeOffset.Now,
            "Validate",
            command));

        viewModel.StatusActionLabel.Should().Be("Validate");
        viewModel.StatusActionCommand.Should().BeSameAs(command);

        viewModel.StatusActionCommand!.Execute(null);

        executed.Should().BeTrue();
    }

    private sealed class FakeStatusService : Meridian.Ui.Services.Contracts.IStatusService
    {
        public string ServiceUrl => "http://localhost";
        public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default) => Task.FromResult<StatusResponse?>(new StatusResponse { IsConnected = true });
        public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default) => Task.FromResult(ApiResponse<StatusResponse>.Ok(new StatusResponse { IsConnected = true }));
        public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ServiceHealthResult { IsReachable = true, IsConnected = true });
    }

    private sealed class FakeNotificationService : Meridian.Ui.Services.Contracts.INotificationService
    {
        public Task NotifyErrorAsync(string title, string message, Exception? exception = null) => Task.CompletedTask;
        public Task NotifyWarningAsync(string title, string message) => Task.CompletedTask;
        public Task NotifyAsync(string title, string message, NotificationType type = NotificationType.Info) => Task.CompletedTask;
        public Task NotifyBackfillCompleteAsync(bool success, int symbolCount, int barsWritten, TimeSpan duration) => Task.CompletedTask;
        public Task NotifyScheduledJobAsync(string jobName, bool started, bool success = true) => Task.CompletedTask;
        public Task NotifyStorageWarningAsync(double usedPercent, long freeSpaceBytes) => Task.CompletedTask;
    }
}
