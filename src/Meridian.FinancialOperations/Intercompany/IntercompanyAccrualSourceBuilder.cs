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
        IntercompanyAccountingArrangementHistoryDto history, DateOnly from, DateOnly to, string profileLabel = "loan-interest")
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
                throw new InvalidOperationException($"No single approved {profileLabel} profile revision applies on {day:yyyy-MM-dd}.");
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
                FormattableString.Invariant($"ledger-principal:{sourceBookId:D}:{arrangement}:{start:yyyy-MM-dd}")));
        }
        var openingDigest = Sha256Digest.ComputeUtf8(string.Join("\n", opening.Select(item =>
            FormattableString.Invariant($"{item.JournalEntryId:D}:{item.EffectiveDate:yyyy-MM-dd}:{item.SignedAmount}"))));
        return new(segments, new(principalArrangementId, role, receivableNodes.Order(StringComparer.Ordinal).ToArray(),
            payableNodes.Order(StringComparer.Ordinal).ToArray(), opening.Sum(item => item.SignedAmount),
            opening.Length, openingDigest, inWindow), null);
    }

    /// <summary>
    /// A posted accrual of the profile whose window intersects <c>[from, to)</c> and has not been reversed.
    /// Reversal is proved by correction lineage: a source-linked exact inverse of that journal. A settled
    /// receivable or payable nets to zero too, but its interest income stays posted, so balances prove nothing.
    /// </summary>
    public static string? FindPostedOverlap(IReadOnlyList<LedgerJournalEntryRecord> records, Guid interestArrangementId,
        DateOnly from, DateOnly to)
    {
        foreach (var (record, transaction) in PostedAccruals(records, interestArrangementId)
            .OrderBy(item => item.Transaction, StringComparer.Ordinal))
        {
            // An inverse is not itself an accrual; it only decides whether the journal it reverses is still live.
            if (IsInverse(record, records) || IsReversed(record, records, 0) || IsReversedCounterpart(record, records))
                continue;
            if (IntercompanyAccrualEvidenceGuard.TryParseWindow(transaction, out var postedFrom, out var postedTo)
                && postedFrom < to && from < postedTo)
                return transaction;
        }
        return null;
    }

    /// <summary>
    /// How many accrual journals, originals and their corrections, are already posted for exactly this window.
    /// It only grows when a journal posts, so it is stable across retries and gives a recomputation after a
    /// reversal its own identity while the immutable original keeps its own.
    /// </summary>
    public static int PostedGeneration(IReadOnlyList<LedgerJournalEntryRecord> records, Guid interestArrangementId,
        DateOnly from, DateOnly to)
    {
        return PostedAccruals(records, interestArrangementId).Count(item =>
            IntercompanyAccrualEvidenceGuard.TryParseWindow(item.Transaction, out var postedFrom, out var postedTo)
            && postedFrom == from && postedTo == to);
    }

    /// <summary>
    /// Journals that recognize interest for a window in this book: those the workbench posted from an accrual
    /// draft (evidence tag) and the counterparts it posted from a proposal draft (proposal origin tag), so the
    /// other entity cannot accrue the same days again from its own book. A manual settlement carries neither.
    /// </summary>
    private static IEnumerable<(LedgerJournalEntryRecord Record, string Transaction)> PostedAccruals(
        IReadOnlyList<LedgerJournalEntryRecord> records, Guid interestArrangementId)
    {
        var arrangement = interestArrangementId.ToString("D");
        foreach (var record in records)
        {
            var tags = record.Entry.Metadata.Tags;
            if (string.IsNullOrWhiteSpace(tags?.GetValueOrDefault(IntercompanyAccrualEvidenceGuard.DigestTag))
                && string.IsNullOrWhiteSpace(tags?.GetValueOrDefault(IntercompanyAccrualEvidenceGuard.CounterpartOriginTag)))
                continue;
            var transaction = record.Entry.Lines.Select(line => line.Dimensions)
                .Where(dimensions => string.Equals(dimensions?.AccountingArrangementId, arrangement, StringComparison.OrdinalIgnoreCase))
                .Select(dimensions => dimensions!.IntercompanyTransactionId)
                .FirstOrDefault(id => IntercompanyAccrualEvidenceGuard.TryParseWindow(id, out _, out _));
            if (transaction is not null)
                yield return (record, transaction);
        }
    }

    /// <summary>
    /// A counterpart reversal is delivered as its own proposal draft, not as a source-linked correction, so a
    /// counterpart and the proposal-originated exact inverse of it cancel as a pair.
    /// </summary>
    internal static bool IsReversedCounterpart(LedgerJournalEntryRecord record, IReadOnlyList<LedgerJournalEntryRecord> records)
    {
        static bool FromProposal(LedgerJournalEntryRecord item)
            => !string.IsNullOrWhiteSpace(item.Entry.Metadata.Tags?.GetValueOrDefault(IntercompanyAccrualEvidenceGuard.CounterpartOriginTag));
        return FromProposal(record) && records.Any(candidate => !ReferenceEquals(candidate, record) && FromProposal(candidate)
            && candidate.Entry.JournalEntryId != record.Entry.JournalEntryId
            && IntercompanyJournalCorrectionEvidence.IsExactInverse(candidate.Entry, record.Entry));
    }

    /// <summary>
    /// A journal is reversed only while one of its source-linked exact inverses is itself still standing.
    /// Reversing that reversal restores the original, so the correction chain is followed to its end.
    /// </summary>
    internal static bool IsReversed(LedgerJournalEntryRecord record, IReadOnlyList<LedgerJournalEntryRecord> records, int depth)
        => depth < 64 && records.Any(candidate => candidate.SourceJournalEntryId == record.Entry.JournalEntryId
            && IntercompanyJournalCorrectionEvidence.IsExactInverse(candidate.Entry, record.Entry)
            && !IsReversed(candidate, records, depth + 1));

    internal static bool IsInverse(LedgerJournalEntryRecord record, IReadOnlyList<LedgerJournalEntryRecord> records)
        => record.SourceJournalEntryId is { } source
            && records.FirstOrDefault(candidate => candidate.Entry.JournalEntryId == source) is { } original
            && IntercompanyJournalCorrectionEvidence.IsExactInverse(record.Entry, original.Entry);

    /// <summary>One window of one profile in one book has one live draft identity; a recomputation updates it.</summary>
    public static Guid DraftId(string tenantId, IntercompanyAccrualRequestDto request, int generation = 0)
        => new(Sha256Digest.ComputeBytesUtf8(string.Join(":", IntercompanyAccrualRules.RuleVersion, tenantId,
            request.InterestArrangementId.ToString("D"), request.SourceBookId.ToString("D"),
            request.AccrualFrom.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
            request.AccrualTo.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture))
            + (generation == 0 ? string.Empty : FormattableString.Invariant($":g{generation}"))).AsSpan(0, 16));

    /// <summary>
    /// Null when posted principal still reads exactly as retained; otherwise why the accrual must be recomputed.
    /// A backdated repayment or correction after drafting changes the interest the window should carry.
    /// </summary>
    public static string? VerifyPrincipal(IntercompanyAccrualEvidenceDto evidence, IReadOnlyList<LedgerJournalEntryRecord> principalJournals)
    {
        var retained = evidence.Principal;
        var current = BuildPrincipal(principalJournals, retained.PrincipalArrangementId, evidence.SourceBookId,
            retained.ReceivableAccountNodeIds.ToHashSet(StringComparer.Ordinal),
            retained.PayableAccountNodeIds.ToHashSet(StringComparer.Ordinal), evidence.Computation.Currency!,
            evidence.FunctionalCurrency, evidence.Computation.AccrualFrom, evidence.Computation.AccrualTo);
        return current.Evidence is not null && IntercompanyAccrualEvidenceGuard.Serialize(current.Evidence)
            == IntercompanyAccrualEvidenceGuard.Serialize(retained) ? null
            : "Posted principal changed after this accrual was computed. Recompute the accrual before review or posting.";
    }

    /// <summary>
    /// The source-side draft: the lender debits interest receivable and credits interest income; the borrower
    /// debits interest expense and credits interest payable. It is a draft for the source book's ordinary
    /// review. Its counterpart is produced only by the existing proposal pipeline after the source posts.
    /// </summary>
    public static IntercompanyAccrualProposalDto BuildDraft(IntercompanyAccrualRequestDto request, string tenantId,
        string? companyId, string actor, LedgerBookRecord book, LedgerAccountingPeriod period,
        ResolvedIntercompanyAccountingArrangementDto posting, IntercompanyAccrualPreviewDto computation,
        IntercompanyAccrualPrincipalEvidenceDto principal, IReadOnlyList<IntercompanyAccountBindingDto> bindings,
        IReadOnlyList<ChartOfAccountsNodeDto> chart, ConsolidationFxAmountDto? fx, DateTimeOffset now, int generation = 0)
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
        var transaction = IntercompanyAccrualEvidenceGuard.TransactionId(request.AccrualFrom, request.AccrualTo, generation);
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
        var draftId = DraftId(tenantId, request, generation);
        var evidence = new IntercompanyAccrualEvidenceDto(IntercompanyAccrualRules.RuleVersion, draftId, tenantId,
            book.LedgerBookId, entity, counterparty, posting.ArrangementId, posting.RevisionId,
            posting.CanonicalAgreementId, posting.CanonicalAgreementVersion, posting.CanonicalPayloadHash,
            transaction, postingDate, principal.Role, main.NodeId, contra.NodeId, book.BaseCurrency, functional, computation, principal, fx);
        var evidenceJson = IntercompanyAccrualEvidenceGuard.Serialize(evidence);
        var digest = IntercompanyAccrualEvidenceGuard.Digest(evidenceJson);
        var versions = string.Join(", ", computation.Slices.Select(slice => slice.AgreementVersion).Distinct().Order()
            .Select(version => "v" + version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var memo = new StringBuilder("Loan interest accrual ").Append(request.AccrualFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" to ").Append(postingDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append(" under agreement ").Append(versions).ToString();
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
            // No fund-event fields: an accrual is not a fund event, and stating a fund-event type would make
            // approval submission demand a fund event id and capital account the workflow does not have.
            TreasuryContext: new(postingDate, $"intercompany-accrual:{draftId:D}"),
            TenantId: tenantId, CompanyId: companyId)
        {
            IntercompanyAccrualEvidenceJson = evidenceJson,
            IntercompanyAccrualEvidenceDigest = digest,
            IntercompanyFx = fx
        };
        return new(computation, computation.Diagnostics, evidence, digest, draft);
    }
}
