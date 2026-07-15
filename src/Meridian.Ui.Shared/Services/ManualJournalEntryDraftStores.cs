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
using Meridian.Storage.Ledger;
using Meridian.Storage.Store;

namespace Meridian.Ui.Shared.Services;

public sealed class InMemoryManualJournalEntryDraftStore : IManualJournalEntryDraftStore
{
    private Dictionary<string, ManualJournalEntryDraftDto> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string[] fundProfileIds;
        lock (_gate)
        {
            fundProfileIds = _drafts.Values
                .Select(static item => NormalizeFundProfileId(item.FundProfileId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

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
        ManualJournalEntryDraftDto[] drafts;
        lock (_gate)
        {
            drafts = _drafts.Values
                .Where(item => string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
                .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
                .Where(item => normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
                .Where(item => normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.JournalEntryId)
                .ToArray();
        }

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
        ManualJournalEntryDraftDto? draft;
        lock (_gate)
        {
            draft = _drafts.Values.FirstOrDefault(item =>
                item.JournalEntryId == journalEntryId &&
                string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) &&
                (normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase)) &&
                (normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase)));
        }
        return Task.FromResult(draft);
    }

    public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
        => SaveBatchAsync([draft], ct);

    public Task SaveBatchAsync(
        IReadOnlyList<ManualJournalEntryDraftDto> drafts,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(drafts);
        var retained = drafts
            .Select(draft => draft ?? throw new ArgumentException("Manual journal entry draft batches cannot contain null drafts.", nameof(drafts)))
            .ToArray();
        lock (_gate)
        {
            var next = new Dictionary<string, ManualJournalEntryDraftDto>(
                _drafts,
                StringComparer.OrdinalIgnoreCase);
            foreach (var draft in retained)
            {
                next[Key(NormalizeFundProfileId(draft.FundProfileId), draft.JournalEntryId, draft.TenantId, draft.CompanyId)] = draft;
            }

            _drafts = next;
        }

        return Task.CompletedTask;
    }

    private static string Key(string fundProfileId, Guid journalEntryId, string? tenantId, string? companyId)
        => $"{NormalizeFundProfileId(fundProfileId)}|{NormalizeOptional(tenantId) ?? "tenant:any"}|{NormalizeOptional(companyId) ?? "company:any"}|{journalEntryId:D}";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FileManualJournalEntryDraftStore :
    JsonFileSnapshotStore<FileManualJournalEntryDraftStore.ManualJournalEntryDraftSnapshot>,
    IManualJournalEntryDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileManualJournalEntryDraftStore(string snapshotPath)
        : base(
            string.IsNullOrWhiteSpace(snapshotPath)
                ? throw new ArgumentException("Manual journal entry draft snapshot path is required.", nameof(snapshotPath))
                : snapshotPath,
            JsonOptions)
    {
    }

    protected override ManualJournalEntryDraftSnapshot CreateEmptySnapshot() => new([]);

    public async Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        return await ReadSnapshotAsync(
            snapshot => snapshot.Drafts
                .Select(static item => NormalizeFundProfileId(item.FundProfileId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ct).ConfigureAwait(false);
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
        return await ReadSnapshotAsync(
            snapshot => snapshot.Drafts
                .Where(item => string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
                .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
                .Where(item => normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
                .Where(item => normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.JournalEntryId)
                .ToArray(),
            ct).ConfigureAwait(false);
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
        return await ReadSnapshotAsync(
            snapshot => snapshot.Drafts.FirstOrDefault(item =>
                item.JournalEntryId == journalEntryId &&
                string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) &&
                (normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase)) &&
                (normalizedCompanyId is null || string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))),
            ct).ConfigureAwait(false);
    }

    public async Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
        => await SaveBatchAsync([draft], ct).ConfigureAwait(false);

    public async Task SaveBatchAsync(
        IReadOnlyList<ManualJournalEntryDraftDto> drafts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        var normalizedDrafts = drafts
            .Select(draft => draft ?? throw new ArgumentException("Manual journal entry draft batches cannot contain null drafts.", nameof(drafts)))
            .Select(draft => draft with
            {
                FundProfileId = NormalizeFundProfileId(draft.FundProfileId),
                TenantId = NormalizeOptional(draft.TenantId),
                CompanyId = NormalizeOptional(draft.CompanyId)
            })
            .ToArray();
        if (normalizedDrafts.Length == 0)
        {
            return;
        }

        var replacementKeys = normalizedDrafts
            .Select(static draft => Key(draft))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await UpdateSnapshotAsync(
            snapshot =>
            {
                var retainedDrafts = snapshot.Drafts
                    .Where(item => !replacementKeys.Contains(Key(item)))
                    .Concat(normalizedDrafts)
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .ThenBy(item => item.JournalEntryId)
                    .ToArray();

                return new ManualJournalEntryDraftSnapshot(retainedDrafts);
            },
            ct).ConfigureAwait(false);
    }

    private static string Key(ManualJournalEntryDraftDto draft)
        => $"{NormalizeFundProfileId(draft.FundProfileId)}|{NormalizeOptional(draft.TenantId) ?? "tenant:any"}|{NormalizeOptional(draft.CompanyId) ?? "company:any"}|{draft.JournalEntryId:D}";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record ManualJournalEntryDraftSnapshot(IReadOnlyList<ManualJournalEntryDraftDto> Drafts);
}
