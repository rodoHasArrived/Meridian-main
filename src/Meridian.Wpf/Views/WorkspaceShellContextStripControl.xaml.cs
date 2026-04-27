using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public partial class WorkspaceShellContextStripControl : UserControl
{
    private const string AttentionTitleDanger = "Action required";
    private const string AttentionTitleWarning = "Review recommended";

    public static readonly DependencyProperty ShellContextProperty =
        DependencyProperty.Register(
            nameof(ShellContext),
            typeof(WorkspaceShellContext),
            typeof(WorkspaceShellContextStripControl),
            new PropertyMetadata(new WorkspaceShellContext(), OnShellContextChanged));

    public WorkspaceShellContextStripControl()
    {
        InitializeComponent();
        UpdateAttentionPresentation();
    }

    public WorkspaceShellContext ShellContext
    {
        get => (WorkspaceShellContext)GetValue(ShellContextProperty);
        set => SetValue(ShellContextProperty, value);
    }

    internal static WorkspaceShellBadge? ResolveAttentionBadge(WorkspaceShellContext? shellContext)
    {
        return shellContext?.Badges
            .Where(static badge => IsAttentionTone(badge.Tone))
            .OrderBy(GetAttentionPriority)
            .FirstOrDefault();
    }

    internal static string BuildAttentionTitle(WorkspaceShellBadge badge)
    {
        ArgumentNullException.ThrowIfNull(badge);

        return string.Equals(badge.Tone, WorkspaceTone.Danger, StringComparison.Ordinal)
            ? AttentionTitleDanger
            : AttentionTitleWarning;
    }

    internal static string BuildAttentionDetail(WorkspaceShellBadge badge)
    {
        ArgumentNullException.ThrowIfNull(badge);

        if (string.IsNullOrWhiteSpace(badge.Label))
        {
            return badge.Value;
        }

        return string.IsNullOrWhiteSpace(badge.Value)
            ? badge.Label
            : $"{badge.Label}: {badge.Value}";
    }

    private static void OnShellContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceShellContextStripControl control)
        {
            control.UpdateAttentionPresentation();
        }
    }

    private static bool IsAttentionTone(string tone)
        => string.Equals(tone, WorkspaceTone.Danger, StringComparison.Ordinal)
            || string.Equals(tone, WorkspaceTone.Warning, StringComparison.Ordinal);

    private static int GetAttentionPriority(WorkspaceShellBadge badge)
    {
        var tonePriority = string.Equals(badge.Tone, WorkspaceTone.Danger, StringComparison.Ordinal) ? 0 : 10;
        var labelPriority = badge.Label switch
        {
            "Critical" => 0,
            "Attention" => 0,
            "Environment" => 1,
            "Freshness" => 2,
            "Alerts" => 3,
            _ => 4
        };

        return tonePriority + labelPriority;
    }

    private void UpdateAttentionPresentation()
    {
        if (AttentionBanner is null)
        {
            return;
        }

        var attentionBadge = ResolveAttentionBadge(ShellContext);
        if (attentionBadge is null)
        {
            AttentionBanner.Visibility = Visibility.Collapsed;
            AttentionTitleText.Text = string.Empty;
            AttentionDetailText.Text = string.Empty;
            return;
        }

        var isDanger = string.Equals(attentionBadge.Tone, WorkspaceTone.Danger, StringComparison.Ordinal);
        var accentBrush = TryResolveBrush(isDanger ? "ErrorColorBrush" : "WarningColorBrush", Brushes.White);
        AttentionBanner.Visibility = Visibility.Visible;
        AttentionBanner.Background = TryResolveBrush(
            isDanger ? "ConsoleAccentRedAlpha10Brush" : "ConsoleAccentOrangeAlpha10Brush",
            Brushes.Transparent);
        AttentionBanner.BorderBrush = accentBrush;
        AttentionGlyphText.Foreground = accentBrush;
        AttentionGlyphText.Text = "!";
        AttentionTitleText.Foreground = accentBrush;
        AttentionTitleText.Text = BuildAttentionTitle(attentionBadge);
        AttentionDetailText.Text = BuildActionableAttentionDetail(attentionBadge, ShellContext);
    }

    private static string BuildActionableAttentionDetail(
        WorkspaceShellBadge badge,
        WorkspaceShellContext? shellContext)
    {
        var detail = BuildAttentionDetail(badge);
        var severity = string.Equals(badge.Tone, WorkspaceTone.Danger, StringComparison.Ordinal)
            ? "action required"
            : "warning";
        var owner = string.IsNullOrWhiteSpace(shellContext?.WorkspaceTitle)
            ? "current workspace"
            : shellContext.WorkspaceTitle.Trim();
        var source = ResolveAttentionSource(badge);
        var action = ResolveAttentionAction(badge, owner);

        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(action))
        {
            return $"{detail}; severity: {severity}; owner: {owner}.";
        }

        return $"{detail}; severity: {severity}; owner: {owner}; source: {source}; action: {action}.";
    }

    private static string ResolveAttentionSource(WorkspaceShellBadge badge)
    {
        if (ContainsAny(badge.Label, "Alert", "Critical") || ContainsAny(badge.Value, "alert", "unread"))
        {
            return "workstation alerts";
        }

        if (ContainsAny(badge.Label, "Freshness") || ContainsAny(badge.Value, "backend", "stale", "loaded"))
        {
            return "shell freshness monitor";
        }

        if (ContainsAny(badge.Label, "Environment") || ContainsAny(badge.Value, "offline", "demo"))
        {
            return "runtime environment";
        }

        return string.IsNullOrWhiteSpace(badge.Label)
            ? "workspace signal"
            : badge.Label.Trim();
    }

    private static string ResolveAttentionAction(WorkspaceShellBadge badge, string owner)
    {
        if (ContainsAny(badge.Label, "Alert", "Critical") || ContainsAny(badge.Value, "alert", "unread"))
        {
            return "open Notification Center";
        }

        if (ContainsAny(badge.Label, "Freshness") || ContainsAny(badge.Value, "backend", "stale", "loaded"))
        {
            return string.Equals(owner, "Diagnostics", StringComparison.OrdinalIgnoreCase)
                ? "refresh Diagnostics"
                : "open Diagnostics or refresh the current view";
        }

        if (ContainsAny(badge.Label, "Environment") || ContainsAny(badge.Value, "offline"))
        {
            return "switch context or reconnect services";
        }

        return "open the linked workspace detail";
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private Brush TryResolveBrush(string resourceKey, Brush fallback)
        => TryFindResource(resourceKey) as Brush ?? fallback;
}
