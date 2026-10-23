using System.Text.Json.Serialization;

namespace Meridian.Contracts.Calendars;

/// <summary>
/// How a date that is not a business day is moved onto one, following the FpML
/// <c>businessDayConvention</c> vocabulary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BusinessDayConvention>))]
public enum BusinessDayConvention
{
    /// <summary>The date is kept as it is, business day or not.</summary>
    Unadjusted = 0,

    /// <summary>The date moves to the first business day on or after it.</summary>
    Following,

    /// <summary>
    /// The date moves to the first business day on or after it, unless that day falls in a later
    /// calendar month; then it moves to the last business day on or before it.
    /// </summary>
    ModifiedFollowing,

    /// <summary>The date moves to the last business day on or before it.</summary>
    Preceding,

    /// <summary>
    /// The date moves to the last business day on or before it, unless that day falls in an earlier
    /// calendar month; then it moves to the first business day on or after it.
    /// </summary>
    ModifiedPreceding,
}
