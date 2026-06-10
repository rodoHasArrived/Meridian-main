using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class FinancialRecordExplorerPage : Page, IWorkspaceShellPageContextAware
{
    private readonly FinancialRecordExplorerViewModel _viewModel;

    public FinancialRecordExplorerPage()
        : this(new FinancialRecordExplorerViewModel(ApiClientService.Instance))
    {
    }

    public FinancialRecordExplorerPage(FinancialRecordExplorerViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    public void ApplyWorkspaceShellPageTag(string pageTag)
        => _viewModel.ApplyPageTag(pageTag);

    private void OnPageLoaded(object sender, RoutedEventArgs e)
        => _viewModel.Activate();

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
        => _viewModel.Dispose();
}
