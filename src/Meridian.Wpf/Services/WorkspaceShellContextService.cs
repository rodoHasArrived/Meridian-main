using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Composes a consistent shell-context strip from shared workstation state.
/// </summary>
public sealed class WorkspaceShellContextService
{
    private readonly FundContextService _fundContextService;
    private readonly FixtureModeDetector _fixtureModeDetector;
    private readonly NotificationService _notificationService;
    private readonly IStatusService _statusService;

    public WorkspaceShellContextService(
        FundContextService fundContextService,
        FixtureModeDetector fixtureModeDetector,
        NotificationService notificationService,
        IStatusService statusService)
    {
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));

        _fundContextService.ActiveFundProfileChanged += (_, _) => SignalsChanged?.Invoke(this, EventArgs.Empty);
        _fixtureModeDetector.ModeChanged += (_, _) => SignalsChanged?.Invoke(this, EventArgs.Empty);
        _notificationService.NotificationReceived += (_, _) => SignalsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SignalsChanged;

    public async Task<WorkspaceShellContext> CreateAsync(
        WorkspaceShellContextInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);
        var profile = _fundContextService.CurrentFundProfile;
        var status = await _statusService.GetStatusAsync(ct).ConfigureAwait(false);
        var history = _notificationService.GetHistory();
        var unreadCount = history.Count(item => !item.IsRead);
        var recentCount = history.Count;

        var badges = new List<WorkspaceShellBadge>
        {
            new()
            {
                Label = input.PrimaryScopeLabel,
                Value = ResolveScopeValue(profile, input.PrimaryScopeValue),
                Glyph = "\uE8B7",
                Tone = string.IsNullOrWhiteSpace(input.PrimaryScopeValue) && profile is null
                    ? WorkspaceTone.Warning
                    : WorkspaceTone.Info
            },
            new()
            {
                Label = "Environment",
                Value = ResolveEnvironmentValue(status),
                Glyph = "\uE7BA",
                Tone = ResolveEnvironmentTone(status)
            },
            new()
            {
                Label = "As Of",
                Value = ResolveValue(input.AsOfValue, "Awaiting scope"),
                Glyph = "\uE823",
                Tone = WorkspaceTone.Neutral
            },
            new()
            {
                Label = "Freshness",
                Value = ResolveFreshnessValue(status, input.FreshnessValue),
                Glyph = "\uE72C",
                Tone = ResolveFreshnessTone(status, input.FreshnessValue)
            },
            new()
            {
                Label = input.ReviewStateLabel,
                Value = ResolveValue(input.ReviewStateValue, "Stable"),
                Glyph = "\uE73E",
                Tone = string.IsNullOrWhiteSpace(input.ReviewStateValue)
                    ? WorkspaceTone.Neutral
                    : input.ReviewStateTone
            },
            new()
            {
                Label = input.CriticalLabel,
                Value = ResolveValue(input.CriticalValue, unreadCount > 0 ? $"{unreadCount} unread alert(s)" : "No critical items"),
                Glyph = "\uE7F4",
                Tone = string.IsNullOrWhiteSpace(input.CriticalValue)
                    ? unreadCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Neutral
                    : input.CriticalTone
            },
            new()
            {
                Label = "Alerts",
                Value = unreadCount > 0 ? $"{unreadCount} unread" : recentCount > 0 ? $"{recentCount} recent" : "No recent alerts",
                Glyph = "\uE7F4",
                Tone = unreadCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Neutral
            }
        };

        if (input.AdditionalBadges.Count > 0)
        {
            badges.AddRange(input.AdditionalBadges.Where(static badge => !string.IsNullOrWhiteSpace(badge.Value)));
        }

        return new WorkspaceShellContext
        {
            WorkspaceTitle = input.WorkspaceTitle,
            WorkspaceSubtitle = input.WorkspaceSubtitle,
            Badges = badges
        };
    }

    public int GetUnreadAlertCount()
        => _notificationService.GetHistory().Count(item => !item.IsRead);

    private string ResolveEnvironmentValue(StatusResponse? status)
    {
        if (_fixtureModeDetector.IsFixtureMode)
        {
            return "Fixture";
        }

        if (_fixtureModeDetector.IsOfflineMode)
        {
            return "Offline";
        }

        if (status?.IsConnected == true)
        {
            return "Live";
        }

        return "Pending";
    }

    private string ResolveEnvironmentTone(StatusResponse? status)
    {
        if (_fixtureModeDetector.IsOfflineMode)
        {
            return WorkspaceTone.Danger;
        }

        if (_fixtureModeDetector.IsFixtureMode)
        {
            return WorkspaceTone.Warning;
        }

        return status?.IsConnected == true
            ? WorkspaceTone.Success
            : WorkspaceTone.Warning;
    }

    private static string ResolveScopeValue(FundProfileDetail? profile, string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue;
        }

        return profile is null
            ? "No fund selected"
            : $"{profile.DisplayName} · {profile.BaseCurrency}";
    }

    private static string ResolveValue(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string ResolveFreshnessValue(StatusResponse? status, string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue;
        }

        return status?.IsConnected == true
            ? "Backend connected"
            : "Awaiting backend";
    }

    private static string ResolveFreshnessTone(StatusResponse? status, string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue.Contains("stale", StringComparison.OrdinalIgnoreCase)
                || requestedValue.Contains("offline", StringComparison.OrdinalIgnoreCase)
                ? WorkspaceTone.Warning
                : WorkspaceTone.Success;
        }

        return status?.IsConnected == true
            ? WorkspaceTone.Success
            : WorkspaceTone.Warning;
    }
}
