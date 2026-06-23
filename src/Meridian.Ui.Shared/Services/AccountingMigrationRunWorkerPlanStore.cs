using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IAccountingMigrationRunWorkerPlanStore
{
    Task<IReadOnlyList<AccountingMigrationRunWorkerPlanDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        AccountingMigrationRunKindDto? kind = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task<AccountingMigrationRunWorkerPlanDto?> GetAsync(
        string planId,
        CancellationToken ct = default);
}

public interface IAccountingMigrationRunWorkerPlanWriter : IAccountingMigrationRunWorkerPlanStore
{
    Task<AccountingMigrationRunWorkerPlanDto> UpsertAsync(
        AccountingMigrationRunWorkerPlanDto plan,
        CancellationToken ct = default);
}

public sealed class InMemoryAccountingMigrationRunWorkerPlanStore : IAccountingMigrationRunWorkerPlanWriter
{
    private readonly Dictionary<string, AccountingMigrationRunWorkerPlanDto> _plans = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<AccountingMigrationRunWorkerPlanDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        AccountingMigrationRunKindDto? kind = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeOptional(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var plans = _plans.Values
            .Where(plan => MatchesScope(plan, normalizedFundProfileId, ledgerBookId, kind, normalizedTenantId, normalizedCompanyId))
            .OrderBy(static plan => plan.PlanId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<AccountingMigrationRunWorkerPlanDto>>(plans);
    }

    public Task<AccountingMigrationRunWorkerPlanDto?> GetAsync(string planId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(planId))
        {
            return Task.FromResult<AccountingMigrationRunWorkerPlanDto?>(null);
        }

        _plans.TryGetValue(planId.Trim(), out var plan);
        return Task.FromResult(plan);
    }

    public Task<AccountingMigrationRunWorkerPlanDto> UpsertAsync(
        AccountingMigrationRunWorkerPlanDto plan,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = NormalizePlan(plan);
        _plans[normalized.PlanId] = normalized;
        return Task.FromResult(normalized);
    }

    internal static bool MatchesScope(
        AccountingMigrationRunWorkerPlanDto plan,
        string? fundProfileId,
        Guid? ledgerBookId,
        AccountingMigrationRunKindDto? kind,
        string? tenantId,
        string? companyId)
        => (fundProfileId is null || string.Equals(plan.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
           (!ledgerBookId.HasValue || plan.LedgerBookId == ledgerBookId.Value) &&
           (!kind.HasValue || plan.Kind == kind.Value) &&
           (tenantId is null || string.Equals(NormalizeOptional(plan.TenantId), tenantId, StringComparison.OrdinalIgnoreCase)) &&
           (companyId is null || string.Equals(NormalizeOptional(plan.CompanyId), companyId, StringComparison.OrdinalIgnoreCase));

    internal static AccountingMigrationRunWorkerPlanDto NormalizePlan(AccountingMigrationRunWorkerPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SourceRecordCount < 0 || plan.MigratedRecordCount < 0)
        {
            throw new ArgumentException("Accounting migration worker plan row counts cannot be negative.", nameof(plan));
        }

        return plan with
        {
            PlanId = RequireText(plan.PlanId, nameof(plan.PlanId)),
            FundProfileId = RequireText(plan.FundProfileId, nameof(plan.FundProfileId)),
            TenantId = NormalizeOptional(plan.TenantId),
            CompanyId = NormalizeOptional(plan.CompanyId),
            Summary = NormalizeOptional(plan.Summary),
            Dimensions = plan.Dimensions is null
                ? null
                : plan.Dimensions with
                {
                    FundId = NormalizeOptional(plan.Dimensions.FundId) ?? plan.FundProfileId.Trim(),
                    BookId = NormalizeOptional(plan.Dimensions.BookId) ?? plan.LedgerBookId.ToString("D")
                },
            EvidenceReferences = plan.EvidenceReferences
                .Where(static reference => !string.IsNullOrWhiteSpace(reference))
                .Select(static reference => reference.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    internal static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.", nameof(value))
            : value.Trim();
}

public sealed class FileAccountingMigrationRunWorkerPlanStore : IAccountingMigrationRunWorkerPlanWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly ILogger<FileAccountingMigrationRunWorkerPlanStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAccountingMigrationRunWorkerPlanStore(
        string snapshotPath,
        ILogger<FileAccountingMigrationRunWorkerPlanStore> logger)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Accounting migration worker plan snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<AccountingMigrationRunWorkerPlanDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        AccountingMigrationRunKindDto? kind = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = InMemoryAccountingMigrationRunWorkerPlanStore.NormalizeOptional(fundProfileId);
        var normalizedTenantId = InMemoryAccountingMigrationRunWorkerPlanStore.NormalizeOptional(tenantId);
        var normalizedCompanyId = InMemoryAccountingMigrationRunWorkerPlanStore.NormalizeOptional(companyId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Plans
            .Where(plan => InMemoryAccountingMigrationRunWorkerPlanStore.MatchesScope(
                plan,
                normalizedFundProfileId,
                ledgerBookId,
                kind,
                normalizedTenantId,
                normalizedCompanyId))
            .OrderBy(static plan => plan.PlanId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AccountingMigrationRunWorkerPlanDto?> GetAsync(
        string planId,
        CancellationToken ct = default)
    {
        var normalizedPlanId = InMemoryAccountingMigrationRunWorkerPlanStore.NormalizeOptional(planId);
        if (normalizedPlanId is null)
        {
            return null;
        }

        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Plans.FirstOrDefault(plan =>
            string.Equals(plan.PlanId, normalizedPlanId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AccountingMigrationRunWorkerPlanDto> UpsertAsync(
        AccountingMigrationRunWorkerPlanDto plan,
        CancellationToken ct = default)
    {
        var normalized = InMemoryAccountingMigrationRunWorkerPlanStore.NormalizePlan(plan);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var plans = snapshot.Plans
                .Where(item => !string.Equals(item.PlanId, normalized.PlanId, StringComparison.OrdinalIgnoreCase))
                .Append(normalized)
                .OrderBy(static item => item.PlanId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var json = JsonSerializer.Serialize(new AccountingMigrationRunWorkerPlanSnapshot(plans), JsonOptions);
            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountingMigrationRunWorkerPlanSnapshot> ReadSnapshotAsync(CancellationToken ct)
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

    private async Task<AccountingMigrationRunWorkerPlanSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new AccountingMigrationRunWorkerPlanSnapshot([]);
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            return await JsonSerializer
                .DeserializeAsync<AccountingMigrationRunWorkerPlanSnapshot>(stream, JsonOptions, ct)
                .ConfigureAwait(false) ?? new AccountingMigrationRunWorkerPlanSnapshot([]);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to read accounting migration worker plan snapshot {SnapshotPath}", _snapshotPath);
            return new AccountingMigrationRunWorkerPlanSnapshot([]);
        }
    }
}

internal sealed record AccountingMigrationRunWorkerPlanSnapshot(
    IReadOnlyList<AccountingMigrationRunWorkerPlanDto>? Plans = null)
{
    public IReadOnlyList<AccountingMigrationRunWorkerPlanDto> Plans { get; init; } = Plans ?? [];
}
