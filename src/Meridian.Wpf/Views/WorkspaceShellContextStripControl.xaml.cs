using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public partial class WorkspaceShellContextStripControl : UserControl
{
    public static readonly DependencyProperty ShellContextProperty =
        DependencyProperty.Register(
            nameof(ShellContext),
            typeof(WorkspaceShellContext),
            typeof(WorkspaceShellContextStripControl),
            new PropertyMetadata(new WorkspaceShellContext()));

    public WorkspaceShellContextStripControl()
    {
        InitializeComponent();
    }

    public WorkspaceShellContext ShellContext
    {
        get => (WorkspaceShellContext)GetValue(ShellContextProperty);
        set => SetValue(ShellContextProperty, value);
    }
}
