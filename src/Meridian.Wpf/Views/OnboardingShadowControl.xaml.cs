using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.Views;

/// <summary>Thin view over <see cref="OnboardingShadowWorkspace"/>; the only code-behind is the file picker.</summary>
public partial class OnboardingShadowControl : UserControl
{
    private const long MaxOpeningFileBytes = 16 * 1024 * 1024;

    public OnboardingShadowControl() => InitializeComponent();

    private void OnChooseOpeningFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OnboardingShadowWorkspace workspace)
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the incumbent opening-balance file",
            Filter = workspace.OpeningSourceKind == Meridian.Contracts.Workstation.OnboardingOpeningSourceKinds.AdministratorPack
                ? "Administrator opening pack (*.csv)|*.csv|All files|*.*"
                : "Bank statements (*.xml;*.camt;*.053;*.bai;*.bai2;*.txt)|*.xml;*.camt;*.053;*.bai;*.bai2;*.txt|All files|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        var file = new FileInfo(dialog.FileName);
        if (file.Length > MaxOpeningFileBytes)
        {
            MessageBox.Show(Window.GetWindow(this), "The opening-balance file is larger than 16 MB.", "Opening balances",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        workspace.SetOpeningFile(file.Name, File.ReadAllBytes(file.FullName));
    }
}
