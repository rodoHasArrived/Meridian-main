using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Detached seller rights require entitlement history rather than payment-date holdings.</summary>
internal static class BondIncomeEntitlementHistoryGuard
{
    public static async Task RequireAsync(ILedgerJournalStore ledger, SecurityProjectionRecord security,
        BondWorkflowResult projection, OpenLotDto selected, Guid owner, DateOnly through, CancellationToken ct)
    {
        var raw = security.AssetSpecificTerms;
        if (SecurityTermsEnvelope.ResolveAssetClassTermsVersion(raw) < 5
            || !raw.TryGetProperty("bondWorkflow", out var workflow) || workflow.ValueKind != JsonValueKind.Object
            || SecurityTermReader.ReadDecimal(workflow, "exCouponBusinessDays") is not > 0m)
            return;
        var windows = projection.CashFlows.Where(f => f.Interest != 0m && f.PaymentDate > selected.AcquiredDate
            && f.PaymentDate <= through).Select(f => (f.AccrualStart, f.PaymentDate)).Distinct().ToArray();
        if (windows.Length == 0)
            return;
        if (ledger is not ILedgerTaxLotDisposalHistory disposalHistory)
            throw new InvalidOperationException("Ex-coupon accounting requires authoritative disposal history to prove the holder cash entitlement pool.");
        var journals = await ledger.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: selected.LedgerBookId), ct).ConfigureAwait(false);
        var disposals = await disposalHistory.GetTaxLotDisposalHistoryAsync(selected.LedgerBookId,
            journals.Select(j => j.Entry.JournalEntryId).ToArray(), ct).ConfigureAwait(false);
        var byId = journals.ToDictionary(j => j.Entry.JournalEntryId);
        foreach (var disposal in disposals)
        {
            if (Guid.TryParse(disposal.Account.FinancialAccountId, out var account) && account != owner)
                continue;
            if (disposal.CanonicalLots is { Count: > 0 } lots && !lots.Any(l => l.SecurityId == selected.SecurityId
                && l.BookPositionId == selected.BookPositionId))
                continue;
            if (!byId.TryGetValue(disposal.JournalEntryId, out var journal)
                || journal.Entry.Metadata.EffectiveDate is not { } effective
                || windows.Any(w => effective >= w.AccrualStart && effective <= w.PaymentDate))
                throw new InvalidOperationException("Ex-coupon holder disposals within a coupon window require retained detached-entitlement history before cash rounding or income recognition.");
        }
    }
}
