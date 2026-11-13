using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>Creates a balanced proposal from retained source facts, never a posting instruction.</summary>
public static class IntercompanyProposalDraftBuilder
{
    public static ManualJournalEntryDraftDto Build(IntercompanyProposalDto proposal,
        ResolvedIntercompanyAccountingArrangementDto agreement, LedgerBookRecord target, LedgerAccountingPeriod period,
        IReadOnlyList<IntercompanyAccountBindingDto> sourceBindings,
        IReadOnlyList<IntercompanyAccountBindingDto> targetBindings,
        IReadOnlyList<ChartOfAccountsNodeDto> chart, ConsolidationFxAmountDto? fx, DateTimeOffset now,
        IReadOnlyList<ChartOfAccountsNodeDto>? sourceChart = null)
    {
        var source = proposal.Source;
        if (source.RuleVersion != "intercompany-mirror-v1")
            throw new InvalidOperationException("The source mirror rule version is unsupported.");
        if (source.RequiresSourceAllocation)
            throw new InvalidOperationException("The source has multiple intercompany event groups and unattributed contra legs. Split the source events or explicitly attribute their operating legs before proposing counterparts.");
        if (agreement.TemplateKind == IntercompanyAccountingTemplateKindDto.Guarantee)
            throw new InvalidOperationException("Guarantee agreements have no supported mirror template.");
        if (agreement.ArrangementId != source.AccountingArrangementId || agreement.RevisionId != source.AccountingArrangementRevisionId
            || agreement.CanonicalAgreementId != source.IntercompanyAgreementId
            || agreement.CanonicalAgreementVersion != source.IntercompanyAgreementVersion
            || agreement.CanonicalPayloadHash != source.IntercompanyAgreementPayloadHash)
            throw new InvalidOperationException("The exact approved source agreement revision is required.");
        var sourceMap = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == source.SourceEntityId && x.LedgerBookId == source.SourceBookId)
            ?? throw new InvalidOperationException("The source book has no approved mapping.");
        var targetMap = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == source.CounterpartyEntityId && x.LedgerBookId == target.LedgerBookId)
            ?? throw new InvalidOperationException("The counterpart book has no unambiguous approved mapping.");
        if (target.FundStructureNodeId != source.CounterpartyEntityId || period.LedgerBookId != target.LedgerBookId
            || period.Status != "Open" || source.EffectiveDate < period.StartDate || source.EffectiveDate > period.EndDate)
            throw new InvalidOperationException("The exact counterpart entity and open effective-date period are required.");
        bool IsAccount(IntercompanyProposalSourceLineDto line, string node) => line.ChartAccountNodeId == node
            || (line.ChartAccountNodeId is null && sourceBindings.Any(binding => binding.ChartAccountNodeId == node
                && (binding.AccountName == line.AccountName || binding.HistoricalNames.Contains(line.AccountName, StringComparer.Ordinal))))
            || (line.ChartAccountNodeId is null && sourceChart?.Any(x => x.NodeId == node && x.AccountName == line.AccountName && x.AccountType == line.AccountType) == true);
        var receivables = source.Lines.Where(x => IsAccount(x, sourceMap.ReceivableAccountNodeId)).ToArray();
        var payables = source.Lines.Where(x => IsAccount(x, sourceMap.PayableAccountNodeId)).ToArray();
        if ((receivables.Length == 0) == (payables.Length == 0))
            throw new InvalidOperationException("One directional monetary leg group is required; split mixed receivable/payable events.");
        var sourceReceivable = receivables.Length > 0;
        var monetary = sourceReceivable ? receivables : payables;
        if (monetary.Any(x => x.Currency != agreement.Currency))
            throw new InvalidOperationException("The source must retain agreement-currency amounts.");
        var signed = monetary.Sum(x => x.TransactionDebit - x.TransactionCredit);
        if (signed == 0m)
            throw new InvalidOperationException("A zero monetary source cannot create a counterpart journal.");
        var originalAmount = Math.Abs(signed);
        var functionalAmount = agreement.Currency == target.BaseCurrency ? originalAmount
            : fx is not null && fx.Currency == target.BaseCurrency ? fx.Amount
            : throw new InvalidOperationException("An approved transaction-date conversion into the destination functional currency is required.");
        if (functionalAmount <= 0m)
            throw new InvalidOperationException("The converted proposal amount must be positive.");
        var intercompanyNode = sourceReceivable ? targetMap.PayableAccountNodeId : targetMap.ReceivableAccountNodeId;
        var operatingLegs = source.Lines.Where(line =>
            (sourceMap.RevenueAccountNodeId is { } revenue && IsAccount(line, revenue))
            || (sourceMap.ExpenseAccountNodeId is { } expense && IsAccount(line, expense))).ToArray();
        var hasOperatingLeg = operatingLegs.Length > 0;
        if (sourceMap.RevenueAccountNodeId is { } sourceRevenue && sourceMap.ExpenseAccountNodeId is { } sourceExpense
            && operatingLegs.Any(line => IsAccount(line, sourceRevenue)) && operatingLegs.Any(line => IsAccount(line, sourceExpense)))
            throw new InvalidOperationException("Split mixed revenue and expense activity before proposing a counterpart; their gross operating roles cannot be collapsed into one net leg.");
        var settlementLegs = sourceMap.SettlementAccountNodeId is { } settlement
            ? source.Lines.Where(line => IsAccount(line, settlement)).ToArray() : [];
        if (hasOperatingLeg && settlementLegs.Length > 0)
            throw new InvalidOperationException("Split mixed settlement and operating activity into separate intercompany event groups.");
        var declaredContra = hasOperatingLeg ? operatingLegs : settlementLegs;
        if (declaredContra.Length == 0 || declaredContra.Any(x => x.Currency != agreement.Currency)
            || declaredContra.Sum(x => x.TransactionDebit - x.TransactionCredit) != -signed)
            throw new InvalidOperationException("An exact opposite source leg on the approved operating or settlement account is required; a settlement is never inferred from a missing revenue leg.");
        var supportedLineIds = monetary.Concat(declaredContra).Select(line => line.LineId).ToHashSet();
        if (supportedLineIds.Count != source.Lines.Count || source.Lines.Select(line => line.LineId).Distinct().Count() != source.Lines.Count
            || source.Lines.Sum(line => line.Debit - line.Credit) != 0m)
            throw new InvalidOperationException("Every retained source leg must belong exclusively to one balanced, supported intercompany event. Split unrelated or ambiguous activity before proposing a counterpart.");
        if (agreement.TemplateKind == IntercompanyAccountingTemplateKindDto.Loan && hasOperatingLeg)
            throw new InvalidOperationException("Loan interest requires its own reviewed treatment; the loan mirror supports principal only.");
        var contraNode = agreement.TemplateKind == IntercompanyAccountingTemplateKindDto.Loan || !hasOperatingLeg
            ? targetMap.SettlementAccountNodeId
            : sourceReceivable ? targetMap.ExpenseAccountNodeId : targetMap.RevenueAccountNodeId;
        if (string.IsNullOrWhiteSpace(contraNode))
            throw new InvalidOperationException("The approved agreement needs a complete destination contra-account template.");
        ChartOfAccountsNodeDto Account(string id) => chart.SingleOrDefault(x => x.NodeId == id && !x.IsArchived)
            ?? throw new InvalidOperationException("A destination template account is missing or archived.");
        var main = Account(intercompanyNode);
        var contra = Account(contraNode);
        var mainRole = sourceReceivable ? IntercompanyAccountRoleDto.Payable : IntercompanyAccountRoleDto.Receivable;
        IntercompanyAccountBindingDto Binding(string node, IntercompanyAccountRoleDto role) =>
            targetBindings.SingleOrDefault(x => x.ChartAccountNodeId == node && x.Role == role && !x.IsArchived)
            ?? throw new InvalidOperationException("An exact destination account-role binding is required.");
        var mainBinding = Binding(intercompanyNode, mainRole);
        var mainSide = signed > 0m ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit;
        LedgerDimensionSetDto Dimensions(ChartOfAccountsNodeDto account, IntercompanyAccountBindingDto? binding) =>
            new(EntityId: source.CounterpartyEntityId.ToString("D"), CounterpartyId: source.SourceEntityId.ToString("D"),
                FundId: target.FundProfileId, BookId: target.LedgerBookId.ToString("D"))
            {
                ChartAccountNodeId = account.NodeId,
                IntercompanyAgreementId = agreement.CanonicalAgreementId,
                IntercompanyAgreementVersion = agreement.CanonicalAgreementVersion,
                AccountingArrangementId = binding is null ? null : agreement.ArrangementId.ToString("D"),
                AccountingArrangementRevisionId = binding is null ? null : agreement.RevisionId.ToString("D"),
                AccountBindingRevisionId = binding?.RevisionId.ToString("D"),
                IntercompanyTransactionId = binding is null ? null : source.TransactionId
            };
        var contraBinding = contra.IntercompanyRole is { } contraRole ? Binding(contra.NodeId, contraRole) : null;
        var original = new ManualJournalLineCurrencyDto(agreement.Currency, originalAmount,
            agreement.Currency == target.BaseCurrency ? 1m : fx!.Rate);
        var lines = new[]
        {
            new ManualJournalEntryLineDto("intercompany", mainSide, functionalAmount, target.BaseCurrency, main.Path,
                EntityId: source.CounterpartyEntityId.ToString("D"), Dimensions: Dimensions(main, mainBinding)) { TransactionCurrency = original },
            new ManualJournalEntryLineDto("counterpart", mainSide == AccountingTemplateLineSideDto.Debit
                    ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit,
                functionalAmount, target.BaseCurrency, contra.Path, EntityId: source.CounterpartyEntityId.ToString("D"),
                Dimensions: Dimensions(contra, contraBinding)) { TransactionCurrency = original }
        };
        return new ManualJournalEntryDraftDto(source.ProposalId, ManualJournalEntryStatusDto.Draft,
            target.FundProfileId, target.LedgerBookId, target.AccountingBasis, source.EffectiveDate,
            period.PeriodId.ToString("D"), source.CounterpartyEntityId.ToString("D"), null, target.BaseCurrency,
            $"{agreement.TemplateKind} counterpart of journal {source.SourceJournalId:D}", "intercompany-proposal-service",
            now, now, 1, lines, [$"journal:{source.SourceJournalId:D}", $"accounting-arrangement:{agreement.ArrangementId:D}/revision:{agreement.RevisionId:D}", $"intercompany-agreement:{agreement.CanonicalAgreementId:D}/version:{agreement.CanonicalAgreementVersion}"],
            [], TreasuryContext: new(source.EffectiveDate, $"intercompany-proposal:{source.ProposalId:D}",
                FundEventType: "IntercompanyMirror"), TenantId: source.TenantId, CompanyId: source.CompanyId)
        {
            IntercompanyProposalId = source.ProposalId,
            IntercompanyProposalSourceDigest = proposal.SourceDigest,
            IntercompanyFx = fx
        };
    }
}
