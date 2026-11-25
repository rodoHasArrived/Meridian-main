using System.Globalization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>
/// Pure builders behind the governed fixed management-fee accrual: overlap detection and the source-side draft.
/// Direction comes from the agreement's declared roles (service provider or recipient); nothing posts or approves.
/// </summary>
public static class IntercompanyFeeAccrualSourceBuilder
{
    public const string ProviderRole = "Service provider";

    /// <summary>
    /// A posted fee of the profile whose window intersects <c>[from, to)</c> and has not been reversed, by the same
    /// correction lineage as interest: a source-linked exact inverse, or a reversed delivered counterpart.
    /// </summary>
    public static string? FindPostedOverlap(IReadOnlyList<LedgerJournalEntryRecord> records, Guid feeArrangementId, DateOnly from, DateOnly to)
    {
        foreach (var (record, transaction) in PostedFees(records, feeArrangementId).OrderBy(item => item.Transaction, StringComparer.Ordinal))
        {
            if (IntercompanyAccrualSourceBuilder.IsInverse(record, records) || IntercompanyAccrualSourceBuilder.IsReversed(record, records, 0)
                || IntercompanyAccrualSourceBuilder.IsReversedCounterpart(record, records))
                continue;
            if (IntercompanyFeeAccrualEvidenceGuard.TryParseWindow(transaction, out var postedFrom, out var postedTo)
                && postedFrom < to && from < postedTo)
                return transaction;
        }
        return null;
    }

    /// <summary>How many fee journals are already posted for exactly this window; a recomputation after a reversal gets its own identity.</summary>
    public static int PostedGeneration(IReadOnlyList<LedgerJournalEntryRecord> records, Guid feeArrangementId, DateOnly from, DateOnly to)
        => PostedFees(records, feeArrangementId).Count(item =>
            IntercompanyFeeAccrualEvidenceGuard.TryParseWindow(item.Transaction, out var postedFrom, out var postedTo)
            && postedFrom == from && postedTo == to);

    private static IEnumerable<(LedgerJournalEntryRecord Record, string Transaction)> PostedFees(
        IReadOnlyList<LedgerJournalEntryRecord> records, Guid feeArrangementId)
    {
        var arrangement = feeArrangementId.ToString("D");
        foreach (var record in records)
        {
            var tags = record.Entry.Metadata.Tags;
            if (string.IsNullOrWhiteSpace(tags?.GetValueOrDefault(IntercompanyAccrualEvidenceGuard.DigestTag))
                && string.IsNullOrWhiteSpace(tags?.GetValueOrDefault(IntercompanyAccrualEvidenceGuard.CounterpartOriginTag)))
                continue;
            var transaction = record.Entry.Lines.Select(line => line.Dimensions)
                .Where(dimensions => string.Equals(dimensions?.AccountingArrangementId, arrangement, StringComparison.OrdinalIgnoreCase))
                .Select(dimensions => dimensions!.IntercompanyTransactionId)
                .FirstOrDefault(id => IntercompanyFeeAccrualEvidenceGuard.TryParseWindow(id, out _, out _));
            if (transaction is not null)
                yield return (record, transaction);
        }
    }

    /// <summary>One window of one fee profile in one book has one live draft identity; a recomputation updates it.</summary>
    public static Guid DraftId(string tenantId, IntercompanyFeeAccrualRequestDto request, int generation = 0)
        => new(Sha256Digest.ComputeBytesUtf8(string.Join(":", IntercompanyFeeAccrualRules.RuleVersion, tenantId,
            request.FeeArrangementId.ToString("D"), request.SourceBookId.ToString("D"),
            request.AccrualFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            request.AccrualTo.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            + (generation == 0 ? string.Empty : FormattableString.Invariant($":g{generation}"))).AsSpan(0, 16));

    /// <summary>True when the source entity holds the declared service-provider role of the agreement.</summary>
    public static bool IsProvider(IntercompanyAgreementDraftDto terms, Guid entity)
    {
        static bool Provider(string role) => string.Equals(role.Replace(" ", string.Empty), ProviderRole.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
        return entity == terms.PartyOneId ? Provider(terms.PartyOneRole)
            : entity == terms.PartyTwoId ? Provider(terms.PartyTwoRole)
            : throw new InvalidOperationException("The source book's entity is not a party to the agreement.");
    }

    /// <summary>
    /// The source-side draft: the provider debits its fee receivable and credits fee revenue; the recipient debits
    /// fee expense and credits its fee payable. Its counterpart arrives only through the proposal pipeline after posting.
    /// </summary>
    public static IntercompanyFeeAccrualProposalDto BuildDraft(IntercompanyFeeAccrualRequestDto request, string tenantId,
        string? companyId, string actor, LedgerBookRecord book, LedgerAccountingPeriod period,
        ResolvedIntercompanyAccountingArrangementDto posting, IntercompanyFeeAccrualPreviewDto computation,
        IReadOnlyList<IntercompanyAccountBindingDto> bindings, IReadOnlyList<ChartOfAccountsNodeDto> chart,
        ConsolidationFxAmountDto? fx, DateTimeOffset now, int generation = 0)
    {
        if (computation is not { IsReady: true, Amount: { } amount, Currency: { } currency })
            throw new InvalidOperationException("Only a ready computation can become a fee draft.");
        if (posting.TemplateKind != IntercompanyAccountingTemplateKindDto.ManagementFee
            || posting.ArrangementId != request.FeeArrangementId || posting.Currency != currency
            || posting.CanonicalAgreementId != computation.AgreementId)
            throw new InvalidOperationException("The fee requires the exact reviewed ManagementFee profile of the computed agreement.");
        var postingDate = request.AccrualTo.AddDays(-1);
        var last = computation.Slices[^1];
        if (last.AccountingArrangementRevisionId != posting.RevisionId || last.AgreementVersion != posting.CanonicalAgreementVersion
            || last.AgreementPayloadHash != posting.CanonicalPayloadHash)
            throw new InvalidOperationException("The posting profile revision must be the one applicable on the last accrued day.");
        var entity = book.FundStructureNodeId;
        var mapping = posting.PartyMappings.SingleOrDefault(item => item.EntityId == entity && item.LedgerBookId == book.LedgerBookId)
            ?? throw new InvalidOperationException("The source book has no approved management-fee mapping.");
        var counterparty = posting.PartyOneEntityId == entity ? posting.PartyTwoEntityId
            : posting.PartyTwoEntityId == entity ? posting.PartyOneEntityId
            : throw new InvalidOperationException("The source book's entity is not a party to the agreement.");
        if (period.LedgerBookId != book.LedgerBookId || period.Status != "Open"
            || postingDate < period.StartDate || postingDate > period.EndDate)
            throw new InvalidOperationException("An open source period containing the last accrued day is required; no date is moved automatically.");
        var provider = IsProvider(posting.LegalRevision.Terms, entity);
        var role = provider ? IntercompanyAccountRoleDto.Receivable : IntercompanyAccountRoleDto.Payable;
        var mainNode = provider ? mapping.ReceivableAccountNodeId : mapping.PayableAccountNodeId;
        var contraNode = provider ? mapping.RevenueAccountNodeId : mapping.ExpenseAccountNodeId;
        if (string.IsNullOrWhiteSpace(contraNode))
            throw new InvalidOperationException(provider
                ? "The management-fee profile needs a fee-revenue node for the service provider's book."
                : "The management-fee profile needs a fee-expense node for the recipient's book.");
        decimal functional;
        decimal rate;
        if (currency == book.BaseCurrency)
            (functional, rate, fx) = (amount, 1m, null);
        else if (fx is not null && fx.Currency == book.BaseCurrency && fx.Amount > 0m)
            (functional, rate) = (fx.Amount, fx.Rate);
        else
            throw new InvalidOperationException("A cross-currency fee requires an approved posting-date conversion into the source book's functional currency.");
        ChartOfAccountsNodeDto Account(string id) => chart.SingleOrDefault(item => item.NodeId == id && !item.IsArchived)
            ?? throw new InvalidOperationException("A management-fee template account is missing or archived.");
        IntercompanyAccountBindingDto Binding(string node, IntercompanyAccountRoleDto bindingRole) =>
            bindings.SingleOrDefault(item => item.ChartAccountNodeId == node && item.Role == bindingRole && !item.IsArchived)
            ?? throw new InvalidOperationException("An exact source account-role binding is required.");
        var main = Account(mainNode);
        var contra = Account(contraNode);
        var mainBinding = Binding(mainNode, role);
        var contraBinding = contra.IntercompanyRole is { } contraRole ? Binding(contra.NodeId, contraRole) : null;
        var transaction = IntercompanyFeeAccrualEvidenceGuard.TransactionId(request.AccrualFrom, request.AccrualTo, generation);
        LedgerDimensionSetDto Dimensions(ChartOfAccountsNodeDto account, IntercompanyAccountBindingDto? binding) =>
            new(EntityId: entity.ToString("D"), CounterpartyId: counterparty.ToString("D"),
                FundId: book.FundProfileId, BookId: book.LedgerBookId.ToString("D"))
            {
                ChartAccountNodeId = account.NodeId,
                IntercompanyAgreementId = posting.CanonicalAgreementId,
                IntercompanyAgreementVersion = posting.CanonicalAgreementVersion,
                AccountingArrangementId = binding is null ? null : posting.ArrangementId.ToString("D"),
                AccountingArrangementRevisionId = binding is null ? null : posting.RevisionId.ToString("D"),
                AccountBindingRevisionId = binding?.RevisionId.ToString("D"),
                IntercompanyTransactionId = binding is null ? null : transaction
            };
        var original = new ManualJournalLineCurrencyDto(currency, amount, rate);
        var mainSide = provider ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit;
        var contraSide = provider ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit;
        var lines = new[]
        {
            new ManualJournalEntryLineDto("intercompany", mainSide, functional, book.BaseCurrency, main.Path,
                EntityId: entity.ToString("D"), Dimensions: Dimensions(main, mainBinding)) { TransactionCurrency = original },
            new ManualJournalEntryLineDto("fee", contraSide, functional, book.BaseCurrency, contra.Path,
                EntityId: entity.ToString("D"), Dimensions: Dimensions(contra, contraBinding)) { TransactionCurrency = original }
        };
        var draftId = DraftId(tenantId, request, generation);
        var evidence = new IntercompanyFeeAccrualEvidenceDto(computation.RuleVersion, draftId, tenantId,
            book.LedgerBookId, entity, counterparty, posting.ArrangementId, posting.RevisionId,
            posting.CanonicalAgreementId, posting.CanonicalAgreementVersion, posting.CanonicalPayloadHash,
            transaction, postingDate, role, main.NodeId, contra.NodeId, book.BaseCurrency, functional, computation, fx);
        var evidenceJson = IntercompanyFeeAccrualEvidenceGuard.Serialize(evidence);
        var digest = IntercompanyAccrualEvidenceGuard.Digest(evidenceJson);
        var versions = string.Join(", ", computation.Slices.Select(slice => slice.AgreementVersion).Distinct().Order()
            .Select(version => "v" + version.ToString(CultureInfo.InvariantCulture)));
        var memo = $"Management fee accrual {request.AccrualFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to "
            + $"{postingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} under agreement {versions}";
        var links = new List<string>
        {
            $"accounting-arrangement:{posting.ArrangementId:D}/revision:{posting.RevisionId:D}",
            $"intercompany-agreement:{posting.CanonicalAgreementId:D}/version:{posting.CanonicalAgreementVersion}",
            $"intercompany-fee-evidence:sha256:{digest}"
        };
        var draft = new ManualJournalEntryDraftDto(draftId, ManualJournalEntryStatusDto.Draft, book.FundProfileId,
            book.LedgerBookId, book.AccountingBasis, postingDate, period.PeriodId.ToString("D"), entity.ToString("D"), null,
            book.BaseCurrency, memo, actor, now, now, 1, lines, links, [],
            // No fund-event fields: an intercompany fee accrual is not a fund event.
            TreasuryContext: new(postingDate, $"intercompany-fee:{draftId:D}"),
            TenantId: tenantId, CompanyId: companyId)
        {
            IntercompanyAccrualEvidenceJson = evidenceJson,
            IntercompanyAccrualEvidenceDigest = digest,
            IntercompanyFx = fx
        };
        return new(computation, computation.Diagnostics, evidence, digest, draft);
    }
}
