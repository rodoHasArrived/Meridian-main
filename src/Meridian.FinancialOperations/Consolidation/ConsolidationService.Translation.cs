using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

public sealed partial class ConsolidationService
{
    private async Task<GovernedTranslation> TranslateGovernedAsync(ConsolidationRequestDto request, LedgerBookRecord book,
        IReadOnlyList<ConsolidationSourceDto> source, CancellationToken ct)
    {
        var authority = fxAuthority ?? throw new InvalidOperationException("Governed reporting FX authority is unavailable.");
        var context = await authority.ResolveAsync(book.LedgerBookId, request.AsOf, ct).ConfigureAwait(false);
        var policy = context.Configuration.Policy;
        if (!string.Equals(policy.ReportingCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The approved FX policy reporting currency must match the elimination book.");
        var bindings = await accountBindings!.ListAsync(book.LedgerBookId, request.AsOf, ct).ConfigureAwait(false);
        var reserveBinding = bindings.Single(x => !x.IsArchived && x.Role == IntercompanyAccountRoleDto.TranslationReserve);
        if (!string.Equals(policy.TranslationReserveAccount, reserveBinding.AccountPath, StringComparison.Ordinal))
            throw new InvalidOperationException("The FX policy reserve account must match the approved translation-reserve binding.");
        var input = source.Select(x =>
        {
            var fact = context.Configuration.HistoricalFacts?.SingleOrDefault(f => f.LedgerBookId == x.LedgerBookId &&
                f.JournalEntryId == x.JournalEntryId && f.LineId == x.LineId);
            return new ConsolidationFxSourceLineDto(x.LedgerBookId, x.JournalEntryId, x.LineId, x.EntityId,
                x.AccountPath, x.AccountType!, x.Currency!, x.Debit, x.Credit, x.EffectiveDate,
                fact?.HistoricalDate, fact?.OpeningTranslatedSignedAmount);
        }).ToArray();
        var result = ConsolidationFxTranslator.Translate(input, context.RateSet, policy, request.AsOf);
        var byId = source.ToDictionary(x => x.LineId);
        var translated = result.IsComplete ? result.Lines.Select(x => byId[x.Source.LineId] with
        { Debit = x.Debit, Credit = x.Credit, Currency = result.Currency }).ToList() : [];
        var reserves = result.IsComplete ? result.Reserves ?? [] : [];
        foreach (var reserve in reserves.Where(x => x.SignedAmount != 0m))
        {
            var id = new Guid(Meridian.Contracts.Integrity.Sha256Digest.ComputeBytesUtf8(
                $"translation-reserve:{reserve.LedgerBookId:D}:{reserve.EntityId}:{request.AsOf:yyyy-MM-dd}").AsSpan(0, 16));
            translated.Add(new ConsolidationSourceDto(reserve.LedgerBookId, Guid.Empty, id, reserve.EntityId, null,
                reserveBinding.AccountPath, Math.Max(reserve.SignedAmount, 0m), Math.Max(-reserve.SignedAmount, 0m),
                request.AsOf, $"fx-rate-set:{context.RateSet.RateSetId:D}/{context.RateSet.Revision}")
            { AccountType = "Equity", Currency = result.Currency, ChartAccountNodeId = reserveBinding.ChartAccountNodeId,
              AccountBindingRevisionId = reserveBinding.RevisionId.ToString("D") });
        }
        return new GovernedTranslation(translated, Hash(new { context, result.Fingerprint }),
            JsonSerializer.Serialize(new { Context = context, Input = input, Result = result }),
            reserves.GroupBy(x => x.EntityId).ToDictionary(x => x.Key, x => x.Sum(y => y.SignedAmount)),
            reserves.GroupBy(x => x.EntityId).ToDictionary(x => x.Key, x => x.Sum(y => y.TranslationDifferenceSignedAmount)),
            reserves.GroupBy(x => x.EntityId).ToDictionary(x => x.Key, x => x.Sum(y => y.RoundingDifferenceSignedAmount)), result.Blockers);
    }

    private sealed record GovernedTranslation(IReadOnlyList<ConsolidationSourceDto> Sources, string Fingerprint,
        string Evidence, IReadOnlyDictionary<string, decimal> Reserve,
        IReadOnlyDictionary<string, decimal> TranslationDifferences, IReadOnlyDictionary<string, decimal> RoundingDifferences,
        IReadOnlyList<string> Blockers);
}
