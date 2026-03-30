using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// QualityArchivePage: WPF page for perpetual symbol quality calendar heatmap.
/// Code-behind is thin: only initialization and constructor DI.
/// </summary>
public partial class QualityArchivePage : Page
{
    public QualityArchivePage(QualityArchiveViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
