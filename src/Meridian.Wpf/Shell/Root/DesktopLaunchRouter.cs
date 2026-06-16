using System;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Shell.Root;

public sealed class DesktopLaunchRouter
{
    internal DesktopLaunchRequest Parse(string[] args) => DesktopLaunchArguments.Parse(args);

    public bool HasActions(string[] args) => Parse(args).HasActions;

    public bool RequiresShell(string[] args) => Parse(args).RequiresShell;

    public void Apply(string[] args, MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        viewModel.HandleLaunchArgs(args);
    }
}
