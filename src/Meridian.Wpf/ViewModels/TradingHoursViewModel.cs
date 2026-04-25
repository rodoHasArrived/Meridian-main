using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Trading Hours page.
/// Fetches live market state from the calendar API; falls back to a local
/// Eastern-Time calculation when the backend is offline.
/// All status-banner logic, timezone handling, and holiday data loading live here.
/// </summary>
public sealed class TradingHoursViewModel : BindableBase
{
    private readonly ApiClientService _apiClient;

    // ── Status banner properties ──────────────────────────────────────────────────────
    private Brush _marketStatusDotFill = Brushes.Gray;
    public Brush MarketStatusDotFill { get => _marketStatusDotFill; private set => SetProperty(ref _marketStatusDotFill, value); }

    private string _marketStatusText = "--";
    public string MarketStatusText { get => _marketStatusText; private set => SetProperty(ref _marketStatusText, value); }

    private Brush _marketStatusTextForeground = Brushes.Gray;
    public Brush MarketStatusTextForeground { get => _marketStatusTextForeground; private set => SetProperty(ref _marketStatusTextForeground, value); }

    private string _marketStatusReasonText = string.Empty;
    public string MarketStatusReasonText { get => _marketStatusReasonText; private set => SetProperty(ref _marketStatusReasonText, value); }

    private string _nextSessionText = string.Empty;
    public string NextSessionText { get => _nextSessionText; private set => SetProperty(ref _nextSessionText, value); }

    private string _marketStatusTimeText = string.Empty;
    public string MarketStatusTimeText { get => _marketStatusTimeText; private set => SetProperty(ref _marketStatusTimeText, value); }

    // ── Exchange row properties ───────────────────────────────────────────────────────
    private Brush _nyseRegularStatusDotFill = Brushes.Gray;
    public Brush NyseRegularStatusDotFill { get => _nyseRegularStatusDotFill; private set => SetProperty(ref _nyseRegularStatusDotFill, value); }

    private string _nyseRegularStatusText = "Closed";
    public string NyseRegularStatusText { get => _nyseRegularStatusText; private set => SetProperty(ref _nyseRegularStatusText, value); }

    private Brush _nyseRegularStatusForeground = Brushes.Gray;
    public Brush NyseRegularStatusForeground { get => _nyseRegularStatusForeground; private set => SetProperty(ref _nyseRegularStatusForeground, value); }

    private Brush _nasdaqRegularStatusDotFill = Brushes.Gray;
    public Brush NasdaqRegularStatusDotFill { get => _nasdaqRegularStatusDotFill; private set => SetProperty(ref _nasdaqRegularStatusDotFill, value); }

    private string _nasdaqRegularStatusText = "Closed";
    public string NasdaqRegularStatusText { get => _nasdaqRegularStatusText; private set => SetProperty(ref _nasdaqRegularStatusText, value); }

    private Brush _nasdaqRegularStatusForeground = Brushes.Gray;
    public Brush NasdaqRegularStatusForeground { get => _nasdaqRegularStatusForeground; private set => SetProperty(ref _nasdaqRegularStatusForeground, value); }

    // ── Holidays ──────────────────────────────────────────────────────────────────────
    private string _holidaysHeaderText = "US Market Holidays";
    public string HolidaysHeaderText { get => _holidaysHeaderText; private set => SetProperty(ref _holidaysHeaderText, value); }

    public ObservableCollection<HolidayDisplayItem> Holidays { get; } = new();

    public TradingHoursViewModel(ApiClientService apiClient)
    {
        _apiClient = apiClient;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await LoadMarketStatusAsync(ct);
        await LoadHolidaysAsync(ct);
    }

    // ── Data loading ──────────────────────────────────────────────────────────────────

    private async Task LoadMarketStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _apiClient.GetAsync<CalendarStatusResponse>(UiApiRoutes.CalendarStatus);
            if (response?.Market is not null)
            {
                UpdateStatusBanner(
                    response.Market.State ?? "Closed",
                    response.Market.Reason ?? "",
                    response.Market.NextSessionStart);
                UpdateSessionRows(response.Market.State ?? "Closed");
                return;
            }
        }
        catch (Exception)
        {
            // Backend offline – fall through to local calculation
        }

        ApplyLocalMarketStatus();
    }

    private void ApplyLocalMarketStatus()
    {
        var eastern = GetEasternTimeZone();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern);
        var time = TimeOnly.FromDateTime(now.DateTime);
        var day = now.DayOfWeek;

        string state;
        string reason;

        if (day is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            state = "Closed";
            reason = "Weekend";
        }
        else if (time >= new TimeOnly(4, 0) && time < new TimeOnly(9, 30))
        {
            state = "PreMarket";
            reason = "Pre-market session";
        }
        else if (time >= new TimeOnly(9, 30) && time < new TimeOnly(16, 0))
        {
            state = "Open";
            reason = "Regular trading session";
        }
        else if (time >= new TimeOnly(16, 0) && time < new TimeOnly(20, 0))
        {
            state = "AfterHours";
            reason = "After-hours session";
        }
        else
        {
            state = "Closed";
            reason = "Outside trading hours";
        }

        UpdateStatusBanner(state, reason, nextSession: null);
        UpdateSessionRows(state);
    }

    private void UpdateStatusBanner(string state, string reason, DateTimeOffset? nextSession)
    {
        var (label, dotColor, textColor) = state switch
        {
            "Open" => ("Open", GetResource("SuccessColorBrush", Brushes.LimeGreen), GetResource("SuccessColorBrush", Brushes.LimeGreen)),
            "PreMarket" => ("Pre-Market", GetResource("InfoColorBrush", Brushes.CornflowerBlue), GetResource("InfoColorBrush", Brushes.CornflowerBlue)),
            "AfterHours" => ("After-Hours", GetResource("WarningColorBrush", Brushes.Orange), GetResource("WarningColorBrush", Brushes.Orange)),
            _ => ("Closed", GetResource("ErrorColorBrush", Brushes.Red), GetResource("ErrorColorBrush", Brushes.Red))
        };

        MarketStatusDotFill = dotColor;
        MarketStatusText = label;
        MarketStatusTextForeground = textColor;
        MarketStatusReasonText = reason;
        MarketStatusTimeText = $"As of {DateTime.UtcNow:HH:mm} UTC";

        if (nextSession.HasValue)
        {
            var eastern = GetEasternTimeZone();
            var local = TimeZoneInfo.ConvertTime(nextSession.Value, eastern);
            NextSessionText = $"Next session: {local:ddd, MMM d} at {local:h:mm tt} ET";
        }
        else
        {
            NextSessionText = string.Empty;
        }
    }

    private void UpdateSessionRows(string state)
    {
        var isOpen = state == "Open";
        var openBrush = GetResource("SuccessColorBrush", Brushes.LimeGreen);
        var closedBrush = GetResource("ErrorColorBrush", Brushes.Red);

        NyseRegularStatusDotFill = isOpen ? openBrush : closedBrush;
        NyseRegularStatusText = isOpen ? "Open" : "Closed";
        NyseRegularStatusForeground = isOpen ? openBrush : closedBrush;

        NasdaqRegularStatusDotFill = isOpen ? openBrush : closedBrush;
        NasdaqRegularStatusText = isOpen ? "Open" : "Closed";
        NasdaqRegularStatusForeground = isOpen ? openBrush : closedBrush;
    }

    private async Task LoadHolidaysAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        HolidaysHeaderText = $"US Market Holidays ({year})";

        try
        {
            var response = await _apiClient.GetAsync<CalendarHolidaysResponse>(
                $"{UiApiRoutes.CalendarHolidays}?year={year}");
            if (response?.Holidays is not null)
            {
                Holidays.Clear();
                foreach (var h in response.Holidays)
                {
                    if (DateOnly.TryParse(h.Date, out var date))
                    {
                        Holidays.Add(new HolidayDisplayItem
                        {
                            DateText = $"{date:MMM d} ({date:ddd})",
                            Name = h.Name ?? string.Empty
                        });
                    }
                }
                return;
            }
        }
        catch (Exception)
        {
            // Fall through – show empty list
        }

        Holidays.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static TimeZoneInfo GetEasternTimeZone()
    {
        try
        { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }

    private static Brush GetResource(string key, Brush fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    // ── Nested display model ──────────────────────────────────────────────────────────

    public sealed class HolidayDisplayItem
    {
        public string DateText { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}

// Calendar API response DTOs (kept in this file — only used by this ViewModel)
internal sealed class CalendarStatusResponse
{
    [JsonPropertyName("market")]
    public CalendarMarketInfo? Market { get; set; }
}

internal sealed class CalendarMarketInfo
{
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("nextSessionStart")]
    public DateTimeOffset? NextSessionStart { get; set; }
}

internal sealed class CalendarHolidaysResponse
{
    [JsonPropertyName("holidays")]
    public List<CalendarHolidayItem>? Holidays { get; set; }
}

internal sealed class CalendarHolidayItem
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
