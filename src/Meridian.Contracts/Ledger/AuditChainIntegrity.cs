namespace Meridian.Contracts.Ledger;

/// <summary>
/// A store whose accounting-action audit history is hash-chained and can be verified on demand,
/// not only implicitly ahead of its next append.
/// </summary>
/// <remarks>
/// W9-GOV-008 criterion three asks for verification tooling. Both the file-backed and the
/// PostgreSQL stores already verify the whole chain before every append, but that check only
/// surfaces when someone writes; an operator asking "is the retained history intact?" needs an
/// answer without mutating anything. Implementations return a broken verification rather than
/// throwing <see cref="AccountingAuditChainIntegrityException"/>.
/// </remarks>
public interface IAccountingAuditChainVerifier
{
    Task<AccountingAuditChainVerification> VerifyAuditChainAsync(CancellationToken ct = default);
}

/// <summary>Outcome of verifying the ledger-event audit chain: the verified suffix is protected; genesis facts were retained before this guarantee.</summary>
public sealed record LedgerEventAuditVerification(long ChainedEvents, long UnprotectedGenesisFacts, DateTimeOffset GenesisAtUtc);

/// <summary>A ledger journal store whose event audit chain can be verified on demand.</summary>
/// <remarks>
/// Implementations throw when the chain does not verify; the message names the break, and the
/// exception's <see cref="Exception.Data"/> carries the keys in <see cref="LedgerEventAuditFailureData"/>
/// when the failure site knows them.
/// </remarks>
public interface ILedgerEventAuditVerifier
{
    Task<LedgerEventAuditVerification> VerifyLedgerEventAuditAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="Exception.Data"/> keys a ledger audit integrity failure carries so a reader can report
/// how far the scan got and where it stopped, rather than only the message.
/// </summary>
public static class LedgerEventAuditFailureData
{
    /// <summary>The chain sequence the break was found at (<see cref="long"/>).</summary>
    public const string FailedSequence = "Meridian.LedgerEventAudit.FailedSequence";

    /// <summary>Chained events that verified before the failure (<see cref="long"/>).</summary>
    public const string ChainedEventsVerified = "Meridian.LedgerEventAudit.ChainedEventsVerified";
}

/// <summary>Stable status values for <see cref="AuditChainIntegrityResultDto.Status"/>.</summary>
public static class AuditChainIntegrityStatus
{
    /// <summary>Every link and covered fact verified.</summary>
    public const string Verified = "verified";

    /// <summary>The chain does not verify: tampering, truncation, or an interrupted append.</summary>
    public const string Broken = "broken";

    /// <summary>This composition retains no store that keeps this chain.</summary>
    public const string NotConfigured = "not-configured";

    /// <summary>The store exists but could not be read; nothing is claimed about the chain.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>Stable chain identifiers for <see cref="AuditChainIntegrityResultDto.Chain"/>.</summary>
public static class AuditChainIdentifiers
{
    public const string AccountingActions = "accounting-actions";
    public const string LedgerEvents = "ledger-events";
}

/// <summary>Verification outcome for one hash-chained audit log.</summary>
/// <param name="Chain">One of <see cref="AuditChainIdentifiers"/>.</param>
/// <param name="Status">One of <see cref="AuditChainIntegrityStatus"/>.</param>
/// <param name="LinksChecked">Chained entries verified (or checked before the break).</param>
/// <param name="UnprotectedPreChainCount">History retained before chaining began, which the chain cannot vouch for.</param>
/// <param name="FailureKind">The specific failure classification when <paramref name="Status"/> is not verified.</param>
/// <param name="Detail">Operator-facing explanation.</param>
/// <param name="FailedSequence">The chain position the break was found at, when known.</param>
public sealed record AuditChainIntegrityResultDto(
    string Chain,
    string Status,
    long LinksChecked,
    long UnprotectedPreChainCount,
    string? FailureKind = null,
    string? Detail = null,
    long? FailedSequence = null)
{
    public bool IsVerified => string.Equals(Status, AuditChainIntegrityStatus.Verified, StringComparison.Ordinal);
}

/// <summary>Read-only integrity report over every accounting and ledger audit chain this composition retains.</summary>
/// <param name="VerifiedAtUtc">When the verification ran.</param>
/// <param name="AllVerified">True only when every configured chain verified and at least one chain is configured.</param>
/// <param name="Chains">One result per chain identifier, configured or not.</param>
public sealed record AccountingAuditIntegrityReportDto(
    DateTimeOffset VerifiedAtUtc,
    bool AllVerified,
    IReadOnlyList<AuditChainIntegrityResultDto> Chains);
