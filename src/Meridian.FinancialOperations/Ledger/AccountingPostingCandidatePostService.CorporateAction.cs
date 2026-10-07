using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private static void ValidateDurableCorporateActionLines(
        PostingRuleJournalCandidateResultDto retained,
        LedgerJournalEntryRecord record,
        string baseCurrency,
        AssetLotMutationInstructionDto lotMutation)
    {
        var previews = retained.DryRunResult.GeneratedLines;
        var generated = retained.GeneratedPostingLines;
        RequireAssetAssertion(previews.Count == generated.Count && previews.Count > 0,
            "Retained corporate-action rule previews and generated lines must agree.");
        var ruleLines = previews.Select((preview, index) =>
        {
            var generatedLine = generated[index];
            RequireAssetAssertion(preview.AccountPath == generatedLine.AccountPath && preview.Side == generatedLine.Side
                && preview.Amount == Math.Abs(generatedLine.Amount) && preview.Currency == generatedLine.Currency
                && preview.Currency == baseCurrency,
                "Retained corporate-action rule account paths, sides, amounts and currencies must agree.");
            return new LedgerEntry(Guid.NewGuid(), record.Entry.JournalEntryId, record.Entry.Timestamp,
                new LedgerAccount(preview.AccountName, LedgerAccountType.Asset),
                preview.Side == AccountingTemplateLineSideDto.Debit ? preview.Amount : 0m,
                preview.Side == AccountingTemplateLineSideDto.Credit ? preview.Amount : 0m,
                record.Entry.Description, LedgerJournalConstruction.ToLedgerLineDimensions(generatedLine.Dimensions));
        }).ToArray();
        var expected = AccountingPostingCandidateService.WithCorporateActionLineage(
            new JournalEntry(record.Entry.JournalEntryId, record.Entry.Timestamp, record.Entry.Description, ruleLines, record.Entry.Metadata),
            lotMutation, generated);
        var actual = record.Entry.Lines;
        var legacy = lotMutation.CorporateAction!.AdditionalPredecessors is null && actual.All(line => line.Dimensions?.TaxLotId is null);
        RequireAssetAssertion(actual.Count == expected.Lines.Count && actual.Select(line => line.EntryId).Distinct().Count() == actual.Count,
            "Existing durable corporate-action journal must contain every exact reviewed lot leg once.");
        foreach (var leg in expected.Lines)
        {
            var matches = actual.Where(line => legacy
                ? line.Account == leg.Account && line.Debit == leg.Debit && line.Credit == leg.Credit
                    && line.Dimensions?.InstrumentId == leg.Dimensions!.InstrumentId && line.Dimensions?.PositionId == leg.Dimensions.PositionId
                : line.Dimensions?.TaxLotId == leg.Dimensions!.TaxLotId).ToArray();
            RequireAssetAssertion(matches.Length == 1, "Existing durable corporate-action journal has a missing or ambiguous reviewed lot identity.");
            var line = matches[0];
            var dimensions = legacy ? line.Dimensions! with { TaxLotId = leg.Dimensions!.TaxLotId } : line.Dimensions;
            RequireAssetAssertion((legacy || line.EntryId == leg.EntryId) && line.JournalEntryId == leg.JournalEntryId
                && line.Account == leg.Account && line.Debit == leg.Debit && line.Credit == leg.Credit
                && PayloadEquals(line.Currency, leg.Currency) && PayloadEquals(dimensions, leg.Dimensions),
                "Existing durable corporate-action lot leg differs from its retained rule effect, reviewed identity, basis, currency or dimension scope.");
        }
    }
}
