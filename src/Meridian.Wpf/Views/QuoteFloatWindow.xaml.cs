using System.Windows;
using System.Windows.Input;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Floating tear-off quote panel window — thin code-behind.
/// All live-data state lives in <see cref="QuoteFloatViewModel"/>.
/// </summary>
public partial class QuoteFloatWindow : Window
{
    private readonly QuoteFloatViewModel _vm;

    public QuoteFloatWindow(QuoteFloatViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;

        Closed += (_, _) => _vm.Dispose();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
