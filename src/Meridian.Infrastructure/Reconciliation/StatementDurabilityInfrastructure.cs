using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.Infrastructure.Reconciliation;

public sealed record StatementRunMatchArtifact(
    string RunId,
    string ImportId,
    IReadOnlyList<ReconciliationBreakRecord> Breaks,
    IReadOnlyList<ReconciliationCase> Cases,
    int MatchCount);

/// <summary>
/// Deterministic sidecar binding a live statement projection to the immutable match artifact that
/// produced it. Missing sidecars may be reconstructed; mismatched sidecars are corruption.
/// </summary>
public sealed record StatementRunProjectionAudit(
    int SchemaVersion,
    string RunId,
    string ImportId,
    string ProjectionKind,
    string ProjectionId,
    string ArtifactSha256,
    DateTimeOffset MaterializedAtUtc)
{
    public const int CurrentSchemaVersion = 1;
    public const string BreakKind = "break";
    public const string CaseKind = "case";
}

public interface IStatementRunMatchArtifactStore
{
    Task<StatementRunMatchArtifact?> GetAsync(string runId, CancellationToken ct = default);
    Task SaveAsync(StatementRunMatchArtifact artifact, CancellationToken ct = default);
}

public sealed class InMemoryStatementRunMatchArtifactStore : IStatementRunMatchArtifactStore
{
    private readonly ConcurrentDictionary<string, StatementRunMatchArtifact> _artifacts =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<StatementRunMatchArtifact?> GetAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_artifacts.TryGetValue(runId.Trim(), out var artifact) ? artifact : null);
    }

    public Task SaveAsync(StatementRunMatchArtifact artifact, CancellationToken ct = default)
    {
        ValidateArtifact(artifact);
        ct.ThrowIfCancellationRequested();
        _artifacts.AddOrUpdate(
            artifact.RunId,
            artifact,
            (_, retained) => StatementDurabilityHashing.FixedTimeEquals(
                    StatementDurabilityHashing.Hash(
                        retained,
                        StatementDurabilityJsonContext.Default.StatementRunMatchArtifact),
                    StatementDurabilityHashing.Hash(
                        artifact,
                        StatementDurabilityJsonContext.Default.StatementRunMatchArtifact))
                ? retained
                : throw new InvalidOperationException(
                    $"Statement run '{artifact.RunId}' already retains a different match artifact."));
        return Task.CompletedTask;
    }

    internal static void ValidateArtifact(StatementRunMatchArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.ImportId);
        ArgumentNullException.ThrowIfNull(artifact.Breaks);
        ArgumentNullException.ThrowIfNull(artifact.Cases);
        if (artifact.MatchCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(artifact), "Statement match count cannot be negative.");
        }
    }
}

public sealed class FileStatementRunMatchArtifactStore : IStatementRunMatchArtifactStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    public FileStatementRunMatchArtifactStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _root = Path.Combine(dataRoot, "reconciliation", "statement-runs");
    }

    public async Task<StatementRunMatchArtifact?> GetAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var path = ArtifactPath(runId);
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
        return await JsonSerializer
            .DeserializeAsync(stream, StatementDurabilityJsonContext.Default.StatementRunMatchArtifact, ct)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(StatementRunMatchArtifact artifact, CancellationToken ct = default)
    {
        InMemoryStatementRunMatchArtifactStore.ValidateArtifact(artifact);
        var path = ArtifactPath(artifact.RunId);
        var gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = await GetAsync(artifact.RunId, ct).ConfigureAwait(false);
            if (retained is not null)
            {
                if (!StatementDurabilityHashing.FixedTimeEquals(
                        StatementDurabilityHashing.Hash(
                            retained,
                            StatementDurabilityJsonContext.Default.StatementRunMatchArtifact),
                        StatementDurabilityHashing.Hash(
                            artifact,
                            StatementDurabilityJsonContext.Default.StatementRunMatchArtifact)))
                {
                    throw new InvalidOperationException(
                        $"Statement run '{artifact.RunId}' already retains a different match artifact.");
                }

                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(
                    artifact,
                    StatementDurabilityJsonContext.Default.StatementRunMatchArtifact);
                await AtomicFileWriter.WriteAsync(temporaryPath, json, ct).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: false);
                await AtomicFileWriter
                    .SyncDirectoryAsync(Path.GetDirectoryName(path)!, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(path))
            {
                retained = await GetAsync(artifact.RunId, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException(
                        $"Statement run '{artifact.RunId}' retained an unreadable concurrent match artifact.");
                if (!StatementDurabilityHashing.FixedTimeEquals(
                        StatementDurabilityHashing.Hash(
                            retained,
                            StatementDurabilityJsonContext.Default.StatementRunMatchArtifact),
                        StatementDurabilityHashing.Hash(
                            artifact,
                            StatementDurabilityJsonContext.Default.StatementRunMatchArtifact)))
                {
                    throw new InvalidOperationException(
                        $"Statement run '{artifact.RunId}' concurrently retained a different match artifact.");
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string ArtifactPath(string runId)
        => Path.Combine(_root, ReconciliationRecordFileName.For(runId), "workflow-match-artifact.json");
}

public sealed record StatementCaseworkCommitEnvelope(
    int SchemaVersion,
    string CommandId,
    string InputHashSha256,
    string ImportId,
    ReconciliationBreakRecord OriginalBreak,
    ReconciliationBreakRecord NextBreak,
    ReconciliationCase? OriginalCase,
    ReconciliationCase? NextCase,
    StatementBreakCaseworkAuditEvent BreakAudit,
    ReconciliationCaseAuditEvent? CaseAudit,
    DateTimeOffset PreparedAtUtc,
    bool AdoptedLegacyReceipt)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record StatementCaseworkLegacyReceipt(
    string CommandId,
    string BreakId,
    string InputHashSha256,
    ReconciliationBreakRecord Record,
    StatementBreakCaseworkAuditEvent Audit);

internal sealed record StatementCaseworkCompletion(
    string CommandId,
    string InputHashSha256,
    string EnvelopeHashSha256,
    DateTimeOffset CompletedAtUtc);

public interface IStatementCaseworkCommitStore
{
    Task<StatementCaseworkCommitEnvelope?> GetAsync(string commandId, CancellationToken ct = default);
    Task<IReadOnlyList<StatementCaseworkCommitEnvelope>> ListByRunAsync(
        string runId,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This statement casework commit store cannot enumerate retained source commits by run.");
    Task<StatementCaseworkLegacyReceipt?> GetLegacyReceiptAsync(
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default);
    Task<StatementCaseworkCommitEnvelope> PrepareAsync(
        StatementCaseworkCommitEnvelope envelope,
        CancellationToken ct = default);
    Task<bool> IsCompletedAsync(string commandId, string inputHashSha256, CancellationToken ct = default);
    Task CompleteAsync(string commandId, string inputHashSha256, CancellationToken ct = default);
}

public sealed class FileStatementCaseworkCommitStore : IStatementCaseworkCommitStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;
    private readonly string _legacyReceiptRoot;

    public FileStatementCaseworkCommitStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _root = Path.Combine(dataRoot, "reconciliation", "statement-casework-commits");
        _legacyReceiptRoot = Path.Combine(
            dataRoot,
            "reconciliation",
            "statement-breaks",
            "_casework",
            "receipts");
    }

    public async Task<StatementCaseworkCommitEnvelope?> GetAsync(
        string commandId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var envelope = await ReadAsync(
                EnvelopePath(commandId),
                StatementDurabilityJsonContext.Default.StatementCaseworkCommitEnvelope,
                ct)
            .ConfigureAwait(false);
        if (envelope is null)
        {
            return null;
        }

        ValidateEnvelope(envelope);
        if (!string.Equals(envelope.CommandId, commandId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Statement casework commit '{commandId}' retained mismatched command identity '{envelope.CommandId}'.");
        }

        return envelope;
    }

    public async Task<IReadOnlyList<StatementCaseworkCommitEnvelope>> ListByRunAsync(
        string runId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var envelopeDirectory = Path.Combine(_root, "envelopes");
        if (!Directory.Exists(envelopeDirectory))
        {
            return [];
        }

        var retained = new List<StatementCaseworkCommitEnvelope>();
        foreach (var path in Directory.EnumerateFiles(envelopeDirectory, "*.json")
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var envelope = await ReadAsync(
                    path,
                    StatementDurabilityJsonContext.Default.StatementCaseworkCommitEnvelope,
                    ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Statement casework envelope '{path}' retained a null payload.");
            ValidateEnvelope(envelope);
            if (!PathsEqual(path, EnvelopePath(envelope.CommandId)))
            {
                throw new InvalidDataException(
                    $"Statement casework envelope '{path}' is retained under the wrong command identity.");
            }

            if (!string.Equals(envelope.ImportId, runId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // This validates an existing completion marker against the immutable envelope while
            // deliberately retaining prepared-but-incomplete envelopes as source authority too.
            _ = await IsCompletedAsync(
                    envelope.CommandId,
                    envelope.InputHashSha256,
                    ct)
                .ConfigureAwait(false);
            retained.Add(envelope);
        }

        return retained
            .OrderBy(static envelope => envelope.PreparedAtUtc)
            .ThenBy(static envelope => envelope.CommandId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<StatementCaseworkLegacyReceipt?> GetLegacyReceiptAsync(
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
    {
        ValidateKey(commandId, inputHashSha256);
        var path = Path.Combine(_legacyReceiptRoot, $"{CommandFileName(commandId)}.json");
        var receipt = await ReadAsync(
                path,
                StatementLegacyCaseworkJsonContext.Default.StatementCaseworkLegacyReceipt,
                ct)
            .ConfigureAwait(false);
        if (receipt is null)
        {
            return null;
        }

        if (!string.Equals(receipt.CommandId, commandId.Trim(), StringComparison.Ordinal) ||
            !StatementDurabilityHashing.FixedTimeEquals(receipt.InputHashSha256, inputHashSha256))
        {
            throw new InvalidOperationException(
                $"Legacy statement casework receipt '{commandId}' is bound to different input and cannot be adopted.");
        }

        return receipt;
    }

    public async Task<StatementCaseworkCommitEnvelope> PrepareAsync(
        StatementCaseworkCommitEnvelope envelope,
        CancellationToken ct = default)
    {
        ValidateEnvelope(envelope);
        var path = EnvelopePath(envelope.CommandId);
        var gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = await ReadAsync(
                    path,
                    StatementDurabilityJsonContext.Default.StatementCaseworkCommitEnvelope,
                    ct)
                .ConfigureAwait(false);
            if (retained is not null)
            {
                ValidateEnvelope(retained);
                EnsureSameInput(retained, envelope.CommandId, envelope.InputHashSha256);
                return retained;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(
                    envelope,
                    StatementDurabilityJsonContext.Default.StatementCaseworkCommitEnvelope);
                await AtomicFileWriter.WriteAsync(temporaryPath, json, ct).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: false);
                await AtomicFileWriter
                    .SyncDirectoryAsync(Path.GetDirectoryName(path)!, CancellationToken.None)
                    .ConfigureAwait(false);
                return envelope;
            }
            catch (IOException) when (File.Exists(path))
            {
                retained = await ReadAsync(
                        path,
                        StatementDurabilityJsonContext.Default.StatementCaseworkCommitEnvelope,
                        ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidDataException(
                        $"Statement casework commit '{envelope.CommandId}' exists without a readable envelope.");
                ValidateEnvelope(retained);
                EnsureSameInput(retained, envelope.CommandId, envelope.InputHashSha256);
                return retained;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> IsCompletedAsync(
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
    {
        ValidateKey(commandId, inputHashSha256);
        var completion = await ReadAsync(
                CompletionPath(commandId),
                StatementDurabilityJsonContext.Default.StatementCaseworkCompletion,
                ct)
            .ConfigureAwait(false);
        if (completion is null)
        {
            return false;
        }

        if (!string.Equals(completion.CommandId, commandId.Trim(), StringComparison.Ordinal) ||
            !StatementDurabilityHashing.FixedTimeEquals(completion.InputHashSha256, inputHashSha256))
        {
            throw new InvalidOperationException(
                $"Statement casework completion '{commandId}' is bound to different input.");
        }

        var envelope = await GetAsync(commandId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Statement casework completion '{commandId}' exists without its immutable envelope.");
        if (!StatementDurabilityHashing.FixedTimeEquals(
                completion.EnvelopeHashSha256,
                StatementDurabilityHashing.Hash(envelope)))
        {
            throw new InvalidDataException(
                $"Statement casework completion '{commandId}' does not match its immutable envelope.");
        }

        return true;
    }

    public async Task CompleteAsync(
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
    {
        ValidateKey(commandId, inputHashSha256);
        var envelope = await GetAsync(commandId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Statement casework commit '{commandId}' cannot complete before its immutable envelope is retained.");
        EnsureSameInput(envelope, commandId, inputHashSha256);
        var completion = new StatementCaseworkCompletion(
            commandId.Trim(),
            inputHashSha256.Trim().ToLowerInvariant(),
            StatementDurabilityHashing.Hash(envelope),
            DateTimeOffset.UtcNow);
        var path = CompletionPath(commandId);
        var gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = await ReadAsync(
                    path,
                    StatementDurabilityJsonContext.Default.StatementCaseworkCompletion,
                    ct)
                .ConfigureAwait(false);
            if (retained is not null)
            {
                if (!string.Equals(retained.CommandId, commandId.Trim(), StringComparison.Ordinal) ||
                    !StatementDurabilityHashing.FixedTimeEquals(retained.InputHashSha256, inputHashSha256) ||
                    !StatementDurabilityHashing.FixedTimeEquals(retained.EnvelopeHashSha256, completion.EnvelopeHashSha256))
                {
                    throw new InvalidOperationException(
                        $"Statement casework completion '{commandId}' conflicts with the retained envelope.");
                }

                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await AtomicFileWriter
                    .WriteAsync(
                        temporaryPath,
                        JsonSerializer.Serialize(
                            completion,
                            StatementDurabilityJsonContext.Default.StatementCaseworkCompletion),
                        ct)
                    .ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: false);
                await AtomicFileWriter
                    .SyncDirectoryAsync(Path.GetDirectoryName(path)!, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(path))
            {
                retained = await ReadAsync(
                        path,
                        StatementDurabilityJsonContext.Default.StatementCaseworkCompletion,
                        ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidDataException(
                        $"Statement casework completion '{commandId}' exists without a readable marker.");
                if (!string.Equals(retained.CommandId, commandId.Trim(), StringComparison.Ordinal) ||
                    !StatementDurabilityHashing.FixedTimeEquals(retained.InputHashSha256, inputHashSha256) ||
                    !StatementDurabilityHashing.FixedTimeEquals(retained.EnvelopeHashSha256, completion.EnvelopeHashSha256))
                {
                    throw new InvalidOperationException(
                        $"Statement casework completion '{commandId}' conflicts with the retained envelope.");
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ValidateEnvelope(StatementCaseworkCommitEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateKey(envelope.CommandId, envelope.InputHashSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.ImportId);
        ArgumentNullException.ThrowIfNull(envelope.OriginalBreak);
        ArgumentNullException.ThrowIfNull(envelope.NextBreak);
        ArgumentNullException.ThrowIfNull(envelope.BreakAudit);
        if (envelope.PreparedAtUtc == default)
        {
            throw new InvalidDataException(
                $"Statement casework commit '{envelope.CommandId}' is missing its preparation timestamp.");
        }

        if (envelope.SchemaVersion != StatementCaseworkCommitEnvelope.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported statement casework commit schema version '{envelope.SchemaVersion}'.");
        }

        if (!string.Equals(envelope.OriginalBreak.BreakId, envelope.NextBreak.BreakId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(envelope.OriginalBreak.RunId, envelope.NextBreak.RunId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(envelope.OriginalBreak.ImportId, envelope.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(envelope.NextBreak.ImportId, envelope.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(envelope.BreakAudit.BreakId, envelope.NextBreak.BreakId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(envelope.BreakAudit.ImportId, envelope.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(envelope.BreakAudit.CommandId, envelope.CommandId, StringComparison.Ordinal) ||
            !string.Equals(envelope.BreakAudit.PreviousStatus, envelope.OriginalBreak.Status, StringComparison.Ordinal) ||
            !string.Equals(envelope.BreakAudit.NewStatus, envelope.NextBreak.Status, StringComparison.Ordinal) ||
            !StatementDurabilityHashing.FixedTimeEquals(
                envelope.BreakAudit.InputHashSha256,
                envelope.InputHashSha256))
        {
            throw new InvalidDataException(
                $"Statement casework commit '{envelope.CommandId}' retains inconsistent break images or audit evidence.");
        }

        if ((envelope.OriginalCase is null) != (envelope.NextCase is null) ||
            (envelope.NextCase is null) != (envelope.CaseAudit is null))
        {
            throw new InvalidDataException(
                $"Statement casework commit '{envelope.CommandId}' retains an incomplete case projection pair.");
        }

        if (envelope.NextCase is not null && envelope.CaseAudit is not null &&
            (!string.Equals(envelope.NextCase.ImportId, envelope.ImportId, StringComparison.OrdinalIgnoreCase) ||
             envelope.OriginalCase is not null &&
             (!string.Equals(envelope.OriginalCase.CaseId, envelope.NextCase.CaseId, StringComparison.OrdinalIgnoreCase) ||
              !string.Equals(envelope.OriginalCase.ImportId, envelope.ImportId, StringComparison.OrdinalIgnoreCase)) ||
              !envelope.NextCase.AuditEvents.Any(item =>
                  string.Equals(item.EventId, envelope.CaseAudit.EventId, StringComparison.Ordinal) &&
                  StatementDurabilityHashing.FixedTimeEquals(
                      StatementDurabilityHashing.Hash(item),
                      StatementDurabilityHashing.Hash(envelope.CaseAudit)))))
        {
            throw new InvalidDataException(
                $"Statement casework commit '{envelope.CommandId}' retains inconsistent case images or audit evidence.");
        }
    }

    private static void EnsureSameInput(
        StatementCaseworkCommitEnvelope envelope,
        string commandId,
        string inputHashSha256)
    {
        if (!string.Equals(envelope.CommandId, commandId.Trim(), StringComparison.Ordinal) ||
            !StatementDurabilityHashing.FixedTimeEquals(envelope.InputHashSha256, inputHashSha256))
        {
            throw new InvalidOperationException(
                $"Statement casework command '{commandId}' is already bound to different input.");
        }
    }

    private static void ValidateKey(string commandId, string inputHashSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputHashSha256);
        if (inputHashSha256.Trim().Length != 64 ||
            inputHashSha256.Trim().Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Statement casework input hash must be a SHA-256 hexadecimal value.", nameof(inputHashSha256));
        }
    }

    private static async Task<T?> ReadAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
        where T : class
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Statement durability artifact '{path}' retained a null payload.");
    }

    private string EnvelopePath(string commandId)
        => Path.Combine(_root, "envelopes", $"{CommandFileName(commandId)}.json");

    private string CompletionPath(string commandId)
        => Path.Combine(_root, "completed", $"{CommandFileName(commandId)}.json");

    private static string CommandFileName(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commandId.Trim())))
            .ToLowerInvariant();
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

}

public static class StatementBreakCaseworkFingerprint
{
    public static string Compute(StatementBreakCaseworkUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var canonical = JsonSerializer.Serialize(update with
        {
            BreakId = update.BreakId.Trim(),
            ImportId = update.ImportId.Trim(),
            Status = update.Status.Trim(),
            Actor = update.Actor.Trim(),
            Action = update.Action.Trim(),
            CommandId = update.CommandId.Trim(),
            CorrelationId = update.CorrelationId.Trim(),
            Reason = Normalize(update.Reason),
            Disposition = Normalize(update.Disposition),
            ApprovalActor = Normalize(update.ApprovalActor),
            ApprovalReference = Normalize(update.ApprovalReference),
            SupersedingBreakId = Normalize(update.SupersedingBreakId),
            EvidenceLinks = NormalizeEvidence(update.EvidenceLinks),
            OccurredAtUtc = update.OccurredAtUtc.ToUniversalTime()
        }, StatementLegacyCaseworkJsonContext.Default.StatementBreakCaseworkUpdate);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"meridian.statement-break-casework.v1\n{canonical}"))).ToLowerInvariant();
    }

    private static IReadOnlyList<string> NormalizeEvidence(IReadOnlyList<string>? evidence)
        => (evidence ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class StatementDurabilityHashing
{
    public static string Hash<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeInfo);
        var json = JsonSerializer.Serialize(value, typeInfo);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static string Hash(ReconciliationBreakRecord value)
        => Hash(value, StatementDurabilityJsonContext.Default.ReconciliationBreakRecord);

    public static string Hash(ReconciliationCase value)
        => Hash(value, StatementDurabilityJsonContext.Default.ReconciliationCase);

    public static string Hash(ReconciliationCaseAuditEvent value)
        => Hash(value, StatementDurabilityJsonContext.Default.ReconciliationCaseAuditEvent);

    public static string Hash(StatementBreakCaseworkAuditEvent value)
        => Hash(value, StatementDurabilityJsonContext.Default.StatementBreakCaseworkAuditEvent);

    public static string Hash(StatementCaseworkCommitEnvelope value)
        => Hash(value, StatementDurabilityJsonContext.Default.StatementCaseworkCommitEnvelope);

    public static bool FixedTimeEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            var leftBytes = Convert.FromHexString(left.Trim());
            var rightBytes = Convert.FromHexString(right.Trim());
            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
