using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Ui.Services.ProviderDiagnostics;

namespace Meridian.Wpf.ViewModels;

public sealed class ProviderQuotaEditorViewModel : BindableBase
{
    private readonly IProviderQuotaApiClient _client;
    private ProviderQuotaResponse? _snapshot;
    private ProviderQuotaTierDto? _tier;
    private IReadOnlyList<ProviderRateLimitDailyRollupDto> _dailyRollups = [];
    private string _providerId = "twelvedata";
    private string _minute = "";
    private string _daily = "";
    private string _pacing = "";
    private string _notice = "Load limits for the selected provider.";
    private string _historyNotice = "";
    private string? _error;
    private bool _isBusy;

    public ProviderQuotaEditorViewModel(IProviderQuotaApiClient client)
    {
        _client = client;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit && BuildEdit() is not null);
    }

    public IReadOnlyList<ProviderQuotaChoice> Providers { get; } =
        [new("twelvedata", "Twelve Data"), new("alphavantage", "Alpha Vantage"), new("fred", "FRED")];

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }

    public string ProviderId
    {
        get => _providerId;
        set
        {
            if (SetProperty(ref _providerId, value))
            {
                Error = null;
                Notice = "Load limits for the selected provider.";
                DailyRollups = [];
                HistoryNotice = "";
                UpdateProjection();
            }
        }
    }

    public IReadOnlyList<ProviderQuotaTierDto> Tiers => Snapshot?.Tiers ?? [];
    public ProviderQuotaTierDto? Tier
    {
        get => _tier;
        set
        {
            if (SetProperty(ref _tier, value))
            {
                if (value is not null)
                {
                    Minute = value.MinuteCeiling.ToString(CultureInfo.InvariantCulture);
                    Pacing = Minute;
                    Daily = value.DefaultDailyLimit?.ToString(CultureInfo.InvariantCulture) ?? "";
                }
                OnPropertyChanged(nameof(DailyLimitLabel));
                OnPropertyChanged(nameof(TierGuidance));
            }
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    public string Minute
    {
        get => _minute;
        set { if (SetProperty(ref _minute, value)) SaveCommand.NotifyCanExecuteChanged(); }
    }

    public string Daily
    {
        get => _daily;
        set { if (SetProperty(ref _daily, value)) SaveCommand.NotifyCanExecuteChanged(); }
    }

    public string Pacing
    {
        get => _pacing;
        set { if (SetProperty(ref _pacing, value)) SaveCommand.NotifyCanExecuteChanged(); }
    }

    public string Notice { get => _notice; private set => SetProperty(ref _notice, value); }
    public string HistoryNotice { get => _historyNotice; private set => SetProperty(ref _historyNotice, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public IReadOnlyList<ProviderRateLimitDailyRollupDto> DailyRollups
    {
        get => _dailyRollups;
        private set => SetProperty(ref _dailyRollups, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            OnPropertyChanged(nameof(CanSelectProvider));
            OnPropertyChanged(nameof(CanEdit));
            LoadCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanSelectProvider => !IsBusy;
    public bool HasSnapshot => Snapshot is not null;
    public bool ShowEditor => Snapshot?.CanEdit == true;
    public bool CanEdit => ShowEditor && !IsBusy;
    private ProviderQuotaResponse? Snapshot => _snapshot?.ProviderId == ProviderId ? _snapshot : null;
    public string MinuteAllowanceLabel => $"Minute allowance ({Snapshot?.Unit ?? "units"})";
    public string DailyLimitLabel => Tier is { DefaultDailyLimit: null }
        ? $"Meridian daily cap ({Snapshot?.Unit ?? "units"})"
        : $"Daily limit ({Snapshot?.Unit ?? "units"})";
    public string TierGuidance => Tier is { } tier
        ? $"Use whole numbers between 1 and {tier.MinuteCeiling} for minute limits and between 1 and {tier.DailyCeiling} for the daily limit."
        : "Select the subscribed vendor tier.";

    public string SavedLimits => Snapshot is { Saved: { } saved } quota
        ? $"{saved.UnitsPerMinute} {quota.Unit}/minute · {FormatDaily(saved.DailyLimit, quota.Unit)} · {saved.RequestsPerMinute} requests/minute pacing"
        : "Adapter-managed limits.";

    public string RunningLimits => Snapshot is { Running: { } running } quota
        ? $"{running.MaximumPerWindow} {quota.Unit} per {running.WindowSeconds:g}s · {FormatDaily(running.DailyLimit, quota.Unit)} · {running.MinimumDelaySeconds:g}s minimum between requests"
        : "No initialized adapter has reported a running budget.";

    public string ResetPolicy => Snapshot?.DailyResetPolicy switch
    {
        "utc-midnight" => "Daily credits reset at midnight UTC.",
        "rolling-24-hours" => "Daily requests use a rolling 24-hour window.",
        _ => ""
    };

    public void Cancel()
    {
        LoadCommand.Cancel();
        SaveCommand.Cancel();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Error = null;
        Notice = "Loading provider limits.";
        try
        {
            var quota = _client.ReadAsync(ProviderId, ct);
            var history = LoadHistoryAsync(ProviderId, ct);
            await Task.WhenAll(quota, history);
            ct.ThrowIfCancellationRequested();
            Apply(await quota, "Provider limits loaded.");
            var retained = await history;
            DailyRollups = retained?.DailyRollups ?? [];
            HistoryNotice = retained?.DailyRollupsAvailable == true
                ? "Daily throttle and rotation totals, last seven UTC dates."
                : "Durable daily totals may be incomplete.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Notice = "Provider limits could not be refreshed.";
        }
        catch (InvalidOperationException error)
        {
            Error = error.Message;
            Notice = "Provider limits could not be loaded.";
        }
        finally { IsBusy = false; }
    }

    private async Task<ProviderRateLimitHistoryResponse?> LoadHistoryAsync(string providerId, CancellationToken ct)
    {
        try
        { return await _client.ReadHistoryAsync(providerId, ct); }
        catch (InvalidOperationException) { return null; }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        if (!CanEdit || BuildEdit() is not { } edit)
            return;
        IsBusy = true;
        Error = null;
        Notice = "Saving provider limits.";
        try
        {
            var saved = await _client.SaveAsync(ProviderId, edit, ct);
            ct.ThrowIfCancellationRequested();
            Apply(saved, "Provider limits saved.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Notice = "Save status is unknown. Load limits to verify the host response.";
        }
        catch (InvalidOperationException error)
        {
            Error = error.Message;
            Notice = "Provider limits could not be saved.";
        }
        finally { IsBusy = false; }
    }

    private ProviderQuotaEditDto? BuildEdit()
        => Tier is { } tier
            && TryParseLimit(Minute, tier.MinuteCeiling, out var minute)
            && TryParseLimit(Daily, tier.DailyCeiling, out var daily)
            && TryParseLimit(Pacing, tier.MinuteCeiling, out var pacing)
                ? new(tier.Id, minute, daily, pacing) : null;

    private static bool TryParseLimit(string value, int maximum, out int parsed)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0 && parsed <= maximum;

    private void Apply(ProviderQuotaResponse quota, string message)
    {
        _snapshot = quota;
        OnPropertyChanged(nameof(Tiers));
        if (quota.Saved is { } saved)
        {
            Tier = quota.Tiers.FirstOrDefault(tier => tier.Id == saved.TierId);
            Minute = saved.UnitsPerMinute.ToString(CultureInfo.InvariantCulture);
            Daily = saved.DailyLimit?.ToString(CultureInfo.InvariantCulture) ?? "";
            Pacing = saved.RequestsPerMinute.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            Tier = null;
        }
        Notice = quota.PendingRestart ? "Saved changes take effect after host restart." : message;
        UpdateProjection();
    }

    private void UpdateProjection()
    {
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Tiers));
        OnPropertyChanged(nameof(SavedLimits));
        OnPropertyChanged(nameof(RunningLimits));
        OnPropertyChanged(nameof(ResetPolicy));
        OnPropertyChanged(nameof(MinuteAllowanceLabel));
        OnPropertyChanged(nameof(DailyLimitLabel));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static string FormatDaily(int? limit, string unit)
        => limit is { } amount ? $"{amount} {unit}/day" : "No daily cap reported";
}

public sealed record ProviderQuotaChoice(string Id, string Label);
