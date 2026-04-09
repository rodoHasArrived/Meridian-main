using System.Windows;
using System.Windows.Input;

namespace Meridian.Wpf.Views;

/// <summary>
/// A generic floating window that hosts any registered Meridian page.
/// Used when a page is dragged to the "Float Window" drop zone in the split pane.
/// All visual chrome lives here; no business logic.
/// </summary>
public partial class FloatingPageWindow : Window
{
    public FloatingPageWindow(string pageTitle, FrameworkElement pageContent)
    {
        InitializeComponent();
        TitleTextBlock.Text = pageTitle;
        Title = pageTitle;
        PageContentPresenter.Content = pageContent;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
