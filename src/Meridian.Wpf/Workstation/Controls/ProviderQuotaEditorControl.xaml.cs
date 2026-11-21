using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Ui.Services.ProviderDiagnostics;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.Controls;

public partial class ProviderQuotaEditorControl : UserControl
{
    private readonly ProviderQuotaEditorViewModel _viewModel = new(new ProviderQuotaApiClient(ApiClientService.Instance));

    public ProviderQuotaEditorControl()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _viewModel.Cancel();
}
