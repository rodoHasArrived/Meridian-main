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
        AttentionDetailText.Text = BuildAttentionDetail(attentionBadge);
    }

    private Brush TryResolveBrush(string resourceKey, Brush fallback)
        => TryFindResource(resourceKey) as Brush ?? fallback;
}
