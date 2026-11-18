using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Services.Services.Comparisons;

/// <summary>One line of a prior-period comparison, formatted for display.</summary>
/// <param name="Key">The server's line key.</param>
/// <param name="Label">The line's label.</param>
/// <param name="Current">The current figure, as shown.</param>
/// <param name="Prior">The prior figure, as shown.</param>
/// <param name="Change">Current minus prior, as shown.</param>
/// <param name="ChangePercent">The server's change percent, or the not-comparable text for a zero prior.</param>
/// <param name="IsTotal">True for the total line.</param>
/// <param name="IsMaterial">True when the change is past the materiality threshold.</param>
/// <param name="IsPriorZero">True when the prior is zero, so there is no percent.</param>
public sealed record PeriodComparisonRow(
    string Key,
    string Label,
    string Current,
    string Prior,
    string Change,
    string ChangePercent,
    bool IsTotal,
    bool IsMaterial,
    bool IsPriorZero)
{
    /// <summary>"Material" when the change is past the threshold, otherwise empty.</summary>
    public string MaterialLabel => IsMaterial ? "Material" : string.Empty;
}

/// <summary>A prior-period comparison ready to show: a heading, why it is empty if it is, rows and notes.</summary>
/// <param name="IsReady">True when the comparison has lines to show.</param>
/// <param name="Heading">The subject and currency.</param>
/// <param name="Windows">The two windows compared.</param>
/// <param name="Message">Why there is nothing to compare, when there is not.</param>
/// <param name="Rows">The lines, in the server's order.</param>
/// <param name="Notes">The server's notes on what the figures include.</param>
public sealed record PeriodComparisonView(
    bool IsReady,
    string Heading,
    string Windows,
    string? Message,
    IReadOnlyList<PeriodComparisonRow> Rows,
    IReadOnlyList<string> Notes);

/// <summary>
/// Presentation projection for <see cref="PeriodComparisonDto"/>, shared by the desktop NAV, run
/// attribution and cash ladder comparisons (UX Phase E parity).
/// <para>
/// Lives here rather than in the desktop assembly so it runs in tests on any platform. It mirrors
/// the browser's <c>period-comparison-panel.tsx</c>: amounts to two places with negatives in
/// parentheses, counts as whole numbers, the server's percent (never recomputed) to two places,
/// "Not comparable · prior zero" for a zero prior, and the Portfolio materiality threshold from
/// <c>lib/reporting-materiality.ts</c> on amount lines.
/// </para>
/// </summary>
public static class PeriodComparisonProjection
{
    /// <summary>The Portfolio policy's absolute-variance threshold in <c>lib/reporting-materiality.ts</c>.</summary>
    public const decimal PortfolioMaterialAbsoluteVariance = 250_000m;

    /// <summary>Shown in place of a percent when the prior is zero.</summary>
    public const string NotComparablePriorZero = "Not comparable · prior zero";

    /// <summary>Lines of the cash ladder comparison that are counts, not amounts.</summary>
    public static readonly IReadOnlyCollection<string> CashLadderCountKeys = ["breaches"];

    /// <summary>Formats a comparison. A null comparison reads as still loading.</summary>
    public static PeriodComparisonView Project(
        PeriodComparisonDto? comparison,
        decimal? materialThreshold = PortfolioMaterialAbsoluteVariance,
        IReadOnlyCollection<string>? countKeys = null)
    {
        if (comparison is null)
        {
            return new PeriodComparisonView(false, string.Empty, string.Empty, "Loading the comparison…", [], []);
        }

        var heading = string.IsNullOrWhiteSpace(comparison.Currency)
            ? comparison.Subject
            : $"{comparison.Subject} · {comparison.Currency}";
        var windows = comparison.Baseline is null
            ? $"Current: {comparison.Current.Label}."
            : $"Current: {comparison.Current.Label}. Prior: {comparison.Baseline.Label}.";
        var notes = comparison.Notes ?? [];
        if (!string.Equals(comparison.Status, PeriodComparisonStatus.Ready, StringComparison.Ordinal) || comparison.Lines is not { Count: > 0 })
        {
            var message = string.IsNullOrWhiteSpace(comparison.Reason) ? "There is nothing to compare for these dates." : comparison.Reason;
            return new PeriodComparisonView(false, heading, windows, message, [], notes);
        }

        var counts = countKeys ?? [];
        var rows = comparison.Lines.Select(line =>
        {
            var isCount = counts.Contains(line.Key);
            string Format(decimal value) => isCount ? FormatCount(value) : FormatAmount(value);
            return new PeriodComparisonRow(
                line.Key,
                line.Label,
                Format(line.Current),
                Format(line.Prior),
                Format(line.Change),
                FormatPercent(line.PercentChange),
                line.IsTotal,
                IsMaterial: !isCount && materialThreshold is { } threshold && Math.Abs(line.Change) > threshold,
                IsPriorZero: line.PercentChange is null);
        }).ToArray();
        return new PeriodComparisonView(true, heading, windows, null, rows, notes);
    }

    /// <summary>Two places, thousands separated, negatives in parentheses.</summary>
    public static string FormatAmount(decimal value)
    {
        var text = Math.Abs(value).ToString("N2", CultureInfo.InvariantCulture);
        return value < 0m ? $"({text})" : text;
    }

    /// <summary>A whole number, negative with a minus sign.</summary>
    public static string FormatCount(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The server's percent to two places, or the not-comparable text when there is none.</summary>
    public static string FormatPercent(decimal? percent) =>
        percent is { } value
            ? $"{Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)}%"
            : NotComparablePriorZero;

    /// <summary>What to tell the operator when a comparison request fails.</summary>
    public static string DescribeFailure<T>(ApiResponse<T> response)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.StatusCode switch
        {
            501 => "This comparison is not available in this host.",
            403 => "You do not have permission to see this comparison.",
            404 => "The comparison's subject was not found.",
            400 => string.IsNullOrWhiteSpace(response.ErrorMessage) ? "The comparison request was not valid." : response.ErrorMessage!,
            _ => string.IsNullOrWhiteSpace(response.ErrorMessage) ? "The comparison could not be loaded." : response.ErrorMessage!
        };
    }
}
