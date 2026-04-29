using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderHealthViewModelTests
{
    [Fact]
    public void BuildProviderPosture_WithNoConnectedStreamingProviders_PrioritizesReconnect()
    {
        var state = ProviderHealthViewModel.BuildProviderPosture(
            connectedStreamingProviders: 0,
            disconnectedStreamingProviders: 4,
            streamingProviders: 4,
            backfillProviders: 2,
            availableBackfillProviders: 2,
            isLastUpdateStale: false);

        state.Tone.Should().Be(ProviderHealthPostureTone.Critical);
        state.Title.Should().Be("Provider session offline");
        state.ActionText.Should().Be("Reconnect primary provider");
        state.TargetText.Should().Be("Streaming session");
        state.EvidenceText.Should().Be("0/4 streaming connected; 2/2 backfill available");
    }

    [Fact]
    public void BuildProviderPosture_WithStaleSnapshot_PrioritizesRefresh()
    {
        var state = ProviderHealthViewModel.BuildProviderPosture(
            connectedStreamingProviders: 1,
            disconnectedStreamingProviders: 0,
            streamingProviders: 1,
            backfillProviders: 1,
            availableBackfillProviders: 1,
            isLastUpdateStale: true);

        state.Tone.Should().Be(ProviderHealthPostureTone.Warning);
        state.Title.Should().Be("Provider snapshot is stale");
        state.ActionText.Should().Be("Refresh provider posture");
        state.TargetText.Should().Be("Provider health snapshot");
    }

    [Fact]
    public void BuildProviderPosture_WithStreamingReadyAndBackfillUnavailable_TargetsBackfillSetup()
    {
        var state = ProviderHealthViewModel.BuildProviderPosture(
            connectedStreamingProviders: 1,
            disconnectedStreamingProviders: 0,
            streamingProviders: 1,
            backfillProviders: 2,
            availableBackfillProviders: 0,
            isLastUpdateStale: false);

        state.Tone.Should().Be(ProviderHealthPostureTone.Warning);
        state.Title.Should().Be("Streaming ready; backfill blocked");
        state.ActionText.Should().Be("Configure backfill provider");
        state.TargetText.Should().Be("Backfill provider grid");
        state.EvidenceText.Should().Be("1/1 streaming connected; 0/2 backfill available");
    }

    [Fact]
    public void BuildProviderPosture_WithConnectedStreamingAndBackfill_IsReady()
    {
        var state = ProviderHealthViewModel.BuildProviderPosture(
            connectedStreamingProviders: 2,
            disconnectedStreamingProviders: 0,
            streamingProviders: 2,
            backfillProviders: 3,
            availableBackfillProviders: 3,
            isLastUpdateStale: false);

        state.Tone.Should().Be(ProviderHealthPostureTone.Ready);
        state.Title.Should().Be("Provider posture ready");
        state.ActionText.Should().Be("Monitor connection history");
        state.TargetText.Should().Be("Connection history");
        state.EvidenceText.Should().Be("2/2 streaming connected; 3/3 backfill available");
    }

    [Fact]
    public void ProviderHealthPageSource_ShouldExposeProviderPostureBriefing()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ProviderHealthPage.xaml"));

        xaml.Should().Contain("Provider posture briefing");
        xaml.Should().Contain("ProviderPostureTitle");
        xaml.Should().Contain("ProviderPostureDetail");
        xaml.Should().Contain("ProviderPostureActionText");
        xaml.Should().Contain("ProviderPostureTargetText");
        xaml.Should().Contain("ProviderPostureEvidenceText");
        xaml.Should().Contain("ProviderPostureBriefingCard");
        xaml.Should().Contain("ProviderPostureHandoffPanel");
    }

    [Fact]
    public void RefreshAsync_ShouldIgnoreDisposedPreviousRefreshTokenSource()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var serviceProvider = services.BuildServiceProvider();
            using var viewModel = new ProviderHealthViewModel(
                serviceProvider.GetRequiredService<WpfServices.StatusService>(),
                serviceProvider.GetRequiredService<WpfServices.ConnectionService>(),
                serviceProvider.GetRequiredService<WpfServices.LoggingService>(),
                serviceProvider.GetRequiredService<WpfServices.NotificationService>());

            await viewModel.StartAsync();

            var ctsField = typeof(ProviderHealthViewModel).GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic);
            ctsField.Should().NotBeNull();

            using var disposedRefreshCts = new CancellationTokenSource();
            disposedRefreshCts.Dispose();
            ctsField!.SetValue(viewModel, disposedRefreshCts);

            var exception = await Record.ExceptionAsync(() => viewModel.RefreshAsync());

            exception.Should().BeNull();
        });
    }
}
