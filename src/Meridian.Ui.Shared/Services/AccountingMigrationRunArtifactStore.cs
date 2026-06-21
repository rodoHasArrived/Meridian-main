using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IAccountingMigrationRunArtifactStore
{
    Task<IReadOnlyList<AccountingMigrationRunArtifactDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task<AccountingMigrationRunArtifactDto> UpsertAsync(
        AccountingMigrationRunArtifactUpsertRequestDto request,
        CancellationToken ct = default);
}

public sealed class InMemoryAccountingMigrationRunArtifactStore : IAccountingMigrationRunArtifactStore
{
    private readonly Dictionary<string, AccountingMigrationRunArtifactDto> _artifacts = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<AccountingMigrationRunArtifactDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var artifacts = _artifacts.Values
            .Where(item => MatchesScope(item, normalizedFundProfileId, ledgerBookId, normalizedTenantId, normalizedCompanyId))
            .OrderByDescending(static item => item.StartedAtUtc)
            .ThenBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<AccountingMigrationRunArtifactDto>>(artifacts);
    }

    public Task<AccountingMigrationRunArtifactDto> UpsertAsync(
        AccountingMigrationRunArtifactUpsertRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var artifact = NormalizeArtifact(request);
        _artifacts[BuildKey(artifact)] = artifact;
        return Task.FromResult(artifact);
    }

    private static string BuildKey(AccountingMigrationRunArtifactDto artifact)
        => $"{NormalizeOptional(artifact.TenantId) ?? "all"}|{NormalizeOptional(artifact.CompanyId) ?? "all"}|{NormalizeFundProfileId(artifact.FundProfileId)}|{artifact.LedgerBookId?.ToString("D") ?? "all"}|{artifact.RunId}";

    private static bool MatchesScope(
        AccountingMigrationRunArtifactDto artifact,
        string fundProfileId,
        Guid? ledgerBookId,
        string? tenantId,
        string? companyId)
        => string.Equals(NormalizeFundProfileId(artifact.FundProfileId), fundProfileId, StringComparison.OrdinalIgnoreCase) &&
           (!ledgerBookId.HasValue || artifact.LedgerBookId == ledgerBookId) &&
           (tenantId is null || string.Equals(NormalizeOptional(artifact.TenantId), tenantId, StringComparison.OrdinalIgnoreCase)) &&
           (companyId is null || string.Equals(NormalizeOptional(artifact.CompanyId), companyId, StringComparison.OrdinalIgnoreCase));

    private static AccountingMigrationRunArtifactDto NormalizeArtifact(AccountingMigrationRunArtifactUpsertRequestDto request)
        => FileAccountingMigrationRunArtifactStore.NormalizeArtifact(request);

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FileAccountingMigrationRunArtifactStore : IAccountingMigrationRunArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly ILogger<FileAccountingMigrationRunArtifactStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAccountingMigrationRunArtifactStore(
        string snapshotPath,
        ILogger<FileAccountingMigrationRunArtifactStore> logger)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Accounting migration run artifact snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<AccountingMigrationRunArtifactDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Artifacts
            .Where(item => MatchesScope(item, normalizedFundProfileId, ledgerBookId, normalizedTenantId, normalizedCompanyId))
            .OrderByDescending(static item => item.StartedAtUtc)
            .ThenBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AccountingMigrationRunArtifactDto> UpsertAsync(
        AccountingMigrationRunArtifactUpsertRequestDto request,
        CancellationToken ct = default)
    {
        var artifact = NormalizeArtifact(request);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var artifacts = snapshot.Artifacts
                .Where(item => !string.Equals(BuildKey(item), BuildKey(artifact), StringComparison.OrdinalIgnoreCase))
                .Append(artifact)
                .OrderByDescending(static item => item.StartedAtUtc)
                .ThenBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var json = JsonSerializer.Serialize(new AccountingMigrationRunArtifactSnapshot(artifacts), JsonOptions);
            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
            return artifact;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static AccountingMigrationRunArtifactDto NormalizeArtifact(AccountingMigrationRunArtifactUpsertRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Artifact);
        if (string.IsNullOrWhiteSpace(request.Artifact.RunId))
        {
            throw new ArgumentException("Migration run artifact run id is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Actor) && string.IsNullOrWhiteSpace(request.Artifact.Actor))
        {
            throw new ArgumentException("Migration run artifact actor is required.", nameof(request));
        }

        var evidence = request.Artifact.EvidenceReferences
            .Concat(request.EvidenceLinks)
            .Append(string.IsNullOrWhiteSpace(request.CorrelationId) ? null : $"correlation:{request.CorrelationId!.Trim()}")
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return request.Artifact with
        {
            RunId = request.Artifact.RunId.Trim(),
            Actor = string.IsNullOrWhiteSpace(request.Artifact.Actor) ? request.Actor.Trim() : request.Artifact.Actor.Trim(),
            FundProfileId = NormalizeFundProfileId(request.Artifact.FundProfileId),
            TenantId = NormalizeOptional(request.Artifact.TenantId),
            CompanyId = NormalizeOptional(request.Artifact.CompanyId),
            Summary = string.IsNullOrWhiteSpace(request.Artifact.Summary) ? null : request.Artifact.Summary.Trim(),
            EvidenceReferences = evidence
        };
    }

    private async Task<AccountingMigrationRunArtifactSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountingMigrationRunArtifactSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new AccountingMigrationRunArtifactSnapshot([]);
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            return await JsonSerializer
                .DeserializeAsync<AccountingMigrationRunArtifactSnapshot>(stream, JsonOptions, ct)
                .ConfigureAwait(false) ?? new AccountingMigrationRunArtifactSnapshot([]);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to read accounting migration run artifact snapshot {SnapshotPath}", _snapshotPath);
            return new AccountingMigrationRunArtifactSnapshot([]);
        }
    }

    private static string BuildKey(AccountingMigrationRunArtifactDto artifact)
        => $"{NormalizeOptional(artifact.TenantId) ?? "all"}|{NormalizeOptional(artifact.CompanyId) ?? "all"}|{NormalizeFundProfileId(artifact.FundProfileId)}|{artifact.LedgerBookId?.ToString("D") ?? "all"}|{artifact.RunId}";

    private static bool MatchesScope(
        AccountingMigrationRunArtifactDto artifact,
        string fundProfileId,
        Guid? ledgerBookId,
        string? tenantId,
        string? companyId)
        => string.Equals(NormalizeFundProfileId(artifact.FundProfileId), fundProfileId, StringComparison.OrdinalIgnoreCase) &&
           (!ledgerBookId.HasValue || artifact.LedgerBookId == ledgerBookId) &&
           (tenantId is null || string.Equals(NormalizeOptional(artifact.TenantId), tenantId, StringComparison.OrdinalIgnoreCase)) &&
           (companyId is null || string.Equals(NormalizeOptional(artifact.CompanyId), companyId, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AccountingMigrationRunArtifactSnapshot(IReadOnlyList<AccountingMigrationRunArtifactDto> Artifacts);
}
