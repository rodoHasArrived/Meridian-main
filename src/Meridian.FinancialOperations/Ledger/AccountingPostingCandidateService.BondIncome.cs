using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using System.Text.Json;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateService
{
    private async Task<LedgerTaxLotRecord?> ResolveAdoptedIncomeLotAsync(PostingRuleJournalCandidateRequestDto request,
        AssetAccountingCandidateAuthorityContext? authority, CancellationToken ct)
    {
        if (request.AssetLotMutation is not { Intent: AssetLotMutationIntentDto.Amortize, Amortization.BondIncome: { } income } mutation)
            return null;
        var expected = mutation.Amortization.ExpectedLot;
        if (authority is null || _taxLotStore is null || authority.EventKind != AssetAccountingEventKindDto.DepreciationAmortization
            || expected.LedgerBookId != authority.LedgerBookId || expected.SecurityId != authority.SecurityId
            || expected.BookPositionId != authority.BookPositionId)
            throw new InvalidOperationException("Adopted income requires canonical event and retained lot-account authority before review.");
        var retained = (await _taxLotStore.GetTaxLotsByIdsAsync(expected.LedgerBookId, [expected.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault();
        if (retained is null || retained.LedgerBookId != expected.LedgerBookId
            || retained.SecurityId != expected.SecurityId || retained.BookPositionId != expected.BookPositionId
            || retained.LotId != expected.LotId || retained.Account.AccountType != LedgerAccountType.Asset
            || retained.Acquisition?.BondIncome is null
            || JsonSerializer.Serialize(retained.Acquisition) != JsonSerializer.Serialize(expected.Acquisition)
            || !Guid.TryParseExact(retained.Account.FinancialAccountId, "D", out var owner) || owner != income.AccountId)
            throw new InvalidOperationException("Adopted income account ownership must match the actual retained carrying lot.");
        return retained;
    }

    internal static LedgerAccount BindAdoptedIncomeAccount(LedgerAccount mapped, string accountPath,
        string carryingPath, LedgerTaxLotRecord retained)
    {
        if (accountPath == carryingPath)
        {
            if (mapped.Name != retained.Account.Name || mapped.AccountType != retained.Account.AccountType)
                throw new InvalidOperationException("Adopted income carrying mapping must match the exact durable lot account.");
            return retained.Account;
        }
        return mapped with { FinancialAccountId = retained.Account.FinancialAccountId };
    }

    private static JournalEntry WithAdoptedIncomeCurrency(JournalEntry entry, LedgerTaxLotRecord? lot)
    {
        if (lot is null)
            return entry;
        var acquisition = lot.Acquisition!;
        if (acquisition.AcquisitionCurrency != acquisition.FunctionalCurrency || acquisition.AcquisitionFxRateToFunctional != 1m)
            throw new InvalidOperationException("Governed bond-income candidates require a same-currency lot and adopted dated FX authority for any translation.");
        var lines = entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account, line.Debit, line.Credit, line.Description, line.Dimensions,
            new(acquisition.AcquisitionCurrency, acquisition.FunctionalCurrency, line.Debit, line.Credit, 1m))).ToArray();
        return new(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata);
    }
}
