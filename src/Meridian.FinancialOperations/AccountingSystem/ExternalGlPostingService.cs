using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Ledger;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingSystem;

/// <summary>Prepares exact journal exports and retains human certification and approval for dispatch.</summary>
public sealed class ExternalGlPostingService(
    PostgresLedgerJournalStore store, AccountingSystemIntegrationService accounting,
    IEnumerable<IExternalGlPostingProvider> providers, TimeProvider time, IBookOfRecordGuard? bookOfRecord = null)
{
    private readonly IReadOnlyDictionary<string, IExternalGlPostingProvider> _providers =
        providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);

    public async Task<ExternalGlBookPolicy> ConfigureBookAsync(Guid bookId, string tenant, string company, string fund,
        string providerId, string connectionId, bool confirmationRequired, string actor, OperationsActionOriginDto origin, CancellationToken ct)
    {
        RequireHuman(actor, origin);
        await store.RequireExternalGlBookAsync(bookId, tenant, company, fund, ct).ConfigureAwait(false);
        var provider = GetProvider(providerId);
        var current = await GetPolicyAsync(bookId, tenant, company, fund, ct).ConfigureAwait(false);
        var destination = current is not null && current.Destination.ProviderId == provider.ProviderId &&
            current.Destination.ConnectionId == connectionId
            ? current.Destination
            : await provider.ResolveDestinationAsync(tenant, connectionId, ct).ConfigureAwait(false);
        var policy = new ExternalGlBookPolicy(destination, confirmationRequired, actor, time.GetUtcNow());
        await store.SaveExternalGlPolicyAsync(bookId, tenant, company, fund, policy, ct).ConfigureAwait(false);
        return policy;
    }

    public Task<ExternalGlBookPolicy?> GetPolicyAsync(Guid bookId, string tenant, string company, string fund, CancellationToken ct) =>
        store.GetExternalGlPolicyAsync(bookId, tenant, company, fund, ct);

    public async Task<IReadOnlyList<ExternalGlMappingProfileDto>> ListMappingsAsync(
        Guid bookId, string tenant, string company, string fund, CancellationToken ct)
    {
        var policy = await GetPolicyAsync(bookId, tenant, company, fund, ct).ConfigureAwait(false);
        if (policy is null)
            return [];
        return await accounting.ListPostingMappingsAsync(bookId, tenant, company, fund, policy.Destination, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExternalGlPackageListItem>> ListAsync(ExternalGlPostingScope scope, CancellationToken ct)
    {
        await RequireScopeAsync(scope, ct).ConfigureAwait(false);
        return await store.ListExternalGlPackagesAsync(scope, ct).ConfigureAwait(false);
    }

    public async Task<ExternalGlPostingPackage?> GetAsync(ExternalGlPostingScope scope, string packageId, CancellationToken ct)
    {
        await RequireScopeAsync(scope, ct).ConfigureAwait(false);
        return await store.GetExternalGlPackageAsync(scope, packageId, ct).ConfigureAwait(false);
    }

    public async Task<ExternalGlPostingPackage?> DecideAsync(ExternalGlPostingScope scope, string packageId, bool approve,
        string expectedPayloadHash, string notes, string actor, OperationsActionOriginDto origin, CancellationToken ct)
    {
        RequireHuman(actor, origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(notes);
        if (!Sha256Digest.IsCanonical(expectedPayloadHash))
            throw new ArgumentException("The exact reviewed package hash is required.", nameof(expectedPayloadHash));
        await RequireScopeAsync(scope, ct).ConfigureAwait(false);
        if (approve)
            await RequireBookOfRecordAsync(bookOfRecord, scope, ct).ConfigureAwait(false);
        return await store.DecideExternalGlPackageAsync(scope, packageId, approve,
            new ExternalGlPostingDecision(actor, notes.Trim(), expectedPayloadHash, time.GetUtcNow()), ct).ConfigureAwait(false);
    }

    public async Task<ExternalGlPostingPackage> PrepareAsync(ExternalGlPostingScope scope,
        string? mappingProfileId, IReadOnlyList<Guid>? journalIds, string rationale, string actor, OperationsActionOriginDto origin,
        ExternalGlPostingPurpose purpose, string? predecessorPackageId, Guid? predecessorPeriodId, CancellationToken ct)
    {
        RequireHuman(actor, origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        if (!Enum.IsDefined(purpose))
            throw new ArgumentException("Select a supported posting purpose.", nameof(purpose));
        var (period, book) = await RequireScopeAsync(scope, ct).ConfigureAwait(false);
        var policy = await GetPolicyAsync(scope.LedgerBookId, scope.TenantId, scope.CompanyId, scope.FundProfileId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Configure the book's external GL destination first.");
        var provider = GetProvider(policy.Destination.ProviderId);
        var currentDestination = await provider.ResolveDestinationAsync(scope.TenantId, policy.Destination.ConnectionId, ct).ConfigureAwait(false);
        if (currentDestination != policy.Destination)
            throw new InvalidOperationException("The connection no longer matches the book's external destination.");

        ExternalGlPostingPackage? predecessor = null;
        if (purpose != ExternalGlPostingPurpose.Export)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(predecessorPackageId);
            var previousScope = scope with { PeriodId = predecessorPeriodId ?? scope.PeriodId };
            predecessor = await GetAsync(previousScope, predecessorPackageId, ct).ConfigureAwait(false);
            if (predecessor?.Approval is null || predecessor.Payload.Destination != policy.Destination)
                throw new InvalidOperationException("Select an approved predecessor for this book and destination.");
        }

        var previousDocs = predecessor?.Payload.Documents.ToDictionary(doc => doc.MeridianJournalId)
            ?? new Dictionary<Guid, ExternalGlPostingDocument>();
        var previousStatuses = predecessor?.Progress.ToDictionary(row => row.IdempotencyKey, row => row.Status, StringComparer.Ordinal)
            ?? new Dictionary<string, ExternalGlPostingStatus>(StringComparer.Ordinal);
        var selectedIds = journalIds?.ToHashSet() ?? [];
        if (purpose == ExternalGlPostingPurpose.Correction)
        {
            if (predecessor!.Payload.Scope.PeriodId != scope.PeriodId)
                throw new InvalidOperationException("A rejected journal must be corrected in its original period.");
            var rejected = previousDocs.Values.Where(doc => previousStatuses[doc.IdempotencyKey] == ExternalGlPostingStatus.Rejected)
                .Select(doc => doc.MeridianJournalId).ToHashSet();
            if (selectedIds.Count == 0)
                selectedIds = rejected;
            if (selectedIds.Count == 0 || !selectedIds.IsSubsetOf(rejected))
                throw new InvalidOperationException("Only definitively rejected journals can be exported with a corrected mapping.");
        }
        if (purpose == ExternalGlPostingPurpose.Reversal && selectedIds.Count == 0)
            throw new ArgumentException("Enter the identity of a posted and approved Meridian reversal journal.", nameof(journalIds));

        var repairsReversal = purpose == ExternalGlPostingPurpose.Correction &&
            selectedIds.Any(id => previousDocs[id].ReversesJournalId is not null);
        var usesPredecessorMapping = purpose == ExternalGlPostingPurpose.Reversal || repairsReversal;
        var mapping = usesPredecessorMapping
            ? predecessor!.Payload.Mapping
            : await accounting.GetPostingMappingAsync(scope, policy.Destination, RequireMappingId(mappingProfileId), ct).ConfigureAwait(false);
        var selectsUnassigned = selectedIds.Count == 0;
        var eligibleIds = (await store.ListExternalGlSourceIdsAsync(scope, policy.Destination,
            unassignedOnly: selectsUnassigned, ct).ConfigureAwait(false)).ToHashSet();
        if (selectedIds.Count > 0 && !selectedIds.IsSubsetOf(eligibleIds))
            throw new InvalidOperationException("A selected journal is outside this book and period or is an internal close transfer.");
        if (selectedIds.Count == 0)
            selectedIds = eligibleIds;
        if (selectedIds.Count == 0)
            throw new InvalidOperationException("This period has no unassigned Meridian journals to export.");

        using var authority = FundScopeTenantAuthority.Enter(scope.TenantId, "external-gl-posting");
        var periodJournals = await store.QueryAsync(
            new LedgerJournalEntryQuery(LedgerBookId: scope.LedgerBookId, PeriodId: scope.PeriodId), ct).ConfigureAwait(false);
        var records = periodJournals.Where(record => selectedIds.Contains(record.Entry.JournalEntryId))
            .OrderBy(record => record.GlobalSequence).ToArray();
        if (records.Length != selectedIds.Count)
            throw new InvalidOperationException("The selected Meridian journal population could not be verified.");
        if (purpose == ExternalGlPostingPurpose.Export)
        {
            var reversals = await FindReversalsAsync(scope, records, periodJournals, ct).ConfigureAwait(false);
            if (reversals.Count > 0 && !selectsUnassigned)
                throw new InvalidOperationException("Use Create reversal package on the accepted predecessor for a posted Meridian reversal.");
            records = records.Where(record => !reversals.Contains(record.Entry.JournalEntryId)).ToArray();
            if (records.Length == 0)
                throw new InvalidOperationException("Only reversal journals are unassigned. Start a reversal package from their accepted predecessor.");
        }

        var documents = new List<ExternalGlPostingDocument>(records.Length);
        foreach (var row in records)
        {
            var prior = purpose == ExternalGlPostingPurpose.Correction ? previousDocs[row.Entry.JournalEntryId] : null;
            var document = ProjectDocument(scope, book, period, policy.Destination, row, mapping, prior?.IdempotencyKey,
                purpose == ExternalGlPostingPurpose.Reversal ? row.SourceJournalEntryId : prior?.ReversesJournalId);
            if (purpose == ExternalGlPostingPurpose.Reversal)
                VerifyReversal(row, document, previousDocs, previousStatuses);
            if (prior?.ReversesJournalId is not null && !prior.Lines.Select(line => ReversalFingerprint(line, reverse: false))
                .SequenceEqual(document.Lines.Select(line => ReversalFingerprint(line, reverse: false))))
                throw new InvalidOperationException("A rejected reversal must retain its original external accounts, dimensions and amounts.");
            provider.ValidateDocument(document);
            documents.Add(document);
        }

        var sourceFacts = records.Select(row => new
        {
            row.GlobalSequence,
            row.PeriodId,
            row.AccountingBasis,
            row.AccountingPolicyId,
            row.AccountingPolicyVersion,
            row.PostingKind,
            row.SourceJournalEntryId,
            row.SourceEventId,
            row.Entry.JournalEntryId,
            row.Entry.Timestamp,
            row.Entry.Metadata.EffectiveDate,
            Lines = row.Entry.Lines.Select(line => new
            {
                line.EntryId,
                line.Account.Name,
                line.Debit,
                line.Credit,
                line.Currency,
                Dimensions = ReadDimensions(line.Dimensions)
            })
        });
        var source = new ExternalGlSourceCheckpoint(records[^1].GlobalSequence,
            Digest(new { scope, period.StartDate, period.EndDate, book.BaseCurrency, sourceFacts }), period.StartDate, period.EndDate);
        var payload = new ExternalGlPostingPayload(scope, policy.Destination, mapping, source, purpose, predecessorPackageId,
            documents, rationale.Trim());
        var packageId = Digest(new { scope, policy.Destination, mapping.Version, source.ContentHash, purpose, predecessorPackageId });
        return await store.RetainExternalGlPackageAsync(payload, packageId, actor, time.GetUtcNow(), ct).ConfigureAwait(false);
    }

    private async Task<(LedgerAccountingPeriod Period, LedgerBookRecord Book)> RequireScopeAsync(ExternalGlPostingScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!TenantReadPredicate.IsResolvedTenant(scope.TenantId) || !TenantReadPredicate.IsResolvedTenant(scope.CompanyId))
            throw new UnauthorizedAccessException("A signed-in company scope is required.");
        await store.RequireExternalGlBookAsync(scope.LedgerBookId, scope.TenantId, scope.CompanyId, scope.FundProfileId, ct).ConfigureAwait(false);
        using var authority = FundScopeTenantAuthority.Enter(scope.TenantId, "external-gl-posting");
        var period = await store.GetPeriodAsync(scope.PeriodId, ct).ConfigureAwait(false);
        var book = await store.GetLedgerBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false);
        if (period?.LedgerBookId != scope.LedgerBookId || book is null)
            throw new LedgerBookNotFoundException("Select a retained accounting period in the selected ledger book.");
        return (period, book);
    }

    private static ExternalGlPostingDocument ProjectDocument(ExternalGlPostingScope scope, LedgerBookRecord book,
        LedgerAccountingPeriod period, ExternalGlDestination destination, LedgerJournalEntryRecord record,
        ExternalGlMappingVersion mapping, string? supersedes, Guid? reverses)
    {
        var journal = record.Entry;
        var date = journal.Metadata.EffectiveDate ?? DateOnly.FromDateTime(journal.Timestamp.UtcDateTime);
        if (date < period.StartDate || date > period.EndDate)
            throw new InvalidOperationException($"Journal {journal.JournalEntryId:D} has an effective date outside the retained period.");
        if (record.AccountingBasis != book.AccountingBasis)
            throw new InvalidOperationException($"Journal {journal.JournalEntryId:D} has a different accounting basis from its book.");
        var currency = book.BaseCurrency.ToUpperInvariant();
        var lines = journal.Lines.Select(line =>
        {
            if (line.Currency is { } leg && (leg.FunctionalCurrency != currency || leg.TransactionCurrency != currency ||
                leg.FxRateToFunctional != 1m || leg.TransactionDebit != line.Debit || leg.TransactionCredit != line.Credit))
                throw new InvalidOperationException($"Journal line {line.EntryId:D} requires a governed currency conversion.");

            if (!mapping.Profile.AccountMappings.TryGetValue(line.Account.Name, out var externalAccount))  // Retained profiles deserialize with ordinal dictionary comparers.
                externalAccount = mapping.Profile.AccountMappings.FirstOrDefault(pair =>
                    string.Equals(pair.Key, line.Account.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(externalAccount))
                throw new InvalidOperationException($"No certified external account mapping covers {line.Account.Name}.");

            var actual = ReadDimensions(line.Dimensions);
            var scoped = actual with { FundId = actual.FundId ?? scope.FundProfileId, BookId = actual.BookId ?? scope.LedgerBookId.ToString("D") };
            var dimensions = ResolveDimensions(mapping.Profile, scoped, line.EntryId);
            return new ExternalGlPostingLine(line.EntryId, line.Account.Name, externalAccount, line.Debit, line.Credit, actual, dimensions);
        }).ToArray();

        if (lines.Length < 2 || lines.Sum(line => line.Debit) != lines.Sum(line => line.Credit))
            throw new InvalidOperationException($"Journal {journal.JournalEntryId:D} must be exactly balanced for external posting.");
        // A change in package scope or mapping cannot give the same source journal another identity
        // in one external ledger. Only a governed correction of a definitive rejection advances it.
        var key = "mdr-" + Digest(new
        {
            destination.ProviderId,
            destination.Environment,
            destination.ExternalLedgerId,
            journal.JournalEntryId,
            supersedes
        })[..44];
        return new(journal.JournalEntryId, record.GlobalSequence, date, currency, key, lines, supersedes, reverses);
    }

    private static IReadOnlyDictionary<string, string> ResolveDimensions(ExternalGlMappingProfileDto profile,
        LedgerDimensionSetDto source, Guid lineId)
    {
        var matched = profile.DimensionMappings
            .Where(rule => rule.CertificationState == AccountingCertificationStateDto.Certified &&
                !rule.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical) &&
                TrialBalanceProjectionService.MatchesDimensions(rule.MeridianDimensions, source)).ToArray();
        if (matched.Length == 0)
            throw new InvalidOperationException($"No certified external dimension mapping covers journal line {lineId:D}.");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in matched)
        {
            foreach (var (key, value) in mapping.ExternalDimensions.ExternalGlDimensions)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                var normalizedKey = key.Trim().ToLowerInvariant();
                var target = value.Trim();
                if (!result.TryAdd(normalizedKey, target) && result[normalizedKey] != target)
                    throw new InvalidOperationException($"Certified mappings disagree about external dimension {normalizedKey} for line {lineId:D}.");
            }
        }
        return result;
    }

    private async Task<HashSet<Guid>> FindReversalsAsync(ExternalGlPostingScope scope, IEnumerable<LedgerJournalEntryRecord> selected,
        IReadOnlyList<LedgerJournalEntryRecord> periodJournals, CancellationToken ct)
    {
        var byId = periodJournals.ToDictionary(record => record.Entry.JournalEntryId);
        var reversals = new HashSet<Guid>();
        foreach (var row in selected)
        {
            if (row.PostingKind != LedgerPostingKindDto.Adjustment || row.SourceJournalEntryId is not { } predecessor)
                continue;
            if (row.AdjustmentApproval?.ReasonCode.EndsWith("reversal", StringComparison.OrdinalIgnoreCase) == true)
            {
                reversals.Add(row.Entry.JournalEntryId);
                continue;
            }

            var original = byId.GetValueOrDefault(predecessor) ?? (await store.QueryAsync(
                new LedgerJournalEntryQuery(LedgerBookId: scope.LedgerBookId, JournalEntryId: predecessor), ct).ConfigureAwait(false)).SingleOrDefault();
            if (original is not null && original.Entry.Lines.Select(line => ReversalSourceFacts(line, reverse: true)).Order()
                .SequenceEqual(row.Entry.Lines.Select(line => ReversalSourceFacts(line, reverse: false)).Order()))
                reversals.Add(row.Entry.JournalEntryId);
        }
        return reversals;
    }

    private static (string Account, string Dimensions, decimal Debit, decimal Credit) ReversalSourceFacts(LedgerEntry line, bool reverse) =>
        (line.Account.Name, Digest(ReadDimensions(line.Dimensions)), reverse ? line.Credit : line.Debit, reverse ? line.Debit : line.Credit);

    private static void VerifyReversal(LedgerJournalEntryRecord record, ExternalGlPostingDocument reversal,
        IReadOnlyDictionary<Guid, ExternalGlPostingDocument> originals, IReadOnlyDictionary<string, ExternalGlPostingStatus> statuses)
    {
        if (record.AdjustmentApproval?.Status != LedgerAdjustmentApprovalStatusDto.Approved ||
            reversal.ReversesJournalId is not { } originalId || !originals.TryGetValue(originalId, out var original) ||
            statuses[original.IdempotencyKey] != ExternalGlPostingStatus.FullyAccepted || reversal.Currency != original.Currency ||
            !original.Lines.Select(line => ReversalFingerprint(line, reverse: true)).Order(StringComparer.Ordinal)
                .SequenceEqual(reversal.Lines.Select(line => ReversalFingerprint(line, reverse: false)).Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("Use an approved, posted Meridian reversal of a fully accepted journal with exactly inverted accounts, dimensions and totals.");
    }

    private static (string Account, string External, string Dimensions, decimal Debit, decimal Credit) ReversalFacts(
        ExternalGlPostingLine line, bool reverse) =>
        (line.MeridianAccount, line.ExternalAccountId, Digest(new { line.ExternalDimensions, line.MeridianDimensions }),
            reverse ? line.Credit : line.Debit, reverse ? line.Debit : line.Credit);

    private static string ReversalFingerprint(ExternalGlPostingLine line, bool reverse)
    {
        var facts = ReversalFacts(line, reverse);
        return Digest(new { facts.Account, facts.External, facts.Dimensions, facts.Debit, facts.Credit });
    }

    private static LedgerDimensionSetDto ReadDimensions(LedgerLineDimensionSet? value)
    {
        if (value is null)
            return new LedgerDimensionSetDto();
        return AccountingSystemIntegrationService.CanonicalPostingDimensions(new LedgerDimensionSetDto(
            value.FundId, value.EntityId, value.SleeveId, value.StrategyId, value.InvestorId, value.CapitalAccountId,
            value.InstrumentId, value.TaxLotId, value.CostCenterId, value.CounterpartyId, value.ExternalGlDimensions,
            value.OrganizationId, value.PortfolioId, value.BookId, value.AccountId, value.CustomerId, value.VendorId, value.ProjectId)
        { PositionId = value.PositionId });
    }

    private static string RequireMappingId(string? mappingProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingProfileId);
        return mappingProfileId.Trim();
    }

    private IExternalGlPostingProvider GetProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId.Trim(), out var provider)
            ? provider
            : throw new InvalidOperationException("The selected provider has no external GL posting adapter.");
    }

    private static string Digest<T>(T value) => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(value));

    internal static async Task RequireBookOfRecordAsync(IBookOfRecordGuard? guard, ExternalGlPostingScope scope, CancellationToken ct)
    {
        if (guard is null)
            return;
        var status = await guard.GetStatusAsync(scope.TenantId, scope.CompanyId, scope.LedgerBookId, ct).ConfigureAwait(false);
        if (!status.IsBookOfRecord)
            throw new ShadowBookNotBookOfRecordException(status.Reason);
    }

    private static void RequireHuman(string actor, OperationsActionOriginDto origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (!OperationsOriginGuard.IsHumanOperator(origin))
            throw new UnauthorizedAccessException("An authenticated human operator must review external GL posting actions.");
    }
}
