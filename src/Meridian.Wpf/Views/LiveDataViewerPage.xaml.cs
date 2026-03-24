using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Live data viewer page — thin code-behind.
/// All state, timers, HTTP loading, and session statistics live in
/// <see cref="LiveDataViewerViewModel"/>.
/// </summary>
public partial class LiveDataViewerPage : Page
{
    private readonly LiveDataViewerViewModel _vm;
    private readonly Meridian.Wpf.Services.NavigationService _navigationService;

    public LiveDataViewerPage(
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();
        _navigationService = Meridian.Wpf.Services.NavigationService.Instance;

        _vm = new LiveDataViewerViewModel(statusService, connectionService, loggingService, notificationService);
        DataContext = _vm;

        LiveFeedList.ItemsSource = _vm.LiveEvents;
        SymbolComboBox.ItemsSource = _vm.AvailableSymbols;

        _vm.AutoScrollRequested += (_, _) =>
        {
            if (_vm.LiveEvents.Count > 0)
                LiveFeedList.ScrollIntoView(_vm.LiveEvents[^1]);
        };

        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _vm.StartAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _vm.Dispose();

    private void Symbol_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SymbolComboBox.SelectedItem is string symbol)
            _vm.SelectSymbol(symbol);
    }

    private void AddSymbol_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddSymbolDialog();
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Symbol))
        {
            var symbol = dialog.Symbol.ToUpperInvariant().Trim();
            _vm.AddSymbolToList(symbol);
            SymbolComboBox.ItemsSource = null;
            SymbolComboBox.ItemsSource = _vm.AvailableSymbols;
            SymbolComboBox.SelectedItem = symbol;
        }
    }

    private void PauseResume_Click(object sender, RoutedEventArgs e) =>
        _vm.PauseResume();

    private void Clear_Click(object sender, RoutedEventArgs e) =>
        _vm.Clear();

    private void OpenStrategyRuns_Click(object sender, RoutedEventArgs e) =>
        _navigationService.NavigateTo("StrategyRuns");
}

/// <summary>Dialog for adding a new symbol to watch.</summary>
public sealed class AddSymbolDialog : Window
{
    private readonly TextBox _symbolBox;

    public string Symbol => _symbolBox.Text;

    public AddSymbolDialog()
    {
        Title = "Add Symbol";
        Width = 300;
        Height = 130;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Symbol:", Margin = new Thickness(0, 0, 0, 4) });

        _symbolBox = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(_symbolBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "Add", IsDefault = true, MinWidth = 80, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
        okBtn.Click += (_, _) => { DialogResult = true; };
        var cancelBtn = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(8, 4, 8, 4) };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        Content = panel;
    }
}
