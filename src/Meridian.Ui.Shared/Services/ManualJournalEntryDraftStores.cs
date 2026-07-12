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

public sealed class InMemoryManualJournalEntryDraftStore : IManualJournalEntryDraftStore
{
    private readonly Dictionary<string, ManualJournalEntryDraftDto> _drafts = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fundProfileIds = _drafts.Values
            .Select(static item => NormalizeFundProfileId(item.FundProfileId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(fundProfileIds);
    }

    public Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var drafts = _drafts.Values
            .Where(item => string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .Where(item => normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.JournalEntryId)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ManualJournalEntryDraftDto>>(drafts);
    }

    public Task<ManualJournalEntryDraftDto?> GetAsync(
        string fundProfileId,
        Guid journalEntryId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var draft = _drafts.Values.FirstOrDefault(item =>
            item.JournalEntryId == journalEntryId &&
            string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) &&
            (normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase)) &&
            (normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase)));
        return Task.FromResult(draft);
    }

    public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(draft);
        _drafts[Key(NormalizeFundProfileId(draft.FundProfileId), draft.JournalEntryId, draft.TenantId, draft.CompanyId)] = draft;
        return Task.CompletedTask;
    }

    private static string Key(string fundProfileId, Guid journalEntryId, string? tenantId, string? companyId)
        => $"{NormalizeFundProfileId(fundProfileId)}|{NormalizeOptional(tenantId) ?? "tenant:any"}|{NormalizeOptional(companyId) ?? "company:any"}|{journalEntryId:D}";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FileManualJournalEntryDraftStore : IManualJournalEntryDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileManualJournalEntryDraftStore(string snapshotPath)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Manual journal entry draft snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
    }

    public async Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Drafts
            .Select(static item => NormalizeFundProfileId(item.FundProfileId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
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
        return snapshot.Drafts
            .Where(item => string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .Where(item => normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.JournalEntryId)
            .ToArray();
    }

    public async Task<ManualJournalEntryDraftDto?> GetAsync(
        string fundProfileId,
        Guid journalEntryId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Drafts.FirstOrDefault(item =>
            item.JournalEntryId == journalEntryId &&
            string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) &&
            (normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase)) &&
            (normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var normalizedFundProfileId = NormalizeFundProfileId(draft.FundProfileId);
            var normalizedTenantId = NormalizeOptional(draft.TenantId);
            var normalizedCompanyId = NormalizeOptional(draft.CompanyId);
            var drafts = snapshot.Drafts
                .Where(item => item.JournalEntryId != draft.JournalEntryId ||
                               !string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                .Append(draft with
                {
                    FundProfileId = normalizedFundProfileId,
                    TenantId = normalizedTenantId,
                    CompanyId = normalizedCompanyId
                })
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.JournalEntryId)
                .ToArray();

            var next = new ManualJournalEntryDraftSnapshot(drafts);
            var json = JsonSerializer.Serialize(next, JsonOptions);
            await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ManualJournalEntryDraftSnapshot> ReadSnapshotAsync(CancellationToken ct)
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

    private async Task<ManualJournalEntryDraftSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new ManualJournalEntryDraftSnapshot([]);
        }

        await using var stream = File.OpenRead(_snapshotPath);
        return await JsonSerializer
            .DeserializeAsync<ManualJournalEntryDraftSnapshot>(stream, JsonOptions, ct)
            .ConfigureAwait(false) ?? new ManualJournalEntryDraftSnapshot([]);
    }

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ManualJournalEntryDraftSnapshot(IReadOnlyList<ManualJournalEntryDraftDto> Drafts);
}
