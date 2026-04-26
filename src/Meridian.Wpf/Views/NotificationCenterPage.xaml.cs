using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Notification Center page for viewing and managing all application notifications.
/// Code-behind handles only: DI wiring, alert group card building (uses FindResource),
/// playbook display, and snooze/suppress actions.
/// </summary>
public partial class NotificationCenterPage : Page
{
    private readonly WpfServices.NotificationService _notificationService;
    private readonly AlertService _alertService;
    private readonly NotificationCenterViewModel _viewModel;

    public NotificationCenterPage(WpfServices.NotificationService notificationService)
    {
        _notificationService = notificationService;
        _alertService = AlertService.Instance;
        _viewModel = new NotificationCenterViewModel(notificationService, _alertService);

        InitializeComponent();
        DataContext = _viewModel;

        // Sync preference checkboxes with current settings
        var settings = _notificationService.GetSettings();
        EnableDesktopNotificationsCheck.IsChecked = settings.Enabled;
        PlayNotificationSoundCheck.IsChecked = settings.SoundType != "None";
        ShowNotificationBadgeCheck.IsChecked = true;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.AlertsRefreshRequested += OnAlertsRefreshRequested;
        _viewModel.Start();
        RefreshGroupedAlerts();
        RefreshAlertSummary();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.AlertsRefreshRequested -= OnAlertsRefreshRequested;
        _viewModel.Stop();
    }

    private void OnAlertsRefreshRequested(object? sender, EventArgs e)
    {
        RefreshGroupedAlerts();
        RefreshAlertSummary();
    }

    private void OnAlertChanged(object? sender, AlertEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshGroupedAlerts();
            RefreshAlertSummary();
        });
    }

    private void RefreshGroupedAlerts()
    {
        GroupedAlertsPanel.Children.Clear();

        var groups = _alertService.GetGroupedAlerts();

        if (groups.Count == 0)
        {
            NoGroupedAlertsPanel.Visibility = Visibility.Visible;
            GroupedAlertsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NoGroupedAlertsPanel.Visibility = Visibility.Collapsed;
        GroupedAlertsPanel.Visibility = Visibility.Visible;

        foreach (var group in groups)
        {
            var card = BuildAlertGroupCard(group);
            GroupedAlertsPanel.Children.Add(card);
        }
    }

    private void RefreshAlertSummary()
    {
        var summary = _alertService.GetSummary();

        AlertTotalText.Text = $"{summary.TotalActive} active";

        CriticalBadge.Visibility = summary.CriticalCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CriticalCountText.Text = $"{summary.CriticalCount} Critical";

        ErrorBadge.Visibility = summary.ErrorCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorCountText.Text = $"{summary.ErrorCount} Error{(summary.ErrorCount != 1 ? "s" : "")}";

        WarningBadge.Visibility = summary.WarningCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningCountText.Text = $"{summary.WarningCount} Warning{(summary.WarningCount != 1 ? "s" : "")}";

        SnoozedCountText.Text = summary.SnoozedCount > 0
            ? $"{summary.SnoozedCount} snoozed"
            : string.Empty;
    }

    private Border BuildAlertGroupCard(AlertGroup group)
    {
        var severityColor = group.Severity switch
        {
            AlertSeverity.Critical or AlertSeverity.Emergency => (Brush)FindResource("ErrorColorBrush"),
            AlertSeverity.Error => new SolidColorBrush(Color.FromRgb(255, 87, 34)),
            AlertSeverity.Warning => (Brush)FindResource("WarningColorBrush"),
            _ => (Brush)FindResource("InfoColorBrush")
        };

        var card = new Border
        {
            Background = (Brush)FindResource("ConsoleBackgroundLightBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            BorderBrush = (Brush)FindResource("ConsoleBorderBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var indicator = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = severityColor,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(indicator, 0);
        outerGrid.Children.Add(indicator);

        var contentPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(contentPanel, 1);

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = group.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = (Brush)FindResource("ConsoleTextPrimaryBrush")
        });

        titlePanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 139, 148, 158)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = group.Category,
                FontSize = 10,
                Foreground = (Brush)FindResource("ConsoleTextMutedBrush")
            }
        });

        if (group.Count > 1)
        {
            titlePanel.Children.Add(new Border
            {
                Background = severityColor,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"x{group.Count}",
                    FontSize = 10,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold
                }
            });
        }

        contentPanel.Children.Add(titlePanel);

        if (group.AffectedResources.Count > 0)
        {
            var resources = string.Join(", ", group.AffectedResources.Take(5));
            if (group.AffectedResources.Count > 5)
                resources += $" +{group.AffectedResources.Count - 5} more";

            contentPanel.Children.Add(new TextBlock
            {
                Text = $"Affected: {resources}",
                FontSize = 12,
                Foreground = (Brush)FindResource("ConsoleTextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        contentPanel.Children.Add(new TextBlock
        {
            Text = $"First: {FormatTimestamp(group.FirstOccurred)}  |  Last: {FormatTimestamp(group.LastOccurred)}",
            FontSize = 11,
            Foreground = (Brush)FindResource("ConsoleTextMutedBrush")
        });

        outerGrid.Children.Add(contentPanel);

        var actionsPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actionsPanel, 2);

        if (group.RepresentativeAlert.Playbook != null)
        {
            var playbookButton = new Button
            {
                Content = "Playbook",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Margin = new Thickness(4, 0, 0, 4),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 11,
                Tag = group.RepresentativeAlert.Id
            };
            playbookButton.Click += ShowPlaybook_Click;
            actionsPanel.Children.Add(playbookButton);
        }

        var snoozeButton = new Button
        {
            Content = "Snooze 1h",
            Style = (Style)FindResource("GhostButtonStyle"),
            Margin = new Thickness(4, 0, 0, 4),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            Tag = group.RepresentativeAlert.Id
        };
        snoozeButton.Click += SnoozeAlert_Click;
        actionsPanel.Children.Add(snoozeButton);

        var suppressButton = new Button
        {
            Content = "Suppress",
            Style = (Style)FindResource("GhostButtonStyle"),
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            Tag = $"{group.Category}|{group.Title}"
        };
        suppressButton.Click += SuppressAlert_Click;
        actionsPanel.Children.Add(suppressButton);

        outerGrid.Children.Add(actionsPanel);
        card.Child = outerGrid;
        return card;
    }

    private void ShowPlaybook_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string alertId)
            return;

        var alert = _alertService.GetActiveAlerts().FirstOrDefault(a => a.Id == alertId);
        if (alert?.Playbook == null)
            return;

        var playbook = alert.Playbook;
        PlaybookTitle.Text = playbook.Title;
        PlaybookWhatHappened.Text = playbook.WhatHappened;

        PlaybookCausesPanel.Children.Clear();
        foreach (var cause in playbook.PossibleCauses)
        {
            var causePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            causePanel.Children.Add(new TextBlock
            {
                Text = "\u2022",
                FontSize = 13,
                Foreground = (Brush)FindResource("ConsoleTextMutedBrush"),
                Margin = new Thickness(8, 0, 8, 0)
            });
            causePanel.Children.Add(new TextBlock
            {
                Text = cause,
                FontSize = 12,
                Foreground = (Brush)FindResource("ConsoleTextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            PlaybookCausesPanel.Children.Add(causePanel);
        }

        PlaybookStepsPanel.Children.Clear();
        foreach (var step in playbook.RemediationSteps)
        {
            var stepBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(15, 88, 166, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stepPanel = new StackPanel { Orientation = Orientation.Horizontal };
            stepPanel.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("InfoColorBrush"),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = step.Priority.ToString(),
                    FontSize = 11,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });

            var stepContent = new StackPanel();
            stepContent.Children.Add(new TextBlock
            {
                Text = step.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = (Brush)FindResource("ConsoleTextPrimaryBrush")
            });
            stepContent.Children.Add(new TextBlock
            {
                Text = step.Description,
                FontSize = 11,
                Foreground = (Brush)FindResource("ConsoleTextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            stepPanel.Children.Add(stepContent);

            stepBorder.Child = stepPanel;
            PlaybookStepsPanel.Children.Add(stepBorder);
        }

        PlaybookIgnoredText.Text = playbook.WhatHappensIfIgnored;
        PlaybookPanel.Visibility = Visibility.Visible;
    }

    private void ClosePlaybook_Click(object sender, RoutedEventArgs e)
    {
        PlaybookPanel.Visibility = Visibility.Collapsed;
    }

    private void SnoozeAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string alertId)
            return;

        _alertService.SnoozeAlert(alertId, TimeSpan.FromHours(1));
        _notificationService.ShowNotification(
            "Alert Snoozed",
            "Alert snoozed for 1 hour.",
            NotificationType.Info);

        RefreshGroupedAlerts();
        RefreshAlertSummary();
    }

    private void SuppressAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tagValue)
            return;

        var parts = tagValue.Split('|', 2);
        if (parts.Length < 2)
            return;

        _alertService.AddSuppressionRule(parts[0], parts[1], TimeSpan.FromHours(24));
        _notificationService.ShowNotification(
            "Alert Suppressed",
            $"Similar alerts in \"{parts[0]}\" will be suppressed for 24 hours.",
            NotificationType.Info);

        RefreshGroupedAlerts();
        RefreshAlertSummary();
    }

    private void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MarkAllRead();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearAll();
    }

    private void MarkRead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NotificationItem item })
        {
            _viewModel.MarkRead(item);
        }
    }

    private static string FormatTimestamp(DateTime timestamp)
    {
        var elapsed = DateTime.Now - timestamp;
        return elapsed.TotalSeconds switch
        {
            < 60 => "Just now",
            < 3600 => $"{(int)elapsed.TotalMinutes}m ago",
            < 86400 => $"{(int)elapsed.TotalHours}h ago",
            < 172800 => "Yesterday",
            _ => timestamp.ToString("MMM dd, HH:mm")
        };
    }
}
