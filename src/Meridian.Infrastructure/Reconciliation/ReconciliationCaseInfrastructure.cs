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

    Task MaterializeRunProjectionAsync(
        ReconciliationCase initialCase,
        StatementRunProjectionAudit audit,
        IReadOnlyList<ReconciliationCase> authorizedImages,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This reconciliation case store does not support verified statement-run projection.");

    Task<StatementRunProjectionAudit?> GetRunProjectionAuditAsync(
        string runId,
        string caseId,
        CancellationToken ct = default)
        => Task.FromResult<StatementRunProjectionAudit?>(null);

    Task MaterializeCaseworkAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This reconciliation case store does not support source-commit case projection.");

    Task MaterializeCaseworkAuditAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This reconciliation case store does not support source-commit case-audit projection.");

    Task<ReconciliationCaseAuditEvent?> GetCaseworkAuditAsync(
        string caseId,
        string commandId,
        CancellationToken ct = default)
        => Task.FromResult<ReconciliationCaseAuditEvent?>(null);
}

public sealed class JsonReconciliationCaseStore : IReconciliationCaseStore
{
    private readonly string _folder;
    private readonly SemaphoreSlim _gate = new(1, 1);
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

        return await ReadCaseCoreAsync(path, ct).ConfigureAwait(false);
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

    public async Task MaterializeCaseworkAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
    {
        var envelope = await RequirePreparedCommitAsync(
                commitStore,
                commandId,
                inputHashSha256,
                ct)
            .ConfigureAwait(false);
        var next = envelope.NextCase
            ?? throw new InvalidOperationException(
                $"Statement casework commit '{commandId}' does not contain a case projection.");
        var audit = envelope.CaseAudit
            ?? throw new InvalidOperationException(
                $"Statement casework commit '{commandId}' does not contain a case audit.");
        if (!next.AuditEvents.Any(item => string.Equals(item.EventId, audit.EventId, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The source-commit case image must contain its retained casework audit event.",
                nameof(next));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var casePath = CasePath(next.CaseId);
            var current = await ReadCaseCoreAsync(casePath, ct).ConfigureAwait(false);
            var retainedEvent = current?.AuditEvents.FirstOrDefault(
                item => string.Equals(item.EventId, audit.EventId, StringComparison.Ordinal));
            if (retainedEvent is not null && !SameArtifact(retainedEvent, audit))
            {
                throw new InvalidOperationException(
                    $"Reconciliation case '{next.CaseId}' retains conflicting evidence for source command '{commandId}'.");
            }

            var alreadyApplied = current is not null && SameArtifact(current, next);
            if (!alreadyApplied && current is not null &&
                (envelope.OriginalCase is null || !SameArtifact(current, envelope.OriginalCase)))
            {
                throw new InvalidOperationException(
                    $"Reconciliation case '{next.CaseId}' no longer matches either retained source-commit image.");
            }

            if (!alreadyApplied)
            {
                await AtomicFileWriter
                    .WriteAsync(
                        casePath,
                        JsonSerializer.Serialize(
                            next,
                            StatementDurabilityJsonContext.Default.ReconciliationCase),
                        ct)
                    .ConfigureAwait(false);
            }

            await MaterializeCaseworkAuditCoreAsync(next.CaseId, commandId, audit, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MaterializeCaseworkAuditAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
    {
        var envelope = await RequirePreparedCommitAsync(
                commitStore,
                commandId,
                inputHashSha256,
                ct)
            .ConfigureAwait(false);
        var next = envelope.NextCase
            ?? throw new InvalidOperationException(
                $"Statement casework commit '{commandId}' does not contain a case projection.");
        var audit = envelope.CaseAudit
            ?? throw new InvalidOperationException(
                $"Statement casework commit '{commandId}' does not contain a case audit.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MaterializeCaseworkAuditCoreAsync(next.CaseId, commandId, audit, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MaterializeRunProjectionAsync(
        ReconciliationCase initialCase,
        StatementRunProjectionAudit audit,
        IReadOnlyList<ReconciliationCase> authorizedImages,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initialCase);
        ValidateRunProjectionAudit(initialCase, audit);
        ValidateAuthorizedCaseImages(initialCase, authorizedImages);
        var authoritativeCase = authorizedImages[^1];
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var casePath = CasePath(initialCase.CaseId);
            var retained = await ReadCaseCoreAsync(casePath, ct).ConfigureAwait(false);
            var auditPath = RunProjectionAuditPath(initialCase.ImportId, initialCase.CaseId);
            var retainedAudit = await ReadRunProjectionAuditCoreAsync(auditPath, ct).ConfigureAwait(false);
            if (retained is not null && !authorizedImages.Any(image => SameArtifact(retained, image)))
            {
                throw new InvalidOperationException(
                    $"Reconciliation case '{initialCase.CaseId}' is outside its immutable match/source-commit authority chain.");
            }

            if (retainedAudit is not null && !SameArtifact(retainedAudit, audit))
            {
                throw new InvalidOperationException(
                    $"Reconciliation case '{initialCase.CaseId}' retains a conflicting run-projection audit.");
            }

            if (retained is null || !SameArtifact(retained, authoritativeCase))
            {
                await AtomicFileWriter
                    .WriteAsync(
                        casePath,
                        JsonSerializer.Serialize(
                            authoritativeCase,
                            StatementDurabilityJsonContext.Default.ReconciliationCase),
                        ct)
                    .ConfigureAwait(false);
            }

            if (retainedAudit is null)
            {
                await AtomicFileWriter
                    .WriteAsync(
                        auditPath,
                        JsonSerializer.Serialize(
                            audit,
                            StatementDurabilityJsonContext.Default.StatementRunProjectionAudit),
                        ct)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StatementRunProjectionAudit?> GetRunProjectionAuditAsync(
        string runId,
        string caseId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadRunProjectionAuditCoreAsync(
                    RunProjectionAuditPath(runId, caseId),
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationCaseAuditEvent?> GetCaseworkAuditAsync(
        string caseId,
        string commandId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadAuditCoreAsync(CaseworkAuditPath(caseId, commandId), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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

    private string CaseworkAuditPath(string caseId, string commandId)
        => Path.Combine(
            _folder,
            "_casework",
            "audit",
            ReconciliationRecordFileName.For(caseId),
            $"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(commandId.Trim()))).ToLowerInvariant()}.json");

    private string RunProjectionAuditPath(string runId, string caseId)
        => Path.Combine(
            _folder,
            "_run-projections",
            ReconciliationRecordFileName.For(runId),
            $"{ReconciliationRecordFileName.For(caseId)}.json");

    private async Task<ReconciliationCase?> ReadCaseCoreAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync(
                stream,
                StatementDurabilityJsonContext.Default.ReconciliationCase,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Reconciliation case artifact '{path}' retained a null payload.");
    }

    private async Task<ReconciliationCaseAuditEvent?> ReadAuditCoreAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync(
                stream,
                StatementDurabilityJsonContext.Default.ReconciliationCaseAuditEvent,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Reconciliation case audit '{path}' retained a null payload.");
    }

    private async Task MaterializeCaseworkAuditCoreAsync(
        string caseId,
        string commandId,
        ReconciliationCaseAuditEvent audit,
        CancellationToken ct)
    {
        var auditPath = CaseworkAuditPath(caseId, commandId);
        var retainedAudit = await ReadAuditCoreAsync(auditPath, ct).ConfigureAwait(false);
        if (retainedAudit is not null)
        {
            if (!SameArtifact(retainedAudit, audit))
            {
                throw new InvalidOperationException(
                    $"Statement case audit for command '{commandId}' conflicts with the retained source commit.");
            }

            return;
        }

        await AtomicFileWriter
            .WriteAsync(
                auditPath,
                JsonSerializer.Serialize(
                    audit,
                    StatementDurabilityJsonContext.Default.ReconciliationCaseAuditEvent),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<StatementRunProjectionAudit?> ReadRunProjectionAuditCoreAsync(
        string path,
        CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync(
                stream,
                StatementDurabilityJsonContext.Default.StatementRunProjectionAudit,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Reconciliation case projection audit '{path}' retained a null payload.");
    }

    private static async Task<StatementCaseworkCommitEnvelope> RequirePreparedCommitAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commitStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputHashSha256);
        var envelope = await commitStore.GetAsync(commandId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Statement casework command '{commandId}' cannot project before its immutable commit is prepared.");
        if (!StatementDurabilityHashing.FixedTimeEquals(envelope.InputHashSha256, inputHashSha256))
        {
            throw new InvalidOperationException(
                $"Statement casework command '{commandId}' is bound to different prepared input.");
        }

        return envelope;
    }

    private static void ValidateRunProjectionAudit(
        ReconciliationCase reconciliationCase,
        StatementRunProjectionAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.SchemaVersion != StatementRunProjectionAudit.CurrentSchemaVersion ||
            !string.Equals(audit.RunId, reconciliationCase.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(audit.ImportId, reconciliationCase.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(audit.ProjectionKind, StatementRunProjectionAudit.CaseKind, StringComparison.Ordinal) ||
            !string.Equals(audit.ProjectionId, reconciliationCase.CaseId, StringComparison.OrdinalIgnoreCase) ||
            !StatementDurabilityHashing.FixedTimeEquals(
                audit.ArtifactSha256,
                StatementDurabilityHashing.Hash(reconciliationCase)))
        {
            throw new InvalidDataException(
                $"Reconciliation case '{reconciliationCase.CaseId}' run-projection audit does not bind the supplied immutable artifact.");
        }
    }

    private static void ValidateAuthorizedCaseImages(
        ReconciliationCase initialCase,
        IReadOnlyList<ReconciliationCase> authorizedImages)
    {
        ArgumentNullException.ThrowIfNull(authorizedImages);
        if (authorizedImages.Count == 0 || !SameArtifact(authorizedImages[0], initialCase) ||
            authorizedImages.Any(image =>
                image is null ||
                !string.Equals(image.CaseId, initialCase.CaseId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(image.ImportId, initialCase.ImportId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Reconciliation case '{initialCase.CaseId}' received an invalid immutable authority chain.");
        }
    }

    private static bool SameArtifact(ReconciliationCase left, ReconciliationCase right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private static bool SameArtifact(ReconciliationCaseAuditEvent left, ReconciliationCaseAuditEvent right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private static bool SameArtifact(StatementRunProjectionAudit left, StatementRunProjectionAudit right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(
                left,
                StatementDurabilityJsonContext.Default.StatementRunProjectionAudit),
            StatementDurabilityHashing.Hash(
                right,
                StatementDurabilityJsonContext.Default.StatementRunProjectionAudit));

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
            .Select(o =>
            {
                var evidenceRef = $"statement-row:{o.RowChecksum}";
                return new ReconciliationCase(
                Guid.NewGuid().ToString("N"),
                importId.Trim(),
                "Open",
                "Unmatched statement row",
                o.Confidence,
                o.Rationale,
                now,
                [new ReconciliationCaseHistoryEntry(now, "None", "Open", "Case created from matcher outcome") { EvidenceId = evidenceRef }])
                {
                    EvidenceReferences = [evidenceRef],
                    DueAtUtc = now.AddDays(2),
                    Priority = "High",
                    LastUpdatedAtUtc = now,
                    LastUpdatedBy = "system",
                    AuditEvents = [new ReconciliationCaseAuditEvent(Guid.NewGuid().ToString("N"), "CaseOpened", now, "system", "Case created from matcher outcome.")]
                };
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
        var decisionNote = string.IsNullOrWhiteSpace(note) ? "Status updated." : note.Trim();
        var isTerminalDecision = normalizedStatus is "Resolved" or "Dismissed" or "SignedOff";
        var evidenceId = c.EvidenceReferences.FirstOrDefault();
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
                    decisionNote)
                {
                    EvidenceId = evidenceId
                }
            ]).ToList(),
            AuditEvents = c.AuditEvents.Concat([new ReconciliationCaseAuditEvent(Guid.NewGuid().ToString("N"), "StatusChanged", now, "system", $"Status changed from {c.Status} to {normalizedStatus}.")]).ToList(),
            DecisionNotes = isTerminalDecision
                ? c.DecisionNotes.Concat([new ReconciliationCaseDecisionNote(Guid.NewGuid().ToString("N"), "system", now, decisionNote, c.EvidenceReferences)]).ToList()
                : c.DecisionNotes,
            Resolution = normalizedStatus == "Resolved" || normalizedStatus == "SignedOff"
                ? new ReconciliationResolutionMetadata("resolved", decisionNote, "system", now, normalizedStatus == "SignedOff" ? "system" : null, normalizedStatus == "SignedOff" ? now : null)
                : normalizedStatus == "Dismissed"
                    ? new ReconciliationResolutionMetadata("dismissed", decisionNote, "system", now)
                    : c.Resolution
        };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }



    public async Task<ReconciliationCase> AssignAsync(string caseId, string assignee, string note, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(assignee))
            throw new ArgumentException("Assignee is required.", nameof(assignee));
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
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Comment body is required.", nameof(body));
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Comment actor is required.", nameof(actor));
        var c = await _store.GetAsync(caseId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"Case not found: {caseId}");
        var now = _timeProvider.GetUtcNow();
        var threads = c.CommentThreads.ToList();
        var threadId = string.IsNullOrWhiteSpace(subject) ? "general" : subject.Trim();
        var idx = threads.FindIndex(t => string.Equals(t.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
        var comment = new ReconciliationCaseComment(Guid.NewGuid().ToString("N"), body.Trim(), actor.Trim(), now, parentCommentId);
        if (idx < 0)
            threads.Add(new ReconciliationCaseCommentThread(threadId, string.IsNullOrWhiteSpace(subject) ? "General" : subject.Trim(), [comment]));
        else
            threads[idx] = threads[idx] with { Comments = threads[idx].Comments.Concat([comment]).ToList() };
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
            "Dismissed" => "Dismissed",
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
            "Open" => toStatus is "Investigating" or "Dismissed",
            "Investigating" => toStatus is "AwaitingEvidence" or "Resolved" or "Dismissed",
            "AwaitingEvidence" => toStatus is "Investigating" or "Resolved" or "Dismissed",
            "Resolved" => toStatus is "SignedOff",
            "Dismissed" => false,
            _ => false
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"Cannot transition reconciliation case from '{fromStatus}' to '{toStatus}'.");
        }
    }
}
