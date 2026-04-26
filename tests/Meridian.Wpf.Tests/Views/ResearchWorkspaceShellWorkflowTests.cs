using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class ResearchWorkspaceShellWorkflowTests
{
    [Fact]
    public void ResearchWorkspaceShell_BriefingOpenAction_ShouldHydrateRunStudioAndDockInspectors()
    {
        WpfTestThread.Run(async () =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            RunMatUiAutomationFacade.EnsureApplicationResources();

            var tempRoot = Path.Combine(Path.GetTempPath(), "research-shell-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            WorkspaceService.SetSettingsFilePathOverrideForTests(Path.Combine(tempRoot, "workspace-data.json"));
            WorkspaceService.Instance.ResetForTests();
            NavigationService.Instance.ResetForTests();
            WorkspaceService.Instance.ResetForTests();
            FixtureModeDetector.Instance.SetFixtureMode(false);
            FixtureModeDetector.Instance.UpdateBackendReachability(true);

            var runId = "briefing-run-001";
            var expectedSummary = "Research desk synced with saved insights, watchlists, and promotion-aware run context.";

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);
            services.RemoveAll(typeof(IWorkstationResearchBriefingApiClient));
            services.AddSingleton<IWorkstationResearchBriefingApiClient>(new FakeWorkstationResearchBriefingApiClient
            {
                Briefing = BuildBriefing(runId, expectedSummary)
            });

            using var serviceProvider = services.BuildServiceProvider();
            NavigationService.Instance.SetServiceProvider(serviceProvider);

            var store = serviceProvider.GetRequiredService<IStrategyRepository>();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun(runId));

            var runService = serviceProvider.GetRequiredService<StrategyRunWorkspaceService>();
            await runService.SetActiveRunContextAsync(null);

            var page = serviceProvider.GetRequiredService<ResearchWorkspaceShellPage>();
            var window = new Window
            {
                Width = 1600,
                Height = 1000,
                Content = page
            };

            try
            {
                window.Show();
                page.ApplyTemplate();
                page.UpdateLayout();
                window.UpdateLayout();

                await WaitForConditionAsync(() =>
                {
                    var summaryText = page.FindName("BriefingSummaryText") as TextBlock;
                    return string.Equals(summaryText?.Text, expectedSummary, StringComparison.Ordinal);
                }).ConfigureAwait(true);

                GetRequired<TextBlock>(page, "BriefingSummaryText").Text.Should().Be(expectedSummary);
                GetRequired<TextBlock>(page, "ResearchHeroFocusText").Text.Should().Be("Promotion queue");
                GetRequired<TextBlock>(page, "ResearchHeroBadgeText").Text.Should().Be("Attention");
                GetRequired<TextBlock>(page, "ResearchWorkflowStatusText").Text.Should().Be("1 run is waiting for trading review.");
                GetRequired<TextBlock>(page, "ResearchWorkflowBlockerLabelText").Text.Should().NotBeNullOrWhiteSpace();
                GetRequired<TextBlock>(page, "ResearchWorkflowBlockerDetailText").Text.Should().NotBeNullOrWhiteSpace();
                GetRequired<Button>(page, "ResearchHeroPrimaryActionButton").Content.Should().Be("Run Browser");
                GetRequired<Button>(page, "ResearchHeroSecondaryActionButton").Content.Should().Be("Open Watchlists");
                GetRequired<TextBlock>(page, "ResearchWorkflowTargetText").Text.Should().Be("Target page: StrategyRuns");
                GetRequired<ItemsControl>(page, "BriefingInsightsList").Items.Count.Should().Be(1);
                GetRequired<TextBlock>(page, "ActiveRunNameText").Text.Should().Be("No selected run");

                var insightButton = FindButtonByTag(GetRequired<ItemsControl>(page, "BriefingInsightsList"), runId, "Open");
                Click(insightButton);

                await WaitForConditionAsync(() =>
                {
                    var activeRun = page.FindName("ActiveRunNameText") as TextBlock;
                    return string.Equals(activeRun?.Text, "Reconciliation Strategy", StringComparison.Ordinal);
                }).ConfigureAwait(true);

                var activeContext = await runService.GetActiveRunContextAsync().ConfigureAwait(true);
                activeContext.Should().NotBeNull();
                activeContext!.RunId.Should().Be(runId);
                activeContext.StrategyName.Should().Be("Reconciliation Strategy");
                activeContext.CanPromoteToPaper.Should().BeTrue();

                GetRequired<TextBlock>(page, "ResearchHeroFocusText").Text.Should().Be("Promotion review");
                GetRequired<TextBlock>(page, "ResearchHeroBadgeText").Text.Should().Be("Ready");
                GetRequired<Button>(page, "ResearchHeroPrimaryActionButton").Content.Should().Be("Open Trading Review");
                GetRequired<Button>(page, "ResearchHeroSecondaryActionButton").Content.Should().Be("Promote to Paper");
                GetRequired<TextBlock>(page, "ResearchWorkflowTargetText").Text.Should().Be("Target page: TradingShell");
                GetRequired<TextBlock>(page, "RunStatusText").Text.Should().Be("Backtest run selected");
                GetRequired<TextBlock>(page, "PortfolioPreviewText").Text.Should().Contain("2 position(s)");
                GetRequired<TextBlock>(page, "RiskPreviewText").Text.Should().Contain("Audit and reconciliation drill-ins stay one action away");

                var openDocumentKeys = GetDockDocumentKeys(GetRequired<MeridianDockingManager>(page, "ResearchDockManager"));
                openDocumentKeys.Should().Contain($"RunDetail:{runId}");
                openDocumentKeys.Should().Contain($"RunPortfolio:{runId}");
            }
            finally
            {
                window.Close();
                RunMatUiAutomationFacade.DrainDispatcher();
                NavigationService.Instance.ResetForTests();
                WorkspaceService.Instance.ResetForTests();
                WorkspaceService.SetSettingsFilePathOverrideForTests(null);

                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best effort cleanup for isolated shell automation state.
                }
            }
        });
    }

    private static ResearchBriefingDto BuildBriefing(string runId, string summary)
    {
        var generatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        var drillIn = new ResearchRunDrillInLinks(
            EquityCurve: $"/api/workstation/runs/{runId}/equity-curve",
            Fills: $"/api/workstation/runs/{runId}/fills",
            Attribution: $"/api/workstation/runs/{runId}/attribution",
            Ledger: $"/api/workstation/runs/{runId}/ledger",
            CashFlows: $"/api/portfolio/{runId}/cash-flows",
            Continuity: $"/api/workstation/runs/{runId}/continuity");

        return new ResearchBriefingDto(
            Workspace: new ResearchBriefingWorkspaceSummary(
                TotalRuns: 1,
                ActiveRuns: 0,
                PromotionCandidates: 1,
                PositivePnlRuns: 0,
                LatestRunId: runId,
                LatestStrategyName: "Reconciliation Strategy",
                HasLedgerCoverage: true,
                HasPortfolioCoverage: true,
                Summary: summary),
            InsightFeed: new InsightFeed(
                FeedId: "research-market-briefing",
                Title: "Pinned Insights",
                Summary: "One saved insight is pinned for quick reopen.",
                GeneratedAt: generatedAt,
                Widgets:
                [
                    new InsightWidget(
                        WidgetId: "widget-run-001",
                        Title: "Reconciliation Strategy",
                        Subtitle: "Backtest · Completed",
                        Headline: "0.00% · +$0",
                        Tone: "default",
                        Summary: "Pinned run with ledger continuity and promotion review context.",
                        RunId: runId,
                        DrillInRoute: drillIn.EquityCurve)
                ]),
            Watchlists:
            [
                new WorkstationWatchlist(
                    WatchlistId: "watchlist-tech",
                    Name: "Desk Focus",
                    Symbols: ["AAPL", "TSLA", "MSFT"],
                    SymbolCount: 3,
                    IsPinned: true,
                    SortOrder: 0,
                    AccentColor: "#2F80ED",
                    Summary: "Tracking 3 symbols: AAPL, TSLA, MSFT.")
            ],
            RecentRuns:
            [
                new ResearchBriefingRun(
                    RunId: runId,
                    StrategyName: "Reconciliation Strategy",
                    Mode: StrategyRunMode.Backtest,
                    Status: StrategyRunStatus.Completed,
                    Dataset: "dataset/us/equities",
                    WindowLabel: "Mar 20 2026 -> Mar 21 2026",
                    ReturnLabel: "0.00% · +$0",
                    SharpeLabel: "Sharpe 0.00",
                    LastUpdatedLabel: "4m ago",
                    Notes: "Completed run staged for paper review with ledger evidence ready.",
                    PromotionState: StrategyRunPromotionState.CandidateForPaper,
                    NetPnl: 0m,
                    TotalReturn: 0m,
                    FinalEquity: 1_000m,
                    DrillIn: drillIn)
            ],
            SavedComparisons:
            [
                new ResearchSavedComparison(
                    ComparisonId: "cmp-reconciliation",
                    StrategyName: "Reconciliation Strategy",
                    ModeSummary: "Backtest",
                    Summary: "Saved comparison anchor for the current strategy lifecycle.",
                    AnchorRunId: runId,
                    Modes:
                    [
                        new ResearchSavedComparisonMode(
                            RunId: runId,
                            Mode: StrategyRunMode.Backtest,
                            Status: StrategyRunStatus.Completed,
                            NetPnl: 0m,
                            TotalReturn: 0m,
                            DrillIn: drillIn)
                    ])
            ],
            Alerts:
            [
                new ResearchBriefingAlert(
                    AlertId: "alert-promotion",
                    Title: "Reconciliation Strategy is queued for promotion review",
                    Summary: "Completed backtests can be reviewed for paper promotion.",
                    Tone: "default",
                    RunId: runId,
                    ActionLabel: "Review")
            ],
            WhatChanged:
            [
                new ResearchWhatChangedItem(
                    ChangeId: "change-reconciliation",
                    Title: "Reconciliation Strategy moved to Backtest",
                    Summary: "Completed run is ready for follow-on operator review.",
                    Category: "research",
                    Timestamp: generatedAt,
                    RelativeTime: "4m ago",
                    RunId: runId)
            ]);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int attempts = 40, int delayMs = 50)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            RunMatUiAutomationFacade.DrainDispatcher();
            if (condition())
            {
                return;
            }

            await Task.Delay(delayMs).ConfigureAwait(true);
        }

        condition().Should().BeTrue("the Research workspace shell should finish refreshing inside the test window");
    }

    private static T GetRequired<T>(FrameworkElement element, string name) where T : FrameworkElement
    {
        element.FindName(name).Should().BeOfType<T>($"{name} should be declared on the Research workspace shell");
        return (T)element.FindName(name)!;
    }

    private static Button FindButtonByTag(DependencyObject root, string tag, string content)
    {
        foreach (var descendant in EnumerateDescendants(root))
        {
            if (descendant is Button button &&
                string.Equals(button.Tag as string, tag, StringComparison.Ordinal) &&
                string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            {
                return button;
            }
        }

        throw new Xunit.Sdk.XunitException($"Unable to locate briefing action button '{content}' for run '{tag}'.");
    }

    private static IReadOnlyList<string> GetDockDocumentKeys(MeridianDockingManager dockingManager)
    {
        var openDocumentsField = typeof(MeridianDockingManager).GetField("_openDocuments", BindingFlags.Instance | BindingFlags.NonPublic);
        openDocumentsField.Should().NotBeNull();

        var openDocuments = openDocumentsField!.GetValue(dockingManager).Should().BeAssignableTo<IDictionary>().Subject;
        return openDocuments.Keys.Cast<object>().Select(static key => key.ToString() ?? string.Empty).ToArray();
    }

    private static void Click(Button button)
    {
        if ((UIElementAutomationPeer.FromElement(button) ?? new ButtonAutomationPeer(button))
            .GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProvider)
        {
            invokeProvider.Invoke();
        }
        else if (button.Command is { } command)
        {
            var parameter = button.CommandParameter;
            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }
        else
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        RunMatUiAutomationFacade.DrainDispatcher();
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;

            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
            {
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var pair in _originalValues)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
