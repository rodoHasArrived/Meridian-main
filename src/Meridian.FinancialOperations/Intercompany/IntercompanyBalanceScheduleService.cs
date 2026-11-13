using System.Text.Json;
using System.Globalization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Meridian.Storage.FundStructure;

namespace Meridian.FinancialOperations.Intercompany;

public interface IIntercompanyBalanceScheduleSource
{
    Task<IntercompanyBalanceScheduleDto> GetAsync(string? tenantId, string? companyId, string? fundProfileId,
        Guid? ledgerBookId, DateOnly asOf, CancellationToken ct = default);
    Task<IntercompanyBalanceScheduleDto> GetForCloseAsync(string? tenant, string? company, string? fund,
        Guid? book, string? periodId, CancellationToken ct = default);
}

/// <summary>Agreement-enriched posted balances for close readiness and external mapping controls.</summary>
public sealed class IntercompanyBalanceScheduleService(ILedgerJournalStore journals, IntercompanyAccountingArrangementService agreements,
    IIntercompanyAccountBindingStore bindings, IAccountingConfigurationStore configuration, IFundProfileTenancyRegistry owners,
    IIntercompanyAccountingArrangementStore agreementStore, ILegacyIntercompanyAttributionStore? legacyAttributions = null) : IIntercompanyBalanceScheduleSource
{
    public async Task<IntercompanyBalanceScheduleDto> GetAsync(string? tenantId, string? companyId,
        string? fundProfileId, Guid? ledgerBookId, DateOnly asOf, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            return Unavailable(tenantId, companyId, fundProfileId, ledgerBookId, asOf, "A concrete tenant and company are required.");
        if (ledgerBookId is null && string.IsNullOrWhiteSpace(fundProfileId))
            return Unavailable(tenantId, companyId, fundProfileId, ledgerBookId, asOf, "Select a fund or ledger book for the intercompany schedule.");
        await using var mappingLease = await bindings.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        await using var agreementLease = await agreementStore.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        await using var attributionLease = legacyAttributions is not null
            ? await legacyAttributions.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false) : EmptyLedgerAuthorityLease.Instance;
        LedgerBookRecord[] selected = ledgerBookId is { } bookId
            ? await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false) is { } selectedBook ? [selectedBook] : Array.Empty<LedgerBookRecord>()
            : (await journals.ListLedgerBooksAsync(fundProfileId, ct: ct).ConfigureAwait(false)).ToArray();
        if (selected.Length == 0)
            return Unavailable(tenantId, companyId, fundProfileId, ledgerBookId, asOf, "No authoritative scoped ledger book is available.");
        var queue = new Queue<(LedgerBookRecord Book, bool Discover, bool EnforceFund)>(selected.Select(book => (book, true, true)));
        var visited = new HashSet<Guid>();
        var selectedAgreements = new HashSet<Guid>();
        var facts = new List<Fact>();
        var issues = new List<IntercompanyBalanceScheduleIssueDto>();
        var usedAttributions = new List<LegacyIntercompanyAttributionDto>();
        while (queue.TryDequeue(out var pending))
        {
            var book = pending.Book;
            if (!visited.Add(book.LedgerBookId))
                continue;
            var owner = await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(tenantId) || !string.Equals(owner.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)
                || (pending.EnforceFund && fundProfileId is not null && !string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)))
                throw new UnauthorizedAccessException("The intercompany schedule book belongs to another tenant, company or fund scope.");
            var workspace = await configuration.GetAsync(book.FundProfileId, book.LedgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
            if (workspace is null)
                issues.Add(new("IntercompanyChartUnavailable", "An authoritative chart is required to establish intercompany mapping completeness.", true, book.LedgerBookId));
            var history = await bindings.HistoryAsync(book.LedgerBookId, ct).ConfigureAwait(false);
            var attributions = legacyAttributions is not null
                ? await legacyAttributions.ListAsync(book.LedgerBookId, ct).ConfigureAwait(false) : [];
            var records = await journals.QueryAsync(new(LedgerBookId: book.LedgerBookId, EffectiveTo: asOf), ct).ConfigureAwait(false);
            foreach (var record in records)
            {
                var effective = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
                if (effective > asOf)
                    continue;
                if (record.Entry.Metadata.Tags?.GetValueOrDefault(Consolidation.ConsolidationService.EvidenceTag) is { } consolidated)
                {
                    var evidence = JsonSerializer.Deserialize<ConsolidationEvidenceDto>(consolidated, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                        ?? throw new InvalidOperationException("Retained consolidation source evidence is invalid.");
                    foreach (var member in evidence.BookVersions.Where(x => x.LedgerBookId != evidence.Request.EliminationBookId))
                    {
                        var sourceBook = await journals.GetLedgerBookAsync(member.LedgerBookId, ct).ConfigureAwait(false);
                        if (sourceBook is not null)
                            queue.Enqueue((sourceBook, true, false));
                        else
                            issues.Add(new("IntercompanySourceBookUnavailable", "A retained consolidation member book is unavailable.", true, member.LedgerBookId));
                    }
                    continue; // Group overlay is not a second bilateral exposure.
                }
                var activeBindings = IntercompanyAccountBindingRules.Resolve(history, effective);
                foreach (var line in record.Entry.Lines)
                {
                    var dimensions = line.Dimensions;
                    LegacyIntercompanyAttributionDto? attribution = null;
                    if (dimensions?.AccountingArrangementId is null)
                    {
                        attribution = attributions.SingleOrDefault(x => x.JournalEntryId == record.Entry.JournalEntryId && x.LineId == line.EntryId);
                        if (attribution is not null)
                        {
                            if (!string.Equals(attribution.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                                || !string.Equals(attribution.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)
                                || !Sha256Digest.FixedEquals(attribution.SourceLineDigest, LegacyIntercompanyAttributionRules.SourceDigest(book, record, line)))
                                throw new InvalidOperationException("Legacy intercompany attribution differs from its immutable posted source or scope.");
                            if (dimensions?.IntercompanyAgreementId is { } retainedLegalId
                                && (retainedLegalId != attribution.IntercompanyAgreementId
                                    || dimensions.IntercompanyAgreementVersion != attribution.IntercompanyAgreementVersion))
                                throw new InvalidOperationException("Legacy attribution cannot replace retained canonical legal source provenance.");
                            dimensions = (dimensions ?? new()) with
                            {
                                IntercompanyAgreementId = attribution.IntercompanyAgreementId,
                                IntercompanyAgreementVersion = attribution.IntercompanyAgreementVersion,
                                AccountingArrangementId = attribution.AccountingArrangementId.ToString("D"),
                                AccountingArrangementRevisionId = attribution.AccountingArrangementRevisionId.ToString("D"),
                                AccountBindingRevisionId = attribution.AccountBindingRevisionId.ToString("D"),
                                ChartAccountNodeId = attribution.ChartAccountNodeId,
                                IntercompanyTransactionId = attribution.IntercompanyTransactionId
                            };
                            usedAttributions.Add(attribution);
                        }
                    }
                    var chart = workspace?.ChartOfAccounts.SingleOrDefault(x => dimensions?.ChartAccountNodeId is { } node
                        ? x.NodeId == node : x.AccountName == line.Account.Name);
                    var candidates = (attribution is null ? activeBindings : history.Where(x => x.RevisionId == attribution.AccountBindingRevisionId)).Where(x => dimensions?.ChartAccountNodeId is { } node
                        ? x.ChartAccountNodeId == node
                        : x.AccountName == line.Account.Name || x.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal)).ToArray();
                    var known = line.Account.Name is Consolidation.ConsolidationService.ReceivableAccount or Consolidation.ConsolidationService.PayableAccount
                        || candidates.Length > 0 || chart?.IntercompanyRole is not null || dimensions?.AccountingArrangementId is not null
                        || history.Any(x => x.AccountName == line.Account.Name || x.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal));
                    if (!known)
                        continue;
                    Guid? agreementId = Guid.TryParse(dimensions?.AccountingArrangementId, out var parsedAgreement) ? parsedAgreement : null;
                    if (!pending.Discover && (agreementId is null || !selectedAgreements.Contains(agreementId.Value)))
                        continue;
                    var binding = candidates.Length == 1 ? candidates[0] : null;
                    var role = binding?.Role ?? chart?.IntercompanyRole;
                    // This schedule contains monetary accounts; operating elimination evidence is a separate treatment.
                    if (role is IntercompanyAccountRoleDto.Revenue or IntercompanyAccountRoleDto.Expense)
                        continue;
                    Guid? counterparty = Guid.TryParse(dimensions?.CounterpartyId, out var parsedCounterparty) ? parsedCounterparty : null;
                    Guid? revisionId = Guid.TryParse(dimensions?.AccountingArrangementRevisionId, out var parsedRevision) ? parsedRevision : null;
                    // Posted history resolves the retained effective binding. A later rename/archive
                    // cannot erase or reclassify an already-posted balance.
                    var verified = binding is { IsArchived: false }
                        && binding.LedgerBookId == book.LedgerBookId
                        && string.Equals(binding.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(binding.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)
                        && binding.AccountType == line.Account.AccountType.ToString()
                        && (binding.AccountName == line.Account.Name || binding.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal))
                        && dimensions?.AccountBindingRevisionId == binding.RevisionId.ToString("D")
                        && dimensions.EntityId == book.FundStructureNodeId.ToString("D") && counterparty is not null;
                    ResolvedIntercompanyAccountingArrangementDto? agreement = null;
                    if (agreementId is { } id && revisionId is { } revision)
                    {
                        try
                        {
                            var original = record.SourceJournalEntryId is { } originalId
                                ? records.SingleOrDefault(item => item.Entry.JournalEntryId == originalId) : null;
                            var exactInverse = original is not null && IntercompanyJournalCorrectionEvidence.IsExactInverse(record.Entry, original.Entry);
                            agreement = exactInverse
                                ? await agreements.GetApprovedRevisionAsync(tenantId, id, revision, ct).ConfigureAwait(false)
                                : await agreements.ResolveApprovedRevisionAsync(tenantId, id, revision,
                                    new DateTimeOffset(effective.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
                            var mapping = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == book.FundStructureNodeId && x.LedgerBookId == book.LedgerBookId);
                            verified &= mapping is not null && counterparty is { } other
                                && ((agreement.PartyOneEntityId == book.FundStructureNodeId && agreement.PartyTwoEntityId == other)
                                    || (agreement.PartyTwoEntityId == book.FundStructureNodeId && agreement.PartyOneEntityId == other))
                                && (role == IntercompanyAccountRoleDto.Receivable ? mapping.ReceivableAccountNodeId : mapping.PayableAccountNodeId) == binding?.ChartAccountNodeId
                                && (line.Currency?.TransactionCurrency ?? book.BaseCurrency) == agreement.Currency
                                && dimensions?.IntercompanyAgreementId == agreement.CanonicalAgreementId
                                && dimensions.IntercompanyAgreementVersion == agreement.CanonicalAgreementVersion
                                && (attribution is null || attribution.IntercompanyAgreementPayloadHash == agreement.CanonicalPayloadHash);
                            if (pending.Discover)
                            {
                                selectedAgreements.Add(id);
                                foreach (var map in agreement.PartyMappings.Where(x => x.EntityId == counterparty))
                                {
                                    var counterpart = await journals.GetLedgerBookAsync(map.LedgerBookId, ct).ConfigureAwait(false);
                                    if (counterpart is not null)
                                        queue.Enqueue((counterpart, false, false));
                                    else
                                        issues.Add(new("IntercompanyCounterpartBookUnavailable", "The approved counterpart book cannot be read.", true, book.LedgerBookId, record.Entry.JournalEntryId));
                                }
                            }
                        }
                        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
                        { verified = false; issues.Add(new("IntercompanyAgreementUnavailable", error.Message, true, book.LedgerBookId, record.Entry.JournalEntryId)); }
                    }
                    else
                        verified = false;
                    if (!verified)
                        issues.Add(new("UnmappedLegacyIntercompany", "Posted intercompany facts lack exact approved agreement/account/party attribution; resolve legacy mapping explicitly.", true, book.LedgerBookId, record.Entry.JournalEntryId));
                    facts.Add(new(book.LedgerBookId, book.FundStructureNodeId, counterparty, agreementId, revisionId,
                        agreement?.TemplateKind.ToString(), binding?.ChartAccountNodeId ?? dimensions?.ChartAccountNodeId, role,
                        line.Currency?.TransactionCurrency ?? book.BaseCurrency, book.BaseCurrency,
                        (line.Currency?.TransactionDebit ?? line.Debit) - (line.Currency?.TransactionCredit ?? line.Credit),
                        line.Debit - line.Credit, verified, record.Entry.JournalEntryId,
                        dimensions?.AccountBindingRevisionId, record.GlobalSequence, agreement?.CanonicalAgreementId,
                        agreement?.CanonicalAgreementVersion, agreement?.CanonicalPayloadHash));
                }
            }
        }
        var rows = facts.GroupBy(x => new { x.Book, x.Entity, x.Counterparty, x.Agreement, x.Revision, x.Kind, x.Node, x.Role, x.Currency, x.Functional, x.LegalId, x.LegalVersion, x.LegalHash })
            .Select(group => new IntercompanyBalanceScheduleRowDto(group.Key.Book, group.Key.Entity, group.Key.Counterparty,
                group.Key.Agreement, group.Key.Revision, group.Key.Kind, group.Key.Node, group.Key.Role, group.Key.Currency, group.Key.Functional,
                group.Sum(x => x.Amount), group.Sum(x => x.FunctionalAmount), group.All(x => x.Verified),
                group.Select(x => x.Journal).Distinct().Order().ToArray(), group.Select(x => x.Binding).Where(x => x is not null).Cast<string>().Distinct().Order(StringComparer.Ordinal).ToArray())
            {
                IntercompanyAgreementId = group.Key.LegalId,
                IntercompanyAgreementVersion = group.Key.LegalVersion,
                IntercompanyAgreementPayloadHash = group.Key.LegalHash
            })
            .OrderBy(x => x.LedgerBookId).ThenBy(x => x.AccountingArrangementId).ThenBy(x => x.ChartAccountNodeId, StringComparer.Ordinal).ToArray();
        foreach (var pair in rows.Where(x => x.MappingVerified).GroupBy(x => new
        {
            x.AccountingArrangementId,
            x.AccountingArrangementRevisionId,
            x.IntercompanyAgreementId,
            x.IntercompanyAgreementVersion,
            x.IntercompanyAgreementPayloadHash,
            x.TransactionCurrency,
            Creditor = x.Role == IntercompanyAccountRoleDto.Receivable ? x.EntityId : x.CounterpartyEntityId!.Value,
            Debtor = x.Role == IntercompanyAccountRoleDto.Receivable ? x.CounterpartyEntityId!.Value : x.EntityId
        }))
            if (pair.Sum(x => x.TransactionSignedBalance) != 0m)
                issues.Add(new("IntercompanyReciprocalBalanceMismatch", "Posted original-currency due-to/due-from balances do not reconcile across the approved parties.", false));
        var orderedIssues = issues.Distinct().OrderBy(x => x.LedgerBookId).ThenBy(x => x.JournalEntryId).ThenBy(x => x.Code, StringComparer.Ordinal).ToArray();
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            asOf,
            Rows = rows,
            Issues = orderedIssues,
            Attributions = usedAttributions.OrderBy(x => x.AttributionId).ToArray(),
            SourceVersions = facts.Select(x => new { x.Book, x.Journal, x.Sequence }).Distinct().OrderBy(x => x.Book).ThenBy(x => x.Sequence).ToArray()
        }));
        return new(tenantId, companyId, fundProfileId, ledgerBookId, asOf, true, rows, orderedIssues, hash)
        { LegacyAttributionIds = usedAttributions.Select(x => x.AttributionId).Distinct().Order().ToArray() };
    }

    public async Task<IntercompanyBalanceScheduleDto> GetForCloseAsync(string? tenant, string? company,
        string? fund, Guid? book, string? periodId, CancellationToken ct = default)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        if (Guid.TryParse(periodId, out var id))
        {
            var period = await journals.GetPeriodAsync(id, ct).ConfigureAwait(false);
            if (period is null || (book is not null && period.LedgerBookId != book))
                return Unavailable(tenant, company, fund, book, asOf, "The selected close period does not match the scoped book.");
            asOf = period.EndDate;
            book ??= period.LedgerBookId;
        }
        else if (!string.IsNullOrWhiteSpace(periodId))
        {
            if (DateTime.TryParseExact(periodId, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                asOf = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
            else if (DateOnly.TryParseExact(periodId, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                asOf = day;
            else
                return Unavailable(tenant, company, fund, book, asOf, "Select a durable period or explicit accounting month/date for the close schedule.");
        }
        return await GetAsync(tenant, company, fund, book, asOf, ct).ConfigureAwait(false);
    }

    private static IntercompanyBalanceScheduleDto Unavailable(string? tenant, string? company, string? fund, Guid? book, DateOnly asOf, string reason)
        => new(tenant ?? "", company, fund, book, asOf, false, [], [new("IntercompanyScheduleUnavailable", reason, true)], "");
    private sealed record Fact(Guid Book, Guid Entity, Guid? Counterparty, Guid? Agreement, Guid? Revision, string? Kind,
        string? Node, IntercompanyAccountRoleDto? Role, string Currency, string Functional, decimal Amount,
        decimal FunctionalAmount, bool Verified, Guid Journal, string? Binding, long Sequence, Guid? LegalId, int? LegalVersion, string? LegalHash);
}
