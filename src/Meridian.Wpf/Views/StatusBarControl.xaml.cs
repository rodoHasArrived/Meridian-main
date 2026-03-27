namespace Meridian.Wpf.Views;

/// <summary>
/// StatusBarControl: persistent status bar showing live system state.
/// Code-behind: minimal, only InitializeComponent().
/// All state and logic is in StatusBarViewModel.
/// </summary>
public partial class StatusBarControl : System.Windows.Controls.UserControl
{
    public StatusBarControl()
    {
        InitializeComponent();
    }
}
