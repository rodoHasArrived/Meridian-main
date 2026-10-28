using System;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Shell.Root;

/// <summary>
/// Applies desktop launch actions and determines whether those actions require the shell.
/// </summary>
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
