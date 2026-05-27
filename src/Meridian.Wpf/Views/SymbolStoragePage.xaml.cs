using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Symbol storage page showing per-symbol storage analytics, data type breakdown,
/// file counts, and growth projections.
/// </summary>
public partial class SymbolStoragePage : Page
{
    private readonly SymbolStorageViewModel _viewModel;

    public SymbolStoragePage()
        : this(new SymbolStorageViewModel())
    {
    }

    internal SymbolStoragePage(SymbolStorageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }
}
