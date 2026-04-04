using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

/// <summary>
/// Dialog for editing or deleting a scheduled backfill job.
/// Replaces the former imperative <c>EditScheduledJobDialog</c> Window class
/// that was embedded in <c>BackfillPage.xaml.cs</c>.
/// </summary>
public partial class EditScheduledJobDialog : Window
{
    /// <summary>Gets the (possibly updated) job name.</summary>
    public string JobName => NameBox.Text;

    /// <summary>Gets the computed next-run display text based on frequency and time selections.</summary>
    public string NextRunText { get; private set; } = string.Empty;

    /// <summary>Gets the selected frequency tag.</summary>
    public string FrequencyTag => (FrequencyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";

    /// <summary>Gets the selected local run time.</summary>
    public string RunTimeText => TimeCombo.SelectedItem?.ToString() ?? "06:00";

    /// <summary>Gets whether the user requested deletion of this job.</summary>
    public bool ShouldDelete { get; private set; }

    public EditScheduledJobDialog(ScheduledJobInfo job)
    {
        InitializeComponent();

        NameBox.Text = job.Name;

        // Populate time combo: 00:00 – 23:30 in 30-minute steps
        for (var hour = 0; hour < 24; hour++)
        {
            TimeCombo.Items.Add($"{hour:D2}:00");
            TimeCombo.Items.Add($"{hour:D2}:30");
        }
        var requestedTime = string.IsNullOrWhiteSpace(job.TimeText) ? "06:00" : job.TimeText;
        TimeCombo.SelectedItem = requestedTime;
        if (TimeCombo.SelectedItem == null)
        {
            TimeCombo.SelectedIndex = 12; // default 06:00
        }

        FrequencyCombo.SelectedIndex = job.FrequencyTag switch
        {
            "Weekly" => 1,
            "Monthly" => 2,
            _ => 0
        };
        UpdateDayComboVisibility();
    }

    private void FrequencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDayComboVisibility();

    private void UpdateDayComboVisibility()
    {
        DayCombo.Visibility = FrequencyCombo.SelectedIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Please enter a job name.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var time = TimeCombo.SelectedItem?.ToString() ?? "06:00";
        var frequency = (FrequencyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Daily";

        NextRunText = frequency switch
        {
            "Daily" => $"Tomorrow {time}",
            "Weekly" => $"{(DayCombo.SelectedItem as ComboBoxItem)?.Content} {time}",
            "Monthly" => $"1st of month {time}",
            _ => $"Tomorrow {time}"
        };

        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        ShouldDelete = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
