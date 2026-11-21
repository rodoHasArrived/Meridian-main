namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// One retained revision of a <c>security_identifiers</c> row (migration 062). The identity is
/// (security, kind, value, valid-from); <see cref="ChangeKind"/> is one of
/// <see cref="SecurityIdentifierRevisionKinds"/>. <see cref="IdentifierKind"/> is the stored text,
/// so a kind this node does not know is reported as written rather than as <c>Unknown</c>.
/// </summary>
public sealed record SecurityIdentifierRevision(
    Guid SecurityId,
    string IdentifierKind,
    string IdentifierValue,
    DateTimeOffset ValidFrom,
    int Revision,
    string ChangeKind,
    string NormalizedIdentifierValue,
    string? Provider,
    string? NormalizedProvider,
    bool IsPrimary,
    DateTimeOffset? ValidTo,
    string Source,
    decimal? Confidence,
    bool ManualOverride,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? HistoryAvailableFrom);

/// <summary>The change kinds a <see cref="SecurityIdentifierRevision"/> records.</summary>
public static class SecurityIdentifierRevisionKinds
{
    /// <summary>A row that existed when history retention began (migration 062); its earlier history is unknown.</summary>
    public const string Baseline = "Baseline";

    /// <summary>The projection began holding this identity, or held it again after a removal.</summary>
    public const string Added = "Added";

    /// <summary>A held identity's row changed other than by first setting its valid-to.</summary>
    public const string Amended = "Amended";

    /// <summary>A held identity's valid-to was first set: the identifier stopped being valid at that valid time.</summary>
    public const string Expired = "Expired";

    /// <summary>The projection no longer holds this identity; the values repeat its last revision.</summary>
    public const string Removed = "Removed";
}

/// <summary>
/// The identifier rows the projection held as recorded at <see cref="RecordedAsOfUtc"/>, optionally
/// restricted to those valid at <see cref="ValidAsOfUtc"/>. Rows migrated as a baseline are absent
/// for a cutoff before <see cref="HistoryAvailableFrom"/>: their earlier history was never retained,
/// so an incomplete or empty result before that boundary is unknown history, not evidence of none.
/// </summary>
public sealed record SecurityIdentifiersRecordedView(
    Guid SecurityId,
    DateTimeOffset RecordedAsOfUtc,
    DateTimeOffset? ValidAsOfUtc,
    IReadOnlyList<SecurityIdentifierRevision> Identifiers,
    DateTimeOffset? HistoryAvailableFrom)
{
    /// <summary>
    /// False when the cutoff precedes the migrated baseline, so rows that already existed then
    /// cannot be reported. True when no baseline applies or the cutoff is at or after it.
    /// </summary>
    public bool IsHistoryAvailable => HistoryAvailableFrom is not { } available || available <= RecordedAsOfUtc;
}

/// <summary>
/// Reads the append-only recorded history of Security Master identifier rows. Recorded cutoffs
/// filter on stored recording timestamps (transaction-start time); like the alias history, these
/// establish eligibility, not commit visibility, so a write committed later can still satisfy an
/// earlier cutoff. Valid time is a separate filter over each revision's valid-from/valid-to.
/// </summary>
public interface ISecurityIdentifierHistoryStore
{
    /// <summary>Every retained revision recorded at or before <paramref name="recordedAsOfUtc"/>, ordered by identity then revision.</summary>
    Task<IReadOnlyList<SecurityIdentifierRevision>> GetIdentifierRevisionsAsync(
        Guid securityId,
        DateTimeOffset recordedAsOfUtc,
        CancellationToken ct = default);

    /// <summary>
    /// The latest revision per identity recorded at or before <paramref name="recordedAsOfUtc"/>,
    /// omitting removed identities and, when <paramref name="validAsOfUtc"/> is given, identities not
    /// valid at that instant.
    /// </summary>
    Task<SecurityIdentifiersRecordedView> GetIdentifiersRecordedAsOfAsync(
        Guid securityId,
        DateTimeOffset recordedAsOfUtc,
        DateTimeOffset? validAsOfUtc = null,
        CancellationToken ct = default);
}
