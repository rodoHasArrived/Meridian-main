using System;
using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

/// <summary>
/// One row of the Security Master History tab: the shared change-history read model plus
/// ADR-034 valid-time display text computed once, so the view binds strings instead of
/// branching in XAML.
/// </summary>
public sealed record SecurityMasterChangeHistoryRow(
    SecurityMasterChangeHistoryItemDto Item,
    string ValidFromText,
    string ValidToText)
{
    /// <summary>Shown for entries that carry no valid time (staged edits, lifecycle actions).</summary>
    public const string NotApplicableText = "—";

    /// <summary>Shown when the event still governs from its valid-from onwards.</summary>
    public const string OpenText = "open";

    public DateTimeOffset ChangedAtUtc => Item.ChangedAtUtc;
    public string EventType => Item.EventType;
    public long StreamVersion => Item.StreamVersion;
    public string Origin => Item.Origin;
    public string Actor => Item.Actor;
    public string SourceSystem => Item.SourceSystem;
    public string ChangedFieldsSummary => Item.ChangedFieldsSummary;
    public string Summary => Item.Summary;

    /// <summary>
    /// Projects a change-history item into a row. Dates use the general short pattern ("g"),
    /// matching the grid's Changed column.
    /// </summary>
    public static SecurityMasterChangeHistoryRow From(
        SecurityMasterChangeHistoryItemDto item,
        IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var provider = formatProvider ?? CultureInfo.CurrentCulture;
        return new SecurityMasterChangeHistoryRow(
            item,
            FormatValidFrom(item, provider),
            FormatValidTo(item, provider));
    }

    public static string FormatValidFrom(SecurityMasterChangeHistoryItemDto item, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.ValidFromUtc is { } validFrom
            ? validFrom.ToString("g", formatProvider ?? CultureInfo.CurrentCulture)
            : NotApplicableText;
    }

    /// <summary>
    /// "superseded by vN" when a later same-valid-from recording replaced the event (it governs no
    /// period); otherwise the exclusive end as known now; "open" when it still governs; and the
    /// not-applicable dash when the entry carries no valid time.
    /// </summary>
    public static string FormatValidTo(SecurityMasterChangeHistoryItemDto item, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.SupersededByStreamVersion is { } supersededBy)
        {
            return string.Create(CultureInfo.InvariantCulture, $"superseded by v{supersededBy}");
        }

        if (item.ValidToUtc is { } validTo)
        {
            return validTo.ToString("g", formatProvider ?? CultureInfo.CurrentCulture);
        }

        return item.ValidFromUtc is null ? NotApplicableText : OpenText;
    }
}
