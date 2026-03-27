using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Contracts.Archive;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

using Meridian.Wpf.Services;
namespace Meridian.Wpf.Views;

/// <summary>
/// Page for archive health monitoring.
/// </summary>
public sealed partial class ArchiveHealthPage : Page
{
    private readonly WpfServices.ArchiveHealthService _healthService;
    private readonly WpfServices.SchemaService _schemaService;
    private readonly ObservableCollection<IssueDisplayItem> _issues;
    private readonly ObservableCollection<string> _recommendations;

    public ArchiveHealthPage(
        WpfServices.ArchiveHealthService healthService,
        WpfServices.SchemaService schemaService)
    {
        InitializeComponent();

        _healthService = healthService;
        _schemaService = schemaService;
        _issues = new ObservableCollection<IssueDisplayItem>();
        _recommendations = new ObservableCollection<string>();

        IssuesList.ItemsSource = _issues;
        RecommendationsList.ItemsSource = _recommendations;

        Loaded += Page_Loaded;

        _healthService.HealthStatusUpdated += OnHealthStatusUpdated;
        _healthService.VerificationCompleted += OnVerificationCompleted;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadHealthStatusAsync();
        await LoadDictionaryStatusAsync();
    }

    private async Task LoadHealthStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _healthService.GetHealthStatusAsync();
            UpdateHealthDisplay(status);
        }
        catch (Exception ex)
        {
            ShowInfoBar("Error", $"Failed to load health status: {ex.Message}", isError: true);
        }
    }

    private void UpdateHealthDisplay(ArchiveHealthStatus status)
    {
        HealthScoreText.Text = $"{status.OverallHealthScore:F0}%";

        HealthStatusText.Text = status.Status;
        HealthStatusBadge.Background = status.Status switch
        {
            "Healthy" => new SolidColorBrush(Color.FromRgb(72, 187, 120)),
            "Warning" => new SolidColorBrush(Color.FromRgb(237, 137, 54)),
            "Critical" => new SolidColorBrush(Color.FromRgb(245, 101, 101)),
            _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
        };

        var healthColor = status.OverallHealthScore switch
        {
            >= 90 => new SolidColorBrush(Color.FromRgb(72, 187, 120)),
            >= 70 => new SolidColorBrush(Color.FromRgb(237, 137, 54)),
            _ => new SolidColorBrush(Color.FromRgb(245, 101, 101))
        };
        HealthScoreText.Foreground = healthColor;

        TotalFilesText.Text = status.TotalFiles.ToString("N0");
        VerifiedFilesText.Text = status.VerifiedFiles.ToString("N0");
        PendingFilesText.Text = status.PendingFiles.ToString("N0");
        FailedFilesText.Text = status.FailedFiles.ToString("N0");

        if (status.LastFullVerificationAt.HasValue)
        {
            var elapsed = DateTime.UtcNow - status.LastFullVerificationAt.Value;
            var elapsedText = elapsed.TotalDays >= 1
                ? $"{(int)elapsed.TotalDays} days ago"
                : elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours} hours ago"
                    : $"{(int)elapsed.TotalMinutes} minutes ago";

            LastVerificationText.Text = $"Last full verification: {elapsedText}";

            if (status.LastVerificationDurationMinutes.HasValue)
            {
                LastVerificationText.Text += $" (took {status.LastVerificationDurationMinutes}m)";
            }
        }
        else
        {
            LastVerificationText.Text = "Last full verification: Never";
        }

        if (status.StorageHealthInfo != null)
        {
            var storage = status.StorageHealthInfo;
            TotalCapacityText.Text = FormatHelpers.FormatBytes(storage.TotalCapacity);
            UsedSpaceText.Text = FormatHelpers.FormatBytes(storage.TotalCapacity - storage.FreeSpace);
            FreeSpaceText.Text = FormatHelpers.FormatBytes(storage.FreeSpace);
            DaysUntilFullText.Text = storage.DaysUntilFull?.ToString() ?? "--";
            DriveTypeText.Text = $"Drive Type: {storage.DriveType}";

            StorageUsageBar.Value = storage.UsedPercent;
            StorageUsagePercent.Text = $"{storage.UsedPercent:F1}%";

            StorageUsageBar.Foreground = storage.UsedPercent switch
            {
                >= 95 => new SolidColorBrush(Color.FromRgb(245, 101, 101)),
                >= 85 => new SolidColorBrush(Color.FromRgb(237, 137, 54)),
                _ => new SolidColorBrush(Color.FromRgb(72, 187, 120))
            };
        }

        _issues.Clear();
        if (status.Issues != null)
        {
            foreach (var issue in status.Issues.Where(i => i.ResolvedAt == null))
            {
                _issues.Add(new IssueDisplayItem(issue));
            }
        }

        IssueCountBadge.Visibility = _issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IssueCountText.Text = _issues.Count.ToString();
        NoIssuesText.Visibility = _issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _recommendations.Clear();
        if (status.Recommendations != null)
        {
            foreach (var rec in status.Recommendations)
            {
                _recommendations.Add(rec);
            }
        }

        NoRecommendationsText.Visibility = _recommendations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadDictionaryStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var dictionary = await _schemaService.GetDataDictionaryAsync();
            DictionaryStatusText.Text = $"Last generated: {dictionary.GeneratedAt:g} UTC ({dictionary.Schemas.Count} schemas)";
        }
        catch
        {
            DictionaryStatusText.Text = "Last generated: Never";
        }
    }

    private async void VerifyAll_Click(object sender, RoutedEventArgs e)
    {
        VerifyAllButton.IsEnabled = false;
        QuickCheckButton.IsEnabled = false;
        VerificationProgress.Visibility = Visibility.Visible;
        VerificationStatusText.Visibility = Visibility.Visible;

        var progress = new Progress<VerificationProgress>(p =>
        {
            VerificationProgress.Value = p.ProgressPercent;
            VerificationStatusText.Text = $"Verifying {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles}) - {p.FilesPerSecond:F1} files/s";

            if (p.EstimatedTimeRemainingSeconds.HasValue)
            {
                var eta = TimeSpan.FromSeconds(p.EstimatedTimeRemainingSeconds.Value);
                VerificationStatusText.Text += $" - ETA: {eta:mm\\:ss}";
            }
        });

        try
        {
            var job = await _healthService.StartFullVerificationAsync(progress);

            if (job.FailedFiles > 0)
            {
                ShowInfoBar("Verification Complete",
                    $"Verified {job.ProcessedFiles:N0} files. {job.FailedFiles:N0} files failed verification.",
                    isError: false);
            }
            else
            {
                ShowInfoBar("Verification Complete",
                    $"All {job.ProcessedFiles:N0} files verified successfully!",
                    isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Verification Failed", ex.Message, isError: true);
        }
        finally
        {
            VerifyAllButton.IsEnabled = true;
            QuickCheckButton.IsEnabled = true;
            VerificationProgress.Visibility = Visibility.Collapsed;
            VerificationStatusText.Visibility = Visibility.Collapsed;
            await LoadHealthStatusAsync();
        }
    }

    private async void QuickCheck_Click(object sender, RoutedEventArgs e)
    {
        QuickCheckButton.IsEnabled = false;
        VerificationProgress.Visibility = Visibility.Visible;
        VerificationProgress.IsIndeterminate = true;
        VerificationStatusText.Visibility = Visibility.Visible;
        VerificationStatusText.Text = "Running quick verification on recent files...";

        try
        {
            var since = DateTime.UtcNow.AddDays(-7);
            var job = await _healthService.StartIncrementalVerificationAsync(since);

            ShowInfoBar("Quick Check Complete",
                $"Checked {job.TotalFiles:N0} recent files. Failed: {job.FailedFiles:N0}",
                isError: false);
        }
        catch (Exception ex)
        {
            ShowInfoBar("Quick Check Failed", ex.Message, isError: true);
        }
        finally
        {
            QuickCheckButton.IsEnabled = true;
            VerificationProgress.Visibility = Visibility.Collapsed;
            VerificationProgress.IsIndeterminate = false;
            VerificationStatusText.Visibility = Visibility.Collapsed;
            await LoadHealthStatusAsync();
        }
    }

    private async void VerifyAuditChain_Click(object sender, RoutedEventArgs e)
    {
        VerifyAuditChainButton.IsEnabled = false;
        AuditChainStatusText.Text = "Verifying audit chain...";
        AuditChainIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(237, 137, 54));

        try
        {
            var result = await _healthService.VerifyAuditChainAsync();

            if (result.IsValid)
            {
                AuditChainStatusText.Text = $"✓ Audit chain valid ({result.EntriesChecked} entries verified)";
                AuditChainStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 187, 120));
                AuditChainIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 187, 120));
                ShowInfoBar("Audit Chain Valid", $"✓ Compliance audit chain is intact. {result.EntriesChecked} entries verified.", isError: false);
            }
            else
            {
                AuditChainStatusText.Text = $"⚠ TAMPER DETECTED at {result.FirstTamperPath ?? "unknown location"}";
                AuditChainStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 101, 101));
                AuditChainIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 101, 101));
                ShowInfoBar("⚠ COMPLIANCE ALERT: Tampering Detected",
                    $"Audit chain integrity violation detected at: {result.FirstTamperPath ?? "unknown"}. " +
                    $"Detected at: {result.TamperedAt:O}. Immediate investigation required.",
                    isError: true);
            }
        }
        catch (Exception ex)
        {
            AuditChainStatusText.Text = $"✗ Verification failed: {ex.Message}";
            AuditChainStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 101, 101));
            AuditChainIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 101, 101));
            ShowInfoBar("Audit Chain Verification Failed", ex.Message, isError: true);
        }
        finally
        {
            VerifyAuditChainButton.IsEnabled = true;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadHealthStatusAsync();
    }

    private async void ResolveIssue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is IssueDisplayItem item)
        {
            try
            {
                await _healthService.ResolveIssueAsync(item.Id);
                await LoadHealthStatusAsync();
            }
            catch (Exception ex)
            {
                ShowInfoBar("Failed to resolve issue", ex.Message, isError: true);
            }
        }
    }

    private async void GenerateDictionary_Click(object sender, RoutedEventArgs e)
    {
        GenerateDictionaryButton.IsEnabled = false;

        try
        {
            var dictionary = await _schemaService.GenerateDataDictionaryAsync();
            DictionaryStatusText.Text = $"Generated: {dictionary.GeneratedAt:g} UTC ({dictionary.Schemas.Count} schemas)";
            ShowInfoBar("Dictionary Generated",
                $"Data dictionary generated successfully with {dictionary.Schemas.Count} schemas.",
                isError: false);
        }
        catch (Exception ex)
        {
            ShowInfoBar("Generation Failed", ex.Message, isError: true);
        }
        finally
        {
            GenerateDictionaryButton.IsEnabled = true;
        }
    }

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var markdown = await _schemaService.GenerateMarkdownDocumentationAsync();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
                FileName = "DATA_DICTIONARY",
                DefaultExt = ".md"
            };

            if (dialog.ShowDialog() == true)
            {
                await System.IO.File.WriteAllTextAsync(dialog.FileName, markdown);
                ShowInfoBar("Export Complete", $"Data dictionary saved to {dialog.FileName}", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Export Failed", ex.Message, isError: true);
        }
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = await _schemaService.ExportDataDictionaryAsync("json");

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "data_dictionary",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
                ShowInfoBar("Export Complete", $"Data dictionary saved to {dialog.FileName}", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Export Failed", ex.Message, isError: true);
        }
    }

    private void CloseInfoBar_Click(object sender, RoutedEventArgs e)
    {
        StatusInfoBar.Visibility = Visibility.Collapsed;
    }

    private void ShowInfoBar(string title, string message, bool isError)
    {
        StatusInfoBar.Visibility = Visibility.Visible;
        StatusInfoBar.Background = isError
            ? new SolidColorBrush(Color.FromArgb(40, 245, 101, 101))
            : new SolidColorBrush(Color.FromArgb(40, 72, 187, 120));
        StatusIcon.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(245, 101, 101))
            : new SolidColorBrush(Color.FromRgb(72, 187, 120));
        StatusIcon.Text = isError ? "\uE7BA" : "\uE73E";
        StatusTitle.Text = title;
        StatusMessage.Text = message;
    }

    private void OnHealthStatusUpdated(object? sender, ArchiveHealthEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Status != null)
            {
                UpdateHealthDisplay(e.Status);
            }
        });
    }

    private void OnVerificationCompleted(object? sender, VerificationJobEventArgs e)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            await LoadHealthStatusAsync();
        });
    }

}

public sealed class IssueDisplayItem
{
    private readonly ArchiveIssue _issue;

    public IssueDisplayItem(ArchiveIssue issue)
    {
        _issue = issue;
    }

    public string Id => _issue.Id;
    public string Message => _issue.Message;
    public string? SuggestedAction => _issue.SuggestedAction;
    public string Severity => _issue.Severity;
    public bool IsAutoFixable => _issue.IsAutoFixable;

    public Brush SeverityColor => Severity switch
    {
        "Critical" => new SolidColorBrush(Color.FromRgb(245, 101, 101)),
        "Warning" => new SolidColorBrush(Color.FromRgb(237, 137, 54)),
        _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
    };

    public Visibility IsAutoFixableVisibility => IsAutoFixable ? Visibility.Visible : Visibility.Collapsed;
}
