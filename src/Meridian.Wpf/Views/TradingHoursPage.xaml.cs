using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Contracts.Api;

namespace Meridian.Wpf.Views;

/// <summary>
/// Trading hours page showing live US market status, session schedule, and upcoming holidays.
/// Fetches live market state from the calendar API when the backend is running;
/// falls back to a local Eastern-Time calculation when the backend is offline.
/// </summary>
public partial class TradingHoursPage : Page
{
    private readonly ApiClientService _apiClient;

    public TradingHoursPage()
    {
        InitializeComponent();
        _apiClient = ApiClientService.Instance;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadMarketStatusAsync();
        await LoadHolidaysAsync();
    }

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
        var easternZone = GetEasternTimeZone();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, easternZone);
        var time = TimeOnly.FromDateTime(now.DateTime);
        var dayOfWeek = now.DayOfWeek;

        string state;
        string reason;

        if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
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
            "Open" => ("Open", (Brush)FindResource("SuccessColorBrush"), (Brush)FindResource("SuccessColorBrush")),
            "PreMarket" => ("Pre-Market", (Brush)FindResource("InfoColorBrush"), (Brush)FindResource("InfoColorBrush")),
            "AfterHours" => ("After-Hours", (Brush)FindResource("WarningColorBrush"), (Brush)FindResource("WarningColorBrush")),
            _ => ("Closed", (Brush)FindResource("ErrorColorBrush"), (Brush)FindResource("ErrorColorBrush"))
        };

        MarketStatusDot.Fill = dotColor;
        MarketStatusText.Text = label;
        MarketStatusText.Foreground = textColor;
        MarketStatusReasonText.Text = reason;

        if (nextSession.HasValue)
        {
            var eastern = GetEasternTimeZone();
            var local = TimeZoneInfo.ConvertTime(nextSession.Value, eastern);
            NextSessionText.Text = $"Next session: {local:ddd, MMM d} at {local:h:mm tt} ET";
        }
        else
        {
            NextSessionText.Text = "";
        }

        MarketStatusTimeText.Text = $"As of {DateTime.UtcNow:HH:mm} UTC";
    }

    private void UpdateSessionRows(string state)
    {
        var isRegularOpen = state == "Open";
        var openDot = (Brush)FindResource("SuccessColorBrush");
        var closedDot = (Brush)FindResource("ErrorColorBrush");
        var openText = (Brush)FindResource("SuccessColorBrush");
        var closedText = (Brush)FindResource("ErrorColorBrush");

        NyseRegularStatusDot.Fill = isRegularOpen ? openDot : closedDot;
        NyseRegularStatusText.Text = isRegularOpen ? "Open" : "Closed";
        NyseRegularStatusText.Foreground = isRegularOpen ? openText : closedText;

        NasdaqRegularStatusDot.Fill = isRegularOpen ? openDot : closedDot;
        NasdaqRegularStatusText.Text = isRegularOpen ? "Open" : "Closed";
        NasdaqRegularStatusText.Foreground = isRegularOpen ? openText : closedText;
    }

    private async Task LoadHolidaysAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        HolidaysHeaderText.Text = $"US Market Holidays ({year})";

        try
        {
            var response = await _apiClient.GetAsync<CalendarHolidaysResponse>(
                $"{UiApiRoutes.CalendarHolidays}?year={year}");
            if (response?.Holidays is not null)
            {
                var items = new List<HolidayDisplayItem>();
                foreach (var h in response.Holidays)
                {
                    if (DateOnly.TryParse(h.Date, out var date))
                    {
                        items.Add(new HolidayDisplayItem
                        {
                            DateText = $"{date:MMM d} ({date:ddd})",
                            Name = h.Name ?? ""
                        });
                    }
                }
                HolidaysList.ItemsSource = items;
                return;
            }
        }
        catch (Exception)
        {
            // Fall through to show empty list
        }

        HolidaysList.ItemsSource = Array.Empty<HolidayDisplayItem>();
    }

    private static TimeZoneInfo GetEasternTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }
}

/// <summary>Display model for a single market holiday row.</summary>
public sealed class HolidayDisplayItem
{
    public string DateText { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

// Calendar API response DTOs
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
