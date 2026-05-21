using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class RoutingMatrixControl : UserControl
{
    public static readonly DependencyProperty MatrixProperty =
        DependencyProperty.Register(
            nameof(Matrix),
            typeof(RoutingMatrixModel),
            typeof(RoutingMatrixControl),
            new PropertyMetadata(null));

    public RoutingMatrixControl()
    {
        InitializeComponent();
    }

    public RoutingMatrixModel? Matrix
    {
        get => (RoutingMatrixModel?)GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }
}
