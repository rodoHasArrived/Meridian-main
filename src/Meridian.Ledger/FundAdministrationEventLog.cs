using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Ledger;

/// <summary>
/// Append-only sink for fund-administration governance events. Services that perform privileged
/// administration actions depend on this seam so the posting/lock/reopen/export/delivery trail is
/// recorded independently of how the log is stored.
/// </summary>
public interface IFundAdministrationEventSink
{
    /// <summary>Appends one governance event and returns the sealed, hash-chained record.</summary>
    FundAdministrationEvent Append(FundAdministrationEventRequest request);

    /// <summary>Convenience overload that builds and appends a governance event from its parts.</summary>
    FundAdministrationEvent Append(
        FundAdministrationEventKind kind,
        string actor,
        string subjectId,
        string summary,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<JournalEvidenceReference>? evidence = null,
        DateTimeOffset? occurredAtUtc = null)
        => Append(new FundAdministrationEventRequest(kind, actor, subjectId, summary, attributes, evidence, occurredAtUtc));
}

/// <summary>
/// In-memory, append-only, tamper-evident log of fund-administration governance events. Each appended
/// event is chained to the previous one by SHA-256 hash (<see cref="FundAdministrationEvent.PreviousHash"/>
/// → <see cref="FundAdministrationEvent.Hash"/>), so any retroactive edit, reorder, insertion, or
/// deletion is detectable by <see cref="VerifyIntegrity"/>. The log offers no mutation or removal API;
/// corrections are made by appending compensating events.
/// </summary>
/// <remarks>
/// This mirrors the hash-chaining contract proven by
/// <c>Meridian.Audit.Compliance.ImmutableAuditLogService</c> but is a pure-domain primitive with a
/// fund-administration event vocabulary and no persistence or identity dependencies, so ledger
/// primitives can emit governance events without referencing the audit/compliance host. A host that
/// needs durable storage can wrap this seam the same way the compliance log wraps its JSONL writer.
/// </remarks>
public sealed class FundAdministrationEventLog : IFundAdministrationEventSink
{
    // The append sequence (read tail hash -> compute chained hash -> enqueue) must be atomic so two
    // concurrent callers cannot read the same predecessor hash and fork the tamper-evident chain.
    private readonly object _gate = new();
    private readonly List<FundAdministrationEvent> _events = [];
    private long _sequence;

    /// <summary>Chronological snapshot of every appended event.</summary>
    public IReadOnlyList<FundAdministrationEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public FundAdministrationEvent Append(FundAdministrationEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Governance events must record an actor.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SubjectId))
            throw new ArgumentException("Governance events must record a subject identifier.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Summary))
            throw new ArgumentException("Governance events must record a summary.", nameof(request));

        var attributes = NormalizeAttributes(request.Attributes);
        var evidence = (request.Evidence ?? []).Select(static reference => reference.Normalize()).ToArray();

        lock (_gate)
        {
            var previousHash = _events.Count == 0 ? null : _events[^1].Hash;
            var pending = new FundAdministrationEvent(
                EventId: $"fund-admin-{Guid.NewGuid():N}",
                Sequence: ++_sequence,
                Kind: request.Kind,
                OccurredAtUtc: (request.OccurredAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Actor: request.Actor.Trim(),
                SubjectId: request.SubjectId.Trim(),
                Summary: request.Summary.Trim(),
                Attributes: attributes,
                Evidence: evidence,
                Hash: string.Empty,
                PreviousHash: previousHash);

            var sealedEvent = pending with { Hash = ComputeHash(pending) };
            _events.Add(sealedEvent);
            return sealedEvent;
        }
    }

    /// <summary>Convenience overload that builds and appends a governance event from its parts.</summary>
    public FundAdministrationEvent Append(
        FundAdministrationEventKind kind,
        string actor,
        string subjectId,
        string summary,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<JournalEvidenceReference>? evidence = null,
        DateTimeOffset? occurredAtUtc = null)
        => Append(new FundAdministrationEventRequest(kind, actor, subjectId, summary, attributes, evidence, occurredAtUtc));

    /// <summary>All events recorded for a specific subject (fund, period, report, delivery, ...).</summary>
    public IReadOnlyList<FundAdministrationEvent> EventsFor(string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        var normalized = subjectId.Trim();
        lock (_gate)
        {
            return _events
                .Where(evt => string.Equals(evt.SubjectId, normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    /// <summary>All events of a specific kind.</summary>
    public IReadOnlyList<FundAdministrationEvent> EventsOfKind(FundAdministrationEventKind kind)
    {
        lock (_gate)
        {
            return _events.Where(evt => evt.Kind == kind).ToArray();
        }
    }

    /// <summary>
    /// Walks the hash chain and returns <see langword="true"/> only when every event's recorded hash
    /// recomputes and links to its predecessor — i.e. the log has not been tampered with.
    /// </summary>
    public bool VerifyIntegrity()
    {
        lock (_gate)
        {
            string? expectedPrevious = null;
            var expectedSequence = 0L;
            foreach (var evt in _events)
            {
                expectedSequence++;
                if (evt.Sequence != expectedSequence)
                    return false;

                if (!string.Equals(expectedPrevious, evt.PreviousHash, StringComparison.Ordinal))
                    return false;

                if (!string.Equals(ComputeHash(evt with { Hash = string.Empty }), evt.Hash, StringComparison.Ordinal))
                    return false;

                expectedPrevious = evt.Hash;
            }

            return true;
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (attributes is null)
            return normalized;

        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            normalized[key.Trim()] = value?.Trim() ?? string.Empty;
        }

        return normalized;
    }

    private static string ComputeHash(FundAdministrationEvent evt)
    {
        var builder = new StringBuilder();

        // Length-prefix every field so the canonical form is unambiguous even when a value contains the
        // delimiter: each value is preceded by its exact character count, so an embedded separator or
        // '=' can never be misread as a field boundary. The attribute and evidence counts are hashed
        // too, so splitting or merging entries also changes the hash. Every evidence field is folded in
        // (not just id + content hash), so tampering with any supporting-record field is detectable.
        AppendField(builder, evt.Sequence.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, ((int)evt.Kind).ToString(CultureInfo.InvariantCulture));
        AppendField(builder, evt.OccurredAtUtc.ToUniversalTime().ToString("O"));
        AppendField(builder, evt.Actor);
        AppendField(builder, evt.SubjectId);
        AppendField(builder, evt.Summary);

        AppendField(builder, evt.Attributes.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var pair in evt.Attributes.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            AppendField(builder, pair.Key);
            AppendField(builder, pair.Value);
        }

        AppendField(builder, evt.Evidence.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var reference in evt.Evidence)
        {
            AppendField(builder, reference.EvidenceId);
            AppendField(builder, reference.Uri);
            AppendField(builder, reference.Kind);
            AppendField(builder, reference.SourceSystem);
            AppendField(builder, reference.RetainedAtUtc.ToUniversalTime().ToString("O"));
            AppendField(builder, reference.RetainedBy);
            AppendField(builder, reference.SubjectId ?? string.Empty);
            AppendField(builder, reference.ContentHash ?? string.Empty);
            AppendField(builder, reference.Description ?? string.Empty);
            AppendField(builder, reference.SourceReference ?? string.Empty);
            AppendField(builder, reference.ReviewStatus ?? string.Empty);
            AppendField(builder, reference.ReviewedBy ?? string.Empty);
            AppendField(builder, reference.ReviewedAtUtc?.ToUniversalTime().ToString("O") ?? string.Empty);
            AppendField(builder, reference.EffectiveDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            AppendField(builder, reference.EvidenceVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendField(builder, reference.SubjectType ?? string.Empty);
        }

        AppendField(builder, evt.PreviousHash ?? string.Empty);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static void AppendField(StringBuilder builder, string value)
        => builder.Append(value.Length).Append((char)0x1F).Append(value);
}
