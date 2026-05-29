using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public class InstitutionalShellFrameControl : UserControl
{
    public static readonly DependencyProperty ShellPostureProperty =
        DependencyProperty.Register(
            nameof(ShellPosture),
            typeof(ShellPosture),
            typeof(InstitutionalShellFrameControl),
            new PropertyMetadata(ShellPosture.Workbench));

    public ShellPosture ShellPosture
    {
        get => (ShellPosture)GetValue(ShellPostureProperty);
        set => SetValue(ShellPostureProperty, value);
    }
}

