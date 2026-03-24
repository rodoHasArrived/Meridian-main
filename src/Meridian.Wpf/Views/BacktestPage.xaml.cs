using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Backtesting page — three-panel layout with configuration, equity curve, and results tabs.
/// </summary>
public partial class BacktestPage : Page
{
    private readonly BacktestViewModel _viewModel = new();

    public BacktestPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.EquityCurvePoints.CollectionChanged += (_, _) => RenderEquityCurve();
        Loaded -= OnPageLoaded;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.Dispose();

    /// <summary>
    /// Re-renders the equity curve Polyline from the current EquityCurvePoints collection.
    /// Called on the UI thread whenever a new progress event is dispatched.
    /// </summary>
    private void RenderEquityCurve()
    {
        var points = _viewModel.EquityCurvePoints;
        if (points.Count < 2) return;

        var w = EquityCanvas.ActualWidth;
        var h = EquityCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var minVal = points.Min(p => p.Value);
        var maxVal = points.Max(p => p.Value);
        var range = maxVal - minVal;
        if (range < 1.0) range = 1.0;

        var ptCount = points.Count;
        var collection = new PointCollection(ptCount);
        for (var i = 0; i < ptCount; i++)
        {
            var x = (double)i / (ptCount - 1) * w;
            var y = h - (points[i].Value - minVal) / range * (h - 8) - 4;
            collection.Add(new Point(x, y));
        }
        EquityPolyline.Points = collection;
    }
}
