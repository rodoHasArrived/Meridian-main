using System.Windows.Controls;

namespace Meridian.Wpf.Features.Reporting;

/// <summary>Read-only keyboard-accessible retained report amounts using the shared amount proof drawer.</summary>
public partial class ReportRetainedAmountsControl : UserControl
{
    public ReportRetainedAmountsControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as ReportRetainedAmountsViewModel)?.Suspend();
    }
}
