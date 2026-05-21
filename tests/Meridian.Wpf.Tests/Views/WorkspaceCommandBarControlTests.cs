using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceCommandBarControlTests
{
    [Fact]
    public void PrimaryClick_ShouldRouteThroughCommandInvokedCommand()
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

            InvokePrimaryClick(sut, commandItem);

            routedItem.Should().NotBeNull();
            routedItem!.Id.Should().Be("Refresh");
        });
    }

    [Fact]
    public void PrimaryClick_ShouldNotRouteWhenCommandCannotExecute()
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

            InvokePrimaryClick(sut, commandItem);

            wasExecuted.Should().BeFalse();
        });
    }

    private static void InvokePrimaryClick(WorkspaceCommandBarControl sut, WorkspaceCommandItem commandItem)
    {
        var click = typeof(WorkspaceCommandBarControl).GetMethod(
            "OnPrimaryCommandClick",
            BindingFlags.Instance | BindingFlags.NonPublic);

        click.Should().NotBeNull();
        var button = new Button
        {
            Tag = commandItem
        };

        click!.Invoke(sut, [button, new RoutedEventArgs()]);
    }
}
