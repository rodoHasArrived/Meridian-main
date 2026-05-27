using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.Infrastructure.Reconciliation;

public interface IReconciliationCaseStore
{
    Task SaveAsync(ReconciliationCase reconciliationCase, CancellationToken ct = default);
    Task<ReconciliationCase?> GetAsync(string caseId, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationCase>> ListAsync(CancellationToken ct = default);
}

public sealed class JsonReconciliationCaseStore : IReconciliationCaseStore
{
    private readonly string _folder;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly JsonSerializerOptions _auditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public JsonReconciliationCaseStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _folder = Path.Combine(dataRoot, "reconciliation", "cases");
    }

    public async Task SaveAsync(ReconciliationCase reconciliationCase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reconciliationCase);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_folder);
        await AtomicFileWriter
            .WriteAsync(CasePath(reconciliationCase.CaseId), JsonSerializer.Serialize(reconciliationCase, _jsonOptions), ct)
            .ConfigureAwait(false);
        await AppendAuditAsync(reconciliationCase, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationCase?> GetAsync(string caseId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = CasePath(caseId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        return await JsonSerializer
            .DeserializeAsync<ReconciliationCase>(stream, _jsonOptions, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReconciliationCase>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        var cases = new List<ReconciliationCase>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json").OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            var reconciliationCase = await JsonSerializer
                .DeserializeAsync<ReconciliationCase>(stream, _jsonOptions, ct)
                .ConfigureAwait(false);
            if (reconciliationCase is not null)
            {
                cases.Add(reconciliationCase);
            }
        }

        return cases;
    }

    private string CasePath(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        var fileName = $"{Uri.EscapeDataString(caseId.Trim())}.json";
        var directory = Path.GetFullPath(_folder);
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        var directoryPrefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(directoryPrefix, PathComparison))
        {
            throw new ArgumentException("Reconciliation case id resolved outside the case store.", nameof(caseId));
        }

        return path;
    }

    private async Task AppendAuditAsync(ReconciliationCase reconciliationCase, CancellationToken ct)
    {
        var auditDirectory = Path.Combine(_folder, "_audit");
        Directory.CreateDirectory(auditDirectory);
        var auditPath = Path.Combine(auditDirectory, "case-history.jsonl");
        var latestHistory = reconciliationCase.History.LastOrDefault();
        var record = new ReconciliationCaseAuditRecord(
            reconciliationCase.CaseId,
            reconciliationCase.ImportId,
            reconciliationCase.Status,
            reconciliationCase.LastUpdatedAtUtc,
            reconciliationCase.LastUpdatedBy,
            latestHistory);
        await AtomicFileWriter
            .AppendLinesAsync(auditPath, [JsonSerializer.Serialize(record, _auditJsonOptions)], ct)
            .ConfigureAwait(false);
    }

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record ReconciliationCaseAuditRecord(
        string CaseId,
        string ImportId,
        string Status,
        DateTimeOffset RecordedAtUtc,
        string Actor,
        ReconciliationCaseHistoryEntry? LatestHistory);
}

public sealed class ReconciliationCaseService : IReconciliationCaseService
{
    private readonly IReconciliationCaseStore _store;
    private readonly TimeProvider _timeProvider;

    public ReconciliationCaseService(IReconciliationCaseStore store, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ReconciliationCase>> CreateOpenCasesAsync(string importId, IReadOnlyList<MatchOutcome> outcomes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(importId);
        ArgumentNullException.ThrowIfNull(outcomes);

        var now = _timeProvider.GetUtcNow();
        var cases = outcomes
            .Where(o => string.Equals(o.OutcomeType, "unmatched", StringComparison.OrdinalIgnoreCase))
            .Select(o => new ReconciliationCase(
                Guid.NewGuid().ToString("N"),
                importId.Trim(),
                "Open",
                "Unmatched statement row",
                o.Confidence,
                o.Rationale,
                now,
                [new ReconciliationCaseHistoryEntry(now, "None", "Open", "Case created from matcher outcome")])
            {
                DueAtUtc = now.AddDays(2),
                Priority = "High",
                LastUpdatedAtUtc = now,
                LastUpdatedBy = "system",
                AuditEvents = [new ReconciliationCaseAuditEvent(Guid.NewGuid().ToString("N"), "CaseOpened", now, "system", "Case created from matcher outcome.")]
            })
            .ToList();
        foreach (var c in cases)
        {
            await _store.SaveAsync(c, ct).ConfigureAwait(false);
        }

        return cases;
    }

    public async Task<ReconciliationCase> UpdateStatusAsync(string caseId, string toStatus, string note, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedStatus = NormalizeStatus(toStatus);
        var c = await _store.GetAsync(caseId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Case not found: {caseId}");
        ValidateTransition(c.Status, normalizedStatus);
        var now = _timeProvider.GetUtcNow();
        var breachedAt = c.SlaBreachedAtUtc ?? ((c.DueAtUtc.HasValue && now > c.DueAtUtc.Value) ? now : null);
        var updated = c with
        {
            Status = normalizedStatus,
            LastUpdatedAtUtc = now,
            LastUpdatedBy = "system",
            SlaBreachedAtUtc = breachedAt,
            History = c.History.Concat(
            [
                new ReconciliationCaseHistoryEntry(
                    now,
                    c.Status,
                    normalizedStatus,
                    string.IsNullOrWhiteSpace(note) ? "Status updated." : note.Trim())
            ]).ToList(),
            AuditEvents = c.AuditEvents.Concat([new ReconciliationCaseAuditEvent(Guid.NewGuid().ToString("N"), "StatusChanged", now, "system", $"Status changed from {c.Status} to {normalizedStatus}.")]).ToList(),
            Resolution = normalizedStatus == "Resolved" || normalizedStatus == "SignedOff" ? new ReconciliationResolutionMetadata("resolved", string.IsNullOrWhiteSpace(note) ? "Resolved." : note.Trim(), "system", now, normalizedStatus == "SignedOff" ? "system" : null, normalizedStatus == "SignedOff" ? now : null) : c.Resolution
        };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }



    public async Task<ReconciliationCase> AssignAsync(string caseId, string assignee, string note, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(assignee)) throw new ArgumentException("Assignee is required.", nameof(assignee));
        var c = await _store.GetAsync(caseId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"Case not found: {caseId}");
        var now = _timeProvider.GetUtcNow();
        var actor = "system";
        var updated = c with
        {
            Owner = assignee.Trim(),
            LastUpdatedAtUtc = now,
            LastUpdatedBy = actor,
            AuditEvents = c.AuditEvents.Concat([new ReconciliationCaseAuditEvent(Guid.NewGuid().ToString("N"), "OwnerChanged", now, actor, string.IsNullOrWhiteSpace(note) ? $"Assigned to {assignee.Trim()}." : note.Trim())]).ToList()
        };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<ReconciliationCase> AddCommentAsync(string caseId, string subject, string body, string actor, string? parentCommentId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Comment body is required.", nameof(body));
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Comment actor is required.", nameof(actor));
        var c = await _store.GetAsync(caseId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"Case not found: {caseId}");
        var now = _timeProvider.GetUtcNow();
        var threads = c.CommentThreads.ToList();
        var threadId = string.IsNullOrWhiteSpace(subject) ? "general" : subject.Trim();
        var idx = threads.FindIndex(t => string.Equals(t.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
        var comment = new ReconciliationCaseComment(Guid.NewGuid().ToString("N"), body.Trim(), actor.Trim(), now, parentCommentId);
        if (idx < 0) threads.Add(new ReconciliationCaseCommentThread(threadId, string.IsNullOrWhiteSpace(subject) ? "General" : subject.Trim(), [comment]));
        else threads[idx] = threads[idx] with { Comments = threads[idx].Comments.Concat([comment]).ToList() };
        var updated = c with
        {
            CommentThreads = threads,
            LastUpdatedAtUtc = now,
            LastUpdatedBy = actor.Trim(),
            AuditEvents = c.AuditEvents.Concat([new ReconciliationCaseAuditEvent(Guid.NewGuid().ToString("N"), "CommentAdded", now, actor.Trim(), $"Comment added to thread '{threadId}'.")]).ToList()
        };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<IReadOnlyList<ReconciliationCase>> ListOpenCasesAsync(CancellationToken ct = default)
        => (await _store.ListAsync(ct).ConfigureAwait(false))
            .Where(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string NormalizeStatus(string status)
        => status?.Trim() switch
        {
            "Open" => "Open",
            "Investigating" => "Investigating",
            "AwaitingEvidence" => "AwaitingEvidence",
            "Resolved" => "Resolved",
            "SignedOff" => "SignedOff",
            _ => throw new ArgumentException($"Unsupported reconciliation case status '{status}'.", nameof(status))
        };

    private static void ValidateTransition(string fromStatus, string toStatus)
    {
        if (string.Equals(fromStatus, toStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Reconciliation case is already '{toStatus}'.");
        }

        var allowed = fromStatus switch
        {
            "Open" => toStatus is "Investigating",
            "Investigating" => toStatus is "AwaitingEvidence" or "Resolved",
            "AwaitingEvidence" => toStatus is "Investigating" or "Resolved",
            "Resolved" => toStatus is "SignedOff",
            _ => false
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"Cannot transition reconciliation case from '{fromStatus}' to '{toStatus}'.");
        }
    }
}
