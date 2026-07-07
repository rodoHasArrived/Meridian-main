using System.Globalization;

namespace Meridian.Reporting;

/// <summary>
/// Shared numeric formatting for reporting surfaces. Centralizes the canonical
/// six-decimal, invariant-culture rendering used by rendered grids and snapshot diffs so the
/// format is defined once rather than duplicated per engine.
/// </summary>
internal static class ReportingNumberFormat
{
    /// <summary>
    /// Renders a decimal with up to six fractional digits using the invariant culture,
    /// trimming trailing zeros (e.g. <c>1.5</c> rather than <c>1.500000</c>).
    /// </summary>
    public static string FormatDecimal(decimal value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
