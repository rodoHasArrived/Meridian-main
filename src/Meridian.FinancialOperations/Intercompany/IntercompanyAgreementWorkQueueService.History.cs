using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;

namespace Meridian.FinancialOperations.Intercompany;

public sealed partial class IntercompanyAgreementWorkQueueService
{
    private async Task<IReadOnlyList<OperationalCaseHistoryRecord>> ReadHistoryAsync(string scopeKey, CancellationToken ct)
    {
        try
        {
            var records = await history.ReadAsync(new() { CaseType = CaseType }, ct).ConfigureAwait(false);
            if (records.Any(record => record.CaseType != CaseType || !record.Data.ContainsKey("scopeKey")))
                throw new InvalidDataException("The retained case scope is missing.");
            var scoped = records.Where(record => record.Data["scopeKey"] == scopeKey).OrderBy(record => record.Sequence).ToArray();
            foreach (var group in scoped.GroupBy(record => record.CaseId, StringComparer.Ordinal))
            {
                OperationalCaseHistoryRecord? previous = null;
                DateTimeOffset? firstSeen = null;
                foreach (var record in group)
                {
                    var item = ReadSnapshot(record);
                    if (!OperationalCaseHistoryHashing.HasValidRecordHash(record)
                        || record.CaseId != CaseId(scopeKey, item.AgreementId, item.FindingCode, item.DueDate)
                        || !record.Data.TryGetValue("expectedPreviousCaseSequence", out var expected)
                        || expected != (previous?.Sequence ?? 0).ToString(CultureInfo.InvariantCulture)
                        || previous is not null && (!record.Data.TryGetValue("expectedPreviousCaseRecordHashSha256", out var expectedHash)
                            || expectedHash != previous.RecordHashSha256)
                        || firstSeen is not null && firstSeen != item.FirstSeenAtUtc)
                        throw new InvalidDataException("The retained case chain or identity is inconsistent.");
                    firstSeen ??= item.FirstSeenAtUtc;
                    previous = record;
                }
            }
            return scoped;
        }
        catch (Exception error) when (IsUnavailable(error) || error is ArgumentException or KeyNotFoundException)
        { throw Unavailable(); }
    }

    private async Task<IntercompanyAgreementWorkItemDto> AppendAsync(Capture capture, string actor,
        Guid commandId, string inputDigest, string action, string reason, IntercompanyAgreementWorkItemDto snapshot,
        OperationalCaseHistoryRecord[] previous, string? note, IReadOnlyList<string> evidence,
        DateTimeOffset now, CancellationToken ct)
    {
        var prior = previous.LastOrDefault();
        var old = prior is null ? null : ReadSnapshot(prior);
        var legal = capture.Legal[snapshot.AgreementId];
        // The store assigns the global sequence and record hash. They are projected from its receipt,
        // not guessed in the saved document; history is never recursively copied into a new event.
        snapshot = snapshot with { Version = 0, RecordHash = null, AgeDays = null, History = [] };
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schema"] = "intercompany-agreement-work-v1",
            ["scopeKey"] = capture.ScopeKey,
            ["snapshot"] = JsonSerializer.Serialize(snapshot, IntercompanyAgreementWorkQueueJsonContext.Default.IntercompanyAgreementWorkItemDto),
            ["commandId"] = commandId.ToString("D"),
            ["partyOneId"] = legal.PartyOneId.ToString("D"),
            ["partyTwoId"] = legal.PartyTwoId.ToString("D"),
            ["observationAsOf"] = capture.Scope.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["sourceFingerprint"] = capture.Fingerprint,
            ["sourceVersions"] = JsonSerializer.Serialize(SourceVersions(capture),
                IntercompanyObligationMonitorJsonContext.Default.IReadOnlyListIntercompanyObligationSourceVersionDto),
            ["note"] = note ?? "",
            ["evidence"] = JsonSerializer.Serialize(evidence.ToArray(), IntercompanyAgreementWorkQueueJsonContext.Default.StringArray),
            ["expectedPreviousCaseSequence"] = (prior?.Sequence ?? 0).ToString(CultureInfo.InvariantCulture),
            ["expectedPreviousCaseRecordHashSha256"] = prior?.RecordHashSha256 ?? ""
        };
        var request = new OperationalCaseHistoryAppendRequest
        {
            CaseId = snapshot.CaseId,
            CaseType = CaseType,
            HistoryEventId = EventId(capture.ScopeKey, commandId),
            EventType = action,
            OccurredAtUtc = now,
            ActorId = actor,
            Reason = reason.Trim(),
            CorrelationId = snapshot.CaseId,
            InputHashSha256 = inputDigest,
            Data = data,
            Transition = old?.State != snapshot.State ? new()
            {
                PreviousState = old?.State.ToString(),
                CurrentState = snapshot.State.ToString(),
                TransitionedAtUtc = now
            } : null,
            Assignment = action == nameof(IntercompanyAgreementWorkItemActionDto.AssignOwner) ? new()
            {
                PreviousAssigneeId = old?.Owner,
                AssigneeId = snapshot.Owner,
                AssignedBy = actor,
                AssignedAtUtc = now
            } : null
        };
        try
        {
            var record = await history.AppendAsync(request, ct).ConfigureAwait(false);
            return Project([.. previous, record], capture with { Now = now });
        }
        catch (InvalidOperationException)
        {
            // Another process may have committed the same command between our read and append.
            // Match the original input digest, not its newly sampled server timestamp.
            var current = capture with { Records = await ReadHistoryAsync(capture.ScopeKey, ct).ConfigureAwait(false) };
            if (Replay(current, actor, commandId, inputDigest) is { } replay)
                return replay;
            throw new IntercompanyAgreementWorkQueueConflictException("The retained case changed before this action committed. Refresh the work queue.");
        }
        catch (Exception error) when (IsUnavailable(error)) { throw Unavailable(); }
    }

    private static IntercompanyAgreementWorkItemDto? Replay(Capture capture, string actor, Guid commandId, string inputDigest)
    {
        var record = capture.Records.SingleOrDefault(item => item.HistoryEventId == EventId(capture.ScopeKey, commandId));
        if (record is null)
            return null;
        if (record.ActorId != actor || !Sha256Digest.FixedEquals(record.InputHashSha256, inputDigest))
            throw new IntercompanyAgreementWorkQueueConflictException("The command ID is already retained with different input. Use a new command ID for a new action.");
        return Project(capture.Records.Where(item => item.CaseId == record.CaseId && item.Sequence <= record.Sequence).ToArray(), capture);
    }

    private static IntercompanyAgreementWorkItemDto Project(OperationalCaseHistoryRecord[] records, Capture capture)
    {
        if (records.Length == 0)
            throw new IntercompanyAgreementWorkQueueConflictException("The selected case is not tracked in this scope.");
        var latest = records[^1];
        var snapshot = ReadSnapshot(latest);
        if (snapshot.LedgerBookId != capture.Book.LedgerBookId)
            throw Unavailable();
        var observed = capture.Candidates.GetValueOrDefault(snapshot.CaseId);
        return snapshot with
        {
            AgreementVersion = observed?.AgreementVersion ?? snapshot.AgreementVersion,
            FindingCode = observed?.FindingCode ?? snapshot.FindingCode,
            Severity = observed?.Severity ?? snapshot.Severity,
            Message = observed?.Message ?? snapshot.Message,
            SuggestedAction = observed?.SuggestedAction ?? snapshot.SuggestedAction,
            ObservedAsOf = observed?.ObservedAsOf ?? snapshot.ObservedAsOf,
            IsCurrentlyObserved = observed is not null,
            IsTracked = true,
            AgeDays = Math.Max(0, DateOnly.FromDateTime(capture.Now.UtcDateTime).DayNumber
                - DateOnly.FromDateTime(snapshot.FirstSeenAtUtc!.Value.UtcDateTime).DayNumber),
            DaysOverdue = Overdue(snapshot.DueDate, capture.Scope.AsOf),
            Version = latest.Sequence,
            RecordHash = latest.RecordHashSha256,
            History = records.Select(record =>
            {
                var state = ReadSnapshot(record);
                return new IntercompanyAgreementWorkItemHistoryDto(ReadCommandId(record), record.Sequence,
                    record.RecordHashSha256, record.EventType, state.State, record.ActorId, record.OccurredAtUtc,
                    record.Reason, state.Owner, record.Data.GetValueOrDefault("note") is { Length: > 0 } note ? note : null,
                    ReadEvidence(record), ReadObservationDate(record), record.Data["sourceFingerprint"])
                { SourceVersions = ReadSourceVersions(record) };
            }).ToArray()
        };
    }

    private static IntercompanyAgreementWorkItemDto ReadSnapshot(OperationalCaseHistoryRecord record)
    {
        try
        {
            if (record.Data.GetValueOrDefault("schema") != "intercompany-agreement-work-v1"
                || !record.Data.TryGetValue("snapshot", out var json))
                throw new InvalidDataException("The retained case schema is unavailable.");
            var item = JsonSerializer.Deserialize(json, IntercompanyAgreementWorkQueueJsonContext.Default.IntercompanyAgreementWorkItemDto);
            if (item is null || item.CaseId != record.CaseId || item.LedgerBookId == Guid.Empty || item.AgreementId == Guid.Empty
                || !item.IsTracked || item.FirstSeenAtUtc is null || item.LastObservedAtUtc is null
                || item.FirstSeenAtUtc > item.LastObservedAtUtc || item.ObservedAsOf == default
                || !Enum.IsDefined(item.State) || string.IsNullOrWhiteSpace(item.FindingCode)
                || string.IsNullOrWhiteSpace(item.Message) || string.IsNullOrWhiteSpace(item.LastObservedSourceFingerprint)
                || item.History is null || item.History.Count != 0 || item.Version != 0 || item.RecordHash is not null)
                throw new InvalidDataException("The retained case snapshot is inconsistent.");
            _ = ReadCommandId(record);
            _ = ReadObservationDate(record);
            _ = ReadEvidence(record);
            _ = ReadSourceVersions(record);
            if (!IsDigest(record.Data.GetValueOrDefault("sourceFingerprint")))
                throw Unavailable();
            return item;
        }
        catch (Exception error) when (IsUnavailable(error) || error is KeyNotFoundException or ArgumentException)
        { throw Unavailable(); }
    }

    private static Guid ReadParty(OperationalCaseHistoryRecord record, string key) =>
        record.Data.TryGetValue(key, out var value) && Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw Unavailable();
    private static Guid ReadCommandId(OperationalCaseHistoryRecord record) => ReadParty(record, "commandId");
    private static DateOnly ReadObservationDate(OperationalCaseHistoryRecord record) =>
        record.Data.TryGetValue("observationAsOf", out var value)
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date != default ? date : throw Unavailable();
    private static string[] ReadEvidence(OperationalCaseHistoryRecord record)
    {
        try
        {
            if (!record.Data.TryGetValue("evidence", out var json))
                throw Unavailable();
            var evidence = JsonSerializer.Deserialize(json, IntercompanyAgreementWorkQueueJsonContext.Default.StringArray);
            return ValidateEvidence(evidence).ToArray();
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw Unavailable(); }
    }

    private static IReadOnlyList<IntercompanyObligationSourceVersionDto> SourceVersions(Capture capture) =>
        capture.Observation.SourceVersions.Concat(capture.Legal.Values.Select(detail =>
            new IntercompanyObligationSourceVersionDto("work-queue-agreement", detail.AgreementId.ToString("D"),
                detail.AggregateVersion.ToString(CultureInfo.InvariantCulture), Sha256Digest.ComputeUtf8(SerializeLegal(detail)))))
            .Concat(capture.Legal.Values.Where(detail => detail.ApplicableRevision is not null).Select(detail =>
                new IntercompanyObligationSourceVersionDto("approved-legal-revision",
                    detail.AgreementId.ToString("D") + ":" + detail.ApplicableRevision!.AgreementVersion.ToString(CultureInfo.InvariantCulture),
                    detail.ApplicableRevision.AgreementVersion.ToString(CultureInfo.InvariantCulture), detail.ApplicableRevision.PayloadHash)))
            .Distinct().OrderBy(source => source.SourceKind, StringComparer.Ordinal).ThenBy(source => source.SourceId, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<IntercompanyObligationSourceVersionDto> ReadSourceVersions(OperationalCaseHistoryRecord record)
    {
        try
        {
            if (!record.Data.TryGetValue("sourceVersions", out var json))
                throw Unavailable();
            var sources = JsonSerializer.Deserialize(json,
                IntercompanyObligationMonitorJsonContext.Default.IReadOnlyListIntercompanyObligationSourceVersionDto);
            if (sources is null || sources.Count == 0 || sources.Any(source => string.IsNullOrWhiteSpace(source.SourceId)
                || string.IsNullOrWhiteSpace(source.SourceKind) || string.IsNullOrWhiteSpace(source.Version) || !IsDigest(source.ContentHash)))
                throw Unavailable();
            return sources;
        }
        catch (JsonException) { throw Unavailable(); }
    }

    private static OperationalCaseHistoryRecord[] CaseRecords(Capture capture, string caseId) =>
        capture.Records.Where(record => record.CaseId == caseId).ToArray();
    private static string EventId(string scopeKey, Guid commandId) => $"ic-work:{scopeKey}:{commandId:N}";
    private static void RequireVersion(OperationalCaseHistoryRecord[] previous, long version, string? hash)
    {
        var current = previous.LastOrDefault();
        if ((current?.Sequence ?? 0) != version || !string.Equals(current?.RecordHashSha256 ?? "", hash ?? "", StringComparison.Ordinal))
            throw new IntercompanyAgreementWorkQueueConflictException("The case version or history hash changed. Refresh before continuing.");
    }
    private static void ValidateCommand(Guid command, string caseId, long version, string? recordHash, string fingerprint, string reason)
    {
        if (command == Guid.Empty || string.IsNullOrWhiteSpace(caseId) || caseId.Length > 160 || version < 0
            || version > 0 && !IsDigest(recordHash) || version == 0 && !string.IsNullOrEmpty(recordHash)
            || !IsDigest(fingerprint) || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            throw new ArgumentException("A command ID, case identity, exact version/hash, current source fingerprint and reason are required.");
    }
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static IReadOnlyList<string> ValidateEvidence(IReadOnlyList<string>? evidence)
    {
        if (evidence is null)
            return [];
        if (evidence.Count > 32 || evidence.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 2000))
            throw new ArgumentException("Evidence must contain at most 32 nonempty references of at most 2000 characters.");
        return evidence.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    }
    private static string InputDigest(IntercompanyAgreementCallContext context, IntercompanyObligationMonitorRequestDto scope,
        string action, Guid commandId, string caseId, long version, string? recordHash, string fingerprint, string reason,
        string? owner = null, string? note = null, string? evidence = null) => HashParts(context.Actor,
            scope.FundProfileId, scope.LedgerBookId?.ToString("D"), scope.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            scope.ExpiryHorizonDays.ToString(CultureInfo.InvariantCulture), scope.EntityId?.ToString("D"),
            action, commandId.ToString("D"), caseId, version.ToString(CultureInfo.InvariantCulture), recordHash,
            fingerprint, reason, owner, note, evidence);
}
