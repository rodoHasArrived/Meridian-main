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
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly FixtureModeDetector _fixtureModeDetector;
    private readonly NotificationService _notificationService;
    private readonly Func<CancellationToken, Task<WorkspaceStatusSnapshot?>> _getStatusAsync;

    public WorkspaceShellContextService(
        FundContextService fundContextService,
        FixtureModeDetector fixtureModeDetector,
        NotificationService notificationService,
        IStatusService statusService)
        : this(
            fundContextService,
            fixtureModeDetector,
            notificationService,
            ct => MapStatusAsync(statusService, ct),
            operatingContextService: null)
    {
    }

    public WorkspaceShellContextService(
        FundContextService fundContextService,
        FixtureModeDetector fixtureModeDetector,
        NotificationService notificationService,
        IStatusService statusService,
        WorkstationOperatingContextService? operatingContextService)
        : this(
            fundContextService,
            fixtureModeDetector,
            notificationService,
            ct => MapStatusAsync(statusService, ct),
            operatingContextService)
    {
    }

    public WorkspaceShellContextService(
        FundContextService fundContextService,
        FixtureModeDetector fixtureModeDetector,
        NotificationService notificationService,
        StatusService statusService)
        : this(
            fundContextService,
            fixtureModeDetector,
            notificationService,
            ct => MapStatusAsync(statusService, ct),
            operatingContextService: null)
    {
    }

    public WorkspaceShellContextService(
        FundContextService fundContextService,
        FixtureModeDetector fixtureModeDetector,
        NotificationService notificationService,
        StatusService statusService,
        WorkstationOperatingContextService? operatingContextService)
        : this(
            fundContextService,
            fixtureModeDetector,
            notificationService,
            ct => MapStatusAsync(statusService, ct),
            operatingContextService)
    {
    }

    private WorkspaceShellContextService(
        FundContextService fundContextService,
        FixtureModeDetector fixtureModeDetector,
        NotificationService notificationService,
        Func<CancellationToken, Task<WorkspaceStatusSnapshot?>> getStatusAsync,
        WorkstationOperatingContextService? operatingContextService)
    {
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService;
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _getStatusAsync = getStatusAsync ?? throw new ArgumentNullException(nameof(getStatusAsync));

        _fundContextService.ActiveFundProfileChanged += (_, _) => SignalsChanged?.Invoke(this, EventArgs.Empty);
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged += (_, _) => SignalsChanged?.Invoke(this, EventArgs.Empty);
            _operatingContextService.WindowModeChanged += (_, _) => SignalsChanged?.Invoke(this, EventArgs.Empty);
        }
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
        if (_operatingContextService is not null)
        {
            await _operatingContextService.LoadAsync(ct).ConfigureAwait(false);
        }

        var profile = _fundContextService.CurrentFundProfile;
        var operatingContext = _operatingContextService?.CurrentContext;
        var status = await _getStatusAsync(ct).ConfigureAwait(false);
        var history = _notificationService.GetHistory();
        var unreadCount = history.Count(item => !item.IsRead);
        var recentCount = history.Count;

        var badges = new List<WorkspaceShellBadge>
        {
            new()
            {
                Label = input.PrimaryScopeLabel,
                Value = ResolveScopeValue(profile, operatingContext, input.PrimaryScopeValue),
                Glyph = "\uE8B7",
                Tone = string.IsNullOrWhiteSpace(input.PrimaryScopeValue) && profile is null
                    ? WorkspaceTone.Warning
                    : WorkspaceTone.Info
            },
            new()
            {
                Label = "Scope",
                Value = operatingContext?.ScopeKind.ToDisplayName() ?? "Fund",
                Glyph = "\uE81E",
                Tone = WorkspaceTone.Neutral
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

        if (!string.IsNullOrWhiteSpace(operatingContext?.BaseCurrency))
        {
            badges.Add(new WorkspaceShellBadge
            {
                Label = "Currency",
                Value = operatingContext.BaseCurrency,
                Glyph = "\uEAFD",
                Tone = WorkspaceTone.Neutral
            });
        }

        if (operatingContext?.LedgerGroupIds.Count > 0)
        {
            badges.Add(new WorkspaceShellBadge
            {
                Label = "Ledger Scope",
                Value = $"{operatingContext.LedgerGroupIds.Count} group(s)",
                Glyph = "\uEE94",
                Tone = WorkspaceTone.Info
            });
        }

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

    private string ResolveEnvironmentValue(WorkspaceStatusSnapshot? status)
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

    private string ResolveEnvironmentTone(WorkspaceStatusSnapshot? status)
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

    private static string ResolveScopeValue(
        FundProfileDetail? profile,
        WorkstationOperatingContext? operatingContext,
        string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue;
        }

        if (operatingContext is not null)
        {
            return $"{operatingContext.DisplayName} · {operatingContext.BaseCurrency}";
        }

        return profile is null
            ? "No fund selected"
            : $"{profile.DisplayName} · {profile.BaseCurrency}";
    }

    private static string ResolveValue(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string ResolveFreshnessValue(WorkspaceStatusSnapshot? status, string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue;
        }

        return status?.IsConnected == true && status.IsStale == false
            ? "Backend connected"
            : "Awaiting backend";
    }

    private static string ResolveFreshnessTone(WorkspaceStatusSnapshot? status, string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue.Contains("stale", StringComparison.OrdinalIgnoreCase)
                || requestedValue.Contains("offline", StringComparison.OrdinalIgnoreCase)
                ? WorkspaceTone.Warning
                : WorkspaceTone.Success;
        }

        return status?.IsConnected == true && status.IsStale == false
            ? WorkspaceTone.Success
            : WorkspaceTone.Warning;
    }

    private static async Task<WorkspaceStatusSnapshot?> MapStatusAsync(IStatusService statusService, CancellationToken ct)
    {
        var status = await statusService.GetStatusAsync(ct).ConfigureAwait(false);
        return status is null
            ? null
            : new WorkspaceStatusSnapshot(
                status.IsConnected,
                status.Metrics?.IsStale ?? false);
    }

    private static async Task<WorkspaceStatusSnapshot?> MapStatusAsync(StatusService statusService, CancellationToken ct)
    {
        var status = await statusService.GetStatusAsync(ct).ConfigureAwait(false);
        return status is null
            ? null
            : new WorkspaceStatusSnapshot(
                status.Provider?.IsConnected == true,
                status.IsStale);
    }

    private sealed record WorkspaceStatusSnapshot(bool IsConnected, bool IsStale);
}
