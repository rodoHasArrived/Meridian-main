using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceCommandBarControlTests
{
    [Fact]
    public void TryInvokeCommandRouting_ShouldRouteThroughCommandInvokedCommand()
    {
        WpfTestThread.Run(() =>
        {
            var routedItem = default(WorkspaceCommandItem);
            var routed = new RelayCommand<WorkspaceCommandItem>(item => routedItem = item);
            var commandItem = new WorkspaceCommandItem
            {
                Id = "Refresh",
                Label = "Refresh"
            };

            var sut = new WorkspaceCommandBarControl
            {
                CommandInvokedCommand = routed
            };

            var wasRouted = sut.TryInvokeCommandRouting(commandItem);

            wasRouted.Should().BeTrue();
            routedItem.Should().NotBeNull();
            routedItem!.Id.Should().Be("Refresh");
        });
    }

    [Fact]
    public void TryInvokeCommandRouting_ShouldNotRouteWhenCommandCannotExecute()
    {
        WpfTestThread.Run(() =>
        {
            var wasExecuted = false;
            var routed = new RelayCommand<WorkspaceCommandItem>(_ => wasExecuted = true, _ => false);
            var commandItem = new WorkspaceCommandItem
            {
                Id = "Blocked",
                Label = "Blocked"
            };

            var sut = new WorkspaceCommandBarControl
            {
                CommandInvokedCommand = routed
            };

            var wasRouted = sut.TryInvokeCommandRouting(commandItem);

            wasRouted.Should().BeFalse();
            wasExecuted.Should().BeFalse();
        });
    }

    [Fact]
    public void TryInvokeCommandRouting_ShouldReturnFalseWhenCommandIsNotConfigured()
    {
        WpfTestThread.Run(() =>
        {
            var sut = new WorkspaceCommandBarControl();
            var commandItem = new WorkspaceCommandItem
            {
                Id = "Noop",
                Label = "Noop"
            };

            var wasRouted = sut.TryInvokeCommandRouting(commandItem);

            wasRouted.Should().BeFalse();
        });
    }

    [Fact]
    public void OverflowMenu_ShouldEndWithResetLayout_WhenTheCommandIsBound()
    {
        WpfTestThread.Run(() =>
        {
            var resetCount = 0;
            var group = new WorkspaceCommandGroup
            {
                SecondaryCommands = [new WorkspaceCommandItem { Id = "Export", Label = "Export" }]
            };
            var sut = new WorkspaceCommandBarControl
            {
                CommandGroup = group,
                ResetLayoutCommand = new RelayCommand(() => resetCount++)
            };

            var items = sut.BuildOverflowMenuItems();

            items.Should().HaveCount(3);
            items[1].Should().BeOfType<System.Windows.Controls.Separator>();
            var reset = items[2].Should().BeOfType<System.Windows.Controls.MenuItem>().Subject;
            reset.Header.Should().Be("Reset layout");
            reset.Command!.Execute(null);
            resetCount.Should().Be(1);
        });
    }

    [Fact]
    public void OverflowMenu_ShouldOfferOnlyResetLayout_WhenThereAreNoSecondaryCommands()
    {
        WpfTestThread.Run(() =>
        {
            var sut = new WorkspaceCommandBarControl { ResetLayoutCommand = new RelayCommand(() => { }) };

            sut.BuildOverflowMenuItems().Should().ContainSingle()
                .Which.Should().BeOfType<System.Windows.Controls.MenuItem>()
                .Which.Header.Should().Be("Reset layout");
            sut.MoreButton.Visibility.Should().Be(System.Windows.Visibility.Visible);
        });
    }

    [Fact]
    public void OverflowMenu_ShouldNotOfferResetLayout_WhenTheCommandIsNotBound()
    {
        WpfTestThread.Run(() =>
        {
            var sut = new WorkspaceCommandBarControl();

            sut.BuildOverflowMenuItems().Should().BeEmpty();
        });
    }
}
