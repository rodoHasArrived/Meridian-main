using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Wraps legacy deep pages in the shared workstation shell chrome so direct navigation
/// preserves workspace context, related workflows, and trust signals.
/// </summary>
public partial class WorkspaceDeepPageHostPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly WorkspaceShellContextService? _shellContextService;
    private readonly ShellPageDescriptor _descriptor;
    private readonly WorkspaceShellDescriptor _workspace;
    private readonly Page _hostedPage;
    private readonly object? _navigationParameter;
    private readonly WorkspaceChromePresentationMode _presentationMode;
    private readonly SettingsConfigurationService _settingsConfigurationService = SettingsConfigurationService.Instance;

    public WorkspaceDeepPageHostPage(
        NavigationService navigationService,
        WorkspaceShellContextService? shellContextService,
        string pageTag,
        Page hostedPage,
        object? navigationParameter,
        WorkspaceChromePresentationMode presentationMode)
    {
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTag);
        ArgumentNullException.ThrowIfNull(hostedPage);

        _navigationService = navigationService;
        _shellContextService = shellContextService;
        _descriptor = ShellNavigationCatalog.GetPage(pageTag)
            ?? throw new InvalidOperationException($"No shell navigation descriptor was found for '{pageTag}'.");
        _workspace = ShellNavigationCatalog.GetWorkspace(_descriptor.WorkspaceId)
            ?? ShellNavigationCatalog.GetDefaultWorkspace();
        _hostedPage = hostedPage;
        _navigationParameter = navigationParameter;
        _presentationMode = presentationMode;

        InitializeComponent();
        ApplyShellSummary();
    }

    public Page HostedPage => _hostedPage;

    public object? HostedContent => HostedFrame.Content;

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyHostedChromeState();
        ApplyPresentationMode();
        CommandBar.CommandGroup = BuildCommandGroup();
        _settingsConfigurationService.DesktopShellPreferencesChanged += OnDesktopShellPreferencesChanged;

        if (!ReferenceEquals(HostedFrame.Content, _hostedPage))
        {
            HostedFrame.Content = _hostedPage;
        }

        if (_presentationMode == WorkspaceChromePresentationMode.Standalone && _shellContextService is not null)
        {
            _shellContextService.SignalsChanged += OnShellSignalsChanged;
            await RefreshContextAsync();
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _settingsConfigurationService.DesktopShellPreferencesChanged -= OnDesktopShellPreferencesChanged;
        WorkspaceShellChromeState.SetIsHostedInWorkspaceShell(_hostedPage, false);
        WorkspaceShellChromeState.SetShellDensityMode(_hostedPage, ShellDensityMode.Standard);

        if (_shellContextService is not null)
        {
            _shellContextService.SignalsChanged -= OnShellSignalsChanged;
        }
    }

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
    {
        var commandId = e.Command.Id;
        if (string.Equals(commandId, "GoBack", StringComparison.OrdinalIgnoreCase))
        {
            if (_navigationService.CanGoBack)
            {
                _navigationService.GoBack();
            }

            return;
        }

        _navigationService.NavigateTo(commandId);
    }

    private async void OnShellSignalsChanged(object? sender, EventArgs e)
    {
        if (_presentationMode != WorkspaceChromePresentationMode.Standalone || _shellContextService is null)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(async () => await RefreshContextAsync());
            return;
        }

        await RefreshContextAsync();
    }

    private void OnDesktopShellPreferencesChanged(object? sender, DesktopShellPreferences preferences)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnDesktopShellPreferencesChanged(sender, preferences));
            return;
        }

        WorkspaceShellChromeState.SetShellDensityMode(_hostedPage, preferences.ShellDensityMode);
    }

    private void ApplyPresentationMode()
    {
        var showStandaloneChrome = _presentationMode == WorkspaceChromePresentationMode.Standalone;
        ContextStrip.Visibility = showStandaloneChrome ? Visibility.Visible : Visibility.Collapsed;
        CommandBar.Visibility = showStandaloneChrome ? Visibility.Visible : Visibility.Collapsed;

        SummaryCard.Margin = showStandaloneChrome
            ? new Thickness(0, 0, 0, 18)
            : new Thickness(0, 0, 0, 12);
    }

    private void ApplyHostedChromeState()
    {
        WorkspaceShellChromeState.SetIsHostedInWorkspaceShell(_hostedPage, true);
        WorkspaceShellChromeState.SetShellDensityMode(_hostedPage, _settingsConfigurationService.GetShellDensityMode());
    }

    private void ApplyShellSummary()
    {
        PageGlyphText.Text = _descriptor.Glyph;
        PageTitleText.Text = _descriptor.Title;
        PageSubtitleText.Text = _descriptor.Subtitle;
        WorkspaceBadgeText.Text = _workspace.Title;
        SectionBadgeText.Text = _descriptor.SectionLabel;
        ReachabilityBadgeText.Text = ToVisibilityLabel(_descriptor.VisibilityTier);
        RelatedBadgeText.Text = BuildRelatedLabel();

        var selectionLabel = BuildSelectionLabel();
        SelectionBadge.Visibility = string.IsNullOrWhiteSpace(selectionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        SelectionBadgeText.Text = selectionLabel;
    }

    private async Task RefreshContextAsync()
    {
        if (_shellContextService is null)
        {
            ContextStrip.ShellContext = BuildFallbackContext();
            return;
        }

        try
        {
            var shellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
            {
                WorkspaceTitle = _descriptor.Title,
                WorkspaceSubtitle = $"{_workspace.Title} · {_descriptor.SectionLabel} workflow with shared environment, freshness, and alert context.",
                PrimaryScopeLabel = "Workspace",
                PrimaryScopeValue = _workspace.Title,
                AsOfValue = DateTimeOffset.Now.ToString("MMM dd yyyy HH:mm"),
                FreshnessValue = _presentationMode == WorkspaceChromePresentationMode.Docked
                    ? "Docked shell host"
                    : $"{_descriptor.SectionLabel} workflow active",
                ReviewStateLabel = "Reachability",
                ReviewStateValue = ToVisibilityLabel(_descriptor.VisibilityTier),
                ReviewStateTone = _descriptor.VisibilityTier == ShellNavigationVisibilityTier.Primary
                    ? WorkspaceTone.Info
                    : WorkspaceTone.Neutral,
                CriticalLabel = "Related",
                CriticalValue = BuildRelatedLabel(),
                CriticalTone = ShellNavigationCatalog.GetRelatedPages(_descriptor.PageTag).Count > 0
                    ? WorkspaceTone.Info
                    : WorkspaceTone.Neutral,
                AdditionalBadges = BuildAdditionalBadges()
            });

            ContextStrip.ShellContext = shellContext;
        }
        catch
        {
            ContextStrip.ShellContext = BuildFallbackContext();
        }
    }

    private WorkspaceShellContext BuildFallbackContext()
    {
        return new WorkspaceShellContext
        {
            WorkspaceTitle = _descriptor.Title,
            WorkspaceSubtitle = $"{_workspace.Title} · {_descriptor.SectionLabel} workflow",
            Badges =
            [
                new WorkspaceShellBadge
                {
                    Label = "Workspace",
                    Value = _workspace.Title,
                    Glyph = "\uE8B7",
                    Tone = WorkspaceTone.Info
                },
                new WorkspaceShellBadge
                {
                    Label = "Reachability",
                    Value = ToVisibilityLabel(_descriptor.VisibilityTier),
                    Glyph = "\uE73E",
                    Tone = WorkspaceTone.Neutral
                },
                new WorkspaceShellBadge
                {
                    Label = "Related",
                    Value = BuildRelatedLabel(),
                    Glyph = "\uE8A5",
                    Tone = WorkspaceTone.Neutral
                }
            ]
        };
    }

    private WorkspaceCommandGroup BuildCommandGroup()
    {
        if (_presentationMode != WorkspaceChromePresentationMode.Standalone)
        {
            return new WorkspaceCommandGroup();
        }

        var relatedPages = ShellNavigationCatalog.GetRelatedPages(_descriptor.PageTag)
            .Where(page => !string.Equals(page.PageTag, _descriptor.PageTag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var primary = new List<WorkspaceCommandItem>
        {
            new()
            {
                Id = _workspace.HomePageTag,
                Label = $"Back to {_workspace.Title}",
                Description = $"Return to the {_workspace.Title} workspace shell.",
                ShortcutHint = "Workspace",
                Glyph = "\uE80F",
                Tone = WorkspaceTone.Primary
            }
        };

        foreach (var related in relatedPages.Take(2))
        {
            primary.Add(new WorkspaceCommandItem
            {
                Id = related.PageTag,
                Label = related.Title,
                Description = related.Subtitle,
                Glyph = related.Glyph,
                Tone = WorkspaceTone.Secondary
            });
        }

        var secondary = relatedPages
            .Skip(2)
            .Select(related => new WorkspaceCommandItem
            {
                Id = related.PageTag,
                Label = related.Title,
                Description = related.Subtitle,
                Glyph = related.Glyph,
                Tone = WorkspaceTone.Secondary
            })
            .ToList();

        secondary.Add(new WorkspaceCommandItem
        {
            Id = "GoBack",
            Label = "Back",
            Description = "Return to the previous navigation step.",
            ShortcutHint = "Alt+Left",
            Glyph = "\uE72B",
            Tone = WorkspaceTone.Secondary,
            IsEnabled = _navigationService.CanGoBack
        });

        return new WorkspaceCommandGroup
        {
            PrimaryCommands = primary,
            SecondaryCommands = secondary
        };
    }

    private IReadOnlyList<WorkspaceShellBadge> BuildAdditionalBadges()
    {
        var badges = new List<WorkspaceShellBadge>
        {
            new()
            {
                Label = "Section",
                Value = _descriptor.SectionLabel,
                Glyph = "\uE8FD",
                Tone = WorkspaceTone.Neutral
            },
            new()
            {
                Label = "Shell Mode",
                Value = _presentationMode == WorkspaceChromePresentationMode.Standalone ? "Standalone" : "Docked",
                Glyph = "\uEE94",
                Tone = WorkspaceTone.Neutral
            }
        };

        var selectionLabel = BuildSelectionLabel();
        if (!string.IsNullOrWhiteSpace(selectionLabel))
        {
            badges.Add(new WorkspaceShellBadge
            {
                Label = "Selection",
                Value = selectionLabel,
                Glyph = "\uE71B",
                Tone = WorkspaceTone.Info
            });
        }

        return badges;
    }

    private string BuildRelatedLabel()
    {
        var relatedCount = ShellNavigationCatalog.GetRelatedPages(_descriptor.PageTag).Count;
        return relatedCount == 0 ? "No linked workflows" : $"{relatedCount} linked workflow(s)";
    }

    private string BuildSelectionLabel()
    {
        if (_navigationParameter is null)
        {
            return string.Empty;
        }

        var text = _navigationParameter.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= 32
            ? text
            : $"{text[..29]}...";
    }

    private static string ToVisibilityLabel(ShellNavigationVisibilityTier tier) => tier switch
    {
        ShellNavigationVisibilityTier.Primary => "Primary route",
        ShellNavigationVisibilityTier.Secondary => "Secondary route",
        _ => "Overflow route"
    };
}
