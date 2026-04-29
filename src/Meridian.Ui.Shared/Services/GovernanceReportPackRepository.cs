using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IGovernanceReportPackRepository
{
    Task<FundReportPackSnapshotDto> SaveAsync(
        FundReportPackSnapshotDto snapshot,
        IReadOnlyList<GovernanceReportPackArtifactContent> artifacts,
        CancellationToken ct = default);

    Task<IReadOnlyList<FundReportPackHistoryItemDto>> GetHistoryAsync(
        string fundProfileId,
        int limit,
        CancellationToken ct = default);

    Task<FundReportPackSnapshotDto?> GetAsync(Guid reportId, CancellationToken ct = default);
}

public sealed record GovernanceReportPackArtifactContent(
    string ArtifactKind,
    GovernanceReportArtifactFormatDto Format,
    string FileName,
    byte[] Content);

public sealed class FileGovernanceReportPackRepository : IGovernanceReportPackRepository
{
    private readonly string _rootDirectory;
    private readonly ILogger<FileGovernanceReportPackRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileGovernanceReportPackRepository(
        string dataDirectory,
        ILogger<FileGovernanceReportPackRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootDirectory = Path.Combine(dataDirectory, "governance-report-packs");
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<FundReportPackSnapshotDto> SaveAsync(
        FundReportPackSnapshotDto snapshot,
        IReadOnlyList<GovernanceReportPackArtifactContent> artifacts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(artifacts);
        EnsureWritableSnapshot(snapshot);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var fundKey = BuildFundKey(snapshot.FundProfileId);
            var reportKey = snapshot.ReportId.ToString("N");
            var reportDirectory = Path.Combine(_rootDirectory, fundKey, reportKey);
            Directory.CreateDirectory(reportDirectory);

            var artifactDtos = new List<FundReportPackArtifactDto>(artifacts.Count + 1);
            foreach (var artifact in artifacts.OrderBy(static item => item.FileName, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var safeFileName = SanitizeFileName(artifact.FileName);
                await AtomicFileWriter
                    .WriteAsync(Path.Combine(reportDirectory, safeFileName), artifact.Content, ct)
                    .ConfigureAwait(false);
                artifactDtos.Add(BuildArtifactDto(fundKey, reportKey, artifact.ArtifactKind, artifact.Format, safeFileName, artifact.Content));
            }

            var provenanceBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot.Provenance, _jsonOptions);
            await AtomicFileWriter
                .WriteAsync(Path.Combine(reportDirectory, "provenance.json"), provenanceBytes, ct)
                .ConfigureAwait(false);
            artifactDtos.Add(BuildArtifactDto(
                fundKey,
                reportKey,
                "provenance",
                GovernanceReportArtifactFormatDto.Json,
                "provenance.json",
                provenanceBytes));

            var finalSnapshot = snapshot with
            {
                Artifacts = artifactDtos
                    .OrderBy(static artifact => artifact.RelativePath, StringComparer.Ordinal)
                    .ToArray()
            };
            EnsureWritableSnapshot(finalSnapshot);

            await AtomicFileWriter
                .WriteAsync(
                    Path.Combine(reportDirectory, "manifest.json"),
                    JsonSerializer.Serialize(finalSnapshot, _jsonOptions),
                    ct)
                .ConfigureAwait(false);

            return finalSnapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<FundReportPackHistoryItemDto>> GetHistoryAsync(
        string fundProfileId,
        int limit,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        limit = Math.Clamp(limit, 1, 200);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fundDirectory = Path.Combine(_rootDirectory, BuildFundKey(fundProfileId));
            if (!Directory.Exists(fundDirectory))
            {
                return [];
            }

            var history = new List<FundReportPackHistoryItemDto>();
            foreach (var manifestPath in Directory.EnumerateFiles(fundDirectory, "manifest.json", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var snapshot = await TryReadSnapshotAsync(manifestPath, ct).ConfigureAwait(false);
                if (snapshot is null)
                {
                    continue;
                }

                history.Add(new FundReportPackHistoryItemDto(
                    ReportId: snapshot.ReportId,
                    FundProfileId: snapshot.FundProfileId,
                    DisplayName: snapshot.DisplayName,
                    ReportKind: snapshot.ReportKind,
                    Currency: snapshot.Currency,
                    AsOf: snapshot.AsOf,
                    GeneratedAt: snapshot.GeneratedAt,
                    TotalNetAssets: snapshot.TotalNetAssets,
                    AuditActor: snapshot.AuditActor,
                    ArtifactCount: snapshot.Artifacts.Count,
                    WarningCount: snapshot.Warnings.Count,
                    RelativeManifestPath: ToRelativePath(manifestPath),
                    SchemaVersion: snapshot.SchemaVersion));
            }

            return history
                .OrderByDescending(static item => item.GeneratedAt)
                .ThenByDescending(static item => item.ReportId)
                .Take(limit)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FundReportPackSnapshotDto?> GetAsync(Guid reportId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_rootDirectory))
            {
                return null;
            }

            var reportKey = reportId.ToString("N");
            foreach (var manifestPath in Directory.EnumerateFiles(_rootDirectory, "manifest.json", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(manifestPath)), reportKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return await TryReadSnapshotAsync(manifestPath, ct).ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FundReportPackSnapshotDto?> TryReadSnapshotAsync(string manifestPath, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            return await JsonSerializer
                .DeserializeAsync<FundReportPackSnapshotDto>(stream, _jsonOptions, ct)
                .ConfigureAwait(false) is { } snapshot && IsReadableSnapshot(snapshot)
                    ? snapshot
                    : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Skipping unreadable governance report-pack manifest {Path}", manifestPath);
            return null;
        }
    }

    private FundReportPackArtifactDto BuildArtifactDto(
        string fundKey,
        string reportKey,
        string artifactKind,
        GovernanceReportArtifactFormatDto format,
        string fileName,
        byte[] content)
        => new(
            ArtifactKind: artifactKind,
            Format: format,
            RelativePath: $"{fundKey}/{reportKey}/{fileName}",
            SizeBytes: content.LongLength,
            ChecksumSha256: Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            SchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion);

    private static void EnsureWritableSnapshot(FundReportPackSnapshotDto snapshot)
    {
        if (snapshot.Provenance is null)
        {
            throw new ArgumentException("Governance report-pack provenance is required.", nameof(snapshot));
        }

        if (snapshot.Artifacts is null)
        {
            throw new ArgumentException("Governance report-pack artifact metadata is required.", nameof(snapshot));
        }

        if (!string.Equals(snapshot.ContractName, GovernanceReportPackContract.ContractName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Governance report-pack contract must be '{GovernanceReportPackContract.ContractName}'.",
                nameof(snapshot));
        }

        if (snapshot.SchemaVersion != GovernanceReportPackContract.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Governance report-pack schema version must be {GovernanceReportPackContract.CurrentSchemaVersion}.",
                nameof(snapshot));
        }

        if (snapshot.Provenance.SchemaVersion != GovernanceReportPackContract.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Governance report-pack provenance schema version must be {GovernanceReportPackContract.CurrentSchemaVersion}.",
                nameof(snapshot));
        }

        if (snapshot.Artifacts.Any(static artifact => artifact.SchemaVersion != GovernanceReportPackContract.CurrentSchemaVersion))
        {
            throw new ArgumentException(
                $"Governance report-pack artifact schema versions must be {GovernanceReportPackContract.CurrentSchemaVersion}.",
                nameof(snapshot));
        }
    }

    private static bool IsReadableSnapshot(FundReportPackSnapshotDto snapshot) =>
        string.Equals(snapshot.ContractName, GovernanceReportPackContract.ContractName, StringComparison.Ordinal)
        && GovernanceReportPackContract.IsReadableSchemaVersion(snapshot.SchemaVersion)
        && snapshot.Provenance is not null
        && GovernanceReportPackContract.IsReadableSchemaVersion(snapshot.Provenance.SchemaVersion)
        && snapshot.Artifacts is not null
        && snapshot.Artifacts.All(static artifact => GovernanceReportPackContract.IsReadableSchemaVersion(artifact.SchemaVersion));

    private string ToRelativePath(string path) =>
        Path.GetRelativePath(_rootDirectory, path).Replace('\\', '/');

    private static string BuildFundKey(string fundProfileId)
    {
        var trimmed = fundProfileId.Trim();
        var buffer = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed.ToLowerInvariant())
        {
            buffer.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-');
        }

        var key = buffer.ToString().Trim('-');
        return key.Length == 0 ? "fund" : key.Length <= 96 ? key : key[..96];
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safe) ? "artifact.bin" : safe;
    }
}
