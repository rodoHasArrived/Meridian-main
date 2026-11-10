using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private async Task<string?> ValidateCurrentCapitalAuthorityAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        if (_privateCapitalAuthorityStore is null)
            return "Durable private-capital authority is unavailable for journal review.";
        var authority = PrivateCapitalPostingAuthority.Deserialize(draft.PrivateCapitalAuthorityJson!);
        var call = await _privateCapitalAuthorityStore.GetCallAsync(authority.Scope, authority.CallId, ct).ConfigureAwait(false);
        var line = call?.Lines.SingleOrDefault(item => item.CommitmentId == authority.CommitmentId);
        if (call is null || line is null || call.Scope != authority.Scope || call.PlanHash != authority.PlanHash
            || call.PlanHash != PrivateCapitalCallPlanHash.Compute(call) || line.CommitmentRevision != authority.CommitmentRevision
            || draft.PeriodId != call.PeriodId || draft.TreasuryContext?.InvestorId != line.InvestorId
            || draft.TreasuryContext.CapitalAccountId != line.CapitalAccountId)
            return "The retained capital-call plan or scope changed; recreate the draft and renew review.";
        if (draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked)
        {
            var receipts = await _privateCapitalAuthorityStore.ListPostingReceiptsAsync(authority.Scope, ct).ConfigureAwait(false);
            return receipts.Any(receipt => receipt.JournalEntryId == draft.JournalEntryId
                && receipt.CallId == authority.CallId && receipt.CommitmentId == authority.CommitmentId
                && receipt.Kind == authority.Kind && receipt.Amount == authority.Amount
                && receipt.FundingReceiptId == authority.FundingReceiptId && receipt.BankLineId == authority.BankLineId)
                ? null : "The posted capital journal has no matching immutable financial receipt.";
        }
        if (authority.Kind == "Funding")
        {
            // A later commitment revision cannot prevent settlement of an already issued call.
            if (line.IssuedAmount != line.Amount || authority.Amount > line.OutstandingAmount)
                return "Posted capital-call receivable changed; review the remaining balance before funding.";
            return null;
        }
        var terms = await _privateCapitalAuthorityStore.ListCommitmentsAsync(authority.Scope, ct).ConfigureAwait(false);
        var current = terms.Where(item => item.CommitmentId == authority.CommitmentId
                && item.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved
                && item.EffectiveFrom <= authority.EffectiveDate)
            .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.Revision).FirstOrDefault();
        if (current is null || current.Revision != authority.CommitmentRevision || !current.IsActive
            || current.InvestorId != line.InvestorId || current.CapitalAccountId != line.CapitalAccountId
            || current.UncalledAmount != line.UncalledBefore || authority.Amount != line.Amount)
            return "Approved commitment terms or available capital changed; recreate the call from a fresh preview.";
        return null;
    }
}
