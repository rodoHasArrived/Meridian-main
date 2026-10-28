using System.Globalization;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// The identity rule of the factor-paydown projection: the model event type and key the projector
/// emits inside its lineage, and the deterministic event identity it derives from Security Master
/// identity, book position, effective date, both factors and the retained source-content hash.
/// Owned here so the Instruments projector and the Contracts paydown instruction recompute one
/// identity rather than two copies that can drift.
/// </summary>
public static class FactorPaydownProjectionIdentity
{
    /// <summary>The model's own event type; the canonical spine event it feeds is typed AssetAccounting.CorporateAction.</summary>
    public const string EventType = "MbsFactorPaydown";

    public const string ModelKey = "mbs-factor-paydown";

    public static Guid EventId(
        Guid securityId,
        Guid positionId,
        DateOnly effectiveDate,
        decimal priorFactor,
        decimal currentFactor,
        string sourceContentHash)
    {
        ArgumentNullException.ThrowIfNull(sourceContentHash);
        return DeterministicGuid(string.Join(
            '|',
            securityId.ToString("N"),
            positionId.ToString("N"),
            effectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            priorFactor.ToString("G29", CultureInfo.InvariantCulture),
            currentFactor.ToString("G29", CultureInfo.InvariantCulture),
            sourceContentHash.Trim().ToLowerInvariant()));
    }

    /// <summary>A version-5-shaped GUID from the SHA-256 of <paramref name="value"/>.</summary>
    public static Guid DeterministicGuid(string value)
    {
        var hash = Sha256Digest.ComputeBytesUtf8(value);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
