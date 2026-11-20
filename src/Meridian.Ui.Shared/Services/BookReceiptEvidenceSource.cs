using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Reconciliation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>Supplies independently retained provider cash and posted journal evidence for an exact book.</summary>
public interface IBookReceiptEvidenceSource
{
    Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query,
        IReadOnlyList<BookReceiptExpectationDto> expectations, CancellationToken ct = default);
}

/// <summary>
/// Adapts canonical statement intake, Security Master and retained reconciliation matches. Receipt
/// cash is never inferred from a journal, and a matching failure never becomes an authoritative zero.
/// Unsupported provider/account authority remains incomplete, including neutral books for which
/// the existing statement-account service has no accepted account population.
/// </summary>
public sealed partial class BookReceiptEvidenceSource(
    IReconciliationApiService? reconciliation = null,
    ICanonicalStatementStore? statements = null,
    IStatementRunMatchArtifactStore? matches = null,
    ILedgerJournalStore? journals = null,
    ISecurityReferenceLookup? securities = null,
    IFundProfileTenancyRegistry? tenancy = null,
    IAccountQueryService? accounts = null,
    IStatementNormalizedEntityRepository? normalized = null,
    IStatementRunRecoveryRepository? recovery = null) : IBookReceiptEvidenceSource
{
    public async Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query,
        IReadOnlyList<BookReceiptExpectationDto> expectations, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(expectations);
        if (reconciliation is null || statements is null || journals is null || tenancy is null || recovery is null)
            return Missing("Authoritative statement, ledger, or book ownership services are unavailable.");
        try
        {
            var book = await journals.GetLedgerBookAsync(query.Scope.LedgerBookId, ct).ConfigureAwait(false);
            var owner = book is null ? null : await tenancy.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            if (book is null || book.LedgerBookId != query.Scope.LedgerBookId || owner is null
                || !owner.IsHeldBy(query.Scope.TenantId) || !Equal(owner.CompanyId, query.Scope.CompanyId))
                return Missing("The exact book tenant and company ownership is unavailable.");
            var coverage = new List<BookReceiptCoverageDto>();
            var receipts = new List<BookReceiptDto>();
            var postings = new List<BookReceiptPostingDto>();
            var windows = new List<(Guid Account, DateOnly Start, DateOnly End)>();
            var ledger = await ReadLedgerAsync(query, coverage, ct).ConfigureAwait(false);
            var scope = new ReconciliationBreakQueueScope(query.Scope.TenantId, query.Scope.CompanyId);
            var runs = await reconciliation.ListStatementRunsAsync(scope, ct).ConfigureAwait(false);
            foreach (var run in runs.OrderBy(r => r.RunId, StringComparer.Ordinal))
            {
                var authority = await reconciliation.GetStatementRunAuthorizationAsync(run.RunId, scope, ct).ConfigureAwait(false);
                if (authority is null || authority.LedgerBookId != book.LedgerBookId || !Equal(authority.FundProfileId, book.FundProfileId))
                    continue;
                var retained = await statements.GetImportAsync(run.ImportId, ct).ConfigureAwait(false);
                if (retained is null || retained.Import.ImportId != run.ImportId || retained.Import.AccountingScope is not { } accounting
                    || accounting.LedgerBookId != book.LedgerBookId || accounting.AccountingPeriodId != authority.AccountingPeriodId
                    || !Equal(accounting.FundProfileId, book.FundProfileId)
                    || run.RunId != authority.RunId || run.RunId != run.ImportId
                    || accounting.AsOfDate != authority.AsOfDate || retained.Import.StatementDate != authority.AsOfDate
                    || retained.Import.StatementPeriodStart != authority.PeriodStart || retained.Import.StatementPeriodEnd != authority.PeriodEnd
                    || retained.Import.StatementPeriodEnd != authority.AsOfDate
                    || !Equal(retained.Import.FundAccountId, authority.FundAccountId.ToString("D")))
                {
                    coverage.Add(new("receipt-authority", false, "An authorized statement lacks exact retained accounting evidence."));
                    continue;
                }
                var import = retained.Import;
                if (DateOnly.FromDateTime(import.ImportedAtUtc.UtcDateTime) > query.AsOfDate)
                    continue;
                var artifact = matches is null ? null : await matches.GetAsync(run.RunId, ct).ConfigureAwait(false);
                var checkpoint = await recovery.GetAsync(run.RunId, ct).ConfigureAwait(false);
                if (artifact?.MatchGroups is null || checkpoint is null
                    || !StatementReconciliationIntakeAuthority.HasVerifiedCompletion(checkpoint, retained, artifact)
                    || artifact.MatchGroups.Count != artifact.MatchCount || retained.Rows.Count != import.NormalizedRowCount
                    || !Sha256Digest.IsWellFormed(import.SourceFileHash)
                    || !Sha256Digest.IsWellFormed(import.ExecutedMappingFingerprint)
                    || !Sha256Digest.FixedEquals(import.ExecutedMappingFingerprint, artifact.SourceComparisonMappingFingerprint)
                    || checkpoint.UpdatedAtUtc == default || DateOnly.FromDateTime(checkpoint.UpdatedAtUtc.UtcDateTime) > query.AsOfDate)
                {
                    const string reason = "Completed canonical statement and mapping hashes are not established for this daily run.";
                    coverage.Add(new("receipt-completion", false, reason));
                    coverage.Add(new("posting-links", false, reason));
                    continue;
                }
                windows.Add((authority.FundAccountId, authority.PeriodStart, authority.PeriodEnd));
                var entities = normalized is null ? null : await normalized.GetAsync(run.RunId, ct).ConfigureAwait(false);
                var runReceipts = new Dictionary<string, BookReceiptDto>(StringComparer.Ordinal);
                foreach (var row in retained.Rows.OrderBy(r => r.SourceRowNumber))
                {
                    var effective = row.SettlementDate ?? row.TradeDate;
                    if (effective > query.AsOfDate)
                        continue;
                    var flow = FlowType(row.ActivityType);
                    if (flow is null)
                    {
                        if (row.CashAmount != 0m && (Equal(row.ActivityType, "transaction") || Equal(row.ActivityType, "cash")))
                            coverage.Add(new("receipt-classification", false, "A provider movement lacks an authoritative income or principal classification."));
                        continue;
                    }
                    if (row.ImportId != import.ImportId || string.IsNullOrWhiteSpace(row.ExternalTransactionId)
                        || string.IsNullOrWhiteSpace(import.SourceInstitution) || string.IsNullOrWhiteSpace(import.ExternalAccountId)
                        || !Equal(row.Account, import.ExternalAccountId))
                    {
                        coverage.Add(new("receipt-identity", false, "A provider receipt lacks a stable payment identity or exact account attribution."));
                        continue;
                    }
                    var source = entities?.RunId == run.RunId && entities.ImportId == import.ImportId
                        ? entities.Transactions.SingleOrDefault(t => t.SourceRowNumber == row.SourceRowNumber
                            && t.SourceRowHash == row.RawChecksum && t.Amount == row.CashAmount
                            && Equal(t.Currency, row.Currency) && t.ExternalReference == row.ExternalTransactionId)
                        : null;
                    var security = await ResolveSecurityAsync(row, source, ct).ConfigureAwait(false);
                    if (security is null)
                    {
                        coverage.Add(new("receipt-security", false, "A provider receipt has no authoritative security identity."));
                        continue;
                    }
                    // Keep the provider kind immutable. Coupon compatibility belongs to
                    // allocation, not a rewrite of cash evidence when accepted terms change.
                    var related = Raw(source, "relatedTransactionId", "reversesTransactionId");
                    if (row.CashAmount <= 0m && string.IsNullOrWhiteSpace(related))
                    {
                        coverage.Add(new("receipt-reversal", false, "A nonpositive provider receipt has no retained original-payment link."));
                        continue;
                    }
                    var evidence = $"{import.ImportId}:{row.SourceRowNumber}";
                    var receipt = new BookReceiptDto(PaymentId(import.SourceInstitution, import.ExternalAccountId, row.ExternalTransactionId),
                        security.Value, effective, row.Currency.Trim().ToUpperInvariant(), row.CashAmount,
                        import.SourceInstitution.Trim().ToLowerInvariant(),
                        JsonSerializer.Serialize(new[] { import.ExternalAccountId.Trim().ToUpperInvariant(), row.ExternalTransactionId.Trim() }),
                        ExplicitAllocations(source, row.CashAmount, security.Value, row.Currency, expectations),
                        string.IsNullOrWhiteSpace(related) ? null : PaymentId(import.SourceInstitution, import.ExternalAccountId, related),
                        [UiApiRoutes.ReconciliationStatementRunById.Replace("{runId}", Uri.EscapeDataString(run.RunId), StringComparison.Ordinal), $"statement:{evidence}"], flow);
                    receipts.Add(receipt);
                    runReceipts[evidence] = receipt;
                }
                if (!StatementReconciliationIntakeAuthority.CanCompareSourceRun(artifact)
                    || artifact.SourceComparisonPopulationKinds?.Contains("Transaction", StringComparer.OrdinalIgnoreCase) != true)
                    coverage.Add(new("posting-comparison", false, "The retained internal transaction comparison is incomplete; provider cash remains separately observed."));
                ProjectPostings(artifact.MatchGroups, runReceipts, ledger, book.BaseCurrency, authority.FundAccountId,
                    import.ExternalAccountId, expectations, postings, coverage);
            }
            await AddProviderCoverageAsync(book, query, windows, expectations, coverage, ct).ConfigureAwait(false);
            var merged = receipts.GroupBy(r => r.ReceiptId, StringComparer.Ordinal)
                .SelectMany(g => g.GroupBy(r => (r.SecurityId, r.EffectiveDate, r.Currency, r.Amount, r.ReversesReceiptId,
                    r.FlowType, Allocations: JsonSerializer.Serialize(r.Allocations)))
                    .Select(same => same.First() with { EvidenceLinks = same.SelectMany(r => r.EvidenceLinks).Distinct().Order(StringComparer.Ordinal).ToArray() }))
                .OrderBy(r => r.ReceiptId, StringComparer.Ordinal).ToArray();
            var uniquePostings = postings.GroupBy(p => (p.JournalEntryId, p.ReceiptId, p.Currency))
                .SelectMany(g => g.Any(p => !p.IsInferredAllocation) ? g.Where(p => !p.IsInferredAllocation)
                    : g.Any(p => p.ObligationKey.Length > 0) ? g.Where(p => p.ObligationKey.Length > 0) : g)
                .DistinctBy(p => p.PostingId).ToArray();
            var overallocated = uniquePostings.GroupBy(p => (p.JournalEntryId, p.Currency))
                .Where(g => g.Sum(p => p.Amount) > ledger.Where(r => r.Entry.JournalEntryId.ToString("D") == g.Key.JournalEntryId)
                    .SelectMany(r => r.Entry.Lines).Where(IsCashLine)
                    .Where(l => Equal(Currency(l, book.BaseCurrency), g.Key.Currency)).Sum(CashAmount))
                .Select(g => g.Key.JournalEntryId).ToHashSet(StringComparer.Ordinal);
            if (overallocated.Count > 0)
                coverage.Add(new("posting-allocation", false, "Retained matches allocate the same journal cash more than once."));
            coverage.Add(new("Receipts", !coverage.Any(c => !c.IsComplete && !c.Area.StartsWith("posting", StringComparison.Ordinal)),
                "Receipt authority requires complete provider-account coverage and stable payment identities."));
            coverage.Add(new("Postings", !coverage.Any(c => !c.IsComplete && c.Area.StartsWith("posting", StringComparison.Ordinal)),
                "Posting authority requires retained matching and exact journal cash allocation."));
            return new([], merged, uniquePostings.Where(p => !overallocated.Contains(p.JournalEntryId))
                .OrderBy(p => p.PostingId, StringComparer.Ordinal).ToArray(), coverage.Distinct().ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do not publish an incomplete read as a complete provider population.
            return Missing("The book's retained receipt evidence could not be read completely.");
        }
    }

    private async Task<IReadOnlyList<LedgerJournalEntryRecord>> ReadLedgerAsync(BookReceiptQueryDto query,
        List<BookReceiptCoverageDto> coverage, CancellationToken ct)
    {
        try
        {
            var periods = await journals!.ListPeriodsAsync(ledgerBookId: query.Scope.LedgerBookId, ct: ct).ConfigureAwait(false);
            var ids = periods.Where(p => p.LedgerBookId == query.Scope.LedgerBookId).Select(p => p.PeriodId).ToHashSet();
            return (await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: query.Scope.LedgerBookId,
                    EffectiveTo: query.AsOfDate), ct).ConfigureAwait(false))
                .Where(r => ids.Contains(r.PeriodId) && EffectiveDate(r) <= query.AsOfDate
                    && DateOnly.FromDateTime(r.CreatedAt.UtcDateTime) <= query.AsOfDate).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            coverage.Add(new("postings", false, "Posted journal evidence could not be read for the exact book."));
            return [];
        }
    }

    private async Task<Guid?> ResolveSecurityAsync(CanonicalStatementRow row, StatementTransaction? source, CancellationToken ct)
    {
        var requested = Guid.TryParse(source?.SecurityId, out var id) && id != Guid.Empty ? (Guid?)id : null;
        if (securities is null || (requested is null && string.IsNullOrWhiteSpace(row.Symbol)))
            return null;
        var security = await securities.GetByCanonicalAsync(new SecurityReferenceLookupRequest(SecurityId: requested,
            Symbol: row.Symbol, Currency: row.Currency, Source: "canonical-statement"), ct).ConfigureAwait(false);
        return security is { CoverageStatus: WorkstationSecurityCoverageStatus.Resolved, IsInferredMatch: false }
            && (security.Status == SecurityStatusDto.Active || (requested is not null && security.Status == SecurityStatusDto.Inactive))
            && security.SecurityId != Guid.Empty && (requested is null || requested == security.SecurityId)
            && Equal(security.Currency, row.Currency) ? security.SecurityId : null;
    }

    private async Task AddProviderCoverageAsync(LedgerBookRecord book, BookReceiptQueryDto query,
        IReadOnlyList<(Guid Account, DateOnly Start, DateOnly End)> windows, IReadOnlyList<BookReceiptExpectationDto> expectations,
        List<BookReceiptCoverageDto> coverage, CancellationToken ct)
    {
        if (accounts is null || !Guid.TryParse(book.FundProfileId, out var ownerId))
        {
            coverage.Add(new("provider-coverage", false, "The complete book provider-account inventory is unavailable."));
            return;
        }
        var inventory = await accounts.GetFundAccountsAsync(ownerId, ct).ConfigureAwait(false);
        var active = inventory.CustodianAccounts.Concat(inventory.BankAccounts).Concat(inventory.BrokerageAccounts)
            .Where(a => a.IsActive && a.FundId == ownerId).DistinctBy(a => a.AccountId).ToArray();
        if (inventory.FundId != ownerId || active.Length == 0)
        {
            coverage.Add(new("provider-coverage", false, "No authoritative provider-account population is available for this book."));
            return;
        }
        var start = expectations.Where(e => e.DueDate <= query.AsOfDate).Select(e => e.DueDate).DefaultIfEmpty(query.AsOfDate).Min();
        foreach (var account in active)
        {
            var through = start;
            var covered = false;
            foreach (var window in windows.Where(w => w.Account == account.AccountId && w.End >= start).OrderBy(w => w.Start))
            {
                if (window.Start > through)
                    break;
                if (window.End >= query.AsOfDate) { covered = true; break; }
                if (window.End.AddDays(1) > through)
                    through = window.End.AddDays(1);
            }
            coverage.Add(new($"provider-account:{account.AccountId:D}", covered,
                covered ? null : "Provider statement coverage is missing or has gaps through the daily run date."));
        }
    }

    private static bool Equal(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? Raw(StatementTransaction? source, params string[] keys)
        => source?.RawSnapshot.FirstOrDefault(p => keys.Any(k => Equal(p.Key, k))).Value;
    private static string? Tag(JournalEntryMetadata metadata, string key) => metadata.Tags?.FirstOrDefault(p => Equal(p.Key, key)).Value;
    private static DateOnly EffectiveDate(LedgerJournalEntryRecord record)
        => record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
    private static string PaymentId(string provider, string account, string transaction)
        => "provider-payment:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { provider.Trim().ToUpperInvariant(), account.Trim().ToUpperInvariant(), transaction.Trim() })))).ToLowerInvariant();
    private static string? FlowType(string activity) => new string(activity.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() switch
    {
        "coupon" or "couponpayment" or "couponreceipt" or "couponreversal" => "coupon",
        "interest" or "interestpayment" or "interestreceipt" or "bondinterest" or "interestreversal" => "interest",
        "principal" or "principalpayment" or "principalreceipt" or "principalrepayment" or "principalpaydown"
            or "redemption" or "maturity" or "principalreversal" => "principal",
        _ => null
    };
    private static BookReceiptSourceSnapshotDto Missing(string reason)
        => new([], [], [], [new("Receipts", false, reason), new("Postings", false, reason)]);
}
