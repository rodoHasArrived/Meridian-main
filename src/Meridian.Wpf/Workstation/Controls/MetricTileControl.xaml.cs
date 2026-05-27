using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class MetricTileControl : UserControl
{
    public static readonly DependencyProperty MetricProperty =
        DependencyProperty.Register(
            nameof(Metric),
            typeof(WorkstationMetricModel),
            typeof(MetricTileControl),
            new PropertyMetadata(null));

    public MetricTileControl()
    {
        InitializeComponent();
    }

    public WorkstationMetricModel? Metric
    {
        get => (WorkstationMetricModel?)GetValue(MetricProperty);
        set => SetValue(MetricProperty, value);
    }
}
