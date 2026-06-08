using System.Windows;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class WatchlistViewModelTests
{
    [Fact]
    public void BuildWatchlistPosture_WhenLibraryIsEmpty_RecommendsCreation()
    {
        var posture = WatchlistViewModel.BuildWatchlistPosture(
            Array.Empty<WatchlistDisplayModel>(),
            Array.Empty<WatchlistDisplayModel>(),
            string.Empty);

        posture.Title.Should().Be("Start a watchlist library");
        posture.ActionText.Should().Be("Next: Create watchlist");
        posture.TotalWatchlistsText.Should().Be("0 watchlists");
        posture.SymbolCoverageText.Should().Be("0 symbols");
        posture.EmptyStateTitle.Should().Be("No watchlists yet");
        posture.EmptyStateDescription.Should().Contain("Create or import");
    }

    [Fact]
    public void BuildWatchlistPosture_WithPinnedLibrary_SummarizesDeskReadiness()
    {
        var all = new[]
        {
            BuildWatchlist("Tech", symbolTotal: 5, isPinned: true),
            BuildWatchlist("Energy", symbolTotal: 3, isPinned: false)
        };

        var posture = WatchlistViewModel.BuildWatchlistPosture(all, all, string.Empty);

        posture.Title.Should().Be("Watchlist library ready");
        posture.Detail.Should().Contain("2 watchlists");
        posture.Detail.Should().Contain("8 symbols");
        posture.PinnedWatchlistsText.Should().Be("1 pinned");
        posture.VisibleScopeText.Should().Be("2 visible watchlists");
        posture.ActionText.Should().Be("Next: Load a watchlist");
    }

    [Fact]
    public void BuildWatchlistPosture_WhenSearchExcludesEverything_ExplainsRecovery()
    {
        var all = new[]
        {
            BuildWatchlist("Core ETFs", symbolTotal: 4, isPinned: true)
        };

        var posture = WatchlistViewModel.BuildWatchlistPosture(
            all,
            Array.Empty<WatchlistDisplayModel>(),
            "semis");

        posture.Title.Should().Be("Search has no matches");
        posture.Detail.Should().Contain("\"semis\"");
        posture.VisibleScopeText.Should().Be("0 visible results for \"semis\"");
        posture.EmptyStateTitle.Should().Be("No watchlists match the current search");
        posture.EmptyStateDescription.Should().Contain("Clear the search");
        posture.ActionText.Should().Be("Next: Clear search or import");
    }

    [Fact]
    public void SortWatchlistsForDeskDisplay_PutsPinnedListsFirstWithoutChangingManualOrderWithinGroups()
    {
        var watchlists = new[]
        {
            BuildServiceWatchlist("Unpinned Alpha", sortOrder: 0, isPinned: false),
            BuildServiceWatchlist("Pinned Later", sortOrder: 3, isPinned: true),
            BuildServiceWatchlist("Pinned Earlier", sortOrder: 1, isPinned: true),
            BuildServiceWatchlist("Unpinned Later", sortOrder: 2, isPinned: false)
        };

        var sorted = WatchlistViewModel.SortWatchlistsForDeskDisplay(watchlists);

        sorted.Select(watchlist => watchlist.Name).Should().ContainInOrder(
            "Pinned Earlier",
            "Pinned Later",
            "Unpinned Alpha",
            "Unpinned Later");
    }

    [Fact]
    public void WatchlistDisplayModel_ExposesPinnedBadgeVisibility()
    {
        var pinned = new WatchlistDisplayModel { IsPinned = true, SymbolsPreview = ["SPY", "QQQ"] };
        pinned.PinnedBadgeVisibility.Should().Be(Visibility.Visible);
        pinned.PinnedText.Should().Be("Pinned");
        pinned.PreviewSymbolsText.Should().Be("SPY, QQQ");

        var unpinned = new WatchlistDisplayModel { IsPinned = false };
        unpinned.PinnedBadgeVisibility.Should().Be(Visibility.Collapsed);
        unpinned.PinnedText.Should().Be("Unpinned");
        unpinned.PreviewSymbolsText.Should().Be("No preview symbols");
    }

    [Fact]
    public void ClearSearchCommand_ClearsActiveSearchAndDisablesRecoveryAction()
    {
        using var viewModel = new WatchlistViewModel(
            WpfServices.WatchlistService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            WpfServices.NavigationService.Instance);

        viewModel.ClearSearchCommand.CanExecute(null).Should().BeFalse();
        viewModel.ClearSearchCommand.Execute(null);
        viewModel.SearchText.Should().BeEmpty();

        viewModel.SearchText = "semis";

        viewModel.HasActiveSearch.Should().BeTrue();
        viewModel.ClearSearchCommand.CanExecute(null).Should().BeTrue();

        viewModel.ClearSearchCommand.Execute(null);

        viewModel.SearchText.Should().BeEmpty();
        viewModel.HasActiveSearch.Should().BeFalse();
        viewModel.ClearSearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SelectedWatchlist_UpdatesInspectorAndSelectedCommandReadiness()
    {
        using var viewModel = new WatchlistViewModel(
            WpfServices.WatchlistService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            WpfServices.NavigationService.Instance);

        viewModel.WatchlistTable.Rows.Should().BeSameAs(viewModel.FilteredWatchlists);
        viewModel.LoadSelectedWatchlistCommand.CanExecute(null).Should().BeFalse();
        viewModel.EditSelectedWatchlistCommand.CanExecute(null).Should().BeFalse();
        viewModel.PinSelectedWatchlistCommand.CanExecute(null).Should().BeFalse();
        viewModel.ExportSelectedWatchlistCommand.CanExecute(null).Should().BeFalse();
        viewModel.DuplicateSelectedWatchlistCommand.CanExecute(null).Should().BeFalse();
        viewModel.DeleteSelectedWatchlistCommand.CanExecute(null).Should().BeFalse();
        viewModel.SelectedWatchlistInspector.Title.Should().Be("No watchlist selected");
        viewModel.LoadSelectedWatchlistTooltip.Should().Be("Select a watchlist before loading symbols.");

        var selected = BuildWatchlist("Core ETFs", symbolTotal: 4, isPinned: true);
        selected.SymbolsPreview = ["SPY", "QQQ", "DIA", "IWM"];
        selected.ModifiedText = "2h ago";
        viewModel.SelectedWatchlist = selected;

        viewModel.HasSelectedWatchlist.Should().BeTrue();
        viewModel.SelectedPinActionLabel.Should().Be("Unpin");
        viewModel.LoadSelectedWatchlistCommand.CanExecute(null).Should().BeTrue();
        viewModel.EditSelectedWatchlistCommand.CanExecute(null).Should().BeTrue();
        viewModel.PinSelectedWatchlistCommand.CanExecute(null).Should().BeTrue();
        viewModel.ExportSelectedWatchlistCommand.CanExecute(null).Should().BeTrue();
        viewModel.DuplicateSelectedWatchlistCommand.CanExecute(null).Should().BeTrue();
        viewModel.DeleteSelectedWatchlistCommand.CanExecute(null).Should().BeTrue();
        viewModel.SelectedWatchlistInspector.Title.Should().Be("Core ETFs");
        viewModel.SelectedWatchlistInspector.Subtitle.Should().Be("Pinned watchlist");
        viewModel.SelectedWatchlistInspector.Facts.Should().Contain(fact => fact.Label == "Symbols" && fact.Detail.Contains("SPY"));
        viewModel.LoadSelectedWatchlistTooltip.Should().Contain("Core ETFs");
        viewModel.PinSelectedWatchlistTooltip.Should().Contain("Unpin");
        viewModel.DeleteSelectedWatchlistTooltip.Should().Contain("Core ETFs");
    }

    [Fact]
    public void WatchlistPageSource_BindsPostureAndDynamicEmptyState()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\WatchlistPage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\WatchlistPage.xaml.cs"));

        xaml.Should().Contain("WatchlistActionStrip");
        xaml.Should().Contain("WatchlistPostureCard");
        xaml.Should().Contain("WatchlistWorkbench");
        xaml.Should().Contain("{Binding PostureTitle}");
        xaml.Should().Contain("{Binding PostureDetail}");
        xaml.Should().Contain("{Binding TotalWatchlistsText}");
        xaml.Should().Contain("{Binding VisibleScopeText}");
        xaml.Should().Contain("{Binding EmptyStateTitle}");
        xaml.Should().Contain("{Binding EmptyStateDescription}");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("WatchlistLibraryGrid");
        xaml.Should().Contain("WatchlistSelectionInspector");
        xaml.Should().Contain("WatchlistActionInspector");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedWatchlist, Mode=TwoWay}\"");
        xaml.Should().Contain("Table=\"{Binding WatchlistTable}\"");
        xaml.Should().Contain("Inspector=\"{Binding SelectedWatchlistInspector}\"");
        xaml.Should().Contain("{Binding LoadSelectedWatchlistCommand}");
        xaml.Should().Contain("{Binding EditSelectedWatchlistCommand}");
        xaml.Should().Contain("{Binding PinSelectedWatchlistCommand}");
        xaml.Should().Contain("{Binding ExportSelectedWatchlistCommand}");
        xaml.Should().Contain("{Binding DuplicateSelectedWatchlistCommand}");
        xaml.Should().Contain("{Binding DeleteSelectedWatchlistCommand}");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("WatchlistClearSearchButton");
        xaml.Should().Contain("{Binding ClearSearchCommand}");
        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("WatchlistCardStyle");
        xaml.Should().NotContain("WatchlistsContainer");
        xaml.Should().NotContain("LoadWatchlist_Click");
        xaml.Should().NotContain("EditWatchlist_Click");
        xaml.Should().NotContain("PinWatchlist_Click");
        xaml.Should().NotContain("WatchlistMenu_Click");
        codeBehind.Should().NotContain("ContextMenu");
        codeBehind.Should().NotContain("LoadWatchlist_Click");
    }

    private static WatchlistDisplayModel BuildWatchlist(string name, int symbolTotal, bool isPinned) =>
        new()
        {
            Id = name.ToLowerInvariant(),
            Name = name,
            SymbolTotal = symbolTotal,
            SymbolCount = $"{symbolTotal} symbols",
            IsPinned = isPinned
        };

    private static WpfServices.Watchlist BuildServiceWatchlist(string name, int sortOrder, bool isPinned) =>
        new()
        {
            Id = name.ToLowerInvariant().Replace(' ', '-'),
            Name = name,
            Symbols = new List<string> { "SPY" },
            SortOrder = sortOrder,
            IsPinned = isPinned,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };

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
