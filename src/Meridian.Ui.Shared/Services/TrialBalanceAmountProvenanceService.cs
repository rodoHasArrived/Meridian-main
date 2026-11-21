using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Binds a trial-balance row to a content-addressed prefix of immutable posted journals.
/// Reopening or appending does not replace the population the operator actually inspected.
/// </summary>
public sealed class TrialBalanceAmountProvenanceService(
    ILedgerJournalStore? journals,
    ILedgerBookService? books,
    IFundProfileTenancyRegistry? tenancy,
    PostedLedgerAmountProvenanceService postedAmounts)
{
    public static bool IsSubject(string subjectId) => subjectId.StartsWith("trial-balance:", StringComparison.Ordinal);

    public async Task<IReadOnlyList<LedgerPeriodTrialBalanceLineDto>> BindAsync(
        LedgerPeriodSummaryDto summary, string? tenantId, string? companyId, CancellationToken ct = default)
    {
        var book = books is null ? null : await books.GetBookAsync(summary.LedgerBookId, ct).ConfigureAwait(false);
        if (book is null || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            return summary.TrialBalance;
        var scope = new LedgerAmountScopeDto(tenantId, companyId, book.FundProfileId, summary.LedgerBookId, summary.PeriodId);
        var records = await AuthorizedPopulationAsync(scope, ct).ConfigureAwait(false);
        if (records is null || records.Count == 0)
            return summary.TrialBalance;
        var projected = PostgresLedgerBookService.ProjectTrialBalanceCheckpoint(records);
        var hash = PopulationHash(scope, records, book.BaseCurrency);
        var sequence = records.Max(static record => record.GlobalSequence);
        return summary.TrialBalance.Select(row =>
        {
            var candidates = projected.Where(candidate => RowIdentity(candidate) == RowIdentity(row)
                && candidate.Balance == row.Balance && candidate.DebitTotal == row.DebitTotal
                && candidate.CreditTotal == row.CreditTotal && candidate.EntryCount == row.EntryCount).ToArray();
            return candidates.Length != 1 || candidates[0].LedgerEntryIds.Count == 0 ? row : row with
            {
                BalanceProofSubjectId = $"trial-balance:{sequence.ToString(CultureInfo.InvariantCulture)}:{RowIdentity(row)}:{hash}",
                BalanceProofScope = scope,
                BalanceProofCurrency = book.BaseCurrency,
                JournalEntryIds = candidates[0].JournalEntryIds,
                LedgerEntryIds = candidates[0].LedgerEntryIds
            };
        }).ToArray();
    }

    public async Task<EvidencePacketDto?> GetPacketAsync(string subjectId, LedgerAmountScopeDto scope, CancellationToken ct = default)
    {
        var parts = subjectId.Split(':');
        if (parts.Length != 4 || parts[0] != "trial-balance"
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0
            || !Sha256Digest.IsCanonical(parts[2]) || !Sha256Digest.IsCanonical(parts[3]))
            return null;
        var all = await AuthorizedPopulationAsync(scope, ct).ConfigureAwait(false);
        if (all is null)
            return null;
        var records = all.Where(record => record.GlobalSequence <= sequence).ToArray();
        var book = await books!.GetBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false);
        if (records.Length == 0 || records.Max(static record => record.GlobalSequence) != sequence
            || book is null || !Sha256Digest.FixedEquals(parts[3], PopulationHash(scope, records, book.BaseCurrency)))
            return null;
        var rows = PostgresLedgerBookService.ProjectTrialBalanceCheckpoint(records)
            .Where(row => RowIdentity(row) == parts[2]).ToArray();
        if (rows.Length != 1)
            return null;
        var row = rows[0];
        var evidence = new List<LedgerAmountProofEvidenceDto>();
        var nodes = new List<EvidenceNodeDto>();
        var edges = new List<EvidenceEdgeDto>();
        var warnings = new List<string>();
        var accountingAttestations = new List<AccountingPreparerAttestationDisclosureDto>();
        var staleSupport = false;
        foreach (var record in records.Where(record => row.JournalEntryIds.Contains(record.Entry.JournalEntryId)))
        foreach (var line in record.Entry.Lines.Where(line => row.LedgerEntryIds.Contains(line.EntryId)))
        {
            // Classification belongs to the frozen posting even when its source proof is blocked.
            // Aggregation must not turn preparer-only accounting into independent approval.
            accountingAttestations.AddRange(AccountingPreparerAttestationDisclosure.Read(record, scope) ?? []);
            var sourceSubject = $"{record.Entry.JournalEntryId:D}:{line.EntryId:D}:{(line.Debit > 0m ? "debit" : "credit")}";
            var packet = await postedAmounts.GetRetainedPacketAsync(sourceSubject, scope, record, book.BaseCurrency, ct).ConfigureAwait(false);
            staleSupport |= packet?.LedgerAmount?.RetainedSupportStale == true;
            if (packet?.LedgerAmount is not { } proof || proof.Status != EvidenceStatusDto.Ready
                || !string.Equals(proof.Currency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("A contributing journal line lacks verified retained source evidence in this currency.");
                warnings.AddRange(packet?.Warnings ?? []);
                continue;
            }
            string Qualify(string id) => $"{id}@{sourceSubject}";
            string? Rebind(string? route, string? digest)
            {
                if (route is null) return null;
                return route.Split('?')[0] + $"?ledgerAmountSubjectId={Uri.EscapeDataString(subjectId)}&ledgerBookId={scope.LedgerBookId:D}"
                    + $"&periodId={scope.PeriodId:D}&fundProfileId={Uri.EscapeDataString(scope.FundProfileId)}&expectedContentHash={digest}";
            }
            evidence.AddRange(proof.Evidence.Select(item => item with
            {
                EvidenceId = Qualify(item.EvidenceId), SourceScope = scope, SourceSubjectId = sourceSubject,
                Route = Rebind(item.Route, item.ContentHash)
            }));
            nodes.AddRange(packet.Nodes.Select(node => node with
            {
                EvidenceId = Qualify(node.EvidenceId),
                ArtifactRefs = node.ArtifactRefs.Select(artifact => artifact with
                {
                    ArtifactId = Qualify(artifact.ArtifactId), Route = Rebind(artifact.Route, artifact.Hash)
                }).ToArray()
            }));
            edges.AddRange(packet.Edges.Select(edge => edge with { FromId = Qualify(edge.FromId), ToId = Qualify(edge.ToId) }));
        }
        if (row.LedgerEntryIds.Count == 0 || evidence.Select(item => item.EvidenceId).Distinct().Count() != evidence.Count)
            warnings.Add("Contributing ledger identities are missing or ambiguous.");
        var result = ReportLedgerAmountProvenanceService.Packet(subjectId, scope, row.AccountName, row.Balance, book.BaseCurrency,
            warnings.Count == 0 ? evidence : [], warnings.Count == 0 ? nodes : [], warnings.Distinct().ToArray(), warnings.Count == 0 ? edges : [],
            AccountingPreparerAttestationDisclosure.Union(accountingAttestations));
        return result with { LedgerAmount = result.LedgerAmount! with { RetainedSupportStale = staleSupport } };
    }

    private async Task<IReadOnlyList<LedgerJournalEntryRecord>?> AuthorizedPopulationAsync(LedgerAmountScopeDto scope, CancellationToken ct)
    {
        if (journals is null || books is null || tenancy is null || scope.LedgerBookId == Guid.Empty || scope.PeriodId == Guid.Empty
            || string.IsNullOrWhiteSpace(scope.FundProfileId) || string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId))
            return null;
        var owner = await tenancy.ResolveAsync(scope.FundProfileId, ct).ConfigureAwait(false);
        var book = await books.GetBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false);
        var period = await journals.GetPeriodAsync(scope.PeriodId, ct).ConfigureAwait(false);
        if (owner is null || book is null || period is null || owner.TenantId != scope.TenantId || owner.CompanyId != scope.CompanyId
            || owner.FundProfileId != scope.FundProfileId || book.FundProfileId != scope.FundProfileId
            || book.LedgerBookId != scope.LedgerBookId || period.LedgerBookId != scope.LedgerBookId || period.PeriodId != scope.PeriodId)
            return null;
        var records = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: scope.LedgerBookId, PeriodId: scope.PeriodId), ct).ConfigureAwait(false);
        return records.Any(record => record.PeriodId != scope.PeriodId || record.GlobalSequence <= 0)
            || records.Select(record => record.GlobalSequence).Distinct().Count() != records.Count ? null : records;
    }

    private static string PopulationHash(LedgerAmountScopeDto scope, IReadOnlyList<LedgerJournalEntryRecord> records, string currency)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { Scope = scope, Currency = currency, Journals = records.OrderBy(record => record.GlobalSequence).ToArray() }));

    private static string RowIdentity(LedgerPeriodTrialBalanceLineDto row)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { row.AccountName, row.AccountType, row.Symbol, row.FinancialAccountId, row.Dimensions }));
}
