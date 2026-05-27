using Meridian.Ui.Services.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ServiceManagerViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WithRunningStatus_ShouldExposeRuntimePosture()
    {
        var backend = new FakeServiceManagerBackend
        {
            Status = new BackendServiceStatus
            {
                IsInstalled = true,
                IsRunning = true,
                IsHealthy = true,
                ProcessId = 4242,
                ExecutablePath = @"D:\Meridian\Meridian.exe",
                StatusMessage = "Backend service is running."
            }
        };
        var viewModel = CreateViewModel(backend);

        await viewModel.InitializeAsync();

        backend.StatusCalls.Should().Be(1);
        viewModel.StatusText.Should().Be("Running");
        viewModel.HealthText.Should().Be("Healthy");
        viewModel.ProcessIdText.Should().Be("4242");
        viewModel.ServiceStateBadgeText.Should().Be("Running");
        viewModel.RuntimePostureText.Should().Be("Health posture: Healthy.");
        viewModel.StatusLoadStateText.Should().Be("Status refreshed");
        viewModel.StatusLoadDetailText.Should().Be("Backend service is running.");
        viewModel.IsStatusLoadInfoVisible.Should().BeFalse();
        viewModel.IsStatusLoadFailed.Should().BeFalse();
        viewModel.CanStop.Should().BeTrue();
        viewModel.StopCommand.CanExecute(null).Should().BeTrue();
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_WhenStatusRefreshFails_ShouldExposeRecoverableFailureState()
    {
        var backend = new FakeServiceManagerBackend
        {
            StatusException = new InvalidOperationException("service state file is locked")
        };
        var viewModel = CreateViewModel(backend);

        await viewModel.InitializeAsync();

        viewModel.StatusText.Should().Be("Unknown");
        viewModel.HealthText.Should().Be("Unknown");
        viewModel.ProcessIdText.Should().Be("-");
        viewModel.ExecutablePathText.Should().Be("Not registered");
        viewModel.OperationResultText.Should().Be("Status refresh failed: service state file is locked");
        viewModel.StatusLoadStateText.Should().Be("Service status unavailable");
        viewModel.StatusLoadDetailText.Should().Be("service state file is locked");
        viewModel.IsStatusLoadFailed.Should().BeTrue();
        viewModel.IsStatusLoadInfoVisible.Should().BeTrue();
        viewModel.IsStatusLoading.Should().BeFalse();
        viewModel.CanRefresh.Should().BeTrue();
        viewModel.RefreshCommand.CanExecute(null).Should().BeTrue();
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task StartCommand_WhenFollowUpRefreshFails_ShouldReleaseBusyState()
    {
        var backend = new FakeServiceManagerBackend
        {
            Status = new BackendServiceStatus
            {
                IsInstalled = true,
                IsRunning = false,
                IsHealthy = false,
                ExecutablePath = @"D:\Meridian\Meridian.exe",
                StatusMessage = "Backend service is stopped."
            },
            StartResult = BackendServiceOperationResult.SuccessResult("Backend service started.")
        };
        var viewModel = CreateViewModel(backend);
        await viewModel.InitializeAsync();
        backend.StatusException = new InvalidOperationException("health endpoint timed out");

        viewModel.StartCommand.CanExecute(null).Should().BeTrue();

        await viewModel.StartCommand.ExecuteAsync(null);

        backend.StartCalls.Should().Be(1);
        viewModel.IsBusy.Should().BeFalse();
        viewModel.IsStatusLoading.Should().BeFalse();
        viewModel.IsStatusLoadFailed.Should().BeTrue();
        viewModel.OperationResultText.Should().Be("Status refresh failed: health endpoint timed out");
        viewModel.CanRefresh.Should().BeTrue();
        viewModel.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ServiceManagerPageSource_ShouldBindStatusLoadStateThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ServiceManagerPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\ServiceManagerViewModel.cs"));

        xaml.Should().Contain("ServiceManagerStatusLoadState");
        xaml.Should().Contain("{Binding IsStatusLoadInfoVisible");
        xaml.Should().Contain("{Binding IsStatusLoading");
        xaml.Should().Contain("{Binding StatusLoadStateText}");
        xaml.Should().Contain("{Binding StatusLoadDetailText}");
        xaml.Should().Contain("ServiceManagerStatusProgress");
        viewModel.Should().Contain("IServiceManagerBackend");
        viewModel.Should().Contain("Status refresh failed:");
        viewModel.Should().Contain("IsStatusLoadFailed");
    }

    private static ServiceManagerViewModel CreateViewModel(IServiceManagerBackend backend)
        => new(backend, WpfServices.LoggingService.Instance);

    private sealed class FakeServiceManagerBackend : IServiceManagerBackend
    {
        public BackendServiceStatus Status { get; set; } = new()
        {
            StatusMessage = "Backend service status was not loaded."
        };

        public Exception? StatusException { get; set; }
        public BackendServiceOperationResult InstallResult { get; set; } = BackendServiceOperationResult.SuccessResult("Installed.");
        public BackendServiceOperationResult StartResult { get; set; } = BackendServiceOperationResult.SuccessResult("Started.");
        public BackendServiceOperationResult StopResult { get; set; } = BackendServiceOperationResult.SuccessResult("Stopped.");
        public BackendServiceOperationResult RestartResult { get; set; } = BackendServiceOperationResult.SuccessResult("Restarted.");
        public int StatusCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int RestartCalls { get; private set; }

        public Task<BackendServiceOperationResult> InstallAsync(CancellationToken ct = default)
        {
            InstallCalls++;
            return Task.FromResult(InstallResult);
        }

        public Task<BackendServiceOperationResult> StartAsync(CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(StartResult);
        }

        public Task<BackendServiceOperationResult> StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            return Task.FromResult(StopResult);
        }

        public Task<BackendServiceOperationResult> RestartAsync(CancellationToken ct = default)
        {
            RestartCalls++;
            return Task.FromResult(RestartResult);
        }

        public Task<BackendServiceStatus> GetStatusAsync(CancellationToken ct = default)
        {
            StatusCalls++;
            if (StatusException is not null)
            {
                throw StatusException;
            }

            return Task.FromResult(Status);
        }
    }
}
