using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Guards the paper trade order lifecycle scenario where operators select live blotter rows
/// before flattening, upsizing, or leaving unsupported rows in review-only mode.
/// </summary>
public sealed class PositionBlotterViewModelTests
{
    [Fact]
    public void SelectionReview_PaperTradeOrderLifecycle_NoSelectionKeepsActionsDisabled()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateLoadedViewModel(
                CreateEntry("AAPL", "AAPL equity long", "T-1", 100m, 12.34m));

            vm.SelectedPositionPreviews.Should().BeEmpty();
            vm.HasSelectedPositions.Should().BeFalse();
            vm.SelectedLongQuantityText.Should().Be("0");
            vm.SelectedShortQuantityText.Should().Be("0");
            vm.SelectedGrossQuantityText.Should().Be("0");
            vm.UnsupportedActionCount.Should().Be(0);
            vm.SelectedActionEligibilityText.Should().Contain("Select positions");
            vm.SelectionSummaryText.Should().Contain("Select one or more positions");
            vm.UpsizeCommand.CanExecute(null).Should().BeFalse();
            vm.TerminateCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void SelectionReview_MixedLongShortSelection_ComputesExposureTotals()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateLoadedViewModel(
                CreateEntry("AAPL", "AAPL equity long", "T-1", 100m, 120.25m),
                CreateEntry("TSLA", "TSLA short hedge", "T-2", -25m, -20.75m));

            FindEntry(vm, "T-1").IsSelected = true;
            FindEntry(vm, "T-2").IsSelected = true;

            vm.SelectedPositionCount.Should().Be(2);
            vm.SelectedGroupCount.Should().Be(2);
            vm.SelectedLongQuantityText.Should().Be("+100");
            vm.SelectedShortQuantityText.Should().Be("-25");
            vm.SelectedGrossQuantityText.Should().Be("125");
            vm.SelectedNetQuantityText.Should().Be("+75");
            vm.SelectedUnrealisedPnlText.Should().Be("+99.50");
            vm.SelectedPositionPreviews.Should().HaveCount(2);
        });
    }

    [Fact]
    public void SelectionReview_MixedEligibilitySelection_ProjectsActionReadiness()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateLoadedViewModel(
                CreateEntry("AAPL", "AAPL equity long", "T-1", 100m, 12.34m, supportsClose: true, supportsUpsize: true),
                CreateEntry("TSLA", "TSLA short hedge", "T-2", -25m, -5.50m, supportsClose: true, supportsUpsize: false),
                CreateEntry("NVDA", "NVDA review-only lot", "T-3", 10m, 1.10m, supportsClose: false, supportsUpsize: false));

            foreach (var entry in vm.Groups.SelectMany(group => group.Entries))
            {
                entry.IsSelected = true;
            }

            vm.UnsupportedActionCount.Should().Be(1);
            vm.SelectedActionEligibilityText.Should().Be("Flatten: 2 | Upsize: 1 | Review-only: 1");
            vm.SelectionActionStateText.Should().Contain("Flatten available on 2 rows");
            vm.SelectedPositionPreviews.Select(preview => preview.EligibilityLabel)
                .Should()
                .BeEquivalentTo(["Flatten + upsize", "Flatten", "Review only"]);
        });
    }

    [Fact]
    public void SelectionReview_FilteredRows_SummaryReflectsDisplayedSelection()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateLoadedViewModel(
                CreateEntry("AAPL", "AAPL equity long", "T-1", 100m, 12.34m),
                CreateEntry("TSLA", "TSLA short hedge", "T-2", -25m, -5.50m));

            FindEntry(vm, "T-1").IsSelected = true;
            FindEntry(vm, "T-2").IsSelected = true;

            vm.SelectedPreset = "Long Only";

            vm.RowCount.Should().Be(1);
            vm.SelectedPositionCount.Should().Be(1);
            vm.SelectedGroupCount.Should().Be(1);
            vm.SelectedLongQuantityText.Should().Be("+100");
            vm.SelectedShortQuantityText.Should().Be("0");
            vm.SelectedGrossQuantityText.Should().Be("100");
            vm.SelectedNetQuantityText.Should().Be("+100");
            vm.SelectedPositionPreviews.Should().ContainSingle(preview => preview.Group == "AAPL");
        });
    }

    [Fact]
    public void EmptyState_FilterSearchWithNoMatches_OffersResetAndRestoresRows()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateLoadedViewModel(
                CreateEntry("AAPL", "AAPL equity long", "T-1", 100m, 12.34m),
                CreateEntry("TSLA", "TSLA short hedge", "T-2", -25m, -5.50m));

            vm.FilterSearchText = "missing-symbol";

            vm.RowCount.Should().Be(0);
            vm.HasRows.Should().BeFalse();
            vm.HasActiveFilters.Should().BeTrue();
            vm.EmptyStateTitle.Should().Be("No positions match current filters.");
            vm.EmptyStateDetail.Should().Contain("Reset");
            vm.ClearFiltersCommand.CanExecute(null).Should().BeTrue();

            vm.ClearFiltersCommand.Execute(null);

            vm.RowCount.Should().Be(2);
            vm.HasRows.Should().BeTrue();
            vm.HasActiveFilters.Should().BeFalse();
            vm.SelectedPreset.Should().Be("All");
            vm.FilterSearchText.Should().BeEmpty();
        });
    }

    [Fact]
    public void EmptyState_NoLoadedPositions_KeepsResetDisabled()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateLoadedViewModel();

            vm.RowCount.Should().Be(0);
            vm.HasRows.Should().BeFalse();
            vm.HasActiveFilters.Should().BeFalse();
            vm.EmptyStateTitle.Should().Be("No positions loaded yet.");
            vm.EmptyStateDetail.Should().Contain("Start a paper or live run");
            vm.ClearFiltersCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void PositionBlotterPageSource_ShouldExposeSelectionReviewRailAndWrappingFilters()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\PositionBlotterPage.xaml"));

        xaml.Should().Contain("PositionBlotterSelectionReviewRail");
        xaml.Should().Contain("PositionBlotterEmptyState");
        xaml.Should().Contain("PositionBlotterResetFiltersButton");
        xaml.Should().Contain("Selected Position Review");
        xaml.Should().Contain("SelectedPositionPreviewList");
        xaml.Should().Contain("<WrapPanel />");
        xaml.Should().Contain("SelectedActionEligibilityText");
    }

    [Fact]
    public void MapToEntry_ExecutionResponseCarriesNoObservationTime_LeavesMarketTimeUnknown()
    {
        // ExecutionPositionDetailResponse has no observation, as-of, quote, or last-trade
        // timestamp, and neither does anything upstream of it. Stamping the local clock here
        // would present row-construction time as a market observation time, so the mapping must
        // leave MarketTime unset rather than invent one.
        var response = new ExecutionPositionDetailResponse(
            PositionKey: "AAPL-T-1",
            Symbol: "AAPL",
            UnderlyingSymbol: "AAPL",
            ProductDescription: "AAPL equity long",
            TradeId: "T-1",
            Quantity: 100m,
            AverageCostBasis: 100m,
            MarketPrice: 101m,
            MarketValue: 10_100m,
            UnrealisedPnl: 12.34m,
            RealisedPnl: 0m,
            AssetClass: "equity",
            Side: "Buy");

        var entry = PositionBlotterViewModel.MapToEntry(response);

        entry.MarketTime.Should().BeNull("the response carries no observation timestamp to map");
        entry.PositionKey.Should().Be("AAPL-T-1");
        entry.Quantity.Should().Be(100m);
    }

    [Fact]
    public void StatusBar_NoEntryCarriesAnObservationTime_RendersUnknownRatherThanAClockReading()
    {
        WpfTestThread.Run(() =>
        {
            var withoutObservation = CreateEntry("AAPL", "AAPL equity long", "T-1", 100m, 12.34m);
            withoutObservation.MarketTime = null;

            using var vm = CreateLoadedViewModel(withoutObservation);

            vm.MinMktTimeText.Should().Be("—");
            vm.MaxMktTimeText.Should().Be("—");
        });
    }

    /// <summary>
    /// The position endpoints answer 200 with a status; a Rejected or PendingApproval result is a
    /// position still open, and reporting it as "submitted" would tell an operator flattening a
    /// book that a close was under way while nothing had reached the broker.
    /// </summary>
    [Fact]
    public void ClassifyActionResponse_AcceptedStatus_CountsAsSubmitted()
    {
        var outcome = PositionBlotterViewModel.ClassifyActionResponse(
            httpSuccess: true,
            errorMessage: null,
            actionStatus: "Accepted",
            actionMessage: "Order accepted.",
            productDescription: "AAPL equity long");

        outcome.Submitted.Should().BeTrue();
        outcome.Kind.Should().Be(PositionBlotterViewModel.PositionActionOutcomeKind.Submitted);
        outcome.Detail.Should().BeNull();
    }

    [Theory]
    [InlineData("Rejected", "Execution circuit breaker is open.")]
    [InlineData("Failed", "Gateway refused the close.")]
    public void ClassifyActionResponse_HttpSuccessCarryingARefusal_IsNotSubmittedAndKeepsTheReason(
        string status,
        string reason)
    {
        var outcome = PositionBlotterViewModel.ClassifyActionResponse(
            httpSuccess: true,
            errorMessage: null,
            actionStatus: status,
            actionMessage: reason,
            productDescription: "AAPL equity long");

        outcome.Submitted.Should().BeFalse("a 200 is not a close; the action status is");
        outcome.Kind.Should().Be(PositionBlotterViewModel.PositionActionOutcomeKind.Failed);
        outcome.Detail.Should().Be($"AAPL equity long: {reason}");
    }

    /// <summary>
    /// A parked close is neither submitted nor failed. Reporting it as a failure invites a
    /// resubmission that parks a second order under a fresh client id, and approving both
    /// over-closes the position; the endpoint's own instruction must reach the operator.
    /// </summary>
    [Fact]
    public void ClassifyActionResponse_PendingApproval_IsItsOwnOutcomeAndKeepsTheInstruction()
    {
        const string instruction = "Parked for governed approval (esc-1); an approver must release it. Do not resubmit.";

        var outcome = PositionBlotterViewModel.ClassifyActionResponse(
            httpSuccess: true,
            errorMessage: null,
            actionStatus: "PendingApproval",
            actionMessage: instruction,
            productDescription: "AAPL equity long");

        outcome.Submitted.Should().BeFalse();
        outcome.Kind.Should().Be(PositionBlotterViewModel.PositionActionOutcomeKind.PendingApproval);
        outcome.Detail.Should().Be($"AAPL equity long: {instruction}");
    }

    [Fact]
    public void ComposeActionStatus_PendingApprovalOnly_TellsTheOperatorNotToResubmit()
    {
        var text = PositionBlotterViewModel.ComposeActionStatus(
            "Close",
            "close",
            successes: 0,
            pendingApprovals: new[] { "AAPL equity long: Parked for governed approval (esc-1). Do not resubmit." },
            failures: Array.Empty<string>());

        text.Should().StartWith("1 parked for governed approval; do not resubmit");
        text.Should().Contain("esc-1");
        text.Should().NotContain("Unable to close");
        text.Should().NotContain("submitted");
    }

    [Fact]
    public void ComposeActionStatus_MixedOutcomes_NamesEachGroup()
    {
        var text = PositionBlotterViewModel.ComposeActionStatus(
            "Close",
            "close",
            successes: 1,
            pendingApprovals: new[] { "TSLA short hedge: parked" },
            failures: new[] { "NVDA review-only lot: Rejected" });

        text.Should().Be(
            "Close submitted for 1 position(s); 1 parked for governed approval; do not resubmit (TSLA short hedge: parked); 1 failed (NVDA review-only lot: Rejected).");
    }

    [Fact]
    public void ClassifyActionResponse_HttpSuccessWithoutAStatusOrMessage_NamesTheMissingStatus()
    {
        var outcome = PositionBlotterViewModel.ClassifyActionResponse(
            httpSuccess: true,
            errorMessage: null,
            actionStatus: null,
            actionMessage: null,
            productDescription: "AAPL equity long");

        outcome.Submitted.Should().BeFalse();
        outcome.Detail.Should().Contain("no status");
    }

    [Fact]
    public void ClassifyActionResponse_HttpFailure_KeepsTheTransportError()
    {
        var outcome = PositionBlotterViewModel.ClassifyActionResponse(
            httpSuccess: false,
            errorMessage: "503 Service Unavailable",
            actionStatus: null,
            actionMessage: null,
            productDescription: "AAPL equity long");

        outcome.Submitted.Should().BeFalse();
        outcome.Detail.Should().Be("AAPL equity long: 503 Service Unavailable");
    }

    private static PositionBlotterViewModel CreateLoadedViewModel(params BlotterEntry[] entries)
    {
        var vm = new PositionBlotterViewModel(ApiClientService.Instance, NavigationService.Instance);
        vm.LoadEntriesForTests(entries);
        return vm;
    }

    private static BlotterEntry CreateEntry(
        string group,
        string productDescription,
        string tradeId,
        decimal quantity,
        decimal unrealisedPnl,
        bool supportsClose = true,
        bool supportsUpsize = true) =>
        new()
        {
            Group = group,
            ProductDescription = productDescription,
            TradeId = tradeId,
            PositionKey = $"{group}-{tradeId}",
            UnitPrice = 100m,
            Quantity = quantity,
            Side = quantity >= 0 ? "Buy" : "Sell",
            Status = "Active",
            Expiry = new DateOnly(2026, 5, 15),
            AssetClass = "equity",
            SupportsClose = supportsClose,
            SupportsUpsize = supportsUpsize,
            UnrealisedPnl = unrealisedPnl,
            MarketTime = new TimeOnly(10, 15)
        };

    private static BlotterEntry FindEntry(PositionBlotterViewModel vm, string tradeId) =>
        vm.Groups
            .SelectMany(group => group.Entries)
            .Single(entry => entry.TradeId == tradeId);

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
