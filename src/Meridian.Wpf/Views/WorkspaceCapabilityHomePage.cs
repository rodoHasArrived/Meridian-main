using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public interface IWorkspaceShellPageContextAware
{
    void ApplyWorkspaceShellPageTag(string pageTag);
}

public sealed class WorkspaceCapabilityHomePage : Page, IWorkspaceShellPageContextAware
{
    private readonly Meridian.Wpf.Services.NavigationService? _navigationService;
    private string _pageTag = "PortfolioShell";

    public WorkspaceCapabilityHomePage()
        : this(null)
    {
    }

    public WorkspaceCapabilityHomePage(Meridian.Wpf.Services.NavigationService? navigationService)
    {
        _navigationService = navigationService;
        Background = Brushes.Transparent;
        BuildContent();
    }

    public void ApplyWorkspaceShellPageTag(string pageTag)
    {
        if (!string.IsNullOrWhiteSpace(pageTag))
        {
            _pageTag = ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        }

        BuildContent();
    }

    private void BuildContent()
    {
        var descriptor = ShellNavigationCatalog.GetPage(_pageTag)
            ?? ShellNavigationCatalog.GetPage("PortfolioShell");
        var workspace = ShellNavigationCatalog.GetWorkspace(descriptor?.WorkspaceId)
            ?? ShellNavigationCatalog.GetDefaultWorkspace();

        var pages = ShellNavigationCatalog.GetPagesForWorkspace(workspace.Id)
            .Where(page => !string.Equals(page.PageTag, workspace.HomePageTag, StringComparison.OrdinalIgnoreCase))
            .OrderBy(page => page.VisibilityTier)
            .ThenBy(page => page.Order)
            .Take(12)
            .ToArray();

        var root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24)
        };

        var panel = new StackPanel
        {
            MaxWidth = 1180
        };
        root.Content = panel;

        panel.Children.Add(new TextBlock
        {
            Text = descriptor?.Title ?? workspace.Title,
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        panel.Children.Add(new TextBlock
        {
            Text = descriptor?.Subtitle ?? workspace.Description,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            Margin = new Thickness(0, 0, 0, 24)
        });

        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(grid);

        foreach (var page in pages)
        {
            grid.Children.Add(CreatePageButton(page));
        }

        Content = root;
    }

    private Button CreatePageButton(ShellPageDescriptor page)
    {
        var title = new TextBlock
        {
            Text = page.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var detail = new TextBlock
        {
            Text = page.Subtitle,
            FontSize = 12,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(detail);

        var button = new Button
        {
            Content = panel,
            Tag = page.PageTag,
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 12, 12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 96
        };
        button.Click += OnPageButtonClick;

        return button;
    }

    private void OnPageButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string pageTag })
        {
            (_navigationService ?? Meridian.Wpf.Services.NavigationService.Instance).NavigateTo(pageTag);
        }
    }
}
