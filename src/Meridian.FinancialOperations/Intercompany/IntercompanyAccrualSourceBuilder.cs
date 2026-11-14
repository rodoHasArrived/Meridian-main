using System.Text;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>Outstanding principal by day, read from posted journals of the reviewed loan profile.</summary>
public sealed record IntercompanyAccrualPrincipalResult(IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> Segments,
    IntercompanyAccrualPrincipalEvidenceDto? Evidence, string? Refusal);

/// <summary>
/// Pure builders behind the governed accrual: profile authority by day, ledger principal, overlap detection
/// and the source-side draft. None of them posts, approves or mutates anything.
/// </summary>
public static class IntercompanyAccrualSourceBuilder
{
    /// <summary>
    /// The approved profile revision applicable on each day of <c>[from, to)</c>, using the same end-of-day
    /// resolution as posting. A day with no single approved revision refuses the whole window.
    /// </summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To, Guid RevisionId)> ResolveAuthority(
        IntercompanyAccountingArrangementHistoryDto history, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(history);
        var approved = history.Events.Where(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve)
            .Select(item => item.AffectedRevision.RevisionId).Distinct().ToArray();
        var segments = new List<(DateOnly From, DateOnly To, Guid RevisionId)>();
        for (var day = from; day < to; day = day.AddDays(1))
        {
            var instant = new DateTimeOffset(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            var effective = approved.Where(revision =>
            {
                try
                { IntercompanyAccountingArrangementRevisionResolver.ResolveApprovedRevision(history, revision, instant); return true; }
                catch (InvalidOperationException) { return false; }
            }).ToArray();
            if (effective.Length != 1)
                throw new InvalidOperationException($"No single approved loan-interest profile revision applies on {day:yyyy-MM-dd}.");
            if (segments.Count > 0 && segments[^1].RevisionId == effective[0])
                segments[^1] = (segments[^1].From, day.AddDays(1), effective[0]);
            else
                segments.Add((day, day.AddDays(1), effective[0]));
        }
        return segments;
    }

    /// <summary>
    /// A movement dated <c>d</c> changes the balance from <c>d</c> inclusive: a drawdown earns interest on its
    /// own date, a repayment stops earning on its own date.
    /// </summary>
    public static IntercompanyAccrualPrincipalResult BuildPrincipal(IReadOnlyList<LedgerJournalEntryRecord> records,
        Guid principalArrangementId, Guid sourceBookId, IReadOnlySet<string> receivableNodes, IReadOnlySet<string> payableNodes,
        string agreementCurrency, string functionalCurrency, DateOnly from, DateOnly to)
    {
        static IntercompanyAccrualPrincipalResult Refuse(string reason) => new([], null, reason);
        var arrangement = principalArrangementId.ToString("D");
        var receivable = new List<IntercompanyAccrualPrincipalMovementDto>();
        var payable = new List<IntercompanyAccrualPrincipalMovementDto>();
        foreach (var record in records)
        {
            var date = IntercompanyJournalFamily.EffectiveDate(record);
            if (date >= to)
                continue;
            decimal owed = 0m, owing = 0m;
            foreach (var line in record.Entry.Lines)
            {
                if (!string.Equals(line.Dimensions?.AccountingArrangementId, arrangement, StringComparison.OrdinalIgnoreCase)
                    || line.Dimensions!.ChartAccountNodeId is not { } node)
                    continue;
                var isReceivable = receivableNodes.Contains(node);
                if (!isReceivable && !payableNodes.Contains(node))
                    continue;
                if (!string.Equals(line.Currency?.TransactionCurrency ?? functionalCurrency, agreementCurrency, StringComparison.Ordinal))
                    return Refuse("Posted principal must retain agreement-currency amounts.");
                var signed = (line.Currency?.TransactionDebit ?? line.Debit) - (line.Currency?.TransactionCredit ?? line.Credit);
                if (isReceivable)
                    owed += signed;
                else
                    owing -= signed;
            }
            if (owed != 0m)
                receivable.Add(new(record.Entry.JournalEntryId, date, owed));
            if (owing != 0m)
                payable.Add(new(record.Entry.JournalEntryId, date, owing));
        }
        if (receivable.Count > 0 && payable.Count > 0)
            return Refuse("This book retains both receivable and payable principal for the loan profile; its lending direction is ambiguous.");
        if (receivable.Count == 0 && payable.Count == 0)
            return Refuse("No posted principal exists for the loan profile in this book before the accrual end.");
        var role = receivable.Count > 0 ? IntercompanyAccountRoleDto.Receivable : IntercompanyAccountRoleDto.Payable;
        var movements = (receivable.Count > 0 ? receivable : payable)
            .OrderBy(item => item.EffectiveDate).ThenBy(item => item.JournalEntryId).ToArray();
        var opening = movements.Where(item => item.EffectiveDate < from).ToArray();
        var inWindow = movements.Where(item => item.EffectiveDate >= from).ToArray();
        var boundaries = inWindow.Select(item => item.EffectiveDate).Where(date => date > from)
            .Append(from).Append(to).Distinct().Order().ToArray();
        var segments = new List<IntercompanyAccrualPrincipalSegmentDto>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var balance = movements.Where(item => item.EffectiveDate <= start).Sum(item => item.SignedAmount);
            if (balance < 0m)
                return Refuse($"Posted principal is negative on {start:yyyy-MM-dd}; resolve the principal balance before accruing interest.");
            segments.Add(new(start, boundaries[index + 1], balance,
                $"ledger-principal:{sourceBookId:D}:{arrangement}:{start:yyyy-MM-dd}"));
        }
        var openingDigest = Sha256Digest.ComputeUtf8(string.Join("\n", opening.Select(item =>
            FormattableString.Invariant($"{item.JournalEntryId:D}:{item.EffectiveDate:yyyy-MM-dd}:{item.SignedAmount}"))));
        return new(segments, new(principalArrangementId, role, opening.Sum(item => item.SignedAmount),
            opening.Length, openingDigest, inWindow), null);
    }

    /// <summary>
    /// A posted accrual whose window intersects <c>[from, to)</c> and still carries a net balance. A fully
    /// reversed accrual nets to zero and no longer blocks a recomputation.
    /// </summary>
    public static string? FindPostedOverlap(IReadOnlyList<LedgerJournalEntryRecord> records, Guid interestArrangementId,
        IReadOnlySet<string> monetaryNodes, DateOnly from, DateOnly to)
    {
        var arrangement = interestArrangementId.ToString("D");
        var net = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var line in records.SelectMany(record => record.Entry.Lines))
        {
            var dimensions = line.Dimensions;
            if (!string.Equals(dimensions?.AccountingArrangementId, arrangement, StringComparison.OrdinalIgnoreCase)
                || dimensions!.ChartAccountNodeId is not { } node || !monetaryNodes.Contains(node)
                || !IntercompanyAccrualEvidenceGuard.TryParseWindow(dimensions.IntercompanyTransactionId, out _, out _))
                continue;
            net[dimensions.IntercompanyTransactionId!] = net.GetValueOrDefault(dimensions.IntercompanyTransactionId!)
                + (line.Currency?.TransactionDebit ?? line.Debit) - (line.Currency?.TransactionCredit ?? line.Credit);
        }
        foreach (var (transaction, amount) in net.OrderBy(item => item.Key, StringComparer.Ordinal))
            if (amount != 0m && IntercompanyAccrualEvidenceGuard.TryParseWindow(transaction, out var postedFrom, out var postedTo)
                && postedFrom < to && from < postedTo)
                return transaction;
        return null;
    }

    /// <summary>One window of one profile in one book has one draft identity; a recomputation updates it.</summary>
    public static Guid DraftId(string tenantId, IntercompanyAccrualRequestDto request)
        => new(Sha256Digest.ComputeBytesUtf8(string.Join(":", IntercompanyAccrualRules.RuleVersion, tenantId,
            request.InterestArrangementId.ToString("D"), request.SourceBookId.ToString("D"),
            request.AccrualFrom.ToString("yyyyMMdd"), request.AccrualTo.ToString("yyyyMMdd"))).AsSpan(0, 16));

    /// <summary>
    /// The source-side draft: the lender debits interest receivable and credits interest income; the borrower
    /// debits interest expense and credits interest payable. It is a draft for the source book's ordinary
    /// review. Its counterpart is produced only by the existing proposal pipeline after the source posts.
    /// </summary>
    public static IntercompanyAccrualProposalDto BuildDraft(IntercompanyAccrualRequestDto request, string tenantId,
        string? companyId, string actor, LedgerBookRecord book, LedgerAccountingPeriod period,
        ResolvedIntercompanyAccountingArrangementDto posting, IntercompanyAccrualPreviewDto computation,
        IntercompanyAccrualPrincipalEvidenceDto principal, IReadOnlyList<IntercompanyAccountBindingDto> bindings,
        IReadOnlyList<ChartOfAccountsNodeDto> chart, ConsolidationFxAmountDto? fx, DateTimeOffset now)
    {
        if (computation is not { IsReady: true, Amount: { } amount, Currency: { } currency })
            throw new InvalidOperationException("Only a ready computation can become an accrual draft.");
        if (posting.TemplateKind != IntercompanyAccountingTemplateKindDto.LoanInterest
            || posting.ArrangementId != request.InterestArrangementId || posting.Currency != currency
            || posting.CanonicalAgreementId != computation.AgreementId)
            throw new InvalidOperationException("The accrual requires the exact reviewed LoanInterest profile of the computed agreement.");
        var postingDate = request.AccrualTo.AddDays(-1);
        var last = computation.Slices[^1];
        if (last.AccountingArrangementRevisionId != posting.RevisionId || last.AgreementVersion != posting.CanonicalAgreementVersion
            || last.AgreementPayloadHash != posting.CanonicalPayloadHash)
            throw new InvalidOperationException("The posting profile revision must be the one applicable on the last accrued day.");
        var entity = book.FundStructureNodeId;
        var mapping = posting.PartyMappings.SingleOrDefault(item => item.EntityId == entity && item.LedgerBookId == book.LedgerBookId)
            ?? throw new InvalidOperationException("The source book has no approved loan-interest mapping.");
        var counterparty = posting.PartyOneEntityId == entity ? posting.PartyTwoEntityId
            : posting.PartyTwoEntityId == entity ? posting.PartyOneEntityId
            : throw new InvalidOperationException("The source book's entity is not a party to the agreement.");
        if (period.LedgerBookId != book.LedgerBookId || period.Status != "Open"
            || postingDate < period.StartDate || postingDate > period.EndDate)
            throw new InvalidOperationException("An open source period containing the last accrued day is required; no date is moved automatically.");
        var lender = principal.Role == IntercompanyAccountRoleDto.Receivable;
        var mainNode = lender ? mapping.ReceivableAccountNodeId : mapping.PayableAccountNodeId;
        var contraNode = lender ? mapping.RevenueAccountNodeId : mapping.ExpenseAccountNodeId;
        if (string.IsNullOrWhiteSpace(contraNode))
            throw new InvalidOperationException(lender
                ? "The loan-interest profile needs an interest-income node for the lending book."
                : "The loan-interest profile needs an interest-expense node for the borrowing book.");
        decimal functional;
        decimal rate;
        if (currency == book.BaseCurrency)
            (functional, rate, fx) = (amount, 1m, null);
        else if (fx is not null && fx.Currency == book.BaseCurrency && fx.Amount > 0m)
            (functional, rate) = (fx.Amount, fx.Rate);
        else
            throw new InvalidOperationException("A cross-currency accrual requires an approved posting-date conversion into the source book's functional currency.");
        ChartOfAccountsNodeDto Account(string id) => chart.SingleOrDefault(item => item.NodeId == id && !item.IsArchived)
            ?? throw new InvalidOperationException("A loan-interest template account is missing or archived.");
        IntercompanyAccountBindingDto Binding(string node, IntercompanyAccountRoleDto role) =>
            bindings.SingleOrDefault(item => item.ChartAccountNodeId == node && item.Role == role && !item.IsArchived)
            ?? throw new InvalidOperationException("An exact source account-role binding is required.");
        var main = Account(mainNode);
        var contra = Account(contraNode);
        var mainBinding = Binding(mainNode, principal.Role);
        var contraBinding = contra.IntercompanyRole is { } contraRole ? Binding(contra.NodeId, contraRole) : null;
        var transaction = IntercompanyAccrualEvidenceGuard.TransactionId(request.AccrualFrom, request.AccrualTo);
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
        var mainSide = lender ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit;
        var contraSide = lender ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit;
        var lines = new[]
        {
            new ManualJournalEntryLineDto("intercompany", mainSide, functional, book.BaseCurrency, main.Path,
                EntityId: entity.ToString("D"), Dimensions: Dimensions(main, mainBinding)) { TransactionCurrency = original },
            new ManualJournalEntryLineDto("interest", contraSide, functional, book.BaseCurrency, contra.Path,
                EntityId: entity.ToString("D"), Dimensions: Dimensions(contra, contraBinding)) { TransactionCurrency = original }
        };
        var draftId = DraftId(tenantId, request);
        var evidence = new IntercompanyAccrualEvidenceDto(IntercompanyAccrualRules.RuleVersion, draftId, tenantId,
            book.LedgerBookId, entity, counterparty, posting.ArrangementId, posting.RevisionId,
            posting.CanonicalAgreementId, posting.CanonicalAgreementVersion, posting.CanonicalPayloadHash,
            transaction, postingDate, principal.Role, book.BaseCurrency, functional, computation, principal, fx);
        var evidenceJson = IntercompanyAccrualEvidenceGuard.Serialize(evidence);
        var digest = IntercompanyAccrualEvidenceGuard.Digest(evidenceJson);
        var versions = string.Join(", ", computation.Slices.Select(slice => slice.AgreementVersion).Distinct().Order()
            .Select(version => "v" + version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var memo = new StringBuilder("Loan interest accrual ").Append(request.AccrualFrom.ToString("yyyy-MM-dd"))
            .Append(" to ").Append(postingDate.ToString("yyyy-MM-dd")).Append(" under agreement ").Append(versions).ToString();
        var links = new List<string>
        {
            $"accounting-arrangement:{posting.ArrangementId:D}/revision:{posting.RevisionId:D}",
            $"intercompany-agreement:{posting.CanonicalAgreementId:D}/version:{posting.CanonicalAgreementVersion}",
            $"accounting-arrangement:{principal.PrincipalArrangementId:D}/principal",
            $"intercompany-accrual-evidence:sha256:{digest}"
        };
        var draft = new ManualJournalEntryDraftDto(draftId, ManualJournalEntryStatusDto.Draft, book.FundProfileId,
            book.LedgerBookId, book.AccountingBasis, postingDate, period.PeriodId.ToString("D"), entity.ToString("D"), null,
            book.BaseCurrency, memo, actor, now, now, 1, lines, links, [],
            TreasuryContext: new(postingDate, $"intercompany-accrual:{draftId:D}", FundEventType: "IntercompanyAccrual"),
            TenantId: tenantId, CompanyId: companyId)
        {
            IntercompanyAccrualEvidenceJson = evidenceJson,
            IntercompanyAccrualEvidenceDigest = digest,
            IntercompanyFx = fx
        };
        return new(computation, computation.Diagnostics, evidence, digest, draft);
    }
}
