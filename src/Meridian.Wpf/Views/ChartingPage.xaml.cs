using System;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class ChartingPage : Page
{
    private readonly ChartingPageViewModel _viewModel;

    public ChartingPage(ChartingPageViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.Initialize();

    private void Symbol_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is SymbolItem item)
            _viewModel.OnSymbolChanged(item.Symbol);
    }

    private void Timeframe_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item
            && item.Tag is string tf && Enum.TryParse<ChartTimeframe>(tf, out var timeframe))
            _viewModel.OnTimeframeChanged(timeframe);
    }

    private void DatePicker_Changed(object? sender, EventArgs e)
    {
        DateOnly? from = FromDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(FromDatePicker.SelectedDate.Value) : null;
        DateOnly? to = ToDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(ToDatePicker.SelectedDate.Value) : null;
        _viewModel.OnDateChanged(from, to);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshChart();

    private void Indicator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string id)
            _viewModel.OnIndicatorToggled(id, cb.IsChecked == true);
    }
}
