using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for administrative and maintenance operations including
/// archive scheduling, tier migration, retention policies, and cleanup.
/// MVVM compliant: all state lives in <see cref="AdminMaintenanceViewModel"/>;
/// the XAML binds to it directly. Code-behind contains only constructor DI wiring
/// and minimal event-handler delegation.
/// </summary>
public partial class AdminMaintenancePage : Page
{
    private readonly AdminMaintenanceViewModel _viewModel;

    public AdminMaintenancePage(IAdminMaintenanceService adminService, IWorkstationBondCutoverApiClient bondCutoverClient)
    {
        InitializeComponent();
        _viewModel = new AdminMaintenanceViewModel(adminService, bondCutoverClient);
        DataContext = _viewModel;

        Loaded += AdminMaintenancePage_Loaded;
    }

    private async void AdminMaintenancePage_Loaded(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => AdminMaintenancePage_LoadedCoreAsync(sender, e), nameof(AdminMaintenancePage_Loaded)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task AdminMaintenancePage_LoadedCoreAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    // ---- Schedule controls ----

    private async void EnableSchedule_Toggled(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => EnableSchedule_ToggledCoreAsync(sender, e), nameof(EnableSchedule_Toggled)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task EnableSchedule_ToggledCoreAsync(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsInitialized)
            await _viewModel.SaveScheduleAsync();
    }

    private async void ScheduleFrequency_Changed(object sender, SelectionChangedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => ScheduleFrequency_ChangedCoreAsync(sender, e), nameof(ScheduleFrequency_Changed)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task ScheduleFrequency_ChangedCoreAsync(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.IsInitialized)
            await _viewModel.SaveScheduleAsync();
    }

    private async void RunMaintenance_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => RunMaintenance_ClickCoreAsync(sender, e), nameof(RunMaintenance_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task RunMaintenance_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.RunMaintenanceNowAsync();
    }

    private async void QuickCheck_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => QuickCheck_ClickCoreAsync(sender, e), nameof(QuickCheck_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task QuickCheck_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.RunQuickCheckAsync();
    }

    // ---- Tier ----

    private async void MigrateNow_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => MigrateNow_ClickCoreAsync(sender, e), nameof(MigrateNow_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task MigrateNow_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will compress and migrate older data to the archive tier. Continue?",
            "Migrate to Archive",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.MigrateToArchiveAsync();
        }
    }

    // ---- Retention policies ----

    private async void AddPolicy_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => AddPolicy_ClickCoreAsync(sender, e), nameof(AddPolicy_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task AddPolicy_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        _viewModel.NotifyAddPolicy();
        await _viewModel.LoadRetentionPoliciesAsync();
    }

    private void EditPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string policyId)
        {
            _viewModel.NotifyEditPolicy(policyId);
        }
    }

    private async void DeletePolicy_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => DeletePolicy_ClickCoreAsync(sender, e), nameof(DeletePolicy_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task DeletePolicy_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string policyId)
        {
            var result = MessageBox.Show(
                "Are you sure you want to delete this policy?",
                "Delete Policy",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _viewModel.DeletePolicyAsync(policyId);
            }
        }
    }

    private async void ApplyRetention_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => ApplyRetention_ClickCoreAsync(sender, e), nameof(ApplyRetention_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task ApplyRetention_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will delete data older than the retention period. This action cannot be undone. Continue?",
            "Apply Retention Policies",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.ApplyRetentionPoliciesAsync();
        }
    }

    // ---- Cleanup ----

    // ---- InfoBar ----

    private void CloseInfoBar_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissStatus();
    }
}
