using System.Windows.Controls;

namespace Meridian.Wpf.Controls;

/// <summary>
/// Shows a prior-period comparison section: the windows compared, the comparison table with material
/// variances flagged, why it is empty when it is, a retry after a failure, and the source's notes.
/// Bind its <c>DataContext</c> to a <see cref="ViewModels.Comparisons.PeriodComparisonSectionViewModel"/>.
/// </summary>
public partial class PeriodComparisonPanel : UserControl
{
    public PeriodComparisonPanel()
    {
        InitializeComponent();
    }
}
