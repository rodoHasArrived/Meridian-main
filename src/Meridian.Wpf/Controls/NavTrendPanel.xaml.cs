using System.Windows.Controls;

namespace Meridian.Wpf.Controls;

/// <summary>
/// Shows ledger NAV and securities exposure at recent month-ends as charts with a data table, why
/// there is no trend when there is not, a retry after a failure, and the source's notes.
/// Bind its <c>DataContext</c> to a <see cref="ViewModels.Comparisons.NavTrendSectionViewModel"/>.
/// </summary>
public partial class NavTrendPanel : UserControl
{
    public NavTrendPanel()
    {
        InitializeComponent();
    }
}
