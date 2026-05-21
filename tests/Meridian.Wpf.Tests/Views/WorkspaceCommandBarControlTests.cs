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
}
