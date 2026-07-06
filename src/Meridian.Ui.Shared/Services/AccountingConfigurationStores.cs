using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed class InMemoryAccountingConfigurationStore : IAccountingConfigurationStore
{
    private readonly Dictionary<string, AccountingConfigurationWorkspaceDto> _workspaces = new(StringComparer.OrdinalIgnoreCase);

    public Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        _workspaces.TryGetValue(Key(fundProfileId, ledgerBookId, tenantId, companyId), out var workspace);
        return Task.FromResult(workspace);
    }

    public Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(workspace);
        _workspaces[Key(workspace.FundProfileId, workspace.LedgerBookId, workspace.TenantId, workspace.CompanyId)] = workspace;
        return Task.CompletedTask;
    }

    private static string Key(string fundProfileId, Guid? ledgerBookId, string? tenantId, string? companyId)
        => $"{NormalizeOptional(tenantId) ?? "all"}|{NormalizeOptional(companyId) ?? "all"}|{NormalizeFundProfileId(fundProfileId)}|{ledgerBookId?.ToString("D") ?? "fund"}";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class InMemoryAccountingActionAuditStore : IAccountingActionAuditStore
{
    private readonly List<AccountingActionAuditEventDto> _events = [];

    public Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _events.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var events = _events
            .Where(item => string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(item.FundProfileId, fundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .Where(item => normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedCompanyId is null || string.Equals(item.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenBy(item => item.AuditEventId)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AccountingActionAuditEventDto>>(events);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FileAccountingConfigurationStore : IAccountingConfigurationStore, IAccountingActionAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAccountingConfigurationStore(string snapshotPath)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Accounting configuration snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
    }

    public async Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Workspaces.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) &&
            item.LedgerBookId == ledgerBookId &&
            string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase)) is { } workspace
            ? workspace with { FundProfileId = normalizedFundProfileId, AuditTrail = [] }
            : null;
    }

    public async Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var normalizedFundProfileId = NormalizeFundProfileId(workspace.FundProfileId);
            var normalizedTenantId = NormalizeOptional(workspace.TenantId);
            var normalizedCompanyId = NormalizeOptional(workspace.CompanyId);
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var workspaces = snapshot.Workspaces
                .Where(item =>
                    !string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) ||
                    item.LedgerBookId != workspace.LedgerBookId ||
                    !string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                .Append(workspace with
                {
                    FundProfileId = normalizedFundProfileId,
                    TenantId = normalizedTenantId,
                    CompanyId = normalizedCompanyId,
                    AuditTrail = []
                })
                .OrderBy(item => item.FundProfileId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.LedgerBookId?.ToString("D") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TenantId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.CompanyId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await WriteSnapshotAsync(snapshot with { Workspaces = workspaces }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var events = snapshot.AuditEvents
                .Append(auditEvent with { FundProfileId = NormalizeOptional(auditEvent.FundProfileId) })
                .OrderByDescending(item => item.RecordedAtUtc)
                .ThenBy(item => item.AuditEventId)
                .ToArray();

            await WriteSnapshotAsync(snapshot with { AuditEvents = events }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeOptional(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.AuditEvents
            .Where(item => string.IsNullOrWhiteSpace(normalizedFundProfileId) ||
                           string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .Where(item => normalizedTenantId is null ||
                           string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedCompanyId is null || string.Equals(item.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenBy(item => item.AuditEventId)
            .ToArray();
    }

    private async Task<AccountingConfigurationSnapshot> ReadSnapshotAsync(CancellationToken ct)
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

    private async Task<AccountingConfigurationSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return AccountingConfigurationSnapshot.Empty;
        }

        await using var stream = File.OpenRead(_snapshotPath);
        return await JsonSerializer
            .DeserializeAsync<AccountingConfigurationSnapshot>(stream, JsonOptions, ct)
            .ConfigureAwait(false) ?? AccountingConfigurationSnapshot.Empty;
    }

    private Task WriteSnapshotAsync(AccountingConfigurationSnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        return AtomicFileWriter.WriteAsync(_snapshotPath, json, ct);
    }

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AccountingConfigurationSnapshot(
        IReadOnlyList<AccountingConfigurationWorkspaceDto> Workspaces,
        IReadOnlyList<AccountingActionAuditEventDto> AuditEvents)
    {
        public static AccountingConfigurationSnapshot Empty { get; } = new([], []);
    }
}
