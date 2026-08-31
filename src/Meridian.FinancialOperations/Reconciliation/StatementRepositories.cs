using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.Reconciliation;

public interface IStatementRunRepository
{
    Task SaveAsync(StatementRunManifest manifest, CancellationToken ct = default);
    Task<StatementRunManifest?> GetAsync(string runId, CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunManifest>> ListAsync(string? fundAccountId = null, CancellationToken ct = default);
}

public interface IStatementValidationIssueRepository
{
    Task SaveAsync(string runId, IReadOnlyList<StatementValidationIssueDto> issues, CancellationToken ct = default);
    Task<IReadOnlyList<StatementValidationIssueDto>> GetAsync(string runId, CancellationToken ct = default);
}

public interface IStatementNormalizedEntityRepository
{
    Task SaveAsync(string runId, StatementNormalizedEntities entities, CancellationToken ct = default);
    Task<StatementNormalizedEntities?> GetAsync(string runId, CancellationToken ct = default);
}

public interface IStatementMatchResultRepository
{
    Task SaveAsync(string runId, StatementMatchResultArtifact result, CancellationToken ct = default);
    Task<StatementMatchResultArtifact?> GetAsync(string runId, CancellationToken ct = default);
}

public interface IStatementRunRecoveryRepository
{
    Task<StatementRunRecoveryCheckpoint?> GetAsync(string runId, CancellationToken ct = default);
    Task<bool> TryCreateAsync(StatementRunRecoveryCheckpoint checkpoint, CancellationToken ct = default);
    Task SaveAsync(StatementRunRecoveryCheckpoint checkpoint, CancellationToken ct = default);
}

public enum StatementRunRecoveryStage
{
    Imported = 1,
    Matched = 2,
    BreaksMaterialized = 3,
    CasesMaterialized = 4,
    Completed = 5
}

public enum StatementRunRecoveryStatus
{
    Running,
    Failed,
    Completed
}

public sealed record StatementRunStageArtifact(string Sha256, int Count);

/// <summary>
/// Versioned, monotonic recovery authority for the canonical statement-run workflow. Artifact
/// hashes bind every completed stage to the exact retained bytes that a retry may adopt.
/// </summary>
public sealed record StatementRunRecoveryCheckpoint(
    int SchemaVersion,
    string RunId,
    string ImportId,
    string RequestFingerprintSha256,
    string InputFingerprintSha256,
    StatementRunRecoveryStage Stage,
    StatementRunRecoveryStatus Status,
    StatementRunStageArtifact ImportArtifact,
    StatementRunStageArtifact? MatchArtifact,
    StatementRunStageArtifact? BreakArtifact,
    StatementRunStageArtifact? CaseArtifact,
    int MatchCount,
    string? FailedStage,
    string? ErrorType,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed class StatementRunRecoveryConflictException(string message) : InvalidOperationException(message)
{
}

public sealed record StatementRunManifest(
    string RunId,
    string ImportId,
    string Broker,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    DateTimeOffset CreatedAtUtc,
    string ImportedBy,
    StatementRawFileEvidenceReference RawFile,
    StatementProfileVersion MappingProfile,
    StatementProfileVersion ToleranceProfile,
    string DuplicateKey,
    int RawRowCount = 0,
    int NormalizedRowCount = 0)
{
    public StatementAccountingScope? AccountingScope { get; init; }

    public static StatementRunManifest FromRequest(
        string runId,
        string importId,
        StatementRunCreateRequest request,
        string mappingProfileVersion,
        string toleranceProfileVersion,
        DateTimeOffset createdAtUtc,
        int rawRowCount = 0,
        int normalizedRowCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(importId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingProfileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(toleranceProfileVersion);

        return new StatementRunManifest(
            runId.Trim(),
            importId.Trim(),
            request.Broker,
            request.SourceInstitution,
            request.FundAccountId,
            request.ExternalAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            createdAtUtc,
            request.ImportedBy,
            StatementRawFileEvidenceReference.ExternalReference(request.SourcePath, request.OriginalFileName, request.SourceFileHash),
            new StatementProfileVersion(request.MappingProfileId, mappingProfileVersion),
            new StatementProfileVersion(request.ToleranceProfileId, toleranceProfileVersion),
            request.DuplicateKey,
            rawRowCount,
            normalizedRowCount)
        {
            AccountingScope = request.AccountingScope
        };
    }
}

public sealed record StatementProfileVersion(string ProfileId, string Version);

public sealed record StatementRawFileEvidenceReference(
    StatementRawFileRetentionMode RetentionMode,
    string SourcePath,
    string OriginalFileName,
    string SourceFileHash,
    string? EvidenceUri = null,
    string PolicyNote = StatementRawFileEvidenceReference.DefaultPolicyNote)
{
    public const string DefaultPolicyNote = "Raw broker and custodian files are not copied by this repository. The manifest stores a source path or approved evidence-store reference plus the source file hash for reproducibility.";

    public static StatementRawFileEvidenceReference ExternalReference(
        string sourcePath,
        string originalFileName,
        string sourceFileHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileHash);

        return new StatementRawFileEvidenceReference(
            StatementRawFileRetentionMode.ExternalReferenceOnly,
            sourcePath.Trim(),
            originalFileName.Trim(),
            sourceFileHash.Trim());
    }

    public static StatementRawFileEvidenceReference EvidenceStoreReference(
        string sourcePath,
        string originalFileName,
        string sourceFileHash,
        string evidenceUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceUri);
        return ExternalReference(sourcePath, originalFileName, sourceFileHash) with
        {
            RetentionMode = StatementRawFileRetentionMode.EvidenceStoreReference,
            EvidenceUri = evidenceUri.Trim(),
            PolicyNote = "Raw file bytes are governed by the configured evidence store. This repository retains only the evidence URI and source file hash."
        };
    }
}

public enum StatementRawFileRetentionMode
{
    ExternalReferenceOnly,
    EvidenceStoreReference
}

public sealed record StatementNormalizedEntities(
    string RunId,
    string ImportId,
    IReadOnlyList<StatementPosition> Positions,
    IReadOnlyList<StatementCashBalance> CashBalances,
    IReadOnlyList<StatementTransaction> Transactions,
    IReadOnlyList<StatementSecurityReference> Securities,
    IReadOnlyList<StatementSourceRowReference> SourceRows)
{
    public static StatementNormalizedEntities FromImport(string runId, NormalizedStatementImportResult import)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(import);

        return new StatementNormalizedEntities(
            runId.Trim(),
            import.ImportId,
            import.Positions,
            import.CashBalances,
            import.Transactions,
            import.Securities,
            import.SourceRows);
    }
}

public sealed record StatementMatchResultArtifact(
    string RunId,
    string ImportId,
    IReadOnlyList<ReconciliationMatchLink> Matches,
    IReadOnlyList<StatementBreakRecord> Breaks,
    IReadOnlyList<StatementCaseLink> CaseLinks);

public sealed record StatementBreakRecord(
    string BreakId,
    string RunId,
    string ImportId,
    string SourceRowHash,
    StatementBreakClassificationType BreakType,
    ReconciliationBreakSeverity Severity,
    StatementBreakRecommendedAction RecommendedAction,
    decimal AbsoluteVariance,
    bool IsMaterial,
    bool IsUnresolved,
    string Status,
    string Rationale,
    DateTimeOffset CreatedAtUtc);

public sealed record StatementCaseLink(
    string CaseId,
    string RunId,
    string ImportId,
    string? BreakId,
    string SourceRowHash,
    string? EvidenceUri,
    DateTimeOffset LinkedAtUtc,
    string LinkReason);

public sealed class FileStatementRunRepository : IStatementRunRepository
{
    private readonly StatementRepositoryPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStatementRunRepository(string dataDirectory)
    {
        _paths = new StatementRepositoryPaths(dataDirectory);
    }

    public async Task SaveAsync(StatementRunManifest manifest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        StatementRepositoryGuard.ValidateRunId(manifest.RunId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StatementRepositoryJson.WriteAsync(_paths.ManifestPath(manifest.RunId), manifest, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<StatementRunManifest?> GetAsync(string runId, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        return StatementRepositoryJson.ReadOrDefaultAsync<StatementRunManifest>(_paths.ManifestPath(runId), ct);
    }

    public async Task<IReadOnlyList<StatementRunManifest>> ListAsync(string? fundAccountId = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(_paths.RootDirectory))
        {
            return [];
        }

        var manifests = new List<StatementRunManifest>();
        foreach (var path in Directory.EnumerateFiles(_paths.RootDirectory, StatementRepositoryPaths.ManifestFileName, SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var manifest = await StatementRepositoryJson.ReadOrDefaultAsync<StatementRunManifest>(path, ct).ConfigureAwait(false);
            if (manifest is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(fundAccountId)
                || string.Equals(manifest.FundAccountId, fundAccountId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                manifests.Add(manifest);
            }
        }

        return manifests
            .OrderByDescending(static manifest => manifest.CreatedAtUtc)
            .ThenBy(static manifest => manifest.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class FileStatementValidationIssueRepository : IStatementValidationIssueRepository
{
    private readonly StatementRepositoryPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStatementValidationIssueRepository(string dataDirectory)
    {
        _paths = new StatementRepositoryPaths(dataDirectory);
    }

    public async Task SaveAsync(string runId, IReadOnlyList<StatementValidationIssueDto> issues, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        ArgumentNullException.ThrowIfNull(issues);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StatementRepositoryJson.WriteAsync(_paths.ValidationIssuesPath(runId), issues, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StatementValidationIssueDto>> GetAsync(string runId, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        return await StatementRepositoryJson.ReadOrDefaultAsync<IReadOnlyList<StatementValidationIssueDto>>(_paths.ValidationIssuesPath(runId), ct)
            .ConfigureAwait(false) ?? [];
    }
}

public sealed class FileStatementNormalizedEntityRepository : IStatementNormalizedEntityRepository
{
    private readonly StatementRepositoryPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStatementNormalizedEntityRepository(string dataDirectory)
    {
        _paths = new StatementRepositoryPaths(dataDirectory);
    }

    public async Task SaveAsync(string runId, StatementNormalizedEntities entities, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        ArgumentNullException.ThrowIfNull(entities);
        if (!string.Equals(runId.Trim(), entities.RunId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Normalized entities must belong to the run being saved.", nameof(entities));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StatementRepositoryJson.WriteAsync(_paths.NormalizedEntitiesPath(runId), entities, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<StatementNormalizedEntities?> GetAsync(string runId, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        return StatementRepositoryJson.ReadOrDefaultAsync<StatementNormalizedEntities>(_paths.NormalizedEntitiesPath(runId), ct);
    }
}

public sealed class FileStatementMatchResultRepository : IStatementMatchResultRepository
{
    private readonly StatementRepositoryPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStatementMatchResultRepository(string dataDirectory)
    {
        _paths = new StatementRepositoryPaths(dataDirectory);
    }

    public async Task SaveAsync(string runId, StatementMatchResultArtifact result, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(runId.Trim(), result.RunId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Match results must belong to the run being saved.", nameof(result));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StatementRepositoryJson.WriteAsync(_paths.MatchResultsPath(runId), result with { Breaks = [], CaseLinks = [] }, ct).ConfigureAwait(false);
            await StatementRepositoryJson.WriteAsync(_paths.BreaksPath(runId), result.Breaks, ct).ConfigureAwait(false);
            await StatementRepositoryJson.WriteAsync(_paths.CaseLinksPath(runId), result.CaseLinks, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StatementMatchResultArtifact?> GetAsync(string runId, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        var result = await StatementRepositoryJson.ReadOrDefaultAsync<StatementMatchResultArtifact>(_paths.MatchResultsPath(runId), ct).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        var breaks = await StatementRepositoryJson.ReadOrDefaultAsync<IReadOnlyList<StatementBreakRecord>>(_paths.BreaksPath(runId), ct).ConfigureAwait(false) ?? [];
        var caseLinks = await StatementRepositoryJson.ReadOrDefaultAsync<IReadOnlyList<StatementCaseLink>>(_paths.CaseLinksPath(runId), ct).ConfigureAwait(false) ?? [];
        return result with { Breaks = breaks, CaseLinks = caseLinks };
    }
}

public sealed class InMemoryStatementRunRecoveryRepository : IStatementRunRecoveryRepository
{
    private readonly ConcurrentDictionary<string, StatementRunRecoveryCheckpoint> _checkpoints =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<StatementRunRecoveryCheckpoint?> GetAsync(string runId, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_checkpoints.TryGetValue(runId.Trim(), out var checkpoint) ? checkpoint : null);
    }

    public Task<bool> TryCreateAsync(StatementRunRecoveryCheckpoint checkpoint, CancellationToken ct = default)
    {
        ValidateCheckpoint(checkpoint);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_checkpoints.TryAdd(checkpoint.RunId, checkpoint));
    }

    public Task SaveAsync(StatementRunRecoveryCheckpoint checkpoint, CancellationToken ct = default)
    {
        ValidateCheckpoint(checkpoint);
        ct.ThrowIfCancellationRequested();
        _checkpoints.AddOrUpdate(
            checkpoint.RunId,
            _ => throw new StatementRunRecoveryConflictException(
                $"Statement run '{checkpoint.RunId}' has no retained recovery checkpoint to update."),
            (_, retained) => ValidateTransition(retained, checkpoint));
        return Task.CompletedTask;
    }

    internal static void ValidateCheckpoint(StatementRunRecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        StatementRepositoryGuard.ValidateRunId(checkpoint.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.ImportId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.RequestFingerprintSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.InputFingerprintSha256);
        if (checkpoint.SchemaVersion != StatementRunRecoveryCheckpoint.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported statement-run recovery schema version '{checkpoint.SchemaVersion}'.");
        }
    }

    internal static StatementRunRecoveryCheckpoint ValidateTransition(
        StatementRunRecoveryCheckpoint retained,
        StatementRunRecoveryCheckpoint next)
    {
        if (!string.Equals(retained.RunId, next.RunId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(retained.ImportId, next.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !StatementRunRecoveryHashing.FixedTimeEquals(
                retained.RequestFingerprintSha256,
                next.RequestFingerprintSha256) ||
            !StatementRunRecoveryHashing.FixedTimeEquals(
                retained.InputFingerprintSha256,
                next.InputFingerprintSha256))
        {
            throw new StatementRunRecoveryConflictException(
                $"Statement run '{next.RunId}' is already bound to a different import or fingerprint.");
        }

        if (next.Stage < retained.Stage)
        {
            throw new StatementRunRecoveryConflictException(
                $"Statement run '{next.RunId}' cannot move backward from '{retained.Stage}' to '{next.Stage}'.");
        }

        if (retained.Status == StatementRunRecoveryStatus.Completed && next != retained)
        {
            throw new StatementRunRecoveryConflictException(
                $"Statement run '{next.RunId}' is already completed and its checkpoint is immutable.");
        }

        return next;
    }
}

public sealed class FileStatementRunRecoveryRepository : IStatementRunRecoveryRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly StatementRepositoryPaths _paths;

    public FileStatementRunRecoveryRepository(string dataDirectory)
    {
        _paths = new StatementRepositoryPaths(dataDirectory);
    }

    public Task<StatementRunRecoveryCheckpoint?> GetAsync(string runId, CancellationToken ct = default)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        return StatementRunRecoveryJson.ReadOrDefaultAsync(
            _paths.RecoveryCheckpointPath(runId),
            ct);
    }

    public async Task<bool> TryCreateAsync(
        StatementRunRecoveryCheckpoint checkpoint,
        CancellationToken ct = default)
    {
        InMemoryStatementRunRecoveryRepository.ValidateCheckpoint(checkpoint);
        var path = _paths.RecoveryCheckpointPath(checkpoint.RunId);
        var gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                var json = StatementRunRecoveryJson.Serialize(checkpoint);
                await AtomicFileWriter.WriteAsync(temporaryPath, json, ct).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: false);
                await AtomicFileWriter
                    .SyncDirectoryAsync(Path.GetDirectoryName(path)!, CancellationToken.None)
                    .ConfigureAwait(false);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
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

    public async Task SaveAsync(StatementRunRecoveryCheckpoint checkpoint, CancellationToken ct = default)
    {
        InMemoryStatementRunRecoveryRepository.ValidateCheckpoint(checkpoint);
        var path = _paths.RecoveryCheckpointPath(checkpoint.RunId);
        var gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = await StatementRunRecoveryJson
                .ReadOrDefaultAsync(path, ct)
                .ConfigureAwait(false)
                ?? throw new StatementRunRecoveryConflictException(
                    $"Statement run '{checkpoint.RunId}' has no retained recovery checkpoint to update.");
            InMemoryStatementRunRecoveryRepository.ValidateTransition(retained, checkpoint);
            await StatementRunRecoveryJson.WriteAsync(path, checkpoint, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}

internal sealed class StatementRepositoryPaths
{
    public const string ManifestFileName = "manifest.json";

    public StatementRepositoryPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        RootDirectory = Path.Combine(dataDirectory, "reconciliation", "statement-runs");
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }

    public string ManifestPath(string runId) => Path.Combine(RunDirectory(runId), ManifestFileName);
    public string ValidationIssuesPath(string runId) => Path.Combine(RunDirectory(runId), "validation-issues.json");
    public string NormalizedEntitiesPath(string runId) => Path.Combine(RunDirectory(runId), "normalized-entities.json");
    public string MatchResultsPath(string runId) => Path.Combine(RunDirectory(runId), "match-results.json");
    public string BreaksPath(string runId) => Path.Combine(RunDirectory(runId), "breaks.json");
    public string CaseLinksPath(string runId) => Path.Combine(RunDirectory(runId), "case-links.json");
    public string RecoveryCheckpointPath(string runId) => Path.Combine(RunDirectory(runId), "workflow-checkpoint.json");

    private string RunDirectory(string runId) => Path.Combine(RootDirectory, SafeRunId(runId));

    private static string SafeRunId(string runId)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        return string.Concat(runId.Trim().Select(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
    }
}

internal static class StatementRunRecoveryHashing
{
    public static bool FixedTimeEquals(string left, string right)
    {
        if (!TryParseSha256(left, out var leftBytes) || !TryParseSha256(right, out var rightBytes))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool TryParseSha256(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != 64)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(value.Trim());
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal static class StatementRunRecoveryJson
{
    public static string Serialize(StatementRunRecoveryCheckpoint checkpoint)
        => JsonSerializer.Serialize(
            checkpoint,
            StatementRunRecoveryJsonContext.Default.StatementRunRecoveryCheckpoint);

    public static async Task WriteAsync(
        string path,
        StatementRunRecoveryCheckpoint checkpoint,
        CancellationToken ct)
        => await AtomicFileWriter.WriteAsync(path, Serialize(checkpoint), ct).ConfigureAwait(false);

    public static async Task<StatementRunRecoveryCheckpoint?> ReadOrDefaultAsync(
        string path,
        CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                stream,
                StatementRunRecoveryJsonContext.Default.StatementRunRecoveryCheckpoint,
                ct)
            .ConfigureAwait(false);
    }
}

internal static class StatementRepositoryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, Options);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
    }

    public static async Task<T?> ReadOrDefaultAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct).ConfigureAwait(false);
    }
}

internal static class StatementRepositoryGuard
{
    public static void ValidateRunId(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
    }
}
