using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Lean Integration page — thin code-behind.
/// All state, business logic, and timer management live in <see cref="LeanIntegrationViewModel"/>.
/// Code-behind is limited to lifecycle wiring, folder/file dialogs (platform UI), and dialog display.
/// </summary>
public partial class LeanIntegrationPage : Page
{
    private readonly LeanIntegrationViewModel _viewModel;

    public LeanIntegrationPage()
    {
        InitializeComponent();
        _viewModel = new LeanIntegrationViewModel(LeanIntegrationService.Instance);
        _viewModel.DialogRequested += OnDialogRequested;
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.Start();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.Dispose();

    // ── Folder / file browse dialogs (platform UI – not business logic) ──────────

    private void BrowseLeanPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Lean Engine Path" };
        if (dialog.ShowDialog() == true)
            _viewModel.LeanPath = dialog.FolderName;
    }

    private void BrowseDataPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Lean Data Path" };
        if (dialog.ShowDialog() == true)
            _viewModel.DataPath = dialog.FolderName;
    }

    private void BrowseAutoExportPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Auto-Export Data Path" };
        if (dialog.ShowDialog() == true)
            _viewModel.AutoExportLeanDataPath = dialog.FolderName;
    }

    private void BrowseIngestFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Lean Backtest Results File",
            Filter = "JSON files|*.json|All files|*.*"
        };
        if (dialog.ShowDialog() == true)
            _viewModel.IngestFilePath = dialog.FileName;
    }

    // ── Dialog display (delegated from ViewModel via event) ───────────────────────

    private void OnDialogRequested(object? sender, LeanDialogRequestArgs args)
    {
        var image = args.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning;
        MessageBox.Show(args.Message, args.Title, MessageBoxButton.OK, image);
    }
}
