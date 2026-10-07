using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

/// <summary>Builds a reviewed overlay; source entity books are never changed by eliminations.</summary>
public sealed class ConsolidationService(
    ConsolidationPerimeterResolver perimeterResolver,
    ILedgerJournalStore journals,
    IAccountingPolicyService policies,
    IAccountingJournalDraftService journalDrafts) : IConsolidationDraftGuard
{
    public const string RuleId = "consolidation.receivable-payable";
    public const string RuleVersion = "w10-v1";
    public const string ReceivableAccount = "Assets:Intercompany Receivable";
    public const string PayableAccount = "Liabilities:Intercompany Payable";
    public const string EvidenceTag = "consolidation.evidence";
    public const string DigestTag = "consolidation.digest";

    public async Task<ConsolidationCalculation> CalculateAsync(ConsolidationRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var perimeter = await perimeterResolver.ResolveAsync(request.OrganizationId, request.OwnershipRootId,
            new DateTimeOffset(request.AsOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
        var book = await journals.GetLedgerBookAsync(request.EliminationBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The elimination book is unavailable.");
        if (book.FundStructureNodeId != request.OwnershipRootId || book.FundStructureNodeKind != FundStructureNodeKindDto.Fund ||
            book.AccountingBasis != AccountingBasisKindDto.Primary ||
            !string.Equals(book.BaseCurrency, perimeter.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The elimination book must belong to the Fund ownership root and use Primary basis and the perimeter currency.");
        var period = await journals.GetPeriodAsync(request.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The elimination period is unavailable.");
        if (period.LedgerBookId != book.LedgerBookId || request.AsOf < period.StartDate || request.AsOf > period.EndDate)
            throw new InvalidOperationException("The consolidation date must fall within the elimination book's period.");
        var policy = await policies.ResolvePolicyAsync(new AccountingPolicyQuery(AccountingBasisKindDto.Primary,
            request.AsOf, book.AccountingPolicyId, book.FundProfileId, book.FundStructureNodeId,
            PolicyVersion: book.AccountingPolicyVersion), ct).ConfigureAwait(false);
        var rule = policy.RulePack?.Rules.SingleOrDefault(x => x.RuleId == RuleId);
        if (rule is null || rule.TreatmentKind != AccountingTreatmentKindDto.ConsolidationElimination ||
            rule.RuleVersion != RuleVersion || policy.Version != book.AccountingPolicyVersion || !rule.RequiresApproval || !rule.RequiresEvidence || rule.AllowsAutoPosting)
            throw new InvalidOperationException("The book policy must enable the governed consolidation receivable/payable rule w10-v1.");

        var ids = perimeter.Entities.Select(x => x.EntityId.ToString("D")).Order(StringComparer.Ordinal).ToArray();
        var scope = "consolidation:" + Hash(new
        {
            request.OrganizationId,
            request.OwnershipRootId,
            request.EliminationBookId,
            request.PeriodId,
            request.AsOf,
            Entities = ids,
            RuleVersion
        });
        var source = new List<ConsolidationSourceDto>();
        var records = new List<LedgerJournalEntryRecord>();
        var versions = new List<ConsolidationBookVersionDto>();
        var blockers = new List<string>();
        foreach (var entity in perimeter.Entities)
        {
            var candidates = (await journals.ListLedgerBooksAsync(book.FundProfileId, entity.EntityId,
                FundStructureNodeKindDto.Entity, ct).ConfigureAwait(false))
                .Where(x => x.AccountingBasis == AccountingBasisKindDto.Primary).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException("Each perimeter entity must have exactly one Primary source book in the fund profile.");
            var entityBook = candidates[0];
            if (!string.Equals(entityBook.BaseCurrency, perimeter.Currency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cross-currency consolidation is outside this slice; no FX translation is performed.");
            var population = await ReadBookAsync(entityBook.LedgerBookId, request.AsOf, ct).ConfigureAwait(false);
            versions.Add(Version(entityBook.LedgerBookId, population));
            records.AddRange(population);
            foreach (var record in population)
            {
                if (record.Entry.Metadata.IdempotencyKey?.StartsWith("consolidation:", StringComparison.Ordinal) == true)
                    throw new InvalidOperationException("Entity source books cannot contain consolidation overlays.");
                foreach (var line in record.Entry.Lines)
                {
                    if (line.Currency is { } currency && (!currency.IsFunctionalCurrency || currency.FxRateToFunctional != 1m ||
                        currency.TransactionDebit != line.Debit || currency.TransactionCredit != line.Credit ||
                        !string.Equals(currency.FunctionalCurrency, perimeter.Currency, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Source currency differs from the declared consolidation currency; FX is unsupported.");
                    var entityId = line.Dimensions?.EntityId;
                    var counterparty = line.Dimensions?.CounterpartyId;
                    if (!Guid.TryParse(entityId, out var postingEntity) || postingEntity != entity.EntityId)
                        throw new InvalidOperationException("Every source line must carry an unambiguous posting entity matching its authoritative book.");
                    if (counterparty is not null && Guid.TryParse(counterparty, out var counterpartyId))
                        counterparty = counterpartyId.ToString("D");
                    var evidence = new ConsolidationSourceDto(entityBook.LedgerBookId, record.Entry.JournalEntryId,
                        line.EntryId, postingEntity.ToString("D"), counterparty, line.Account.Name, line.Debit, line.Credit,
                        EffectiveDate(record), $"journal:{record.Entry.JournalEntryId:D}/line:{line.EntryId:D}");
                    source.Add(evidence);
                    if (line.Account.Name is ReceivableAccount or PayableAccount)
                    {
                        var expectedType = line.Account.Name == ReceivableAccount ? LedgerAccountType.Asset : LedgerAccountType.Liability;
                        if (line.Account.AccountType != expectedType || line.Account.Symbol is not null || line.Account.FinancialAccountId is not null)
                            blockers.Add("Intercompany accounts must use the supported unscoped asset/receivable and liability/payable identities.");
                        if (counterparty is null || !ids.Contains(counterparty) || counterparty == evidence.EntityId)
                            blockers.Add($"Intercompany line {line.EntryId:D} has a missing, self, or outside-perimeter counterparty.");
                    }
                }
            }
        }
        // Later eliminations already include the earlier source balances. A backdated draft
        // would add those balances again unless every later overlay were recomputed, which
        // this first slice does not support. Inspect the whole dedicated overlay book.
        var overlay = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book.LedgerBookId), ct)
            .ConfigureAwait(false);
        if (overlay.Any(record => EffectiveDate(record) > request.AsOf))
            throw new InvalidOperationException("Backdated consolidation is outside this slice: the elimination book contains later posted eliminations. Use the latest posted date or later and obtain renewed review.");
        versions.Add(Version(book.LedgerBookId, overlay));
        // A dedicated overlay book prevents unrelated journals or overlapping as-of runs from disappearing.
        var posted = new List<LedgerJournalEntryRecord>();
        foreach (var record in overlay)
        {
            var prior = ReadEvidence(record.Entry.Metadata.Tags?.GetValueOrDefault(EvidenceTag));
            if (prior is null || prior.Request.OrganizationId != request.OrganizationId ||
                prior.Request.OwnershipRootId != request.OwnershipRootId)
                throw new InvalidOperationException("Use a dedicated consolidation book containing only this perimeter's governed eliminations.");
            if (prior.ExpectedLines.Any(x => x.EntityId is null || !ids.Contains(x.EntityId) ||
                x.Dimensions?.CounterpartyId is null || !ids.Contains(x.Dimensions.CounterpartyId) ||
                x.EntityId == x.Dimensions.CounterpartyId) ||
                !Sha256Digest.FixedEquals(record.Entry.Metadata.Tags?.GetValueOrDefault(DigestTag),
                    Sha256Digest.ComputeUtf8(record.Entry.Metadata.Tags![EvidenceTag])))
                throw new InvalidOperationException("Posted consolidation provenance is inconsistent with this perimeter.");
            posted.Add(record);
        }
        var matches = ids.Select(entityId =>
        {
            var cp = ids.Single(x => x != entityId);
            var lines = source.Where(x => (x.EntityId == entityId && x.CounterpartyId == cp && x.AccountPath == ReceivableAccount) ||
                (x.EntityId == cp && x.CounterpartyId == entityId && x.AccountPath == PayableAccount)).ToArray();
            var ar = lines.Where(x => x.AccountPath == ReceivableAccount).Sum(x => x.Debit - x.Credit);
            var ap = lines.Where(x => x.AccountPath == PayableAccount).Sum(x => x.Credit - x.Debit);
            // Abnormal/reversed signs remain an explicit break, never offset an unrelated balance.
            var amount = ar >= 0m && ap >= 0m ? Math.Min(ar, ap) : 0m;
            return new ConsolidationMatchDto(entityId, cp, ar, ap, amount, ar - amount, ap - amount, lines);
        }).ToArray();
        var proposed = new List<ManualJournalEntryLineDto>();
        foreach (var match in matches)
        {
            var current = posted.SelectMany(x => x.Entry.Lines).Where(x => x.Account.Name == ReceivableAccount &&
                x.Dimensions?.EntityId == match.PostingEntityId && x.Dimensions.CounterpartyId == match.CounterpartyId)
                .Sum(x => x.Credit - x.Debit);
            var delta = match.MatchedAmount - current;
            if (delta == 0m)
                continue;
            Add(ReceivableAccount, match.PostingEntityId, match.CounterpartyId, -delta);
            Add(PayableAccount, match.CounterpartyId, match.PostingEntityId, delta);
        }
        void Add(string account, string entity, string counterparty, decimal signed)
        {
            proposed.Add(new ManualJournalEntryLineDto($"line-{proposed.Count + 1}",
                signed > 0 ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
                Math.Abs(signed), perimeter.Currency, account, EntityId: entity,
                Description: "Reviewed intercompany elimination", EvidenceLink: $"consolidation:{scope}",
                Dimensions: new LedgerDimensionSetDto(FundId: request.OwnershipRootId.ToString("D"), EntityId: entity, CounterpartyId: counterparty)));
        }
        var orderedVersions = versions.OrderBy(x => x.LedgerBookId).ToArray();
        var perimeterFingerprint = Hash(perimeter);
        var fingerprint = Hash(new
        {
            Versions = orderedVersions,
            Perimeter = perimeterFingerprint,
            Policy = policy,
            Sources = source.OrderBy(x => x.JournalEntryId).ThenBy(x => x.LineId).ToArray()
        });
        var evidenceDto = new ConsolidationEvidenceDto(request, scope, fingerprint, orderedVersions,
            perimeterFingerprint, RuleVersion, posted.Select(x => x.Entry.JournalEntryId).Order().ToArray(), proposed,
            perimeter.Currency, ids, perimeter.OwnershipEvidence,
            source.OrderBy(x => x.JournalEntryId).ThenBy(x => x.LineId).ToArray());
        return new ConsolidationCalculation(request, book, period, perimeter, policy, source, records, posted,
            matches, proposed, evidenceDto, blockers.Distinct().ToArray());
    }

    public async Task<ManualJournalEntryDraftDto?> BuildDraftAsync(ConsolidationCalculation calculation,
        string actor, string? tenantId, string? companyId, CancellationToken ct = default)
    {
        if (calculation.Blockers.Count > 0)
            throw new InvalidOperationException(string.Join(" ", calculation.Blockers));
        if (!string.Equals(calculation.Period.Status, "Open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Elimination drafts require an open period.");
        if (calculation.ProposedLines.Count == 0)
            return null;
        var evidenceJson = JsonSerializer.Serialize(calculation.Evidence);
        var digest = Sha256Digest.ComputeUtf8(evidenceJson);
        var key = calculation.Evidence.ScopeKey + ":" + digest;
        var id = new Guid(Sha256Digest.ComputeBytesUtf8(key).AsSpan(0, 16));
        var evidenceLinks = calculation.Sources.Select(x => x.DrillThrough)
            .Concat(calculation.Perimeter.OwnershipEvidence.Select(x => $"ownership:{x.OwnershipLinkId:D}"))
            .Append($"policy:{calculation.Policy.PolicyId}/{RuleId}/{RuleVersion}")
            .Concat(calculation.Posted.Select(x => $"correction:{x.Entry.JournalEntryId:D}"))
            .Distinct().ToArray();
        var typed = await journalDrafts.BuildDraftAsync(new AccountingJournalDraftRequest(id,
            calculation.Request.PeriodId, new DateTimeOffset(calculation.Request.AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            "Consolidation elimination", calculation.ProposedLines.Select(x => new AccountingJournalDraftLineRequest(
                new LedgerAccount(x.AccountPath, x.AccountPath == ReceivableAccount ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                x.Side == AccountingTemplateLineSideDto.Debit ? x.Amount : 0m,
                x.Side == AccountingTemplateLineSideDto.Credit ? x.Amount : 0m,
                x.Description, evidenceLinks, x.Dimensions)).ToArray(),
            EffectiveDate: calculation.Request.AsOf, FundProfileId: calculation.Book.FundProfileId,
            FundStructureNodeId: calculation.Book.FundStructureNodeId, PolicyId: calculation.Policy.PolicyId,
            RuleId: RuleId, TreatmentKind: AccountingTreatmentKindDto.ConsolidationElimination,
            LedgerBookId: calculation.Book.LedgerBookId, EvidenceLinks: evidenceLinks,
            PolicyVersion: calculation.Policy.Version), ct).ConfigureAwait(false);
        if (!typed.CanSubmitForApproval || typed.CanPostWithoutAdditionalApproval || typed.Rule?.RuleId != RuleId)
            throw new InvalidOperationException("The accounting policy did not produce a reviewable elimination draft.");
        var now = DateTimeOffset.UtcNow;
        return new ManualJournalEntryDraftDto(id, ManualJournalEntryStatusDto.Draft, calculation.Book.FundProfileId,
            calculation.Book.LedgerBookId, AccountingBasisKindDto.Primary, calculation.Request.AsOf,
            calculation.Request.PeriodId.ToString("D"), null, calculation.Request.OwnershipRootId.ToString("D"),
            calculation.Perimeter.Currency, "Consolidation elimination — " + calculation.Request.AsOf,
            actor, now, now, 0, calculation.ProposedLines, evidenceLinks, [],
            TreasuryContext: new TreasuryLedgerContextDto(calculation.Request.AsOf, key),
            Dimensions: HeaderDimensions(calculation.Request.OwnershipRootId),
            RebookedFromJournalEntryId: calculation.Posted.LastOrDefault()?.Entry.JournalEntryId,
            TenantId: tenantId, CompanyId: companyId, ConsolidationEvidenceJson: evidenceJson,
            ConsolidationEvidenceDigest: digest, RequiresConsolidationEvidence: true);
    }

    public async Task ValidateEvidenceCurrentAsync(ConsolidationEvidenceDto evidence, CancellationToken ct = default)
    {
        var current = await CalculateAsync(evidence.Request, ct).ConfigureAwait(false);
        if (current.Blockers.Count > 0 || Hash(evidence) != Hash(current.Evidence))
            throw new InvalidOperationException("Consolidation authority or source evidence changed. Rerun consolidation and obtain renewed review.");
    }

    public async Task ValidateCurrentAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
    {
        var evidence = ReadEvidence(draft.ConsolidationEvidenceJson)
            ?? throw new InvalidOperationException("Consolidation evidence is missing; rerun and renew review.");
        if (!Sha256Digest.FixedEquals(draft.ConsolidationEvidenceDigest, Sha256Digest.ComputeUtf8(draft.ConsolidationEvidenceJson!)))
            throw new InvalidOperationException("Consolidation evidence has changed; rerun and renew review.");
        var current = await CalculateAsync(evidence.Request, ct).ConfigureAwait(false);
        if (current.Blockers.Count > 0 || Hash(evidence) != Hash(current.Evidence) ||
            evidence.SourceFingerprint != current.Evidence.SourceFingerprint ||
            evidence.ScopeKey != current.Evidence.ScopeKey || Hash(draft.Lines) != Hash(current.ProposedLines) ||
            Hash(evidence.ExpectedLines) != Hash(current.ProposedLines) || draft.LedgerBookId != current.Book.LedgerBookId ||
            draft.FundProfileId != current.Book.FundProfileId || draft.AccountingDate != evidence.Request.AsOf ||
            draft.PeriodId != evidence.Request.PeriodId.ToString("D") || draft.Currency != current.Perimeter.Currency ||
            draft.AccountingBasis != AccountingBasisKindDto.Primary ||
            !HasCanonicalHeader(draft, current.Evidence))
            throw new InvalidOperationException("Consolidation sources or draft changed. Rerun consolidation and obtain renewed review.");
    }

    /// <summary>Group-level provenance may name only the authoritative root; entity attribution remains on each source-backed line.</summary>
    public static bool HasCanonicalHeader(ManualJournalEntryDraftDto draft, ConsolidationEvidenceDto evidence)
        => draft.EntityId is null && draft.FundNodeId == evidence.Request.OwnershipRootId.ToString("D") &&
           draft.EntryType == ManualJournalEntryTypeDto.General &&
           Hash(draft.Dimensions) == Hash(HeaderDimensions(evidence.Request.OwnershipRootId)) &&
           draft.TreasuryContext == new TreasuryLedgerContextDto(evidence.Request.AsOf,
               evidence.ScopeKey + ":" + draft.ConsolidationEvidenceDigest);

    private static LedgerDimensionSetDto HeaderDimensions(Guid ownershipRootId)
        => new(FundId: ownershipRootId.ToString("D"));

    private Task<IReadOnlyList<LedgerJournalEntryRecord>> ReadBookAsync(Guid book, DateOnly asOf, CancellationToken ct)
        => journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book, EffectiveTo: asOf), ct);
    private static ConsolidationBookVersionDto Version(Guid book, IReadOnlyList<LedgerJournalEntryRecord> records)
        => new(book, records.Select(x => x.GlobalSequence).DefaultIfEmpty().Max(), records.Count);
    internal static DateOnly EffectiveDate(LedgerJournalEntryRecord record)
        => record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
    public static string Hash<T>(T value) => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(value));
    public static ConsolidationEvidenceDto? ReadEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        { return JsonSerializer.Deserialize<ConsolidationEvidenceDto>(json); }
        catch (JsonException) { return null; }
    }
}

public sealed record ConsolidationCalculation(ConsolidationRequestDto Request, LedgerBookRecord Book,
    LedgerAccountingPeriod Period, ConsolidationPerimeter Perimeter, AccountingPolicyDto Policy,
    IReadOnlyList<ConsolidationSourceDto> Sources, IReadOnlyList<LedgerJournalEntryRecord> SourceRecords,
    IReadOnlyList<LedgerJournalEntryRecord> Posted, IReadOnlyList<ConsolidationMatchDto> Matches,
    IReadOnlyList<ManualJournalEntryLineDto> ProposedLines, ConsolidationEvidenceDto Evidence,
    IReadOnlyList<string> Blockers);
