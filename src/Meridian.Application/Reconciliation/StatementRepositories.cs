using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.Application.Reconciliation;

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
            normalizedRowCount);
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

    private string RunDirectory(string runId) => Path.Combine(RootDirectory, SafeRunId(runId));

    private static string SafeRunId(string runId)
    {
        StatementRepositoryGuard.ValidateRunId(runId);
        return string.Concat(runId.Trim().Select(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
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

