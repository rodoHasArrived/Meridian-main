using System.Windows.Controls;

namespace Meridian.Wpf.Views;

/// <summary>
/// Reusable visual for the Security Master passport governed-write editor. Hosted both by
/// <see cref="SecurityPassportEditorPage"/> and inline on the Security Master page; it carries no
/// DataContext of its own and binds to whatever <c>SecurityPassportEditorViewModel</c> the host supplies.
/// </summary>
public partial class SecurityPassportEditorView : UserControl
{
    public SecurityPassportEditorView()
    {
        InitializeComponent();
    }
}
