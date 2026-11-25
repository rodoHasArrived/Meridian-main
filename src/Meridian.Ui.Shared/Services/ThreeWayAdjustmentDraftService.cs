using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Events;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Prepares a reviewed correction candidate; never approves, posts, or clears casework.</summary>
public sealed class ThreeWayAdjustmentDraftService(
    IThreeWayReconciliationSourceReader sources,
    FileThreeWayReconciliationEvidenceStore evidence,
    ThreeWayReconciliationOptions options,
    ILedgerJournalStore? ledger = null,
    IAccountingConfigurationService? configuration = null,
    IManualJournalEntryWorkbenchService? workbench = null,
    IManualJournalEntryDraftStore? drafts = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ManualJournalEntryDraftDto> PrepareAsync(Guid accountId, ReconciliationBreakQueueScope access,
        PrepareThreeWayAdjustmentRequestDto request, string actor, IReadOnlyList<string>? principalIds = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var scope = await sources.AuthorizeAsync(accountId, access, request.LedgerBookId, request.AccountingPeriodId, ct)
            .ConfigureAwait(false);
        await using var lease = await evidence.AcquireOperationAsync(scope, request.OperationId, ct).ConfigureAwait(false);
        var retained = await evidence.ReadRunAsync(scope, request.OperationId, ct).ConfigureAwait(false);
        if (retained is not { Completed: true } || retained.Result.Scope != scope)
            throw new InvalidOperationException("A completed comparison in the exact current account/book scope is required.");
        var proposal = retained.Result.AdjustmentProposals.SingleOrDefault(proposal => proposal.CheckId == request.CheckId);
        if (proposal is not { Status: "Ready", Amount: > 0m } || proposal.EvidenceHash != request.EvidenceHash ||
            retained.Result.AccountingPeriodVersion != request.ExpectedPeriodVersion)
            throw new InvalidOperationException("The reviewed correction proposal, evidence hash or period version is unavailable or stale.");
        if (ledger is null || configuration is null || workbench is null)
            throw new InvalidOperationException("Governed manual journal and accounting configuration services are required.");
        var feeId = proposal.EvidenceIds.Single();
        var economicHash = ThreeWayFeeCorrectionIdentity.Compute(scope.TenantId, scope.CompanyId,
            scope.ProviderId, scope.ExternalAccountId, accountId, scope.LedgerBookId, feeId);
        var draftId = new Guid(Convert.FromHexString(economicHash).AsSpan(0, 16));
        var key = "three-way-correction:" + economicHash;
        if (drafts is not null && await drafts.GetAsync(scope.FundProfileId, draftId, ct, scope.TenantId, scope.CompanyId)
                .ConfigureAwait(false) is { } existing)
        {
            if (existing.LedgerBookId != scope.LedgerBookId || existing.PeriodId != scope.AccountingPeriodId.ToString("D") ||
                existing.TreasuryContext?.IdempotencyKey != key || !existing.EvidenceLinks.Contains("urn:sha256:" + request.EvidenceHash, StringComparer.Ordinal))
                throw new InvalidOperationException("The retained correction identity belongs to another draft.");
            return existing;
        }
        var period = await ledger.GetPeriodAsync(scope.AccountingPeriodId, ct).ConfigureAwait(false);
        if (period is null || period.LedgerBookId != scope.LedgerBookId || period.Version != request.ExpectedPeriodVersion || period.Status != "Open")
            throw new InvalidOperationException("A current open accounting period with the reviewed version is required.");
        await sources.CaptureAsync(scope, ct).ConfigureAwait(false);
        var cutoff = _time.GetUtcNow();
        var latestSources = await sources.ReadAsync(scope, cutoff, ct).ConfigureAwait(false);
        var checks = ThreeWayReconciliationComparer.Compare(latestSources, cutoff, options.MaximumSourceAge, options.MaximumSourceDrift);
        var fresh = ThreeWayAdjustmentProposalBuilder.Build(latestSources, checks, cutoff)
            .SingleOrDefault(candidate => candidate.CheckId == proposal.CheckId && candidate.Status == "Ready");
        if (fresh is null || fresh.EvidenceHash != proposal.EvidenceHash || fresh.Amount != proposal.Amount ||
            !string.Equals(fresh.Currency, proposal.Currency, StringComparison.Ordinal) ||
            !fresh.EvidenceIds.SequenceEqual(proposal.EvidenceIds, StringComparer.Ordinal))
            throw new InvalidOperationException("Current source operands or posted fee identities changed; rerun and review reconciliation before preparing a correction.");
        var fee = latestSources.Single(source => source.Source == "broker").CashActivities.Single(activity => activity.TransactionId == feeId);
        var date = DateOnly.FromDateTime(fee.PostedAtUtc.UtcDateTime);
        if (date < period.StartDate || date > period.EndDate)
            throw new InvalidOperationException("The evidenced fee is outside the selected accounting period.");
        var workspace = await configuration.GetWorkspaceAsync(scope.FundProfileId, scope.LedgerBookId, ct,
            scope.TenantId, scope.CompanyId).ConfigureAwait(false);
        if (workspace.FundProfileId != scope.FundProfileId || workspace.LedgerBookId != scope.LedgerBookId ||
            workspace.TenantId != scope.TenantId || workspace.CompanyId != scope.CompanyId || workspace.Status != AccountingConfigurationStatusDto.Active)
            throw new InvalidOperationException("Correction mappings do not belong to the exact active account/book configuration.");
        var cash = Resolve(options.CashAccountPath, "Asset");
        var expense = Resolve(options.FeeExpenseAccountPath, "Expense");
        if (!cash.AccountName.StartsWith("Cash", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured cash account is not recognized by the ledger cash convention.");
        var links = new[] { "urn:sha256:" + request.EvidenceHash, fee.EvidenceReference,
            "evidence://three-way/runs/" + Uri.EscapeDataString(retained.Result.RunId) }.Distinct(StringComparer.Ordinal).ToArray();
        var now = retained.Result.RecordedAtUtc;
        var memo = $"Missing broker fee {feeId}; source run {retained.Result.RunId}; check {request.CheckId}; review required.";
        var draft = new ManualJournalEntryDraftDto(draftId, ManualJournalEntryStatusDto.Draft, scope.FundProfileId,
            scope.LedgerBookId, AccountingBasisKindDto.Primary, date, scope.AccountingPeriodId.ToString("D"),
            scope.EntityId.ToString("D"), null, scope.Currency, memo, actor, now, now, 0,
            [
                new("fee", AccountingTemplateLineSideDto.Debit, proposal.Amount.Value, scope.Currency, expense.Path,
                    EntityId: scope.EntityId.ToString("D"), Description: memo, EvidenceLink: links[0], LedgerAccountFinancialAccountId: accountId.ToString("D")),
                new("cash", AccountingTemplateLineSideDto.Credit, proposal.Amount.Value, scope.Currency, cash.Path,
                    EntityId: scope.EntityId.ToString("D"), Description: memo, EvidenceLink: links[0], LedgerAccountFinancialAccountId: accountId.ToString("D"))
            ], links, [],
            EvidenceAttachments: [new("three-way-fee-proof", "Retained broker fee and reconciliation proof", "Reconciliation",
                links[0], "ThreeWayReconciliation", now, actor, Description: $"Fee {feeId}; {fee.Amount} {fee.Currency}; expected period version {request.ExpectedPeriodVersion}.")],
            EntryType: ManualJournalEntryTypeDto.Expense,
            TreasuryContext: new(EffectiveDate: date, IdempotencyKey: key, SettlementReference: "three-way-fee:" + feeId),
            TenantId: scope.TenantId, CompanyId: scope.CompanyId)
        {
            DataProvenanceToken = retained.Result.DataProvenanceToken,
            IsAdjustingEntry = true,
            CurrencyEvidenceVersion = 1,
            ThreeWayFeeCorrection = new(economicHash, scope.TenantId, scope.CompanyId, scope.ProviderId, scope.ExternalAccountId,
                feeId, accountId, scope.LedgerBookId, retained.Result.RunId, request.EvidenceHash, proposal.Amount.Value,
                scope.Currency, fee.PostedAtUtc)
        };
        // The existing manual workbench retains evidence and draft validation; its independent
        // submission/approval/posting controls continue to own every material transition.
        var save = new SaveManualJournalEntryDraftRequest(draft, actor, CorrelationId: key, EvidenceLinks: links,
            LedgerBookId: scope.LedgerBookId, TenantId: scope.TenantId, CompanyId: scope.CompanyId,
            ReportGroupPrincipalIds: principalIds);
        return workbench is ManualJournalEntryWorkbenchService trustedWorkbench
            ? await trustedWorkbench.SaveAutomatedDraftAsync(save, ct).ConfigureAwait(false)
            : await workbench.SaveDraftAsync(save, ct).ConfigureAwait(false);

        ChartOfAccountsNodeDto Resolve(string? path, string type)
        {
            var matches = workspace.ChartOfAccounts.Where(node => !node.IsArchived && node.Path == path &&
                string.Equals(node.AccountType, type, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(node.Symbol) &&
                (string.IsNullOrWhiteSpace(node.FinancialAccountId) || string.Equals(node.FinancialAccountId,
                    accountId.ToString("D"), StringComparison.OrdinalIgnoreCase))).ToArray();
            if (string.IsNullOrWhiteSpace(path) || matches.Length != 1)
                throw new InvalidOperationException("Correction requires one active configured cash account and fee-expense account in the selected book.");
            return matches[0];
        }
    }
}
