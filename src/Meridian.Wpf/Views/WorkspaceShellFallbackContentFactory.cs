using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Meridian.Wpf.Views;

internal sealed class WorkspaceShellFallbackContent : Border
{
}

internal static class WorkspaceShellFallbackContentFactory
{
    public static FrameworkElement CreateDockFailureContent(string pageTitle, Exception ex)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(20)
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Workflow unavailable",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });

        stack.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = pageTitle,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.WhiteSmoke
        });

        stack.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = ex.Message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gainsboro
        });

        stack.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Text = "The shell stayed active so you can continue using other workspace tools.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gainsboro,
            Opacity = 0.82
        });

        return new WorkspaceShellFallbackContent
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 37, 52)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(177, 93, 74)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = stack
        };
    }

    internal static bool IsFallbackContent(object? content) => content is WorkspaceShellFallbackContent;
}
