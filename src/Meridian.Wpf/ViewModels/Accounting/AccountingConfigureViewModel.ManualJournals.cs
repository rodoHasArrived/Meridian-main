using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Contracts.Integrity;
using UtcFormat = Meridian.Ui.Services.FormatHelpers;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    public async Task SaveManualJournalDraftAsync(CancellationToken ct = default)
    {
        if (!CanEditManualJournalDraft)
        { ManualJournalStatusText = AssetAccountingDraftRestrictionText; return; }
        if (_activeFundProfile is null)
        {
            ManualJournalStatusText = "Select a fund-linked context before saving a manual journal draft.";
            return;
        }

        try
        {
            var saved = await _manualJournalEntryWorkbenchService.SaveDraftAsync(
                new SaveManualJournalEntryDraftRequest(
                    BuildManualJournalDraft(),
                    ResolveActor(),
                    CorrelationId: "wpf-manual-je-save",
                    EvidenceLinks: NormalizeEvidenceLink(DraftEvidenceLink),
                    LedgerBookId: _configuration?.LedgerBookId),
                ct).ConfigureAwait(false);
            _selectedDraft = saved;
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            ManualJournalStatusText = $"Draft {saved.JournalEntryId:D} saved as {saved.Status}; {saved.ValidationIssues.Count} validation issue(s).";
            RaisePropertyChanged(nameof(CanSubmitManualJournal));
            RaiseManualJournalDraftComputedProperties();
        }
        catch (Exception ex)
        {
            ManualJournalStatusText = $"Manual journal draft could not be saved: {ex.Message}";
        }
    }

    public async Task ValidateManualJournalDraftAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ManualJournalStatusText = "Select a fund-linked context before validating a manual journal draft.";
            return;
        }

        try
        {
            var validated = await _manualJournalEntryWorkbenchService.ValidateDraftAsync(
                new ValidateManualJournalEntryDraftRequest(
                    BuildManualJournalDraft(),
                    ResolveActor(),
                    CorrelationId: "wpf-manual-je-validate",
                    LedgerBookId: _configuration?.LedgerBookId),
                ct).ConfigureAwait(false);
            ManualJournalStatusText = AccountingWorkbenchPresentation.BuildManualJournalValidationText(validated);
            ApplyManualJournalValidationRows(validated.ValidationIssues);
        }
        catch (Exception ex)
        {
            ManualJournalStatusText = $"Manual journal validation failed: {ex.Message}";
        }
    }

    public async Task SubmitManualJournalDraftAsync(CancellationToken ct = default)
    {
        if (_selectedDraft is null)
        {
            ManualJournalStatusText = "Save the manual journal draft before submitting approval.";
            return;
        }

        try
        {
            var submitted = await _manualJournalEntryWorkbenchService.SubmitApprovalAsync(
                new SubmitManualJournalEntryApprovalRequest(
                    _selectedDraft.JournalEntryId,
                    _selectedDraft.FundProfileId,
                    ResolveActor(),
                    _selectedDraft.Version,
                    Notes: "Submitted from WPF Accounting Configure.",
                    CorrelationId: "wpf-manual-je-submit",
                    EvidenceLinks: NormalizeEvidenceLink(DraftEvidenceLink),
                    LedgerBookId: _selectedDraft.LedgerBookId),
                ct).ConfigureAwait(false);
            _selectedDraft = submitted;
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            ManualJournalStatusText = $"Draft submitted for approval as {submitted.ApprovalId}.";
            RaisePropertyChanged(nameof(CanSubmitManualJournal));
            RaiseManualJournalDraftComputedProperties();
        }
        catch (Exception ex)
        {
            ManualJournalStatusText = $"Manual journal approval submission is blocked: {ex.Message}";
        }
    }

    public async Task ApplyManualJournalLifecycleActionAsync(
        JournalEntryLifecycleActionDto action,
        CancellationToken ct = default)
    {
        if (!CanApplySelectedJournalAction(action))
        { ManualJournalLifecycleStatusText = AssetAccountingDraftRestrictionText; return; }
        if (_manualJournalEntryLifecycleService is null)
        {
            ManualJournalLifecycleStatusText = "Manual journal lifecycle service is not registered for this desktop session.";
            return;
        }

        if (_selectedDraft is null)
        {
            ManualJournalLifecycleStatusText = "Save or select a manual journal draft before applying lifecycle actions.";
            return;
        }

        try
        {
            var request = BuildManualJournalLifecycleRequest(_selectedDraft, action);
            var result = await _manualJournalEntryLifecycleService
                .ApplyLifecycleActionAsync(request, ct)
                .ConfigureAwait(false);
            _selectedDraft = result.JournalEntry;
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            ApplyManualJournalLifecycleRows(result);
            ManualJournalLifecycleStatusText =
                $"{action} moved journal {_selectedDraft.JournalEntryId:D} from {result.Transition.FromStatus} to {result.Transition.ToStatus}; {result.GeneratedJournalEntries.Count} correction draft(s) generated.";
            ManualJournalStatusText = $"Manual journal lifecycle action {action} completed as {_selectedDraft.Status}.";
            RaisePropertyChanged(nameof(CanSubmitManualJournal));
            RaiseManualJournalDraftComputedProperties();
        }
        catch (Exception ex)
        {
            ManualJournalLifecycleStatusText = $"Manual journal lifecycle action {action} is blocked: {ex.Message}";
            ManualJournalLifecycleRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    action.ToString(),
                    "Blocked",
                    ex.Message,
                    "No lifecycle transition was written; approval, posting, correction, and close-lock controls remain enforced.",
                    _selectedDraft.JournalEntryId.ToString("D"))
            ]);
        }
    }

    private JournalEntryLifecycleActionRequestDto BuildManualJournalLifecycleRequest(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action)
    {
        var notes = action switch
        {
            JournalEntryLifecycleActionDto.Approve => "Approved from WPF Accounting Configure with retained reviewer evidence.",
            JournalEntryLifecycleActionDto.Post => "Posted from WPF Accounting Configure after approval review.",
            JournalEntryLifecycleActionDto.Reverse => "Reverse from WPF Accounting Configure with retained correction review.",
            JournalEntryLifecycleActionDto.Rebook => "Rebook from WPF Accounting Configure with retained correction review.",
            JournalEntryLifecycleActionDto.LockAfterClose => "Close lock from WPF Accounting Configure with retained period-lock evidence.",
            _ => $"Lifecycle action {action} from WPF Accounting Configure."
        };
        return new JournalEntryLifecycleActionRequestDto(
            draft.JournalEntryId,
            draft.FundProfileId,
            action,
            DefaultLifecycleActor,
            draft.Version,
            notes,
            CorrelationId: $"wpf-manual-je-{action.ToString().ToLowerInvariant()}",
            EvidenceLinks: AccountingWorkbenchPresentation.BuildManualJournalLifecycleEvidenceLinks(draft, action),
            RebookLines: action == JournalEntryLifecycleActionDto.Rebook
                ? AccountingWorkbenchPresentation.BuildManualJournalRebookLines(draft)
                : [],
            LedgerBookId: draft.LedgerBookId);
    }

    private void ApplyManualJournalLifecycleRows(JournalEntryLifecycleActionResultDto result)
    {
        var rows = new List<AccountingWorkbenchRow>
        {
            new(
                result.Transition.Action.ToString(),
                $"{result.Transition.FromStatus} -> {result.Transition.ToStatus}",
                $"{result.Transition.Actor} at {UtcFormat.FormatUtcTimestamp(result.Transition.RecordedAtUtc)}",
                $"{result.Transition.EvidenceLinks.Count} evidence link(s); {result.Transition.Notes ?? "no notes"}",
                result.Transition.TransitionId)
        };
        rows.AddRange(result.GeneratedJournalEntries.Select(entry =>
            new AccountingWorkbenchRow(
                entry.JournalEntryId.ToString("D"),
                entry.Status.ToString(),
                $"{FormatManualJournalEntryType(entry.EntryType)} | {entry.Memo}",
                $"{entry.TotalDebits:0.##} / {entry.TotalCredits:0.##} {entry.Currency}; reversal {entry.ReversalOfJournalEntryId?.ToString("D") ?? "none"}; rebook {entry.RebookedFromJournalEntryId?.ToString("D") ?? "none"}",
                entry.JournalEntryId.ToString("D"))));
        rows.AddRange((result.JournalEntry.LifecycleTransitions ?? [])
            .Reverse()
            .Take(8)
            .Select(transition => new AccountingWorkbenchRow(
                transition.Action.ToString(),
                $"{transition.FromStatus} -> {transition.ToStatus}",
                $"{transition.Actor} at {UtcFormat.FormatUtcTimestamp(transition.RecordedAtUtc)}",
                $"{transition.EvidenceLinks.Count} evidence link(s); {transition.Notes ?? "no notes"}",
                transition.TransitionId)));

        ManualJournalLifecycleRows.ReplaceWith(rows);
    }

    private async Task LoadManualJournalWorkbenchAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            return;
        }

        var workbench = await _manualJournalEntryWorkbenchService
            .GetWorkbenchAsync(_activeFundProfile.FundProfileId, _configuration?.LedgerBookId, ct)
            .ConfigureAwait(false);

        RememberManualJournalDrafts(workbench.Drafts);
        ManualJournalDraftRows.ReplaceWith(workbench.Drafts.Select(draft =>
            new AccountingWorkbenchRow(
                $"{FormatManualJournalEntryType(draft.EntryType)} | {draft.Memo}",
                draft.Status.ToString(),
                $"{draft.TotalDebits:0.##} / {draft.TotalCredits:0.##} {draft.Currency}",
                $"{draft.ValidationIssues.Count} issue(s) | v{draft.Version}",
                draft.JournalEntryId.ToString("D"))));
        ApplyPrivateCapitalActivityRows(workbench.PrivateCapitalActivity);
        await LoadCapitalAccountWorkbenchAsync(ct).ConfigureAwait(false);
        _selectedDraft ??= workbench.Drafts.FirstOrDefault();
        var entityOptions = (_activeFundProfile.EntityIds ?? [])
            .Concat(workbench.Drafts.Where(draft => draft.FundProfileId == _activeFundProfile.FundProfileId
                && draft.LedgerBookId == _configuration?.LedgerBookId).Select(draft => draft.EntityId))
            .Where(entity => !string.IsNullOrWhiteSpace(entity)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        RecurringJournalEntityOptions.ReplaceWith(entityOptions);
        _recurringJournalFundProfileId = _activeFundProfile.FundProfileId;
        _recurringJournalLedgerBookId = _configuration?.LedgerBookId;
        var entityId = entityOptions.Contains(SelectedRecurringJournalEntityId, StringComparer.Ordinal)
            ? SelectedRecurringJournalEntityId : entityOptions.Length == 1 ? entityOptions[0] : null;
        SetProperty(ref _selectedRecurringJournalEntityId, entityId, nameof(SelectedRecurringJournalEntityId));
        await LoadGovernedQueuesAsync(entityId, ct).ConfigureAwait(false);
        ApplyManualJournalLifecycleRows(_selectedDraft);
        ManualJournalStatusText = AccountingWorkbenchPresentation.BuildManualJournalStatusText(workbench);
        RaisePropertyChanged(nameof(CanSubmitManualJournal));
        RaiseManualJournalDraftComputedProperties();
    }

    private void ApplyManualJournalLifecycleRows(ManualJournalEntryDraftDto? draft)
    {
        if (draft is null)
        {
            ManualJournalLifecycleRows.Clear();
            return;
        }

        ManualJournalLifecycleRows.ReplaceWith(draft.LifecycleTransitions
            .Reverse()
            .Take(10)
            .Select(transition => new AccountingWorkbenchRow(
                transition.Action.ToString(),
                $"{transition.FromStatus} -> {transition.ToStatus}",
                $"{transition.Actor} at {UtcFormat.FormatUtcTimestamp(transition.RecordedAtUtc)}",
                $"{transition.EvidenceLinks.Count} evidence link(s); {transition.Notes ?? "no notes"}",
                transition.TransitionId)));
    }

    private void RaiseManualJournalDraftComputedProperties()
    {
        NotifyAssetAccountingDraftState();
        RaisePropertyChanged(nameof(ManualJournalDraftReferenceText));
        RaisePropertyChanged(nameof(ManualJournalDraftDateText));
        RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
        RaisePropertyChanged(nameof(ManualJournalDraftBalanceText));
        RaisePropertyChanged(nameof(ManualJournalDraftBalanceBadgeText));
    }

    private async Task LoadManualJournalPeriodAsync(CancellationToken ct)
    {
        _selectedManualJournalPeriod = null;
        if (_ledgerBookService is null || _configuration is null)
        {
            RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
            return;
        }

        var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId ?? _configuration.LedgerBookId;
        if (!ledgerBookId.HasValue)
        {
            RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var openPeriods = await _ledgerBookService.ListOpenPeriodsAsync(ledgerBookId.Value, ct).ConfigureAwait(false);
        _selectedManualJournalPeriod = openPeriods
            .OrderByDescending(period => period.StartDate <= today && today <= period.EndDate)
            .ThenByDescending(period => period.StartDate)
            .ThenByDescending(period => period.PeriodNo)
            .FirstOrDefault();
        RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
    }

    private void ApplyManualJournalEntryTypePreset(ManualJournalEntryTypeDto entryType)
    {
        var preset = GetManualJournalEntryTypePreset(entryType);
        DraftMemo = preset.Memo;
        DraftDebitAccountPath = preset.DebitAccountPath;
        DraftCreditAccountPath = preset.CreditAccountPath;
        DraftEvidenceLink = preset.EvidenceLink;
    }

    private static IReadOnlyList<ChartOfAccountsNodeDto> BuildManualJournalEntryTypeChartNodes()
        =>
        [
            new ChartOfAccountsNodeDto("liabilities-accrued-expenses", "Liabilities:Accrued Expenses", "Accrued Expenses", "Liability"),
            new ChartOfAccountsNodeDto("assets-prepaid-expenses", "Assets:Prepaid Expenses", "Prepaid Expenses", "Asset"),
            new ChartOfAccountsNodeDto("expenses-operating-expenses", "Expenses:Operating Expenses", "Operating Expenses", "Expense"),
            new ChartOfAccountsNodeDto("expenses-amortization-expense", "Expenses:Amortization Expense", "Amortization Expense", "Expense"),
            new ChartOfAccountsNodeDto("assets-accumulated-amortization", "Assets:Accumulated Amortization", "Accumulated Amortization", "ContraAsset"),
            new ChartOfAccountsNodeDto("liabilities-deferred-revenue", "Liabilities:Deferred Revenue", "Deferred Revenue", "Liability"),
            new ChartOfAccountsNodeDto("equity-capital-contributions", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
            new ChartOfAccountsNodeDto("equity-distributions", "Equity:Distributions", "Distributions", "Equity"),
            new ChartOfAccountsNodeDto("assets-subscription-receivable", "Assets:Subscription Receivable", "Subscription Receivable", "Asset"),
            new ChartOfAccountsNodeDto("equity-redemptions", "Equity:Redemptions", "Redemptions", "Equity"),
            new ChartOfAccountsNodeDto("equity-lp-transfer-out", "Equity:LP Transfer Out", "LP Transfer Out", "Equity"),
            new ChartOfAccountsNodeDto("equity-lp-transfer-in", "Equity:LP Transfer In", "LP Transfer In", "Equity"),
            new ChartOfAccountsNodeDto("expenses-management-fees", "Expenses:Management Fees", "Management Fees", "Expense")
        ];

    private static IReadOnlyList<JournalEntryTemplateDto> BuildManualJournalEntryTypeTemplates(string currency)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        return ManualJournalEntryTypePresets
            .Where(preset => preset.EntryType != ManualJournalEntryTypeDto.General)
            .Select(preset => new JournalEntryTemplateDto(
                GetManualJournalEntryTemplateId(preset.EntryType),
                $"{preset.Label} desktop template",
                preset.Description,
                [
                    new JournalEntryTemplateLineDto(
                        $"{GetManualJournalEntrySlug(preset.EntryType)}-debit",
                        preset.DebitAccountPath,
                        AccountingTemplateLineSideDto.Debit,
                        1m,
                        normalizedCurrency,
                        $"Debit {preset.DebitAccountPath}."),
                    new JournalEntryTemplateLineDto(
                        $"{GetManualJournalEntrySlug(preset.EntryType)}-credit",
                        preset.CreditAccountPath,
                        AccountingTemplateLineSideDto.Credit,
                        1m,
                        normalizedCurrency,
                        $"Credit {preset.CreditAccountPath}.")
                ]))
            .ToArray();
    }

    private static IReadOnlyList<PostingRuleDto> BuildManualJournalEntryTypePostingRules()
        => ManualJournalEntryTypePresets
            .Where(preset => preset.EntryType != ManualJournalEntryTypeDto.General)
            .Select(preset => new PostingRuleDto(
                GetManualJournalEntryPostingRuleId(preset.EntryType),
                $"{preset.Label} policy",
                $"ManualJournalEntry.{preset.EntryType}",
                GetManualJournalEntryTemplateId(preset.EntryType),
                Description: $"Fund/book configurable WPF policy route for {preset.Label.ToLowerInvariant()} manual journal entries."))
            .ToArray();

    private static ManualJournalEntryTypePreset GetManualJournalEntryTypePreset(ManualJournalEntryTypeDto entryType)
        => ManualJournalEntryTypePresets.FirstOrDefault(preset => preset.EntryType == entryType)
            ?? ManualJournalEntryTypePresets[0];

    private static string FormatManualJournalEntryType(ManualJournalEntryTypeDto entryType)
        => GetManualJournalEntryTypePreset(entryType).Label;

    private static string GetManualJournalEntryTemplateId(ManualJournalEntryTypeDto entryType)
        => entryType == ManualJournalEntryTypeDto.General
            ? "desktop-manual-adjustment-v1"
            : $"desktop-{GetManualJournalEntrySlug(entryType)}-v1";

    private static string GetManualJournalEntryPostingRuleId(ManualJournalEntryTypeDto entryType)
        => entryType == ManualJournalEntryTypeDto.General
            ? "manual-adjustment-policy-v1"
            : $"manual-{GetManualJournalEntrySlug(entryType)}-policy-v1";

    private static string GetManualJournalEntrySlug(ManualJournalEntryTypeDto entryType)
        => entryType switch
        {
            ManualJournalEntryTypeDto.AccruedBalance => "accrued-balance",
            ManualJournalEntryTypeDto.AccruedExpense => "accrued-expense",
            ManualJournalEntryTypeDto.PrepaidExpense => "prepaid-expense",
            ManualJournalEntryTypeDto.Expense => "expense",
            ManualJournalEntryTypeDto.Amortization => "amortization",
            ManualJournalEntryTypeDto.Deferral => "deferral",
            ManualJournalEntryTypeDto.Reclassification => "reclassification",
            ManualJournalEntryTypeDto.Reversal => "reversal",
            ManualJournalEntryTypeDto.CapitalCall => "capital-call",
            ManualJournalEntryTypeDto.Distribution => "distribution",
            ManualJournalEntryTypeDto.Subscription => "subscription",
            ManualJournalEntryTypeDto.Redemption => "redemption",
            ManualJournalEntryTypeDto.LpTransfer => "lp-transfer",
            ManualJournalEntryTypeDto.ManagementFee => "management-fee",
            _ => "manual-adjustment"
        };

    private static TreasuryLedgerContextDto? BuildManualJournalTreasuryContext(
        string fundProfileId,
        ManualJournalEntryTypeDto entryType,
        DateOnly accountingDate,
        Guid journalEntryId)
    {
        if (!RequiresPrivateCapitalTreasuryContext(entryType))
        {
            return null;
        }

        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? "default-fund" : fundProfileId.Trim();
        var slug = GetManualJournalEntrySlug(entryType);
        var dateToken = accountingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var entryToken = journalEntryId.ToString("N");
        return new TreasuryLedgerContextDto(
            EffectiveDate: accountingDate,
            IdempotencyKey: $"wpf:{normalizedFundProfileId}:{slug}:{entryToken}",
            FundEventId: $"fund-event:{normalizedFundProfileId}:{slug}:{dateToken}",
            FundEventType: entryType.ToString(),
            CapitalAccountId: $"capital-account:{normalizedFundProfileId}:default",
            InvestorId: $"investor:{normalizedFundProfileId}:default",
            PaymentIntentId: RequiresPaymentLinkage(entryType) ? $"payment:{normalizedFundProfileId}:{slug}:{entryToken}" : null,
            SettlementReference: RequiresPaymentLinkage(entryType) ? $"settlement:{normalizedFundProfileId}:{slug}:{dateToken}" : null);
    }

    private static bool RequiresPrivateCapitalTreasuryContext(ManualJournalEntryTypeDto entryType)
        => entryType is ManualJournalEntryTypeDto.CapitalCall
            or ManualJournalEntryTypeDto.Distribution
            or ManualJournalEntryTypeDto.Subscription
            or ManualJournalEntryTypeDto.Redemption
            or ManualJournalEntryTypeDto.LpTransfer
            or ManualJournalEntryTypeDto.ManagementFee;

    private static bool RequiresPaymentLinkage(ManualJournalEntryTypeDto entryType)
        => entryType is ManualJournalEntryTypeDto.CapitalCall
            or ManualJournalEntryTypeDto.Distribution
            or ManualJournalEntryTypeDto.Redemption
            or ManualJournalEntryTypeDto.ManagementFee;

    private static AccountingTreatmentKindDto MapPostingCandidateTreatment(ManualJournalEntryTypeDto entryType)
        => entryType switch
        {
            ManualJournalEntryTypeDto.AccruedBalance or ManualJournalEntryTypeDto.AccruedExpense => AccountingTreatmentKindDto.Accrual,
            ManualJournalEntryTypeDto.PrepaidExpense => AccountingTreatmentKindDto.PrepaidExpense,
            ManualJournalEntryTypeDto.Expense or ManualJournalEntryTypeDto.ManagementFee => AccountingTreatmentKindDto.Expense,
            ManualJournalEntryTypeDto.Amortization => AccountingTreatmentKindDto.Amortization,
            ManualJournalEntryTypeDto.Deferral => AccountingTreatmentKindDto.Deferral,
            ManualJournalEntryTypeDto.Reclassification => AccountingTreatmentKindDto.Reclassification,
            ManualJournalEntryTypeDto.Reversal => AccountingTreatmentKindDto.Reversal,
            _ => AccountingTreatmentKindDto.General
        };

    private void ApplyManualJournalValidationRows(IReadOnlyList<AccountingConfigurationValidationIssueDto> issues)
    {
        ValidationRows.ReplaceWith(issues.Select(issue =>
            new AccountingWorkbenchRow(issue.Code, issue.Severity.ToString(), issue.Message, issue.SuggestedAction ?? string.Empty, issue.TargetId ?? string.Empty)));
    }
}
