using System.Text.Json;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering trading calendar API endpoints.
/// Provides market status, holidays, and trading day information.
/// </summary>
public static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Calendar");

        // Current market status
        group.MapGet(UiApiRoutes.CalendarStatus, () =>
        {
            var calendar = new TradingCalendar();
            var status = calendar.GetCurrentStatus();
            var timeUntilOpen = calendar.GetTimeUntilOpen();
            var timeUntilClose = calendar.GetTimeUntilClose();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var isTradingDay = calendar.IsTradingDay(today);

            return Results.Json(new
            {
                timestamp = DateTimeOffset.UtcNow,
                market = new
                {
                    state = status.State.ToString(),
                    reason = status.Reason,
                    isRegularHours = status.IsRegularTradingHours,
                    isAnySessionActive = status.IsAnySessionActive,
                    isHalfDay = status.IsHalfDay,
                    isTradingDay,
                    currentSessionStart = status.CurrentSessionStart,
                    currentSessionEnd = status.CurrentSessionEnd,
                    nextSessionStart = status.NextSessionStart,
                    timeRemainingInSession = status.TimeRemainingInSession?.ToString(@"hh\:mm\:ss"),
                    timeUntilOpen = timeUntilOpen?.ToString(@"hh\:mm\:ss"),
                    timeUntilClose = timeUntilClose?.ToString(@"hh\:mm\:ss")
                },
                sessions = new
                {
                    preMarketOpen = TradingCalendar.PreMarketOpen.ToString("HH:mm"),
                    regularOpen = TradingCalendar.RegularMarketOpen.ToString("HH:mm"),
                    regularClose = TradingCalendar.RegularMarketClose.ToString("HH:mm"),
                    halfDayClose = TradingCalendar.HalfDayClose.ToString("HH:mm"),
                    afterHoursClose = TradingCalendar.AfterHoursClose.ToString("HH:mm"),
                    timezone = "America/New_York"
                }
            }, jsonOptions);
        })
        .WithName("GetCalendarStatus")
        .WithDescription("Returns current US market status including session state, next open/close times, and trading day info.")
        .Produces(200);

        // Holidays for a given year
        group.MapGet(UiApiRoutes.CalendarHolidays, ([FromQuery] int? year) =>
        {
            var calendar = new TradingCalendar();
            var targetYear = year ?? DateTime.UtcNow.Year;
            var holidays = calendar.GetHolidays(targetYear);
            var halfDays = calendar.GetHalfDays(targetYear);

            return Results.Json(new
            {
                year = targetYear,
                holidays = holidays.Select(h => new
                {
                    date = h.Date.ToString("yyyy-MM-dd"),
                    name = h.Name
                }),
                halfDays = halfDays.Select(d => d.ToString("yyyy-MM-dd")),
                holidayCount = holidays.Count,
                halfDayCount = halfDays.Count
            }, jsonOptions);
        })
        .WithName("GetCalendarHolidays")
        .WithDescription("Returns market holidays and half-days for a given year (defaults to current year).")
        .Produces(200);

        // Trading days for a date range
        group.MapGet(UiApiRoutes.CalendarTradingDays, (
            [FromQuery] string? from,
            [FromQuery] string? to) =>
        {
            var calendar = new TradingCalendar();
            var fromDate = DateOnly.TryParse(from, out var fd)
                ? fd
                : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
            var toDate = DateOnly.TryParse(to, out var td)
                ? td
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var tradingDays = calendar.GetTradingDays(fromDate, toDate);
            var totalDays = toDate.DayNumber - fromDate.DayNumber + 1;

            return Results.Json(new
            {
                from = fromDate.ToString("yyyy-MM-dd"),
                to = toDate.ToString("yyyy-MM-dd"),
                tradingDayCount = tradingDays.Count,
                totalCalendarDays = totalDays,
                tradingDays = tradingDays.Select(d => new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    isHalfDay = calendar.IsHalfDay(d)
                })
            }, jsonOptions);
        })
        .WithName("GetCalendarTradingDays")
        .WithDescription("Returns trading days within a date range, including half-day indicators.")
        .Produces(200);
    }
}
