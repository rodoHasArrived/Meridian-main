using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;
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
        xaml.Should().Contain("WorkstationStatePanelControl");
        xaml.Should().Contain("ProviderPostureState");
        xaml.Should().Contain("HealthBadgeControl");
        xaml.Should().Contain("ProviderPostureBadge");
        xaml.Should().Contain("ProviderPostureBriefingCard");
        xaml.Should().Contain("ProviderManagementCenter");
        xaml.Should().Contain("Provider Management");
        xaml.Should().Contain("WorkstationCommandBarControl");
        xaml.Should().Contain("ProviderManagementCommandGroup");
        xaml.Should().Contain("Configured Provider");
        xaml.Should().Contain("DenseDataGridControl");
        xaml.Should().Contain("ProviderManagementTable");
        xaml.Should().Contain("InspectorPanelControl");
        xaml.Should().Contain("SelectedProviderInspector");
        xaml.Should().Contain("DiagnosticsChecklistControl");
        xaml.Should().Contain("SelectedProviderDiagnostics");
        xaml.Should().Contain("RoutingMatrixControl");
        xaml.Should().Contain("SelectedProviderRoutingMatrix");
        xaml.Should().Contain("ActivityLogGridControl");
        xaml.Should().Contain("ProviderActivityTimeline");
    }

    [Fact]
    public void BuildProviderManagementRows_MergesStreamingAndBackfillProvidersWithoutDuplicatingProviderFamilies()
    {
        var streamingProviders = new[]
        {
            new ProviderStatusModel
            {
                ProviderId = "polygon",
                Name = "Polygon.io",
                StatusText = "Connected",
                LatencyText = "Latency: 82ms",
                UptimeText = "Uptime: 2h",
                ActionText = "Disconnect",
                IsConnected = true
            }
        };
        var backfillProviders = new[]
        {
            new BackfillProviderModel
            {
                ProviderId = "polygon",
                Name = "Polygon.io",
                StatusText = "Available",
                RateLimitText = "5 req/min (free)",
                LastUsedText = "Last used: 5m ago"
            },
            new BackfillProviderModel
            {
                ProviderId = "yahoo",
                Name = "Yahoo Finance",
                StatusText = "Available",
                RateLimitText = "100 req/min",
                LastUsedText = "Last used: --"
            }
        };

        var rows = ProviderHealthViewModel.BuildProviderManagementRows(
            streamingProviders,
            backfillProviders,
            new DateTime(2026, 5, 20, 18, 30, 0, DateTimeKind.Utc));

        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(row => row.ProviderId == "polygon")
            .Which.Should().Match<ProviderManagementRowModel>(row =>
                row.DisplayName == "Polygon.io" &&
                row.CapabilityText == "Data + Backfill" &&
                row.HealthText == "Healthy" &&
                row.CredentialStateText == "Configured" &&
                row.VerificationStateText == "Verified" &&
                row.AffectedWorkflowsText.Contains("Live quotes") &&
                row.AffectedWorkflowsText.Contains("Backfill") &&
                row.ActionText == "View Details");
        rows.Should().ContainSingle(row => row.ProviderId == "yahoo")
            .Which.Should().Match<ProviderManagementRowModel>(row =>
                row.CredentialStateText == "Not Required" &&
                row.VerificationStateText == "Not Required" &&
                row.MaskedKeyPreviewText == "Not required");
    }

    [Fact]
    public void BuildProviderManagementRows_SeparatesMissingCredentialsFromProviderHealth()
    {
        var rows = ProviderHealthViewModel.BuildProviderManagementRows(
            [
                new ProviderStatusModel
                {
                    ProviderId = "alpaca",
                    Name = "Alpaca Paper",
                    StatusText = "Not Configured",
                    LatencyText = "Latency: --",
                    UptimeText = "Uptime: --",
                    ActionText = "Configure",
                    IsConnected = false
                }
            ],
            Array.Empty<BackfillProviderModel>(),
            null);

        var alpaca = rows.Should().ContainSingle().Subject;
        alpaca.HealthText.Should().Be("Blocked");
        alpaca.CredentialStateText.Should().Be("Missing");
        alpaca.VerificationStateText.Should().Be("Failed");
        alpaca.RecommendedActionText.Should().Contain("Add credentials");
        alpaca.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Label == "Credential presence" &&
            diagnostic.StatusText == "Fail");
    }

    [Fact]
    public void BuildProviderManagementTable_ShouldExposeReusableDenseGridColumns()
    {
        var rows = new ObservableCollection<ProviderManagementRowModel>
        {
            new()
            {
                ProviderId = "polygon",
                DisplayName = "Polygon.io",
                CapabilityText = "Data + Backfill",
                HealthText = "Healthy"
            }
        };

        var table = ProviderHealthViewModel.BuildProviderManagementTable(rows);

        table.Title.Should().Be("Provider management table");
        table.Columns.Should().Contain(column => column.Header == "Provider" && column.BindingPath == nameof(ProviderManagementRowModel.DisplayName));
        table.Columns.Should().Contain(column => column.Header == "Verification" && column.BindingPath == nameof(ProviderManagementRowModel.VerificationStateText));
        table.Rows.Should().ContainSingle().Which.DisplayName.Should().Be("Polygon.io");
        ((WorkstationTableModel)table).Rows.Cast<object>().Should().ContainSingle();
    }

    [Fact]
    public void BuildProviderDiagnosticsChecklist_ShouldMapProviderDiagnosticsToSharedChecklist()
    {
        var row = new ProviderManagementRowModel
        {
            DisplayName = "Alpaca Paper",
            HealthText = "Blocked",
            CredentialStateText = "Missing",
            VerificationStateText = "Failed"
        };
        row.Diagnostics.Add(new ProviderManagementDiagnosticModel
        {
            Label = "Credential presence",
            StatusText = "Fail",
            Detail = "Missing"
        });

        var checklist = ProviderHealthViewModel.BuildProviderDiagnosticsChecklist(row);

        checklist.Title.Should().Be("Diagnostics");
        checklist.Items.Should().ContainSingle().Which.Should().Match<DiagnosticsChecklistItemModel>(item =>
            item.Label == "Credential presence" &&
            item.StatusText == "Fail" &&
            item.Tone == WorkspaceTone.Danger);
    }

    [Fact]
    public void BuildProviderInspectorAndRoutingMatrix_ShouldKeepProviderSpecificLogicOutOfControls()
    {
        var row = new ProviderManagementRowModel
        {
            DisplayName = "Polygon.io",
            CapabilityText = "Data + Backfill",
            HealthText = "Healthy",
            CredentialStateText = "Configured",
            CredentialSourceText = "Source: encrypted store",
            VerificationStateText = "Verified",
            LastVerifiedText = "Last verified: 2026-05-20 18:30 UTC",
            LastSuccessfulConnectionText = "Latency: 82ms",
            FallbackText = "Fallback not active",
            AffectedWorkflowsText = "Live quotes, Backfill",
            TrustExplanationText = "Health=Healthy; credentials=Configured; verification=Verified;",
            RecommendedActionText = "Keep provider active and monitor diagnostics."
        };

        var inspector = ProviderHealthViewModel.BuildProviderInspector(row);
        var routing = ProviderHealthViewModel.BuildProviderRoutingMatrix(row);

        inspector.Title.Should().Be("Polygon.io");
        inspector.Badge.Should().NotBeNull();
        inspector.Facts.Should().Contain(fact => fact.Label == "Credential state" && fact.Value == "Configured");
        routing.Rows.Should().HaveCount(2);
        routing.Rows.Should().Contain(row => row.Workflow == "Live quotes" && row.Route == "Polygon.io");
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
            AppServiceTestHost.InvokeConfigureServices(configureServices!, services);

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

    [Fact]
    public void ActivationLifetime_DeactivateCancelsVisiblePageLifetimeWithoutClearingLoadedRows()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            using var viewModel = new ProviderHealthViewModel(
                WpfServices.StatusService.Instance,
                WpfServices.ConnectionService.Instance,
                WpfServices.LoggingService.Instance,
                WpfServices.NotificationService.Instance);
            viewModel.Should().BeAssignableTo<IPageActivationLifetime>();
            viewModel.StreamingProviders.Add(new ProviderStatusModel { ProviderId = "polygon", Name = "Polygon.io" });

            viewModel.ActivateAsync().GetAwaiter().GetResult();
            var token = viewModel.ActivationToken;

            viewModel.IsActive.Should().BeTrue();
            token.CanBeCanceled.Should().BeTrue();

            viewModel.Deactivate();

            viewModel.IsActive.Should().BeFalse();
            token.IsCancellationRequested.Should().BeTrue();
            viewModel.StreamingProviders.Should().ContainSingle(provider => provider.ProviderId == "polygon");
        });
    }
}
