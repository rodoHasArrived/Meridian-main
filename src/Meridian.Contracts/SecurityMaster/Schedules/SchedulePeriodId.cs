using System.Globalization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Text;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>
/// Deterministic identity for a schedule period, keyed on its unadjusted dates within a
/// consumer-chosen scope and stream, so a calendar, business-day convention or payment-lag change never
/// re-identifies a period.
/// </summary>
/// <remarks>
/// <para>
/// The id is a name-based (version 5 style) GUID: the first 16 bytes of the SHA-256 of
/// <c>namespace|meridian/schedule-period/v1|scope|streamKey|yyyy-MM-dd|yyyy-MM-dd</c>, with the version
/// and variant bits set, derived the same way as Security Master underlying-link ids.
/// </para>
/// <para>
/// The scope is the consumer's: for example the security id for asset-operations flows, whose
/// published projections are replaced per security, or the projection run id where flow ids are a
/// global primary key.
/// </para>
/// </remarks>
public static class SchedulePeriodId
{
    // Chosen once for this derivation; changing it would re-identify every period.
    private static readonly Guid Namespace = Guid.Parse("780e78eb-4b9c-4b04-8b13-233f6a94a559");

    private const string Discriminator = "meridian/schedule-period/v1";

    /// <summary>The id of <paramref name="period"/> within <paramref name="scopeId"/> and <paramref name="streamKey"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="period"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="streamKey"/> is blank.</exception>
    public static Guid Derive(Guid scopeId, string streamKey, SchedulePeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return Derive(scopeId, streamKey, period.UnadjustedStart, period.UnadjustedEnd);
    }

    /// <summary>The id of the period [<paramref name="unadjustedStart"/>, <paramref name="unadjustedEnd"/>] within <paramref name="scopeId"/> and <paramref name="streamKey"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="streamKey"/> is blank.</exception>
    public static Guid Derive(Guid scopeId, string streamKey, DateOnly unadjustedStart, DateOnly unadjustedEnd)
    {
        var identity = string.Join(
            "|",
            Namespace.ToString("D"),
            Discriminator,
            scopeId.ToString("D"),
            TextPrimitives.RequireText(streamKey),
            FormatDate(unadjustedStart),
            FormatDate(unadjustedEnd));
        var hash = Sha256Digest.ComputeBytesUtf8(identity);
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        // Mark as a name-based (version 5 style) GUID so it is never mistaken for a random one.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>The period's unadjusted dates as <c>yyyy-MM-dd/yyyy-MM-dd</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="period"/> is null.</exception>
    public static string PeriodKey(SchedulePeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return FormatDate(period.UnadjustedStart) + "/" + FormatDate(period.UnadjustedEnd);
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
