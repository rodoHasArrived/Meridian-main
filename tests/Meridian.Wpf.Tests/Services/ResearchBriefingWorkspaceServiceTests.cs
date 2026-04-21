using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Services;

public sealed class ResearchBriefingWorkspaceServiceTests
{
    [Fact]
    public async Task GetBriefingAsync_WhenEndpointReturnsPayload_ShouldPreferEndpointAndBackfillLocalWatchlists()
    {
        var store = new StrategyRunStore();
        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var apiClient = new FakeWorkstationResearchBriefingApiClient
        {
            Briefing = new ResearchBriefingDto(
                Workspace: new ResearchBriefingWorkspaceSummary(
                    TotalRuns: 5,
                    ActiveRuns: 2,
                    PromotionCandidates: 1,
                    PositivePnlRuns: 4,
                    LatestRunId: "api-run",
                    LatestStrategyName: "API Strategy",
                    HasLedgerCoverage: true,
                    HasPortfolioCoverage: true,
                    Summary: "Endpoint-backed market briefing."),
                InsightFeed: new InsightFeed(
                    FeedId: "feed-api",
                    Title: "Pinned Insights",
                    Summary: "Endpoint summary",
                    GeneratedAt: DateTimeOffset.UtcNow,
                    Widgets:
                    [
                        new InsightWidget(
                            WidgetId: "widget-api",
                            Title: "API Strategy",
                            Subtitle: "Paper",
                            Headline: "+3.0%",
                            Tone: "success",
                            Summary: "Endpoint widget",
                            RunId: "api-run",
                            DrillInRoute: "/api/workstation/runs/api-run/equity-curve")
                    ]),
                Watchlists: Array.Empty<WorkstationWatchlist>(),
                RecentRuns: Array.Empty<ResearchBriefingRun>(),
                SavedComparisons: Array.Empty<ResearchSavedComparison>(),
                Alerts: Array.Empty<ResearchBriefingAlert>(),
                WhatChanged: Array.Empty<ResearchWhatChangedItem>())
        };
        var watchlistReader = new FakeWatchlistReader(
        [
            new Watchlist
            {
                Id = "wl-local",
                Name = "Local Watchlist",
                Symbols = ["AAPL", "MSFT"],
                IsPinned = true,
                SortOrder = 0
            }
        ]);

        var service = new ResearchBriefingWorkspaceService(apiClient, runService, watchlistReader);

        var briefing = await service.GetBriefingAsync();

        briefing.Workspace.Summary.Should().Be("Endpoint-backed market briefing.");
        briefing.InsightFeed.Widgets.Should().ContainSingle(widget => widget.RunId == "api-run");
        briefing.Watchlists.Should().ContainSingle(watchlist => watchlist.Name == "Local Watchlist");
    }

    [Fact]
    public async Task GetBriefingAsync_WhenEndpointIsUnavailable_ShouldBuildFromLocalRuns()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("briefing-run-1"));

        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var apiClient = new FakeWorkstationResearchBriefingApiClient();
        var watchlistReader = new FakeWatchlistReader(
        [
            new Watchlist
            {
                Id = "wl-local",
                Name = "Desk Watch",
                Symbols = ["AAPL", "TSLA", "MSFT"],
                IsPinned = true,
                SortOrder = 0
            }
        ]);

        var service = new ResearchBriefingWorkspaceService(apiClient, runService, watchlistReader);

        var briefing = await service.GetBriefingAsync();

        briefing.Workspace.TotalRuns.Should().Be(1);
        briefing.Watchlists.Should().ContainSingle(watchlist => watchlist.Name == "Desk Watch");
        briefing.InsightFeed.Widgets.Should().ContainSingle();
        briefing.RecentRuns.Should().ContainSingle(run => run.RunId == "briefing-run-1");
        briefing.Alerts.Should().NotBeEmpty();
        briefing.WhatChanged.Should().ContainSingle(change => change.RunId == "briefing-run-1");
    }

    private sealed class FakeWatchlistReader : IWatchlistReader
    {
        private readonly IReadOnlyList<Watchlist> _watchlists;

        public FakeWatchlistReader(IReadOnlyList<Watchlist> watchlists)
        {
            _watchlists = watchlists;
        }

        public Task<IReadOnlyList<Watchlist>> GetAllWatchlistsAsync(CancellationToken ct = default)
            => Task.FromResult(_watchlists);
    }
}
