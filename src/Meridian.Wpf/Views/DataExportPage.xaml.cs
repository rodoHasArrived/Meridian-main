using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for exporting collected market data and configuring integrations.
/// Code-behind is limited to:
///  – constructor DI and DataContext wiring
///  – secret input read for the database password
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

    // ── Database secret input ─────────────────────────────────────────────

    private void SetDatabaseCredentials_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordInput.Secret;
        _viewModel.SetDatabaseCredentialsCommand.Execute(null);
    }

    private void TestDatabaseConnection_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordInput.Secret;
        _viewModel.TestDatabaseConnectionCommand.Execute(null);
    }

    private void ConfigureDatabaseSync_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordInput.Secret;
        _viewModel.ConfigureDatabaseSyncCommand.Execute(null);
    }
}
