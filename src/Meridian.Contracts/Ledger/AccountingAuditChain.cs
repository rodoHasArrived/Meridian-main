using System.Globalization;
using System.Text;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Ledger;

/// <summary>
/// One link of the accounting-action audit hash chain: the position an event was appended at, the
/// digest of its canonical payload, and the digest binding it to its predecessor.
/// </summary>
/// <remarks>
/// Chain links are kept beside the events rather than on <see cref="AccountingActionAuditEventDto"/>
/// itself so the contract DTO stays the shape every other reader (PostgreSQL store, workstation,
/// desktop) already binds. The link list is also the canonical <b>append order</b>: the retained
/// event list is sorted for display (newest first), so re-deriving append order from it would make
/// a reorder undetectable.
/// </remarks>
public sealed record AccountingAuditChainLink(
    long Sequence,
    Guid AuditEventId,
    string PayloadHash,
    string? PreviousHash,
    string EntryHash);

/// <summary>
/// The chain state retained alongside a snapshot's audit events: the schema version that fixes the
/// hashing rules, the sequence the chain starts at, and the links themselves.
/// </summary>
/// <remarks>
/// <para><b>Why a genesis sequence.</b> The accounting audit history predates chaining, and those
/// rows were appended with no predecessor hash. Chaining on top of them has exactly two honest
/// outcomes and no third: reject the retained history, or present pre-upgrade events as
/// tamper-evident when nothing ever protected them. <see cref="GenesisSequence"/> is the declared
/// boundary — events retained before the chain began are reported by verification as
/// <see cref="AccountingAuditChainVerification.PreChainEventCount"/> and are explicitly
/// <b>outside</b> the chain's guarantee rather than silently inside it.</para>
/// </remarks>
public sealed record AccountingAuditChainState(
    int SchemaVersion,
    long GenesisSequence,
    int PreChainEventCount,
    IReadOnlyList<AccountingAuditChainLink> Links)
{
    /// <summary>The chain schema this build writes and can verify.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The sequence assigned to the first chained event.</summary>
    public const long FirstSequence = 1;

    public static AccountingAuditChainState Begin(int preChainEventCount)
        => new(CurrentSchemaVersion, FirstSequence, preChainEventCount, []);

    /// <summary>The last link appended, or null when the chain carries no events yet.</summary>
    public AccountingAuditChainLink? Head => Links.Count == 0 ? null : Links[^1];
}

/// <summary>Whether an anchored append had been confirmed against the snapshot.</summary>
public enum AccountingAuditChainAnchorPhase
{
    /// <summary>The append was declared but the snapshot write had not yet been confirmed.</summary>
    Pending,

    /// <summary>The snapshot carrying the event was written.</summary>
    Committed,
}

/// <summary>One record in the external head journal.</summary>
public sealed record AccountingAuditChainAnchorRecord(
    int SchemaVersion,
    long Sequence,
    string EntryHash,
    AccountingAuditChainAnchorPhase Phase,
    DateTimeOffset RecordedAtUtc,
    string? PreviousAnchorHash,
    string AnchorHash);

/// <summary>Why a chain failed verification. <see cref="Valid"/> is the only passing value.</summary>
public enum AccountingAuditChainStatus
{
    Valid,

    /// <summary>The chain was written by a newer schema whose hashing rules this build cannot check.</summary>
    UnsupportedSchemaVersion,

    /// <summary>A link's sequence is missing, duplicated, or out of order.</summary>
    BrokenSequence,

    /// <summary>A link references an event that is no longer retained.</summary>
    MissingEvent,

    /// <summary>An event's content no longer digests to the payload hash its link recorded.</summary>
    EventMutated,

    /// <summary>A link's entry hash does not bind to its recorded predecessor.</summary>
    BrokenLink,

    /// <summary>
    /// The chain verifies internally but is shorter than, or diverges from, the head retained
    /// outside the snapshot — a rollback or a truncated tail.
    /// </summary>
    AnchorMismatch,

    /// <summary>Chained events are retained but no external head was ever recorded for them.</summary>
    AnchorMissing,

    /// <summary>
    /// An append was declared but its snapshot write never landed. The retained chain is intact —
    /// this is a crash between the two writes, not tampering.
    /// </summary>
    InterruptedAppend,

    /// <summary>
    /// More events are retained than the genesis boundary and the chain account for: a record was
    /// added without a link. Every link binding to a real, unmutated event says nothing about an
    /// event that no link points at, and such an event is served by
    /// <c>ListAsync</c> as ordinary audit history.
    /// </summary>
    UnlinkedEvent,
}

/// <summary>Outcome of verifying an accounting audit chain.</summary>
public sealed record AccountingAuditChainVerification(
    AccountingAuditChainStatus Status,
    int LinksChecked,
    int PreChainEventCount,
    string? Detail = null,
    long? FailedSequence = null)
{
    public bool IsValid => Status == AccountingAuditChainStatus.Valid;
}

/// <summary>Raised when an append or a read would build on a chain that no longer verifies.</summary>
public sealed class AccountingAuditChainIntegrityException : Exception
{
    public AccountingAuditChainIntegrityException(AccountingAuditChainVerification verification)
        : base(Describe(verification))
    {
        Verification = verification;
    }

    public AccountingAuditChainVerification Verification { get; }

    private static string Describe(AccountingAuditChainVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var sequence = verification.FailedSequence is { } failed
            ? $" at sequence {failed.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
        return $"Accounting audit chain verification failed ({verification.Status}){sequence}."
            + (verification.Detail is null ? string.Empty : $" {verification.Detail}");
    }
}

/// <summary>
/// Hashing and verification rules for the accounting-action audit chain. Mirrors the database-side
/// precedent in <c>PostgresReportingArtifactAuditStore</c> — sequence, predecessor hash, and payload
/// folded into one entry digest — so the two postures do not carry different schemes.
/// </summary>
public static class AccountingAuditChain
{
    /// <summary>
    /// Digest of an audit event's canonical payload. Every field of the DTO participates, so a
    /// change to any of them breaks the link that recorded it.
    /// </summary>
    /// <remarks>
    /// The payload is built field by field rather than by serializing the DTO: JSON property order,
    /// casing policy, and default-value omission are serializer settings, and a digest that silently
    /// depends on them would start reporting tamper the day one of those settings changed.
    /// </remarks>
    public static string ComputePayloadHash(AccountingActionAuditEventDto auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var builder = new StringBuilder();
        Append(builder, auditEvent.AuditEventId.ToString("D", CultureInfo.InvariantCulture));
        Append(builder, NormalizeTimestamp(auditEvent.RecordedAtUtc));
        Append(builder, auditEvent.Actor);
        Append(builder, auditEvent.Action);
        Append(builder, auditEvent.FundProfileId);
        Append(builder, auditEvent.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture));
        Append(builder, auditEvent.CorrelationId);
        Append(builder, auditEvent.BeforeHash);
        Append(builder, auditEvent.AfterHash);
        Append(builder, auditEvent.CompanyId);
        Append(builder, auditEvent.TenantId);

        var issues = auditEvent.ValidationIssues ?? [];
        Append(builder, issues.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var issue in issues)
        {
            Append(builder, issue.Code);
            Append(builder, issue.Severity.ToString());
            Append(builder, issue.Message);
            Append(builder, issue.TargetId);
            Append(builder, issue.SuggestedAction);
        }

        var evidence = auditEvent.EvidenceLinks ?? [];
        Append(builder, evidence.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var link in evidence)
        {
            Append(builder, link);
        }

        var principals = auditEvent.ReportGroupPrincipalIds ?? [];
        Append(builder, principals.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var principal in principals)
        {
            Append(builder, principal);
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    /// <summary>Digest binding a payload to its position and predecessor.</summary>
    public static string ComputeEntryHash(long sequence, string? previousHash, string payloadHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        var material = string.Concat(
            sequence.ToString(CultureInfo.InvariantCulture),
            "\n",
            previousHash ?? string.Empty,
            "\n",
            payloadHash);
        return Sha256Digest.ComputeUtf8(material);
    }

    /// <summary>Builds the link that appends <paramref name="auditEvent"/> after <paramref name="head"/>.</summary>
    public static AccountingAuditChainLink CreateLink(
        AccountingAuditChainState state,
        AccountingActionAuditEventDto auditEvent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(auditEvent);

        var head = state.Head;
        var sequence = head is null ? state.GenesisSequence : head.Sequence + 1;
        var payloadHash = ComputePayloadHash(auditEvent);
        return new AccountingAuditChainLink(
            sequence,
            auditEvent.AuditEventId,
            payloadHash,
            head?.EntryHash,
            ComputeEntryHash(sequence, head?.EntryHash, payloadHash));
    }

    /// <summary>
    /// Verifies the chain against the events it covers <b>and</b> against the head retained outside
    /// the snapshot. This is the full check: it is the only one that can see a rollback.
    /// </summary>
    /// <param name="state">The chain retained in the snapshot, or null when chaining never began.</param>
    /// <param name="auditEvents">Every retained event, in any order.</param>
    /// <param name="anchor">
    /// The externally retained head. Null is <b>not</b> "skip the anchor check": a chained history
    /// with no retained head has had its anchor removed, which is reported as
    /// <see cref="AccountingAuditChainStatus.AnchorMissing"/>. Use <see cref="VerifyLinks"/> for the
    /// narrower question of whether the chain is internally consistent.
    /// </param>
    public static AccountingAuditChainVerification Verify(
        AccountingAuditChainState? state,
        IReadOnlyList<AccountingActionAuditEventDto> auditEvents,
        AccountingAuditChainAnchorRecord? anchor)
    {
        var links = VerifyLinks(state, auditEvents);
        if (!links.IsValid)
        {
            return links;
        }

        return VerifyAnchor(state, anchor) ?? links;
    }

    /// <summary>
    /// Verifies only that the chain is internally consistent: contiguous sequences, every covered
    /// event still retained and still matching its recorded digest, every link binding to its
    /// predecessor.
    /// </summary>
    /// <remarks>
    /// A passing result here is <b>not</b> tamper-evidence on its own. Removing the newest events
    /// together with the links that covered them leaves a shorter chain that satisfies every check
    /// in this method — which is precisely why the head is retained outside the snapshot and why
    /// <see cref="Verify"/> is what callers should use. This narrower entry point exists so
    /// verification tooling can tell an operator <i>which</i> half failed: "the chain is intact but
    /// the head disagrees" is the rollback diagnosis, and it reads very differently from "an event
    /// was edited".
    /// </remarks>
    public static AccountingAuditChainVerification VerifyLinks(
        AccountingAuditChainState? state,
        IReadOnlyList<AccountingActionAuditEventDto> auditEvents)
    {
        ArgumentNullException.ThrowIfNull(auditEvents);

        if (state is null)
        {
            // Chaining never began here, so every retained event is pre-chain.
            return new AccountingAuditChainVerification(
                AccountingAuditChainStatus.Valid,
                LinksChecked: 0,
                PreChainEventCount: auditEvents.Count);
        }

        if (state.SchemaVersion != AccountingAuditChainState.CurrentSchemaVersion)
        {
            return new AccountingAuditChainVerification(
                AccountingAuditChainStatus.UnsupportedSchemaVersion,
                LinksChecked: 0,
                state.PreChainEventCount,
                $"Chain schema version {state.SchemaVersion.ToString(CultureInfo.InvariantCulture)} "
                + $"is not version {AccountingAuditChainState.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        var eventsById = new Dictionary<Guid, AccountingActionAuditEventDto>();
        foreach (var auditEvent in auditEvents)
        {
            eventsById[auditEvent.AuditEventId] = auditEvent;
        }

        var linkedEventIds = new HashSet<Guid>();
        string? previousHash = null;
        var expectedSequence = state.GenesisSequence;
        foreach (var link in state.Links)
        {
            if (link.Sequence != expectedSequence)
            {
                return Fail(
                    AccountingAuditChainStatus.BrokenSequence,
                    state,
                    expectedSequence,
                    $"Expected sequence {expectedSequence.ToString(CultureInfo.InvariantCulture)} "
                    + $"but found {link.Sequence.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (!eventsById.TryGetValue(link.AuditEventId, out var auditEvent))
            {
                return Fail(
                    AccountingAuditChainStatus.MissingEvent,
                    state,
                    link.Sequence,
                    $"Audit event '{link.AuditEventId.ToString("D", CultureInfo.InvariantCulture)}' is no longer retained.");
            }

            // Each link must claim a distinct event. Two links sharing one event would otherwise
            // satisfy the count check below while leaving a second event unlinked.
            if (!linkedEventIds.Add(link.AuditEventId))
            {
                return Fail(
                    AccountingAuditChainStatus.UnlinkedEvent,
                    state,
                    link.Sequence,
                    $"Audit event '{link.AuditEventId.ToString("D", CultureInfo.InvariantCulture)}' is claimed by more than one link.");
            }

            if (!string.Equals(ComputePayloadHash(auditEvent), link.PayloadHash, StringComparison.Ordinal))
            {
                return Fail(
                    AccountingAuditChainStatus.EventMutated,
                    state,
                    link.Sequence,
                    $"Audit event '{link.AuditEventId.ToString("D", CultureInfo.InvariantCulture)}' no longer matches its recorded digest.");
            }

            if (!string.Equals(link.PreviousHash, previousHash, StringComparison.Ordinal)
                || !string.Equals(
                    ComputeEntryHash(link.Sequence, previousHash, link.PayloadHash),
                    link.EntryHash,
                    StringComparison.Ordinal))
            {
                return Fail(
                    AccountingAuditChainStatus.BrokenLink,
                    state,
                    link.Sequence,
                    "The link does not bind to its predecessor.");
            }

            previousHash = link.EntryHash;
            expectedSequence++;
        }

        // The loop above proves every LINK points at a real, unmutated event. It says nothing about
        // an EVENT that no link points at -- and an unlinked event is served by ListAsync as
        // ordinary audit history, so without this check appending a fabricated record is a way past
        // tamper detection that leaves the chain and its anchor both reporting Valid.
        //
        // The declared genesis is what makes the count checkable: pre_chain_event_count fixed how
        // many unprotected rows were retained when chaining began, so everything beyond that must be
        // matched one-for-one by a link. Note this bounds the pre-chain history rather than
        // protecting it -- swapping one pre-chain event for another keeps the count and stays
        // outside the guarantee, which is what "outside the chain" has meant throughout.
        var expectedEventCount = state.PreChainEventCount + state.Links.Count;
        if (auditEvents.Count != expectedEventCount)
        {
            return Fail(
                AccountingAuditChainStatus.UnlinkedEvent,
                state,
                state.Head?.Sequence ?? state.GenesisSequence,
                $"{auditEvents.Count.ToString(CultureInfo.InvariantCulture)} events are retained but "
                + $"{expectedEventCount.ToString(CultureInfo.InvariantCulture)} are accounted for "
                + $"({state.PreChainEventCount.ToString(CultureInfo.InvariantCulture)} before the genesis "
                + $"and {state.Links.Count.ToString(CultureInfo.InvariantCulture)} chained).");
        }

        return new AccountingAuditChainVerification(
            AccountingAuditChainStatus.Valid,
            state.Links.Count,
            state.PreChainEventCount);
    }

    /// <summary>
    /// Compares the chain's head against the head retained outside the snapshot. Returns null when
    /// they agree.
    /// </summary>
    private static AccountingAuditChainVerification? VerifyAnchor(
        AccountingAuditChainState? state,
        AccountingAuditChainAnchorRecord? anchor)
    {
        var head = state?.Head;
        var preChainEventCount = state?.PreChainEventCount ?? 0;
        var linksChecked = state?.Links.Count ?? 0;

        if (anchor is null)
        {
            // Deleting the head journal must not be a way out of detection: a chained history with
            // no retained head is a removed anchor, not an unanchored store.
            return head is null
                ? null
                : new AccountingAuditChainVerification(
                    AccountingAuditChainStatus.AnchorMissing,
                    linksChecked,
                    preChainEventCount,
                    "No external head is retained for the chained events.",
                    head.Sequence);
        }

        if (head is not null
            && head.Sequence == anchor.Sequence
            && string.Equals(head.EntryHash, anchor.EntryHash, StringComparison.Ordinal))
        {
            // Committed, or pending with the snapshot already carrying the event: either way the two
            // records agree, and a lost commit line costs nothing the chain does not re-derive.
            return null;
        }

        // Write-ahead ordering means a crash can only leave the journal one *declared* append ahead
        // of the snapshot. A *committed* head the snapshot has fallen behind is the rollback
        // signature, and the two must not be reported alike.
        var expectedNextSequence = head is null
            ? state?.GenesisSequence ?? AccountingAuditChainState.FirstSequence
            : head.Sequence + 1;

        if (anchor.Phase == AccountingAuditChainAnchorPhase.Pending
            && anchor.Sequence == expectedNextSequence)
        {
            return new AccountingAuditChainVerification(
                AccountingAuditChainStatus.InterruptedAppend,
                linksChecked,
                preChainEventCount,
                $"Append at sequence {anchor.Sequence.ToString(CultureInfo.InvariantCulture)} was declared "
                + "but its snapshot write did not land.",
                anchor.Sequence);
        }

        return new AccountingAuditChainVerification(
            AccountingAuditChainStatus.AnchorMismatch,
            linksChecked,
            preChainEventCount,
            head is null
                ? $"The retained snapshot carries no chained events while the external head records "
                    + $"sequence {anchor.Sequence.ToString(CultureInfo.InvariantCulture)} ({anchor.Phase})."
                : $"The retained chain ends at sequence {head.Sequence.ToString(CultureInfo.InvariantCulture)} "
                    + $"but the external head records {anchor.Sequence.ToString(CultureInfo.InvariantCulture)} "
                    + $"({anchor.Phase}).",
            head?.Sequence ?? anchor.Sequence);
    }

    private static AccountingAuditChainVerification Fail(
        AccountingAuditChainStatus status,
        AccountingAuditChainState state,
        long sequence,
        string detail)
        => new(status, state.Links.Count, state.PreChainEventCount, detail, sequence);

    /// <summary>Ticks per microsecond — the finest resolution both retention postures preserve.</summary>
    private const long TicksPerMicrosecond = 10;

    /// <summary>
    /// The canonical rendering of an audit timestamp: UTC, truncated to microseconds.
    /// </summary>
    /// <remarks>
    /// UTC so the same instant recorded behind a different offset digests alike. Truncated because
    /// <c>timestamptz</c> stores microseconds while <see cref="DateTimeOffset"/> carries 100ns ticks,
    /// so a digest over the full tick would verify in memory and then fail the moment the same event
    /// came back from PostgreSQL — reported as tampering, caused by rounding. Truncating to the
    /// coarser of the two resolutions is what makes one digest scheme usable in both postures.
    /// </remarks>
    private static string NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var truncated = new DateTimeOffset(
            utc.Ticks - (utc.Ticks % TicksPerMicrosecond),
            TimeSpan.Zero);
        return truncated.ToString("O", CultureInfo.InvariantCulture);
    }

    // Length-prefixed so no combination of field values can be re-partitioned into a different
    // event that digests the same, and so null is distinct from empty.
    private static void Append(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-|");
            return;
        }

        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('|');
    }
}
