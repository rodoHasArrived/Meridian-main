using System.Globalization;
using System.Text;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Identifies this process as a Security Master "node" for cross-node projection-cache
/// notifications. A node ignores the notifications it emitted itself, because its own write path
/// already upserted its cache.
/// </summary>
public sealed class SecurityMasterNodeIdentity
{
    /// <summary>The identity shared by every component of this process.</summary>
    public static SecurityMasterNodeIdentity Process { get; } = new(Guid.NewGuid());

    public SecurityMasterNodeIdentity(Guid nodeId)
    {
        if (nodeId == Guid.Empty)
        {
            throw new ArgumentException("A Security Master node id must not be empty.", nameof(nodeId));
        }

        NodeId = nodeId;
    }

    public Guid NodeId { get; }
}

/// <summary>What a projection-change notification asks the receiving node to do.</summary>
public enum SecurityProjectionChangeKind
{
    /// <summary>One security's projection was committed at <see cref="SecurityProjectionChangeNotification.Version"/>.</summary>
    Security,

    /// <summary>
    /// A bulk publish (e.g. a full projection rebuild) committed more securities than are worth
    /// announcing one by one; the receiver re-synchronises its whole cache.
    /// </summary>
    Resync
}

/// <summary>
/// A committed Security Master projection change, as carried by PostgreSQL <c>NOTIFY</c>.
/// </summary>
/// <remarks>
/// The wire payload is a compact pipe-delimited text form (well under PostgreSQL's 8000-byte
/// payload limit) rather than JSON, so no serializer context is involved:
/// <c>v1|&lt;node&gt;|s|&lt;security&gt;|&lt;version&gt;</c> or <c>v1|&lt;node&gt;|r</c>.
/// </remarks>
public sealed record SecurityProjectionChangeNotification(
    Guid OriginNodeId,
    SecurityProjectionChangeKind Kind,
    Guid SecurityId,
    long Version)
{
    private const string FormatVersion = "v1";
    private const string SecurityTag = "s";
    private const string ResyncTag = "r";

    /// <summary>
    /// Bulk publishes above this many records emit one <see cref="SecurityProjectionChangeKind.Resync"/>
    /// notification instead of one notification per security, so a full rebuild does not make every
    /// other node re-read the master one security at a time.
    /// </summary>
    public const int MaxPerSecurityNotificationsPerBatch = 256;

    public static SecurityProjectionChangeNotification ForSecurity(Guid originNodeId, Guid securityId, long version)
        => new(originNodeId, SecurityProjectionChangeKind.Security, securityId, version);

    public static SecurityProjectionChangeNotification ForResync(Guid originNodeId)
        => new(originNodeId, SecurityProjectionChangeKind.Resync, Guid.Empty, 0);

    public string ToPayload()
        => Kind == SecurityProjectionChangeKind.Resync
            ? string.Join('|', FormatVersion, OriginNodeId.ToString("N"), ResyncTag)
            : string.Join(
                '|',
                FormatVersion,
                OriginNodeId.ToString("N"),
                SecurityTag,
                SecurityId.ToString("N"),
                Version.ToString(CultureInfo.InvariantCulture));

    public static bool TryParse(string? payload, out SecurityProjectionChangeNotification? notification)
    {
        notification = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length < 3
            || !string.Equals(parts[0], FormatVersion, StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[1], "N", out var originNodeId)
            || originNodeId == Guid.Empty)
        {
            return false;
        }

        if (parts.Length == 3 && string.Equals(parts[2], ResyncTag, StringComparison.Ordinal))
        {
            notification = ForResync(originNodeId);
            return true;
        }

        if (parts.Length == 5
            && string.Equals(parts[2], SecurityTag, StringComparison.Ordinal)
            && Guid.TryParseExact(parts[3], "N", out var securityId)
            && securityId != Guid.Empty
            && long.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            notification = ForSecurity(originNodeId, securityId, version);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The PostgreSQL notification channel for a Security Master schema. Scoped by schema so two
    /// Security Masters sharing one database (or parallel test schemas) do not cross-invalidate.
    /// Always a lowercase identifier of at most 63 bytes; long schema names are hashed.
    /// </summary>
    public static string ChannelFor(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        const string prefix = "meridian_sm_projection_";
        const int maxIdentifierLength = 63;

        var normalized = new StringBuilder(schema.Length);
        foreach (var c in schema.ToLowerInvariant())
        {
            normalized.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        var candidate = prefix + normalized;
        if (candidate.Length <= maxIdentifierLength
            && string.Equals(normalized.ToString(), schema, StringComparison.Ordinal))
        {
            return candidate;
        }

        // Normalization or truncation could alias two schemas; a hash of the exact schema keeps
        // the channel distinct.
        var hash = Sha256Digest.ComputeUtf8(schema)[..16];
        var room = maxIdentifierLength - prefix.Length - hash.Length - 1;
        var head = normalized.Length <= room ? normalized.ToString() : normalized.ToString(0, room);
        return $"{prefix}{head}_{hash}";
    }
}
