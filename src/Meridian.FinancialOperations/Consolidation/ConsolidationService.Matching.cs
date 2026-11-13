using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

public sealed partial class ConsolidationService
{
    private static (IReadOnlyList<ConsolidationMatchDto> Matches, IReadOnlyList<ManualJournalEntryLineDto> Proposed)
        MatchGoverned(IReadOnlyList<ConsolidationSourceDto> sources,
            IReadOnlyDictionary<Guid, IntercompanyAccountRoleDto> sourceRoles,
            IReadOnlyList<LedgerJournalEntryRecord> posted, IReadOnlyList<IntercompanyAccountBindingDto> overlayBindings,
            LedgerBookRecord book, ConsolidationRequestDto request, string scope, bool income,
            Func<IntercompanyAccountRoleDto, IntercompanyAccountBindingDto> output)
    {
        var matches = new List<ConsolidationMatchDto>();
        var proposed = new List<ManualJournalEntryLineDto>();
        var eligible = sources.Where(x => sourceRoles.ContainsKey(x.LineId) && x.CounterpartyId is not null).ToArray();
        var grouped = eligible.GroupBy(x =>
        {
            var role = sourceRoles[x.LineId];
            var first = role is IntercompanyAccountRoleDto.Receivable or IntercompanyAccountRoleDto.Revenue;
            var isIncome = role is IntercompanyAccountRoleDto.Revenue or IntercompanyAccountRoleDto.Expense;
            return new MatchKey(first ? x.EntityId : x.CounterpartyId!, first ? x.CounterpartyId! : x.EntityId,
                x.AccountingArrangementId, x.AccountingArrangementRevisionId, x.TransactionCurrency ?? x.Currency ?? book.BaseCurrency, isIncome,
                isIncome ? x.IntercompanyTransactionId : null);
        }).ToDictionary(x => x.Key, x => x.ToArray());
        // An attributed legacy population may move into agreement-specific groups. Retain old
        // overlay groups as zero targets so migration creates a reviewed reclassification rather
        // than counting the old aggregate elimination a second time.
        foreach (var line in posted.SelectMany(x => x.Entry.Lines))
        {
            var role = ResolveBinding(overlayBindings, line.Account.Name, line.Dimensions?.ChartAccountNodeId)?.Role;
            if (role is null or IntercompanyAccountRoleDto.TranslationReserve)
                continue;
            var first = role is IntercompanyAccountRoleDto.Receivable or IntercompanyAccountRoleDto.Revenue;
            var isIncome = role is IntercompanyAccountRoleDto.Revenue or IntercompanyAccountRoleDto.Expense;
            if (isIncome && !income)
                continue;
            var creditor = first ? line.Dimensions!.EntityId! : line.Dimensions!.CounterpartyId!;
            var debtor = first ? line.Dimensions!.CounterpartyId! : line.Dimensions!.EntityId!;
            if (grouped.Keys.Any(x => x.Creditor == creditor && x.Debtor == debtor &&
                x.Agreement == line.Dimensions!.AccountingArrangementId && x.Revision == line.Dimensions.AccountingArrangementRevisionId && x.IsIncome == isIncome &&
                (!isIncome || x.Transaction == line.Dimensions.IntercompanyTransactionId)))
                continue;
            grouped.TryAdd(new MatchKey(creditor, debtor, line.Dimensions!.AccountingArrangementId, line.Dimensions.AccountingArrangementRevisionId, book.BaseCurrency,
                isIncome, isIncome ? line.Dimensions.IntercompanyTransactionId : null), []);
        }
        foreach (var pair in grouped.OrderBy(x => x.Key.Creditor, StringComparer.Ordinal).ThenBy(x => x.Key.Debtor, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Agreement, StringComparer.Ordinal).ThenBy(x => x.Key.Revision, StringComparer.Ordinal).ThenBy(x => x.Key.Currency, StringComparer.Ordinal)
            .ThenBy(x => x.Key.IsIncome).ThenBy(x => x.Key.Transaction, StringComparer.Ordinal))
        {
            var key = pair.Key;
            var group = pair.Value;
            var firstRole = key.IsIncome ? IntercompanyAccountRoleDto.Revenue : IntercompanyAccountRoleDto.Receivable;
            var secondRole = key.IsIncome ? IntercompanyAccountRoleDto.Expense : IntercompanyAccountRoleDto.Payable;
            decimal Signed(ConsolidationSourceDto line, bool original)
            {
                var debit = original ? line.TransactionDebit!.Value : line.Debit;
                var credit = original ? line.TransactionCredit!.Value : line.Credit;
                return sourceRoles[line.LineId] is IntercompanyAccountRoleDto.Payable or IntercompanyAccountRoleDto.Revenue
                    ? credit - debit : debit - credit;
            }
            var first = group.Where(x => sourceRoles[x.LineId] == firstRole).ToArray();
            var second = group.Where(x => sourceRoles[x.LineId] == secondRole).ToArray();
            var firstOriginal = first.Sum(x => Signed(x, true));
            var secondOriginal = second.Sum(x => Signed(x, true));
            var firstReporting = first.Sum(x => Signed(x, false));
            var secondReporting = second.Sum(x => Signed(x, false));
            var matchedOriginal = firstOriginal >= 0m && secondOriginal >= 0m ? Math.Min(firstOriginal, secondOriginal) : 0m;
            var firstTarget = firstOriginal > 0m ? RoundConsolidation(firstReporting * matchedOriginal / firstOriginal, book.BaseCurrency) : 0m;
            var secondTarget = secondOriginal > 0m ? RoundConsolidation(secondReporting * matchedOriginal / secondOriginal, book.BaseCurrency) : 0m;
            var firstBinding = output(firstRole);
            var secondBinding = output(secondRole);
            var firstSign = key.IsIncome ? 1m : -1m;
            var secondSign = -firstSign;
            decimal Current(IntercompanyAccountRoleDto role, string entity, string cp)
                => posted.SelectMany(x => x.Entry.Lines).Where(x =>
                    ResolveBinding(overlayBindings, x.Account.Name, x.Dimensions?.ChartAccountNodeId)?.Role == role &&
                    x.Dimensions?.EntityId == entity && x.Dimensions.CounterpartyId == cp &&
                    x.Dimensions.AccountingArrangementId == key.Agreement &&
                    x.Dimensions.AccountingArrangementRevisionId == key.Revision &&
                    (!key.IsIncome || x.Dimensions.IntercompanyTransactionId == key.Transaction))
                    .Sum(x => x.Debit - x.Credit);
            var firstDelta = firstTarget * firstSign - Current(firstRole, key.Creditor, key.Debtor);
            var secondDelta = secondTarget * secondSign - Current(secondRole, key.Debtor, key.Creditor);
            Add(firstBinding, key.Creditor, key.Debtor, firstDelta);
            Add(secondBinding, key.Debtor, key.Creditor, secondDelta);
            // Different functional booking rates are kept apart from principal breaks. The governed
            // reserve role balances their elimination; it never disguises a missing exchange rate.
            if (firstDelta + secondDelta != 0m)
                Add(output(IntercompanyAccountRoleDto.TranslationReserve), key.Creditor, key.Debtor, -(firstDelta + secondDelta));
            matches.Add(new ConsolidationMatchDto(key.Creditor, key.Debtor, firstReporting, secondReporting,
                Math.Min(firstTarget, secondTarget), firstReporting - firstTarget, secondReporting - secondTarget, group.ToArray())
            {
                AccountingArrangementId = key.Agreement,
                AccountingArrangementRevisionId = key.Revision,
                Treatment = key.IsIncome ? RevenueExpenseRuleId : RuleId,
                IntercompanyTransactionId = key.Transaction,
                MatchingCurrency = key.Currency,
                MatchedTransactionAmount = matchedOriginal,
                UnmatchedFirstTransactionAmount = firstOriginal - matchedOriginal,
                UnmatchedSecondTransactionAmount = secondOriginal - matchedOriginal,
                TranslationDifference = firstTarget - secondTarget
            });

            void Add(IntercompanyAccountBindingDto binding, string entity, string cp, decimal signed)
            {
                if (signed == 0m)
                    return;
                proposed.Add(new ManualJournalEntryLineDto($"line-{proposed.Count + 1}",
                    signed > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
                    Math.Abs(signed), book.BaseCurrency, binding.AccountPath, EntityId: entity,
                    Description: key.IsIncome ? "Reviewed intercompany income elimination" : "Reviewed intercompany balance elimination",
                    EvidenceLink: $"consolidation:{scope}", Dimensions: new LedgerDimensionSetDto(
                        FundId: request.OwnershipRootId.ToString("D"), EntityId: entity, CounterpartyId: cp)
                    {
                        AccountingArrangementId = key.Agreement,
                        AccountingArrangementRevisionId = key.Revision,
                        IntercompanyTransactionId = key.Transaction,
                        ChartAccountNodeId = binding.ChartAccountNodeId,
                        AccountBindingRevisionId = binding.RevisionId.ToString("D")
                    }));
            }
        }
        return (matches, proposed);
    }

    private static decimal RoundConsolidation(decimal amount, string currency)
        => decimal.Round(amount, CurrencyMinorUnits.GetPrecision(currency.ToUpperInvariant()), MidpointRounding.AwayFromZero);

    private sealed record MatchKey(string Creditor, string Debtor, string? Agreement, string? Revision, string Currency,
        bool IsIncome, string? Transaction);
}
