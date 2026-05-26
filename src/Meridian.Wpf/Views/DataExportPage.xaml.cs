using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for exporting collected market data and configuring integrations.
/// Code-behind is limited to:
///  – constructor DI and DataContext wiring
///  – PasswordBox read (WPF security restriction prevents binding Password)
/// All business logic lives in <see cref="DataExportViewModel"/>.
/// </summary>
public partial class DataExportPage : Page
{
    private readonly DataExportViewModel _viewModel;

    public DataExportPage()
    {
        InitializeComponent();
        _viewModel = new DataExportViewModel();
        DataContext = _viewModel;
    }

    // ── Database PasswordBox (non-bindable) ───────────────────────────────

    private void SetDatabaseCredentials_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordBox.Password;
        _viewModel.SetDatabaseCredentialsCommand.Execute(null);
    }

    private void TestDatabaseConnection_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordBox.Password;
        _viewModel.TestDatabaseConnectionCommand.Execute(null);
    }

    private void ConfigureDatabaseSync_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordBox.Password;
        _viewModel.ConfigureDatabaseSyncCommand.Execute(null);
    }
}
