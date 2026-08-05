using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class EvidenceWorkbenchPage : Page
{
    private readonly EvidenceWorkbenchViewModel _viewModel;

    public EvidenceWorkbenchPage()
        : this(new EvidenceWorkbenchViewModel(new WpfServices.EvidenceWorkbenchApiClient(ApiClientService.Instance)))
    {
    }

    public EvidenceWorkbenchPage(EvidenceWorkbenchViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
        => _viewModel.Activate();

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
        => _viewModel.Dispose();
}
