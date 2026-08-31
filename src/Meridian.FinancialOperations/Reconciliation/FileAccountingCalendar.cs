using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Loads the production <see cref="IAccountingCalendar"/> from an operator-maintained calendar file
/// under the deployment data root (<c>reconciliation/business-calendar.json</c>), mirroring the
/// <see cref="FileReconciliationFxRateProvider"/> pattern. This is the seam that turns the
/// weekends-only default into a real market calendar (exchange holidays, regional weekends).
/// </summary>
/// <remarks>
/// A missing, empty, or malformed file yields the weekends-only
/// <see cref="BusinessDayAccountingCalendar.Default"/>, so reconciliation always has a working
/// calendar and never fabricates holiday knowledge it was not given. File format:
/// <code>
/// { "weekendDays": ["Saturday", "Sunday"], "holidays": ["2026-01-01", "2026-07-03"] }
/// </code>
/// </remarks>
public static class FileAccountingCalendar
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public const string RelativePath = "reconciliation/business-calendar.json";

    public static IAccountingCalendar Load(string dataRoot, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var path = Path.Combine(dataRoot, "reconciliation", "business-calendar.json");
        if (!File.Exists(path))
        {
            return BusinessDayAccountingCalendar.Default;
        }

        try
        {
            var document = JsonSerializer.Deserialize<BusinessCalendarFile>(File.ReadAllText(path), JsonOptions);
            if (document is null)
            {
                return BusinessDayAccountingCalendar.Default;
            }

            return new BusinessDayAccountingCalendar(document.Holidays, document.WeekendDays);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            // Fail safe: an unreadable, malformed, or degenerate calendar must never stall
            // reconciliation — fall back to the weekends-only default and tell the operator.
            logger?.LogWarning(
                ex,
                "Failed to load reconciliation business calendar from {RelativePath}; using the weekends-only default calendar.",
                RelativePath);
            return BusinessDayAccountingCalendar.Default;
        }
    }

    private sealed record BusinessCalendarFile(
        IReadOnlyList<DayOfWeek>? WeekendDays,
        IReadOnlyList<DateOnly>? Holidays);
}
