using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for administrative and maintenance operations including
/// archive scheduling, tier migration, retention policies, and cleanup.
/// </summary>
public partial class AdminMaintenancePage : Page
{
    private readonly AdminMaintenanceViewModel _viewModel;
    private bool _isLoaded;

    public AdminMaintenancePage(AdminMaintenanceServiceBase adminService)
    {
        InitializeComponent();
        _viewModel = new AdminMaintenanceViewModel(adminService);
        DataContext = _viewModel;

        // Wire ObservableCollections to named controls (transitional until XAML uses {Binding})
        QuickCheckList.ItemsSource = _viewModel.QuickCheckItems;
        TiersList.ItemsSource = _viewModel.TierItems;
        PoliciesList.ItemsSource = _viewModel.PolicyItems;
        CleanupFilesList.ItemsSource = _viewModel.CleanupItems;
        HistoryList.ItemsSource = _viewModel.HistoryItems;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += AdminMaintenancePage_Loaded;
    }

    private async void AdminMaintenancePage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        await _viewModel.InitializeAsync();
        SyncScheduleControls();
    }

    // ---- Schedule controls ----

    private async void EnableSchedule_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            _viewModel.ScheduleEnabled = EnableScheduleToggle.IsChecked == true;
            await _viewModel.SaveScheduleAsync();
        }
    }

    private async void ScheduleFrequency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded)
        {
            _viewModel.CronExpression = (ScheduleFrequencyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0 2 * * *";
            await _viewModel.SaveScheduleAsync();
        }
    }

    private async void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        ReadScheduleCheckboxes();
        await _viewModel.SaveScheduleAsync();
        _viewModel.ShowSuccess("Schedule saved successfully.");
    }

    private async void RunMaintenance_Click(object sender, RoutedEventArgs e)
    {
        ReadScheduleCheckboxes();
        await _viewModel.RunMaintenanceNowAsync();
    }

    private async void QuickCheck_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RunQuickCheckAsync();
    }

    // ---- Tier ----

    private async void MigrateNow_Click(object sender, RoutedEventArgs e)
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

    private async void PreviewCleanup_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PreviewCleanupAsync();
    }

    private async void ExecuteCleanup_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will permanently delete the listed files. Continue?",
            "Execute Cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.ExecuteCleanupAsync();
        }
    }

    // ---- InfoBar ----

    private void CloseInfoBar_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissStatus();
    }

    // ---- ViewModel → named-control sync (transitional bridge) ----

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AdminMaintenanceViewModel.ScheduleEnabled):
                EnableScheduleToggle.IsChecked = _viewModel.ScheduleEnabled;
                break;

            case nameof(AdminMaintenanceViewModel.NextRunText):
                NextRunText.Text = _viewModel.NextRunText;
                break;

            case nameof(AdminMaintenanceViewModel.LastRunText):
                LastRunText.Text = _viewModel.LastRunText;
                break;

            case nameof(AdminMaintenanceViewModel.IsQuickCheckBusy):
                QuickCheckButton.IsEnabled = !_viewModel.IsQuickCheckBusy;
                break;

            case nameof(AdminMaintenanceViewModel.IsMaintenanceBusy):
                RunMaintenanceButton.IsEnabled = !_viewModel.IsMaintenanceBusy;
                break;

            case nameof(AdminMaintenanceViewModel.IsQuickCheckResultsVisible):
                QuickCheckResultsCard.Visibility = _viewModel.IsQuickCheckResultsVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdminMaintenanceViewModel.QuickCheckIcon):
                QuickCheckIcon.Text = _viewModel.QuickCheckIcon;
                QuickCheckIcon.Foreground = new SolidColorBrush(_viewModel.QuickCheckIconColor);
                break;

            case nameof(AdminMaintenanceViewModel.QuickCheckStatusText):
                QuickCheckStatusText.Text = _viewModel.QuickCheckStatusText;
                QuickCheckOverallText.Text = _viewModel.QuickCheckOverallText;
                break;

            case nameof(AdminMaintenanceViewModel.IsCleanupResultsVisible):
                CleanupResultsCard.Visibility = _viewModel.IsCleanupResultsVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdminMaintenanceViewModel.CleanupFilesText):
                CleanupFilesText.Text = _viewModel.CleanupFilesText;
                CleanupSizeText.Text = _viewModel.CleanupSizeText;
                break;

            case nameof(AdminMaintenanceViewModel.IsStatusVisible):
                StatusInfoBar.Visibility = _viewModel.IsStatusVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdminMaintenanceViewModel.StatusMessage):
                StatusInfoIcon.Text = _viewModel.StatusIcon;
                StatusInfoIcon.Foreground = new SolidColorBrush(_viewModel.StatusColor);
                StatusInfoTitle.Text = _viewModel.StatusTitle;
                StatusInfoMessage.Text = _viewModel.StatusMessage;
                break;
        }
    }

    // ---- Helpers ----

    private void ReadScheduleCheckboxes()
    {
        _viewModel.RunCompression = RunCompressionCheck.IsChecked == true;
        _viewModel.RunCleanup = RunCleanupCheck.IsChecked == true;
        _viewModel.RunIntegrityCheck = RunIntegrityCheck.IsChecked == true;
        _viewModel.RunTierMigration = RunTierMigrationCheck.IsChecked == true;
    }

    private void SyncScheduleControls()
    {
        EnableScheduleToggle.IsChecked = _viewModel.ScheduleEnabled;

        // Select matching cron item
        foreach (ComboBoxItem item in ScheduleFrequencyCombo.Items)
        {
            if (item.Tag?.ToString() == _viewModel.CronExpression)
            {
                ScheduleFrequencyCombo.SelectedItem = item;
                break;
            }
        }

        RunCompressionCheck.IsChecked = _viewModel.RunCompression;
        RunCleanupCheck.IsChecked = _viewModel.RunCleanup;
        RunIntegrityCheck.IsChecked = _viewModel.RunIntegrityCheck;
        RunTierMigrationCheck.IsChecked = _viewModel.RunTierMigration;
    }
}

