using Meridian.Contracts.Accounting.Lots;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

internal static class BondIncomeTerminalCouponGuard
{
    internal sealed record Authorities(IReadOnlyList<BondIncomeTerminalCouponReceiptDto> Receipts,
        IReadOnlyList<BondIncomeTerminalCouponOpeningDto> Openings);

    public static async Task<Authorities> ResolveAsync(ILedgerJournalStore ledger,
        OpenLotFactorPaydownInstructionDto principal, IBondCouponRecognitionService? couponAuthority, CancellationToken ct)
    {
        var proof = new List<BondIncomeTerminalCouponReceiptDto>();
        var openings = new List<BondIncomeTerminalCouponOpeningDto>();
        if (principal.NewFactor != 0m || principal.ScheduledPrincipal is not { } scheduled)
            return new(proof, openings);
        foreach (var inputs in scheduled.IncomeInstructions)
        {
            if (!BondIncomeTerminalCoupon.HasCouponEconomics(inputs))
                continue;
            var scope = BondIncomeTerminalCoupon.Scope(principal, inputs);
            var store = ledger as IBondCouponRecognitionStore
                ?? throw new InvalidOperationException("Terminal principal requires authoritative posted coupon receipt history.");
            var receipt = await store.GetLatestCouponRecognitionAsync(scope, inputs.ExpectedLot.TaxLotRecordId, ct).ConfigureAwait(false);
            var targets = BondIncomeTerminalCoupon.EarnedTargets(inputs);
            if (receipt is null && targets.RawReceivableEarned == 0m && targets.RawPayableEarned == 0m
                && BondIncomeTerminalCoupon.HasCashRights(inputs))
            {
                var lot = (await ledger.GetTaxLotsByIdsAsync(principal.Scope.LedgerBookId,
                    [inputs.ExpectedLot.TaxLotRecordId], ct).ConfigureAwait(false)).Single();
                var opening = BondIncomeTerminalCoupon.Opening(inputs, lot.OriginatingMutationBatchId ?? Guid.Empty,
                    lot.SourceJournalEntryId ?? Guid.Empty);
                await (couponAuthority ?? throw new InvalidOperationException("Terminal purchased-interest opening requires authoritative coupon reconciliation."))
                    .VerifyTerminalOpeningAsync(scope, inputs, opening, principal.Scope.PeriodId, ct).ConfigureAwait(false);
                openings.Add(opening);
                continue;
            }
            var reconciled = BondIncomeTerminalCoupon.Reconcile(inputs, scope, receipt);
            if (reconciled is not null)
                proof.Add(reconciled);
        }
        return new(proof, openings);
    }

    public static async Task RequireAsync(ILedgerJournalStore ledger, OpenLotFactorPaydownInstructionDto principal,
        IBondCouponRecognitionService? couponAuthority, CancellationToken ct)
    {
        var actual = await ResolveAsync(ledger, principal, couponAuthority, ct).ConfigureAwait(false);
        BondIncomeTerminalCoupon.RequireEvidence(principal, actual.Receipts, actual.Openings);
    }
}
