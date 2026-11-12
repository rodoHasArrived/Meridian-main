using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>Reverses an isolated posted counterpart using its retained economics, without repricing.</summary>
public static class IntercompanyCounterpartReversalBuilder
{
    public static DateOnly VerifySource(IntercompanyProposalSourceDto source,
        LedgerJournalEntryRecord original, LedgerJournalEntryRecord inverse)
    {
        if (source.ReversesJournalId != original.Entry.JournalEntryId
            || source.SourceJournalId != inverse.Entry.JournalEntryId
            || inverse.SourceJournalEntryId != original.Entry.JournalEntryId
            || IntercompanyJournalFamily.EffectiveDate(inverse) != source.EffectiveDate
            || !IntercompanyJournalCorrectionEvidence.IsExactInverse(inverse.Entry, original.Entry))
            throw new InvalidOperationException("A source reversal must prove the exact complete economic inverse of its retained same-book original.");
        return IntercompanyJournalFamily.EffectiveDate(original);
    }

    public static ManualJournalEntryDraftDto Build(IntercompanyProposalDto proposal, IntercompanyProposalDto prior,
        IntercompanyAgreementDto agreement, LedgerJournalEntryRecord original, LedgerBookRecord target,
        LedgerAccountingPeriod period, IReadOnlyList<IntercompanyAccountBindingDto> bindings,
        IReadOnlyList<ChartOfAccountsNodeDto> chart, DateTimeOffset now)
    {
        var source = proposal.Source;
        var retained = prior.Source;
        if (source.RuleVersion != "intercompany-mirror-v1" || retained.RuleVersion != "intercompany-mirror-v1"
            || source.ReversesJournalId != retained.SourceJournalId || retained.SourceBookId != source.SourceBookId
            || retained.SourceEntityId != source.SourceEntityId || retained.CounterpartyEntityId != source.CounterpartyEntityId
            || retained.AgreementId != source.AgreementId || retained.AgreementRevisionId != source.AgreementRevisionId
            || retained.TransactionId != source.TransactionId || retained.TenantId != source.TenantId
            || agreement.AgreementId != source.AgreementId || agreement.RevisionId != source.AgreementRevisionId
            || prior.ResolvedTargetBookId != target.LedgerBookId || prior.ResolvedTargetJournalId != original.Entry.JournalEntryId
            || target.FundStructureNodeId != source.CounterpartyEntityId
            || period.LedgerBookId != target.LedgerBookId || period.Status != "Open"
            || source.EffectiveDate < period.StartDate || source.EffectiveDate > period.EndDate
            || original.Entry.Lines.Count != 2 || agreement.Kind == IntercompanyAgreementKindDto.Guarantee)
            throw new InvalidOperationException("An exact isolated posted counterpart and its original event authority are required for an automatic reversal.");
        var sourceMap = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == source.SourceEntityId && x.LedgerBookId == source.SourceBookId)
            ?? throw new InvalidOperationException("The original source book has no approved mapping.");
        var targetMap = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == source.CounterpartyEntityId && x.LedgerBookId == target.LedgerBookId)
            ?? throw new InvalidOperationException("The original counterpart book has no approved mapping.");
        var receivables = retained.Lines.Where(x => x.ChartAccountNodeId == sourceMap.ReceivableAccountNodeId).ToArray();
        var payables = retained.Lines.Where(x => x.ChartAccountNodeId == sourceMap.PayableAccountNodeId).ToArray();
        if (retained.RequiresSourceAllocation || (receivables.Length == 0) == (payables.Length == 0))
            throw new InvalidOperationException("The original event must retain one unambiguous monetary direction.");
        var receivable = receivables.Length > 0;
        var monetary = receivable ? receivables : payables;
        var signed = monetary.Sum(x => x.TransactionDebit - x.TransactionCredit);
        var operatingNodes = new[] { sourceMap.RevenueAccountNodeId, sourceMap.ExpenseAccountNodeId }
            .Where(node => node is not null && retained.Lines.Any(x => x.ChartAccountNodeId == node)).Distinct().ToArray();
        if (operatingNodes.Length > 1)
            throw new InvalidOperationException("Split mixed revenue and expense activity before proposing its counterpart reversal.");
        var operating = operatingNodes.Length == 1;
        var sourceContra = operating ? operatingNodes[0] : sourceMap.SettlementAccountNodeId;
        var contraFacts = retained.Lines.Where(x => x.ChartAccountNodeId == sourceContra).ToArray();
        if (signed == 0m || sourceContra is null || contraFacts.Length == 0
            || monetary.Concat(contraFacts).Select(x => x.LineId).Distinct().Count() != retained.Lines.Count
            || monetary.Concat(contraFacts).Any(x => x.Currency != agreement.Currency)
            || contraFacts.Sum(x => x.TransactionDebit - x.TransactionCredit) != -signed
            || (agreement.Kind == IntercompanyAgreementKindDto.Loan && operating))
            throw new InvalidOperationException("The original source must retain complete supported event legs before automatic reversal.");
        var mainNode = receivable ? targetMap.PayableAccountNodeId : targetMap.ReceivableAccountNodeId;
        var contraNode = operating ? (receivable ? targetMap.ExpenseAccountNodeId : targetMap.RevenueAccountNodeId) : targetMap.SettlementAccountNodeId;
        var main = original.Entry.Lines.SingleOrDefault(x => x.Dimensions?.ChartAccountNodeId == mainNode);
        var contra = original.Entry.Lines.SingleOrDefault(x => x.Dimensions?.ChartAccountNodeId == contraNode);
        if (main is null || contra is null || ReferenceEquals(main, contra)
            || !ExactEvent(main.Dimensions, source, target) || !BoundedContra(contra.Dimensions, source, target)
            || OriginalSigned(main, target.BaseCurrency, agreement.Currency) != -signed
            || OriginalSigned(contra, target.BaseCurrency, agreement.Currency) != signed
            || main.Debit - main.Credit != -(contra.Debit - contra.Credit))
            throw new InvalidOperationException("The original counterpart includes unrelated or unproved activity. Prepare its correction manually.");
        var inverse = new List<ManualJournalEntryLineDto>();
        foreach (var line in original.Entry.Lines)
        {
            var account = chart.SingleOrDefault(x => x.NodeId == line.Dimensions?.ChartAccountNodeId && !x.IsArchived)
                ?? throw new InvalidOperationException("The original counterpart account must remain available before its inverse can be proposed.");
            var binding = account.IntercompanyRole is { } role
                ? bindings.SingleOrDefault(x => x.ChartAccountNodeId == account.NodeId && x.Role == role && !x.IsArchived) : null;
            if (line.Account.Symbol is not null || line.Account.FinancialAccountId is not null
                || line.Account.AccountType.ToString() != account.AccountType
                || (account.AccountName != line.Account.Name && binding?.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal) != true)
                || (account.IntercompanyRole is not null && binding is null)
                || (account.IntercompanyRole is not null && !ExactEvent(line.Dimensions, source, target)))
                throw new InvalidOperationException("The original counterpart account or event scope cannot be proved for an automatic inverse.");
            var retainedDimensions = line.Dimensions!;
            var dimensions = new LedgerDimensionSetDto(FundId: retainedDimensions.FundId, EntityId: retainedDimensions.EntityId,
                CounterpartyId: retainedDimensions.CounterpartyId, BookId: retainedDimensions.BookId)
            {
                ChartAccountNodeId = retainedDimensions.ChartAccountNodeId, AgreementId = retainedDimensions.AgreementId,
                AgreementRevisionId = retainedDimensions.AgreementRevisionId,
                AccountBindingRevisionId = retainedDimensions.AccountBindingRevisionId,
                IntercompanyTransactionId = retainedDimensions.IntercompanyTransactionId
            };
            if (binding is not null) dimensions = dimensions with { AccountBindingRevisionId = binding.RevisionId.ToString("D") };
            inverse.Add(new("reverse:" + line.EntryId.ToString("D"), line.Debit > 0m ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit,
                line.Debit + line.Credit, target.BaseCurrency, account.Path, EntityId: source.CounterpartyEntityId.ToString("D"), Dimensions: dimensions)
            {
                TransactionCurrency = line.Currency is { } currency
                    ? new(currency.TransactionCurrency, currency.TransactionDebit + currency.TransactionCredit, currency.FxRateToFunctional) : null
            });
        }
        return new(source.ProposalId, ManualJournalEntryStatusDto.Draft, target.FundProfileId, target.LedgerBookId,
            target.AccountingBasis, source.EffectiveDate, period.PeriodId.ToString("D"), source.CounterpartyEntityId.ToString("D"), null,
            target.BaseCurrency, $"Exact counterpart reversal of journal {original.Entry.JournalEntryId:D}", "intercompany-proposal-service", now, now, 1,
            inverse, [$"journal:{source.SourceJournalId:D}", $"journal:{original.Entry.JournalEntryId:D}", $"intercompany-agreement:{agreement.AgreementId:D}/revision:{agreement.RevisionId:D}"], [],
            EntryType: ManualJournalEntryTypeDto.Reversal,
            TreasuryContext: new(source.EffectiveDate, $"intercompany-proposal:{source.ProposalId:D}", FundEventType: "IntercompanyMirror"),
            ReversalOfJournalEntryId: original.Entry.JournalEntryId,
            Reversal: new(original.Entry.JournalEntryId, source.ProposalId, "Source-linked exact counterpart reversal proposal.", now, "intercompany-proposal-service"),
            TenantId: source.TenantId, CompanyId: source.CompanyId)
        { IntercompanyProposalId = source.ProposalId, IntercompanyProposalSourceDigest = proposal.SourceDigest };
    }

    private static decimal OriginalSigned(LedgerEntry line, string functional, string agreement)
    {
        if (!string.Equals(line.Currency?.TransactionCurrency ?? functional, agreement, StringComparison.OrdinalIgnoreCase)
            || (line.Currency is { } currency && currency.FunctionalCurrency != functional))
            throw new InvalidOperationException("The original counterpart must retain the agreement and functional currencies.");
        return (line.Currency?.TransactionDebit ?? line.Debit) - (line.Currency?.TransactionCredit ?? line.Credit);
    }

    private static bool ExactEvent(LedgerLineDimensionSet? dimensions, IntercompanyProposalSourceDto source, LedgerBookRecord target)
        => dimensions is not null && BoundedScope(dimensions, source, target)
            && Guid.TryParse(dimensions.AgreementId, out var agreement) && agreement == source.AgreementId
            && Guid.TryParse(dimensions.AgreementRevisionId, out var revision) && revision == source.AgreementRevisionId
            && dimensions.IntercompanyTransactionId == source.TransactionId;

    private static bool BoundedContra(LedgerLineDimensionSet? dimensions, IntercompanyProposalSourceDto source, LedgerBookRecord target)
        => dimensions is not null && BoundedScope(dimensions, source, target)
            && ((dimensions.AgreementId is null && dimensions.AgreementRevisionId is null && dimensions.IntercompanyTransactionId is null)
                || ExactEvent(dimensions, source, target));

    private static bool BoundedScope(LedgerLineDimensionSet dimensions, IntercompanyProposalSourceDto source, LedgerBookRecord target)
        => Guid.TryParse(dimensions.EntityId, out var entity) && entity == source.CounterpartyEntityId
            && Guid.TryParse(dimensions.CounterpartyId, out var counterparty) && counterparty == source.SourceEntityId
            && dimensions.FundId == target.FundProfileId && Guid.TryParse(dimensions.BookId, out var book) && book == target.LedgerBookId
            && dimensions is { SleeveId: null, StrategyId: null, InvestorId: null, CapitalAccountId: null, InstrumentId: null,
                TaxLotId: null, CostCenterId: null, OrganizationId: null, PortfolioId: null, AccountId: null,
                CustomerId: null, VendorId: null, ProjectId: null, PositionId: null }
            && dimensions.ExternalGlDimensions.Count == 0;
}
