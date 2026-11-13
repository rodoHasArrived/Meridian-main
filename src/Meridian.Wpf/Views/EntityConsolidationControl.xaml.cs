using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Views;

public partial class EntityConsolidationControl : UserControl
{
    public EntityConsolidationControl() => InitializeComponent();
    public event RoutedEventHandler? ReviewDraftsRequested;
    private void OnReviewDraftsClick(object sender, RoutedEventArgs e) => ReviewDraftsRequested?.Invoke(this, e);
}
