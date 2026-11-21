using System;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Package manager page — thin code-behind.
/// All create/validate/import/list logic lives in <see cref="PackageManagerViewModel"/>.
/// </summary>
public partial class PackageManagerPage : Page
{
    private readonly PackageManagerViewModel _viewModel;

    public PackageManagerPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();

        _viewModel = new PackageManagerViewModel(
            Meridian.Ui.Services.PortablePackagerService.Instance,
            notificationService);
        DataContext = _viewModel;

        // Set default date range text on the input boxes (view-only default values)
        var today = DateTime.UtcNow;
        PackageFromInput.Text = today.AddDays(-30).ToString("yyyy-MM-dd");
        PackageToInput.Text = today.ToString("yyyy-MM-dd");
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (System.OperationCanceledException)
        {
            // Navigation cancelled the in-flight load before it completed; benign during teardown.
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "Page load cancelled during navigation.",
                ("page", GetType().Name));
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Package Manager page failed to load.", ex);
        }
    }

    private async void RefreshPackages_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => RefreshPackages_ClickCoreAsync(sender, e), nameof(RefreshPackages_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task RefreshPackages_ClickCoreAsync(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshPackagesCommand.ExecuteAsync(null);

    private async void CreatePackage_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => CreatePackage_ClickCoreAsync(sender, e), nameof(CreatePackage_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task CreatePackage_ClickCoreAsync(object sender, RoutedEventArgs e)
        => await _viewModel.CreatePackageAsync(
            PackageSymbolsInput.Text,
            PackageFromInput.Text,
            PackageToInput.Text);

    private async void ValidatePackage_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => ValidatePackage_ClickCoreAsync(sender, e), nameof(ValidatePackage_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task ValidatePackage_ClickCoreAsync(object sender, RoutedEventArgs e)
        => await _viewModel.ValidatePackageAsync(ValidatePackageInput.Text);

    private async void ImportPackage_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => ImportPackage_ClickCoreAsync(sender, e), nameof(ImportPackage_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task ImportPackage_ClickCoreAsync(object sender, RoutedEventArgs e)
        => await _viewModel.ImportPackageAsync(ImportPackageInput.Text);
}
