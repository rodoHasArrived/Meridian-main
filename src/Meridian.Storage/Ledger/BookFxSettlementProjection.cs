using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Checks the exact economic legs of a reviewed foreign-currency partial sale.</summary>
internal static class BookFxSettlementProjection
{
    public static RetainBookFxSettlementSetRequestDto Normalize(RetainBookFxSettlementSetRequestDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.SetId == Guid.Empty || input.GaapBookId == Guid.Empty || input.CashBookId == Guid.Empty
            || input.GaapBookId == input.CashBookId || input.TradeEventId == Guid.Empty || input.SettlementEventId == Guid.Empty
            || input.TradeEventId == input.SettlementEventId || input.SecurityId == Guid.Empty
            || input.RateSetId == Guid.Empty || input.RateSetRevision <= 0 || input.TradeDate == default
            || input.TradeDate >= input.SettlementDate || input.GrossProceeds <= 0m || input.SettledProceeds <= 0m
            || input.SettledProceeds >= input.GrossProceeds || input.TradeQuantity <= 0m || input.SettledQuantity <= 0m
            || input.SettledQuantity >= input.TradeQuantity || input.GaapCostBasis <= 0m || input.CashRelievedBasis <= 0m
            || input.GrossProceeds * input.SettledQuantity != input.SettledProceeds * input.TradeQuantity)
            throw new ArgumentException("A foreign sale set requires exact sale and partial-settlement identities, dates, quantities and basis.");

        var result = input with
        {
            Symbol = NormalizeText(input.Symbol, 64).ToUpperInvariant(),
            FinancialAccountId = NormalizeText(input.FinancialAccountId, 200),
            ReceivableAccountName = NormalizeText(input.ReceivableAccountName, 200),
            TransactionCurrency = NormalizeText(input.TransactionCurrency, 3).ToUpperInvariant(),
            EvidenceReference = NormalizeText(input.EvidenceReference, 2000),
            GaapTrade = NormalizeJournal(input.GaapTrade),
            GaapSettlement = NormalizeJournal(input.GaapSettlement),
            CashSettlement = NormalizeJournal(input.CashSettlement)
        };
        ConsolidationFxCurrencyRules.RequireSupported(result.TransactionCurrency);
        if (result.GaapTrade.JournalEntryId == result.GaapSettlement.JournalEntryId
            || result.GaapTrade.JournalEntryId == result.CashSettlement.JournalEntryId
            || result.GaapSettlement.JournalEntryId == result.CashSettlement.JournalEntryId)
            throw new ArgumentException("Every required settlement leg must identify a different journal.");
        var account = result.ReceivableAccountName;
        if (account.StartsWith("Cash", StringComparison.OrdinalIgnoreCase)
            || account == LedgerAccounts.SecuritiesAccountName)
            throw new ArgumentException("The GAAP receivable must identify the approved settlement receivable account.");
        return result;
    }

    public static BookFxSettlementSetPlanDto Build(RetainBookFxSettlementSetRequestDto input,
        LedgerBookRecord gaapBook, LedgerBookRecord cashBook, ConsolidationFxRateSetDto rates,
        string actor, string role, DateTimeOffset approvedAt)
    {
        if (gaapBook.AccountingBasis != AccountingBasisKindDto.Gaap || cashBook.AccountingBasis != AccountingBasisKindDto.Cash
            || gaapBook.FundStructureNodeId == Guid.Empty || cashBook.FundStructureNodeId != gaapBook.FundStructureNodeId
            || cashBook.FundProfileId != gaapBook.FundProfileId
            || !string.Equals(gaapBook.BaseCurrency, cashBook.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            throw new LedgerValidationException("The selected GAAP and cash books must belong to the same entity and functional currency.");
        var functional = gaapBook.BaseCurrency.ToUpperInvariant();
        ConsolidationFxCurrencyRules.RequireSupported(functional);
        if (input.TransactionCurrency == functional)
            throw new ArgumentException("This settlement requires a foreign transaction currency.");
        if (!Sha256Digest.IsCanonical(rates.ContentHash) || rates.RateSetId != input.RateSetId || rates.Revision != input.RateSetRevision)
            throw new InvalidDataException("The retained approved FX source does not match the selected rate revision.");

        var trade = SelectRate(rates, input.TransactionCurrency, functional, input.TradeDate);
        var settlement = SelectRate(rates, input.TransactionCurrency, functional, input.SettlementDate);
        var gross = LedgerCurrencyRounding.RoundCurrency(input.GrossProceeds * trade.Rate, functional);
        var relieved = LedgerCurrencyRounding.RoundCurrency(input.SettledProceeds * trade.Rate, functional);
        var cash = LedgerCurrencyRounding.RoundCurrency(input.SettledProceeds * settlement.Rate, functional);
        if (gross <= relieved || relieved <= 0m || cash <= 0m
            || LedgerCurrencyRounding.RoundCurrency(input.GaapCostBasis, functional) != input.GaapCostBasis
            || LedgerCurrencyRounding.RoundCurrency(input.CashRelievedBasis, functional) != input.CashRelievedBasis)
            throw new LedgerValidationException("The approved trade does not produce a positive partial settlement in the book currency.");

        var amounts = new BookFxSettlementAmountsDto(gross, gross - input.GaapCostBasis, relieved, cash,
            gross - relieved, relieved - input.CashRelievedBasis, cash - relieved);
        return new(input, gaapBook.FundStructureNodeId, gaapBook.FundProfileId, functional,
            gaapBook.UpdatedAt, cashBook.UpdatedAt, trade, settlement, rates.ContentHash!, amounts,
            approvedAt, actor, role, string.Empty);
    }

    public static (Guid BookId, BookFxSettlementJournalDto Journal, DateOnly Date, Guid SourceEventId, AccountingBasisKindDto Basis)
        Resolve(RetainBookFxSettlementSetRequestDto input, BookFxSettlementLegDto leg)
        => leg switch
        {
            BookFxSettlementLegDto.GaapTrade => (input.GaapBookId, input.GaapTrade, input.TradeDate, input.TradeEventId, AccountingBasisKindDto.Gaap),
            BookFxSettlementLegDto.GaapSettlement => (input.GaapBookId, input.GaapSettlement, input.SettlementDate, input.SettlementEventId, AccountingBasisKindDto.Gaap),
            BookFxSettlementLegDto.CashSettlement => (input.CashBookId, input.CashSettlement, input.SettlementDate, input.SettlementEventId, AccountingBasisKindDto.Cash),
            _ => throw new ArgumentOutOfRangeException(nameof(leg))
        };

    public static void ValidateJournal(BookFxSettlementSetPlanDto plan, BookFxSettlementLegDto leg, LedgerJournalEntryRecord record)
    {
        var scope = Resolve(plan.Input, leg);
        var entry = record.Entry;
        var metadata = entry.Metadata;
        var input = plan.Input;
        if (entry.JournalEntryId != scope.Journal.JournalEntryId || !entry.IsBalanced || record.AggregateId != scope.BookId
            || record.PeriodId != scope.Journal.PeriodId || record.AccountingBasis != scope.Basis
            || record.AccountingPolicyId != scope.Journal.AccountingPolicyId || record.AccountingPolicyVersion != scope.Journal.AccountingPolicyVersion
            || record.SourceEventId != scope.SourceEventId || record.PostingKind != LedgerPostingKindDto.Originating
            || record.SourceJournalEntryId is not null || record.CommandId is null || record.CommandId == Guid.Empty
            || metadata.EffectiveDate != scope.Date || metadata.SecurityId != input.SecurityId
            || !string.Equals(metadata.Symbol, input.Symbol, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadata.FinancialAccountId, input.FinancialAccountId, StringComparison.OrdinalIgnoreCase)
            || !metadata.EvidenceReferences.Any(reference => reference.Uri == input.EvidenceReference))
            throw new LedgerValidationException("The posted journal does not match the reviewed book, period, policy, source and evidence.");

        var expected = BuildLines(plan, leg);
        if (entry.Lines.Count != expected.Count)
            throw new LedgerValidationException("The posted journal has a different set of legs than the reviewed settlement.");
        foreach (var line in expected)
        {
            if (entry.Lines.Count(posted => Matches(posted, line, plan.FunctionalCurrency)) != 1)
                throw new LedgerValidationException("The posted account, functional amount, original amount or directed FX rate differs from the reviewed settlement.");
        }
    }

    public static Guid ValidateLotRelief(BookFxSettlementSetPlanDto plan, BookFxSettlementLegDto leg,
        IReadOnlyList<LedgerTaxLotDisposalHistoryRecord> history)
    {
        var scope = Resolve(plan.Input, leg);
        var input = plan.Input;
        var isGaap = leg == BookFxSettlementLegDto.GaapTrade;
        if (history.Count != 1 || history[0] is not { CanonicalLots.Count: > 0 } disposition
            || disposition.JournalEntryId != scope.Journal.JournalEntryId || disposition.MutationBatchId == Guid.Empty
            || disposition.WashSaleBasisIncreases.Count != 0
            || !LedgerSecurityKeyMigration.IsRetainedEquivalent(SecuritiesAccount(input, input.FinancialAccountId), disposition.Account)
            || disposition.CanonicalLots.Any(lot => lot.LedgerBookId != scope.BookId || lot.SecurityId != input.SecurityId)
            || disposition.Lots.Sum(lot => lot.Quantity) != (isGaap ? input.TradeQuantity : input.SettledQuantity)
            || disposition.Lots.Sum(lot => lot.CostBasis) != (isGaap ? input.GaapCostBasis : input.CashRelievedBasis))
            throw new LedgerValidationException("The posted sale has no matching canonical lot quantity and functional basis receipt.");
        return disposition.MutationBatchId;
    }

    private static List<ExpectedLine> BuildLines(BookFxSettlementSetPlanDto plan, BookFxSettlementLegDto leg)
    {
        var input = plan.Input;
        var amounts = plan.Amounts;
        var functional = plan.FunctionalCurrency;
        var original = input.TransactionCurrency;
        var account = input.FinancialAccountId;
        var receivable = new LedgerAccount(input.ReceivableAccountName, LedgerAccountType.Asset, FinancialAccountId: account);
        var securities = SecuritiesAccount(input, account);
        var cash = LedgerAccounts.CashFor(account);
        var cashAlternative = LedgerAccounts.CashInCurrency(original, account);
        var expected = new List<ExpectedLine>(3);
        switch (leg)
        {
            case BookFxSettlementLegDto.GaapTrade:
                expected.Add(new(receivable, true, amounts.GaapTradeReceivable, input.GrossProceeds, original, plan.TradeRate.Rate));
                expected.Add(new(securities, false, input.GaapCostBasis, input.GaapCostBasis, functional, 1m));
                AddResult(amounts.GaapMarketResult, fx: false);
                break;
            case BookFxSettlementLegDto.GaapSettlement:
                expected.Add(new(cash, true, amounts.FunctionalCash, input.SettledProceeds, original, plan.SettlementRate.Rate, cashAlternative));
                expected.Add(new(receivable, false, amounts.ReceivableRelieved, input.SettledProceeds, original, plan.TradeRate.Rate));
                AddResult(amounts.SettlementFxResult, fx: true);
                break;
            case BookFxSettlementLegDto.CashSettlement:
                expected.Add(new(cash, true, amounts.FunctionalCash, input.SettledProceeds, original, plan.SettlementRate.Rate, cashAlternative));
                expected.Add(new(securities, false, input.CashRelievedBasis, input.CashRelievedBasis, functional, 1m));
                // The existing lot-disposal convention books the total realized cash-basis result.
                // The proof preserves the market and FX contributions independently.
                AddResult(amounts.CashMarketResult + amounts.SettlementFxResult, fx: false);
                break;
        }
        return expected;

        void AddResult(decimal amount, bool fx)
        {
            if (amount == 0m)
                return;
            var loss = amount < 0m;
            var target = (fx, loss) switch
            {
                (true, true) => LedgerAccounts.RealizedFxLossOrDefault(account),
                (true, false) => LedgerAccounts.RealizedFxGainOrDefault(account),
                (false, true) => LedgerAccounts.RealizedLossOrDefault(account),
                _ => LedgerAccounts.RealizedGainOrDefault(account)
            };
            expected.Add(new(target, loss, Math.Abs(amount), Math.Abs(amount), functional, 1m));
        }
    }

    // Keyed by the reviewed SecurityId (Security Master review F2); the ticker is display-only.
    private static LedgerAccount SecuritiesAccount(RetainBookFxSettlementSetRequestDto input, string? financialAccountId)
        => LedgerAccounts.Securities(LedgerSecurityKey.Resolved(input.SecurityId, input.Symbol), financialAccountId);

    private static bool Matches(LedgerEntry posted, ExpectedLine line, string functional)
        => (LedgerSecurityKeyMigration.IsRetainedEquivalent(line.Account, posted.Account)
                || (line.AlternateAccount is { } alternate && posted.Account == alternate))
            && (line.IsDebit ? posted.Debit == line.FunctionalAmount && posted.Credit == 0m
                : posted.Credit == line.FunctionalAmount && posted.Debit == 0m)
            && posted.Currency is { } currency && currency.FunctionalCurrency == functional
            && currency.TransactionCurrency == line.Currency && currency.FxRateToFunctional == line.FxRate
            && (line.IsDebit ? currency.TransactionDebit == line.TransactionAmount && currency.TransactionCredit == 0m
                : currency.TransactionCredit == line.TransactionAmount && currency.TransactionDebit == 0m);

    private static ConsolidationFxRateDto SelectRate(ConsolidationFxRateSetDto rates, string from, string to, DateOnly date)
    {
        var direct = rates.Rates.Where(rate => rate.Kind == ConsolidationFxRateKindDto.TransactionDate
            && rate.RateDate == date && rate.FromCurrency == from && rate.ToCurrency == to).ToArray();
        if (direct.Length != 1 || direct[0].RateId == Guid.Empty || direct[0].Rate <= 0m || string.IsNullOrWhiteSpace(direct[0].SourceEvidence))
            throw new LedgerValidationException($"The selected FX revision lacks one supported {from} to {to} rate for {date:yyyy-MM-dd}.");
        return direct[0];
    }

    private static BookFxSettlementJournalDto NormalizeJournal(BookFxSettlementJournalDto scope)
    {
        if (scope is null || scope.JournalEntryId == Guid.Empty || scope.PeriodId == Guid.Empty || scope.PeriodVersion <= 0)
            throw new ArgumentException("Each settlement leg must bind its journal, period and reviewed period version.");
        return scope with
        {
            AccountingPolicyId = NormalizeText(scope.AccountingPolicyId, 200),
            AccountingPolicyVersion = NormalizeText(scope.AccountingPolicyVersion, 100)
        };
    }

    private static string NormalizeText(string value, int maxLength)
    {
        if (value?.Trim() is not { Length: > 0 } normalized || normalized.Length > maxLength)
            throw new ArgumentException("A required settlement evidence or scope field is missing or too long.");
        return normalized;
    }

    private sealed record ExpectedLine(LedgerAccount Account, bool IsDebit, decimal FunctionalAmount,
        decimal TransactionAmount, string Currency, decimal FxRate, LedgerAccount? AlternateAccount = null);
}
