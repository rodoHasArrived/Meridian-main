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
