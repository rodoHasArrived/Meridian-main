using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class HealthBadgeControl : UserControl
{
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(
            nameof(Badge),
            typeof(WorkstationBadgeModel),
            typeof(HealthBadgeControl),
            new PropertyMetadata(null, OnBadgeChanged));

    public HealthBadgeControl()
    {
        InitializeComponent();
    }

    public WorkstationBadgeModel? Badge
    {
        get => (WorkstationBadgeModel?)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    private static void OnBadgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HealthBadgeControl control)
        {
            control.ApplyTone();
        }
    }

    private void ApplyTone()
    {
        if (BadgeBorder is null || Badge is null)
        {
            return;
        }

        BadgeBorder.BorderBrush = ResolveBrush(Badge.Tone);
        BadgeBorder.Foreground = ResolveBrush(Badge.Tone);
    }

    private Brush ResolveBrush(string tone)
    {
        var key = tone switch
        {
            WorkspaceTone.Success => "SuccessColorBrush",
            WorkspaceTone.Warning => "WarningColorBrush",
            WorkspaceTone.Danger => "ErrorColorBrush",
            WorkspaceTone.Primary or WorkspaceTone.Info => "InfoColorBrush",
            _ => "ConsoleTextMutedBrush"
        };

        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
