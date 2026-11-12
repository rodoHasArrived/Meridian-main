using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

/// <summary>Deterministic translation of an already balanced functional trial balance.</summary>
public static class ConsolidationFxTranslator
{
    /// <summary>Exact-date conversion for counterpart proposals under an approved destination-book configuration.</summary>
    public static ConsolidationFxAmountDto ConvertAmount(decimal amount, string fromCurrency,
        ConsolidationFxContextDto context, DateOnly date)
    {
        ConsolidationFxCurrencyRules.Validate(context.Configuration);
        ConsolidationFxCurrencyRules.Validate(context.RateSet);
        ConsolidationFxCurrencyRules.RequireSupported(fromCurrency);
        var configuration = context.Configuration;
        if (configuration.RateSetId != context.RateSet.RateSetId || configuration.RateSetRevision != context.RateSet.Revision ||
            configuration.TenantId != context.RateSet.TenantId || configuration.CompanyId != context.RateSet.CompanyId ||
            date < configuration.EffectiveFrom || (configuration.EffectiveTo is { } end && date >= end))
            throw new InvalidOperationException("The approved FX configuration does not cover this source, date or scope.");
        var currency = configuration.Policy.ReportingCurrency;
        var rate = 1m;
        Guid? rateId = null;
        var inverse = false;
        if (fromCurrency != currency)
        {
            var candidates = context.RateSet.Rates.Where(x => x.Kind == ConsolidationFxRateKindDto.TransactionDate && x.RateDate == date &&
                ((x.FromCurrency == fromCurrency && x.ToCurrency == currency) ||
                 (x.AllowInverse && x.ToCurrency == fromCurrency && x.FromCurrency == currency))).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException("Exactly one approved transaction-date rate is required for counterpart conversion.");
            inverse = candidates[0].FromCurrency != fromCurrency;
            rate = inverse ? 1m / candidates[0].Rate : candidates[0].Rate;
            if (rate <= 0m) throw new InvalidOperationException("The approved inverse quote cannot be represented at supported decimal precision.");
            rateId = candidates[0].RateId;
        }
        return new(Round(amount * rate, configuration.Policy), currency, rate, rateId, inverse,
            context.RateSet.RateSetId, context.RateSet.Revision, configuration.Policy.PolicyId,
            configuration.Policy.Revision, configuration.ContentHash ?? ConsolidationFxValidation.Hash(configuration));
    }

    public static ConsolidationFxTranslationResultDto Translate(
        IReadOnlyList<ConsolidationFxSourceLineDto> source,
        ConsolidationFxRateSetDto rates, ConsolidationFxPolicyDto policy, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(source);
        ConsolidationFxCurrencyRules.Validate(rates);
        ConsolidationFxCurrencyRules.Validate(policy);
        var blockers = new List<string>();
        var translated = new List<ConsolidationFxTranslatedLineDto>();
        var rules = (policy.AccountRules ?? []).ToDictionary(x => x.AccountPath, StringComparer.Ordinal);
        if (source.Select(x => (x.LedgerBookId, x.JournalEntryId, x.LineId)).Distinct().Count() != source.Count)
            blockers.Add("Source populations contain duplicate journal-line identities.");
        foreach (var book in source.GroupBy(x => x.LedgerBookId))
        {
            if (book.Sum(x => x.Debit - x.Credit) != 0m)
                blockers.Add($"Book {book.Key:D} does not have a balanced functional trial balance.");
            if (book.Select(x => x.FunctionalCurrency).Distinct(StringComparer.Ordinal).Count() != 1)
                blockers.Add($"Book {book.Key:D} contains ambiguous functional currencies.");
            if (book.Select(x => x.EntityId).Distinct(StringComparer.Ordinal).Count() != 1)
                blockers.Add($"Book {book.Key:D} contains ambiguous owning entities.");
        }
        foreach (var line in source.OrderBy(x => x.LedgerBookId).ThenBy(x => x.JournalEntryId).ThenBy(x => x.LineId))
        {
            if (!ConsolidationFxCurrencyRules.IsSupported(line.FunctionalCurrency))
            {
                blockers.Add($"Source line {line.LineId:D} requires a recognized currency with a defined minor-unit scale.");
                continue;
            }
            if (!Enum.TryParse<LedgerAccountType>(line.AccountType, out var accountType) ||
                !Enum.IsDefined(accountType) || accountType.ToString() != line.AccountType)
            {
                blockers.Add($"Source line {line.LineId:D} requires a canonical ledger account type.");
                continue;
            }
            if (line.LedgerBookId == Guid.Empty || line.JournalEntryId == Guid.Empty || line.LineId == Guid.Empty ||
                string.IsNullOrWhiteSpace(line.EntityId) || string.IsNullOrWhiteSpace(line.AccountPath) ||
                line.EffectiveDate > asOf || line.Debit < 0m || line.Credit < 0m ||
                (line.Debit != 0m && line.Credit != 0m))
            {
                blockers.Add($"Source line {line.LineId:D} has an invalid date or debit/credit amount.");
                continue;
            }
            rules.TryGetValue(line.AccountPath, out var rule);
            var method = rule?.Method ?? accountType switch
            {
                LedgerAccountType.Asset or LedgerAccountType.Liability => ConsolidationFxRateKindDto.Closing,
                LedgerAccountType.Revenue or LedgerAccountType.Expense => ConsolidationFxRateKindDto.TransactionDate,
                LedgerAccountType.Equity => ConsolidationFxRateKindDto.Historical,
                _ => (ConsolidationFxRateKindDto?)null
            };
            if (method is null)
            {
                blockers.Add($"Source line {line.LineId:D} has no supported translation treatment.");
                continue;
            }
            if (rule?.RequiresOpeningTranslatedBalance == true && line.OpeningTranslatedSignedAmount is null)
            {
                blockers.Add($"Opening translated balance evidence is required for {line.AccountPath}.");
                continue;
            }
            if (line.OpeningTranslatedSignedAmount is { } opening)
            {
                if (accountType != LedgerAccountType.Equity || (opening != 0m && Math.Sign(opening) != Math.Sign(line.Debit - line.Credit)) ||
                    (line.FunctionalCurrency == policy.ReportingCurrency && opening != line.Debit - line.Credit))
                {
                    blockers.Add($"Opening translated amounts require consistent equity source evidence, line {line.LineId:D}.");
                    continue;
                }
                // The retained opening amount was translated under its original approved policy.
                // It is never retranslated at today's closing rate.
                translated.Add(new(line, Round(Math.Max(opening, 0m), policy),
                    Round(Math.Max(-opening, 0m), policy), 1m, null, false, method.Value)
                    { UnroundedSignedAmount = opening });
                continue;
            }
            if (line.FunctionalCurrency == policy.ReportingCurrency)
            {
                translated.Add(new(line, Round(line.Debit, policy), Round(line.Credit, policy),
                    1m, null, false, method.Value) { UnroundedSignedAmount = line.Debit - line.Credit });
                continue;
            }
            DateOnly date;
            if (method == ConsolidationFxRateKindDto.Historical)
            {
                if (line.HistoricalDate is not { } historicalDate || historicalDate > line.EffectiveDate)
                {
                    blockers.Add($"Historical date evidence is required for {line.AccountPath}, line {line.LineId:D}.");
                    continue;
                }
                date = historicalDate;
            }
            else date = method == ConsolidationFxRateKindDto.Closing
                ? policy.ClosingRateDates?.SingleOrDefault(x => x.AsOf == asOf)?.RateDate ?? asOf
                : line.EffectiveDate;
            if (method == ConsolidationFxRateKindDto.PeriodAverage &&
                (rule?.PeriodStart is null || rule.PeriodEnd is null || line.EffectiveDate < rule.PeriodStart || line.EffectiveDate > rule.PeriodEnd))
            {
                blockers.Add($"An explicit average period containing the activity is required for {line.AccountPath}.");
                continue;
            }
            var candidates = rates.Rates.Where(rate => rate.Kind == method &&
                (method == ConsolidationFxRateKindDto.PeriodAverage
                    ? rate.PeriodStart == rule!.PeriodStart && rate.PeriodEnd == rule.PeriodEnd && rate.RateDate <= asOf
                    : rate.RateDate == date) &&
                ((rate.FromCurrency == line.FunctionalCurrency && rate.ToCurrency == policy.ReportingCurrency) ||
                 (rate.AllowInverse && rate.ToCurrency == line.FunctionalCurrency && rate.FromCurrency == policy.ReportingCurrency)))
                .ToArray();
            if (candidates.Length != 1)
            {
                blockers.Add($"Exactly one approved {method} rate is required for {line.FunctionalCurrency}/{policy.ReportingCurrency} on {date:yyyy-MM-dd}.");
                continue;
            }
            var selected = candidates[0];
            var inverse = selected.FromCurrency != line.FunctionalCurrency;
            var rateValue = inverse ? 1m / selected.Rate : selected.Rate;
            if (rateValue <= 0m)
            {
                blockers.Add($"The inverse quote for line {line.LineId:D} cannot be represented at supported decimal precision.");
                continue;
            }
            translated.Add(new(line, Round(line.Debit * rateValue, policy), Round(line.Credit * rateValue, policy),
                rateValue, selected.RateId, inverse, method.Value) { UnroundedSignedAmount = (line.Debit - line.Credit) * rateValue });
        }
        // A partial result is inspectable but must never be exposed as a consolidated balance.
        var reserve = blockers.Count == 0 ? -translated.Sum(x => x.Debit - x.Credit) : 0m;
        var fingerprint = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            rates.RateSetId, rates.Revision, ContentHash = rates.ContentHash ?? ConsolidationFxValidation.Hash(rates), Policy = policy, AsOf = asOf,
            Lines = translated, Reserve = reserve, Blockers = blockers
        }));
        var reserves = blockers.Count == 0
            ? translated.GroupBy(x => (x.Source.LedgerBookId, x.Source.EntityId))
                .Select(group => new ConsolidationFxReserveDto(group.Key.LedgerBookId,
                    group.Key.EntityId, -group.Sum(x => x.Debit - x.Credit),
                    -group.Sum(x => x.UnroundedSignedAmount),
                    group.Sum(x => x.UnroundedSignedAmount - (x.Debit - x.Credit)))).ToArray()
            : [];
        return new(policy.ReportingCurrency, translated, reserve, blockers, fingerprint, reserves)
        {
            TranslationDifferenceSignedAmount = blockers.Count == 0 ? -translated.Sum(x => x.UnroundedSignedAmount) : 0m,
            RoundingDifferenceSignedAmount = blockers.Count == 0 ? translated.Sum(x => x.UnroundedSignedAmount - (x.Debit - x.Credit)) : 0m
        };
    }

    private static decimal Round(decimal amount, ConsolidationFxPolicyDto policy)
        => LedgerCurrencyRounding.RoundCurrency(amount, policy.ReportingCurrency);
}
