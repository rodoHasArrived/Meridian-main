using System.Windows.Controls;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Xunit.Sdk;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Wave 2 Operator Inbox Acceptance Tests — WPF desktop layer.
/// Validates the Lane A / W2 blocker path: operator inbox items carry a concrete
/// tone, label, and shared route target so that the desktop shell can direct the
/// operator to the correct clearing workbench without falling back to a desktop-only
/// page or leaving the blocker reason silent.
/// </summary>
[Trait("Category", "Scenario")]
public sealed class Wave2OperatorInboxAcceptanceTests
{
    // ── Blocker-path: tone and label surface correctly in the WPF inbox ──────

    [Fact]
    public void BlockerWorkItem_WithCriticalTone_SurfacesCorrectToneAndLabel()
    {
        WpfTestThread.Run(async () =>
        {
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "paper-session-missing-acceptance",
                        Kind: OperatorWorkItemKindDto.PaperReplay,
                        Label: "Paper session required",
                        Detail: "No active paper session found. Create one before enabling paper operation.",
                        Tone: OperatorWorkItemToneDto.Critical,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetRoute: UiApiRoutes.WorkstationTradingReadiness,
                        TargetPageTag: "TradingShell")
                ],
                CriticalCount: 1,
                WarningCount: 0,
                ReviewCount: 1,
                Summary: "1 critical item is blocking paper operation.");
            var inboxClient = new SequentialOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 1);

            vm.OperatorInboxTone.Should().Be(WorkspaceTone.Danger,
                "a Critical work item must surface as Danger tone in the WPF inbox");
            vm.OperatorInboxPrimaryLabel.Should().Contain("Paper session required",
                "the work item label must be visible in the inbox primary label");
            vm.OperatorInboxSummary.Should().Contain("critical",
                "the inbox summary must identify this as a critical blocker");
            vm.OperatorInboxButtonText.Should().Contain("Queue",
                "the inbox button must indicate pending items");
        });
    }

    [Fact]
    public void BlockerWorkItem_WithWarningTone_SurfacesWarningToneNotSilenced()
    {
        WpfTestThread.Run(async () =>
        {
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "replay-verification-stale-acceptance",
                        Kind: OperatorWorkItemKindDto.PaperReplay,
                        Label: "Replay verification is stale",
                        Detail: "Session state changed after the last replay audit. Verify replay before paper operation.",
                        Tone: OperatorWorkItemToneDto.Warning,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetRoute: UiApiRoutes.WorkstationTradingReadiness,
                        TargetPageTag: "TradingShell")
                ],
                CriticalCount: 0,
                WarningCount: 1,
                ReviewCount: 1,
                Summary: "1 warning item needs operator review.");
            var inboxClient = new SequentialOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 1);

            vm.OperatorInboxTone.Should().Be(WorkspaceTone.Warning,
                "a Warning work item must surface as Warning tone — not silenced or promoted to Danger");
            vm.OperatorInboxPrimaryLabel.Should().Contain("Replay verification is stale");
            vm.OperatorInboxReviewCount.Should().Be(1);
        });
    }

    // ── Blocker-path: WPF navigates to shared TargetPageTag, not a desktop-only page ──

    [Fact]
    public void BlockerWorkItem_PaperReplayKind_NavigatesToSharedTradingShell()
    {
        WpfTestThread.Run(async () =>
        {
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "paper-replay-blocker-route-acceptance",
                        Kind: OperatorWorkItemKindDto.PaperReplay,
                        Label: "Replay verification is stale",
                        Detail: "Replay has drifted from the persisted fill log.",
                        Tone: OperatorWorkItemToneDto.Critical,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetRoute: UiApiRoutes.WorkstationTradingReadiness,
                        TargetPageTag: "TradingShell")
                ],
                CriticalCount: 1,
                WarningCount: 0,
                ReviewCount: 1,
                Summary: "1 critical item.");
            var inboxClient = new SequentialOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 1);

            vm.OperatorInboxTargetText.Should().Be("TradingShell",
                "the shared TargetPageTag must drive WPF navigation, not a desktop-only override");

            vm.OpenOperatorInboxCommand.Execute(null);

            vm.CurrentWorkspace.Should().Be("trading",
                "WPF must activate the trading workspace when the blocker targets TradingShell");
            vm.CurrentPageTag.Should().Be("TradingShell",
                "WPF must navigate to TradingShell — not FundAuditTrail or any other desktop-only page");
        });
    }

    [Fact]
    public void BlockerWorkItem_BrokerageSyncKind_NavigatesToSharedTargetPage()
    {
        WpfTestThread.Run(async () =>
        {
            var fundAccountId = Guid.Parse("9c37d51f-2eba-40f5-9c86-6c9eb3863b8b");
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "brokerage-sync-blocker-acceptance",
                        Kind: OperatorWorkItemKindDto.BrokerageSync,
                        Label: "Brokerage sync failed",
                        Detail: "Account sync failed. Portfolio positions may be stale.",
                        Tone: OperatorWorkItemToneDto.Critical,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetRoute: $"{UiApiRoutes.FundAccountBrokerageSyncAccounts}?fundAccountId={fundAccountId:D}",
                        TargetPageTag: "TradingShell")
                ],
                CriticalCount: 1,
                WarningCount: 0,
                ReviewCount: 1,
                Summary: "1 critical item.");
            var inboxClient = new SequentialOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 1);

            vm.OpenOperatorInboxCommand.Execute(null);

            vm.CurrentPageTag.Should().Be("AccountPortfolio",
                "a brokerage-sync blocker with an account-scoped TargetRoute must open AccountPortfolio, not a desktop-only page");
        });
    }

    // ── Blocker-path: fundAccountId from operating context reaches the inbox API client ──

    [Fact]
    public void BlockerWorkItem_FundAccountIdFromOperatingContext_IsThreadedToApiClient()
    {
        WpfTestThread.Run(async () =>
        {
            var expectedAccountId = Guid.Parse("7f3a9b21-0c44-48e8-bdf1-3e2a107d9f66");
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [],
                CriticalCount: 0,
                WarningCount: 0,
                ReviewCount: 0,
                Summary: "No operator work items are open.");
            var inboxClient = new SequentialOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            vm.SelectedOperatingContext = new WorkstationOperatingContext
            {
                ScopeKind = OperatingContextScopeKind.Account,
                ScopeId = expectedAccountId.ToString("D"),
                AccountId = expectedAccountId.ToString("D"),
                DisplayName = "Wave 2 Paper Account",
                DefaultWorkspaceId = "trading",
                DefaultLandingPageTag = "TradingShell"
            };
            vm.RefreshPageCommand.Execute(null);

            await WaitForConditionAsync(() => inboxClient.RecordedFundAccountIds.Contains(expectedAccountId));

            inboxClient.RecordedFundAccountIds.Should().Contain(expectedAccountId,
                "the fundAccountId from the active operating context must be forwarded to GetInboxAsync");
        });
    }

    // ── Blocker-path: mixed severity prioritises Critical over Warning in the WPF inbox ──

    [Fact]
    public void BlockerWorkItems_MixedSeverity_CriticalItemDrivesPrimaryLabelAndTone()
    {
        WpfTestThread.Run(async () =>
        {
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "promotion-review-warning-acceptance",
                        Kind: OperatorWorkItemKindDto.PromotionReview,
                        Label: "Promotion checklist requires review",
                        Detail: "Checklist evidence awaits sign-off.",
                        Tone: OperatorWorkItemToneDto.Warning,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetPageTag: "TradingShell"),
                    new OperatorWorkItemDto(
                        WorkItemId: "replay-critical-acceptance",
                        Kind: OperatorWorkItemKindDto.PaperReplay,
                        Label: "Replay verification required",
                        Detail: "Session has drifted — replay audit is stale.",
                        Tone: OperatorWorkItemToneDto.Critical,
                        CreatedAt: DateTimeOffset.UtcNow.AddSeconds(1),
                        TargetRoute: UiApiRoutes.WorkstationTradingReadiness,
                        TargetPageTag: "TradingShell")
                ],
                CriticalCount: 1,
                WarningCount: 1,
                ReviewCount: 2,
                Summary: "2 items need review.");
            var inboxClient = new SequentialOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 2);

            vm.OperatorInboxTone.Should().Be(WorkspaceTone.Danger,
                "any Critical item must cause the inbox tone to be Danger regardless of Warning items");
            vm.OperatorInboxPrimaryLabel.Should().Contain("Replay verification required",
                "the most-recent Critical item must drive the primary label");
        });
    }

    // ── Recovery-path: clearing the inbox returns the shell to a non-blocked posture ──

    [Fact]
    public void BlockerWorkItem_AfterRefreshClearsItems_InboxReturnsToNeutralPosture()
    {
        WpfTestThread.Run(async () =>
        {
            var blockedInbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "replay-stale-clears-acceptance",
                        Kind: OperatorWorkItemKindDto.PaperReplay,
                        Label: "Replay verification is stale",
                        Detail: "Session changed after the last replay audit.",
                        Tone: OperatorWorkItemToneDto.Critical,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetRoute: UiApiRoutes.WorkstationTradingReadiness,
                        TargetPageTag: "TradingShell")
                ],
                CriticalCount: 1,
                WarningCount: 0,
                ReviewCount: 1,
                Summary: "1 critical item is blocking paper operation.");
            var clearedInbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [],
                CriticalCount: 0,
                WarningCount: 0,
                ReviewCount: 0,
                Summary: "No operator work items are open.");
            var inboxClient = new SequentialOperatorInboxApiClient(blockedInbox, clearedInbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 1);

            vm.OperatorInboxTone.Should().Be(WorkspaceTone.Danger);
            vm.OperatorInboxReviewCount.Should().Be(1);

            vm.RefreshPageCommand.Execute(null);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 0);

            vm.OperatorInboxTone.Should().Be(WorkspaceTone.Neutral,
                "after refresh returns an empty inbox the shell must drop the Danger tone");
            vm.OperatorInboxReviewCount.Should().Be(0,
                "stale blocked items must not persist in-memory after a refresh that returns no items");
            vm.OperatorInboxSummary.Should().Contain("No operator work items",
                "the cleared summary from the shared endpoint must replace the old blocker summary");
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MainPageViewModel CreateMainPageViewModel(
        IWorkstationOperatorInboxApiClient? operatorInboxClient = null)
    {
        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        navigationService.Initialize(new Frame());
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
        WorkspaceService.Instance.ResetForTests();

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        return new MainPageViewModel(
            navigationService,
            fixtureModeDetector,
            operatorInboxApiClient: operatorInboxClient);
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, int timeoutMs = 5000)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
            {
                throw new XunitException("Timed out waiting for condition.");
            }

            await Task.Delay(25);
        }
    }

    /// <summary>
    /// Returns inbox responses from a pre-configured sequence, cycling through
    /// each call in order. Useful for testing refresh/recovery paths where the
    /// server-side state changes between calls.
    /// </summary>
    private sealed class SequentialOperatorInboxApiClient : IWorkstationOperatorInboxApiClient
    {
        private readonly object _gate = new();
        private readonly List<OperatorInboxDto> _sequence;
        private readonly List<Guid?> _recordedFundAccountIds = [];
        private int _callIndex;

        public SequentialOperatorInboxApiClient(params OperatorInboxDto[] sequence)
        {
            _sequence = [.. sequence];
        }

        public IReadOnlyList<Guid?> RecordedFundAccountIds
        {
            get
            {
                lock (_gate)
                {
                    return _recordedFundAccountIds.ToArray();
                }
            }
        }

        public Task<OperatorInboxDto?> GetInboxAsync(Guid? fundAccountId = null, CancellationToken ct = default)
        {
            lock (_gate)
            {
                _recordedFundAccountIds.Add(fundAccountId);
                var index = _callIndex;
                if (_callIndex < _sequence.Count - 1)
                {
                    _callIndex++;
                }

                return Task.FromResult<OperatorInboxDto?>(_sequence[index]);
            }
        }
    }
}
