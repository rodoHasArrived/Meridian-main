using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingSystem;

/// <summary>The exact retained inputs of a read-only GL comparison, including full-precision ledger records.</summary>
public sealed record AccountingOnboardingCapture(
    AccountingSystemImportDetailDto Import,
    ExternalGlMappingProfileDto Mapping,
    string MappingVersion,
    LedgerBookRecord Book,
    IReadOnlyList<LedgerAccountingPeriod> Periods,
    IReadOnlyList<LedgerJournalEntryRecord> Journals,
    AccountingSystemReconciliationSummaryDto Reconciliation)
{
    public IReadOnlyList<string> MappingEvidenceReferences { get; init; } = [];
}

public sealed partial class AccountingSystemIntegrationService
{
    /// <summary>Lists only already-retained exact import and certified mapping choices.</summary>
    public IReadOnlyList<OnboardingSourceSelectionDto> GetRetainedOnboardingSelections(
        string fundProfileId, Guid ledgerBookId, string tenantId, string companyId)
        => _latestImports.Values.Where(import => import.Summary.LedgerBookId == ledgerBookId
                && string.Equals(import.Summary.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(import.Summary.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(import.Summary.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(import => _mappingProfiles.Values.Where(mapping => MappingMatchesImport(import, mapping)
                    && mapping.Profile.CertificationState == AccountingCertificationStateDto.Certified)
                .Select(mapping => new OnboardingSourceSelectionDto(import.Summary.ProviderId,
                    import.Summary.ImportId, import.Summary.PeriodEnd, mapping.Profile.ProfileId,
                    GetOnboardingMappingVersion(mapping.Profile))))
            .OrderBy(choice => choice.AsOfDate).ThenBy(choice => choice.ProviderId, StringComparer.Ordinal)
            .ThenBy(choice => choice.MappingProfileId, StringComparer.Ordinal).ToArray();

    /// <summary>Content version of a mapping, independent of dictionary enumeration order.</summary>
    public static string GetOnboardingMappingVersion(ExternalGlMappingProfileDto profile)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(profile with
        {
            AccountMappings = profile.AccountMappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            DimensionMappings = profile.DimensionMappings.OrderBy(mapping => mapping.ProfileId, StringComparer.Ordinal).ToArray()
        }, JsonOptions));

    /// <summary>
    /// Captures only already-retained evidence. It never invokes a provider or falls back to an import.
    /// The existing reconciliation algorithm runs against a private, read-only copy of its inputs.
    /// Missing requested inputs return null; a changed explicit mapping version is refused.
    /// </summary>
    public async Task<AccountingOnboardingCapture?> CaptureRetainedOnboardingAsync(
        string providerId, string fundProfileId, Guid ledgerBookId, string importId,
        string mappingProfileId, string mappingVersion, DateOnly asOfDate,
        string tenantId, string companyId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = ImportKey(providerId, fundProfileId, ledgerBookId, tenantId, companyId);
        if (_ledgerJournalStore is null || !_latestImports.TryGetValue(key, out var retained)
            || !string.Equals(retained.Summary.ImportId, importId, StringComparison.Ordinal)
            || retained.Summary.PeriodEnd != asOfDate)
            return null;

        // Freeze nested collection contents before awaiting any downstream read.
        var import = JsonSerializer.Deserialize<AccountingSystemImportDetailDto>(
            JsonSerializer.Serialize(retained, JsonOptions), JsonOptions)!;
        if (!_mappingProfiles.TryGetValue(MappingProfileKey(providerId, fundProfileId, ledgerBookId,
                mappingProfileId, tenantId, companyId), out var retainedMapping)
            || !MappingMatchesImport(import, retainedMapping))
            return null;
        var mapping = JsonSerializer.Deserialize<ExternalGlMappingProfileDto>(
            JsonSerializer.Serialize(retainedMapping.Profile, JsonOptions), JsonOptions)!;
        var version = GetOnboardingMappingVersion(mapping);
        if (!string.Equals(version, mappingVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The requested onboarding mapping version does not match the retained mapping contents.");
        if (mapping.CertificationState != AccountingCertificationStateDto.Certified)
            throw new InvalidOperationException("Onboarding comparisons require a certified retained mapping profile.");

        var book = await _ledgerJournalStore.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false);
        if (book is null || !string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            return null;
        var periods = (await _ledgerJournalStore.ListPeriodsAsync(
                ledgerBookId: ledgerBookId, fundProfileId: fundProfileId, ct: ct).ConfigureAwait(false))
            .Where(period => period.LedgerBookId == ledgerBookId && period.StartDate <= asOfDate)
            .OrderBy(period => period.StartDate).ThenBy(period => period.PeriodId).ToArray();
        var journals = new List<LedgerJournalEntryRecord>();
        foreach (var period in periods)
        {
            var rows = await _ledgerJournalStore.GetByPeriodAsync(period.PeriodId, ct).ConfigureAwait(false);
            if (rows.Any(row => row.PeriodId != period.PeriodId))
                throw new InvalidOperationException("The ledger returned journals outside the requested onboarding period.");
            journals.AddRange(rows.Where(row =>
                (row.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(row.Entry.Timestamp.UtcDateTime)) <= asOfDate));
        }
        var orderedJournals = JsonSerializer.Deserialize<LedgerJournalEntryRecord[]>(JsonSerializer.Serialize(
            journals.OrderBy(row => row.GlobalSequence).ThenBy(row => row.Entry.JournalEntryId).ToArray(), JsonOptions), JsonOptions)!;
        var frozenStore = new OnboardingLedgerSnapshotStore(book, periods, orderedJournals);
        var isolated = new AccountingSystemIntegrationService(_providers, frozenStore);
        isolated._latestImports[key] = import;
        var exactMapping = retainedMapping with { Profile = mapping };
        var reconciliation = await isolated.ReconcileLatestCoreAsync(providerId, fundProfileId,
            ledgerBookId, ct, tenantId, companyId, exactMapping).ConfigureAwait(false);
        return new(import, mapping, version, book, periods, orderedJournals, reconciliation)
        {
            MappingEvidenceReferences = retainedMapping.EvidenceLinks.ToArray()
        };
    }

    private sealed class OnboardingLedgerSnapshotStore(
        LedgerBookRecord book, IReadOnlyList<LedgerAccountingPeriod> periods,
        IReadOnlyList<LedgerJournalEntryRecord> journals) : ILedgerJournalStore
    {
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(journals.Where(row => row.PeriodId == periodId).ToArray());

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(journals.Where(row => row.AggregateId == aggregateId).ToArray());

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult(periods.FirstOrDefault(period => period.PeriodId == periodId));

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(Guid? ledgerBookId = null,
            string? status = null, string? fundProfileId = null, Guid? fundStructureNodeId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(periods.Where(period =>
                (ledgerBookId is null || period.LedgerBookId == ledgerBookId)
                && (status is null || period.Status == status)
                && (fundProfileId is null || book.FundProfileId == fundProfileId)
                && (fundStructureNodeId is null || book.FundStructureNodeId == fundStructureNodeId)).ToArray());

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
            => Task.FromResult<LedgerBookRecord?>(ledgerBookId == book.LedgerBookId ? book : null);

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(string? fundProfileId = null,
            Guid? fundStructureNodeId = null, FundStructureNodeKindDto? fundStructureNodeKind = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerBookRecord>>(
                (fundProfileId is null || book.FundProfileId == fundProfileId)
                && (fundStructureNodeId is null || book.FundStructureNodeId == fundStructureNodeId)
                && (fundStructureNodeKind is null || book.FundStructureNodeKind == fundStructureNodeKind) ? [book] : []);

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
            => throw new NotSupportedException("Onboarding ledger snapshots are read-only.");

        public Task<LedgerAccountingPeriod> SavePeriodAsync(LedgerAccountingPeriod period, long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null, CancellationToken ct = default)
            => throw new NotSupportedException("Onboarding ledger snapshots are read-only.");

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord value, CancellationToken ct = default)
            => throw new NotSupportedException("Onboarding ledger snapshots are read-only.");
    }
}
