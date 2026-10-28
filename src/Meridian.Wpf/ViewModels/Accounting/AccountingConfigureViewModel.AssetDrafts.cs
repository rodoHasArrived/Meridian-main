using Meridian.Contracts.Ledger;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    private readonly Dictionary<Guid, ManualJournalEntryDraftDto> _loadedManualDrafts = [];
    private AccountingWorkbenchRow? _selectedManualJournalDraftRow;
    public bool CanEditManualJournalDraft => _selectedDraft?.AssetAccountingDraft is null;
    public bool IsAssetAccountingDraft => !CanEditManualJournalDraft;
    public string ManualJournalEntryHeadingText => IsAssetAccountingDraft ? "ASSET ACCOUNTING DRAFT — RETAINED LINES" : "MANUAL JOURNAL ENTRY - BALANCED DOUBLE-ENTRY";
    public string AssetAccountingDraftRestrictionText => IsAssetAccountingDraft
        ? "This asset accounting draft is read-only and may be submitted for review. Typed retained approval and atomic lot posting are unavailable; generic approval, posting and corrections are blocked."
        : string.Empty;
    public IReadOnlyList<ManualJournalEntryLineDto> AssetAccountingDraftLines => IsAssetAccountingDraft ? _selectedDraft!.Lines : [];

    public AccountingWorkbenchRow? SelectedManualJournalDraftRow
    {
        get => _selectedManualJournalDraftRow;
        set
        {
            if (!SetProperty(ref _selectedManualJournalDraftRow, value) || value is null
                || !Guid.TryParse(value.Key, out var id) || !_loadedManualDrafts.TryGetValue(id, out var draft)) return;
            _selectedDraft = draft;
            DraftMemo = draft.Memo;
            DraftCurrency = draft.Currency;
            DraftEvidenceLink = draft.EvidenceLinks.FirstOrDefault() ?? string.Empty;
            DraftAmount = draft.TotalDebits;
            DraftDebitAccountPath = draft.Lines.FirstOrDefault(line => line.Side == AccountingTemplateLineSideDto.Debit)?.AccountPath ?? string.Empty;
            DraftCreditAccountPath = draft.Lines.FirstOrDefault(line => line.Side == AccountingTemplateLineSideDto.Credit)?.AccountPath ?? string.Empty;
            SelectedAccountingBasis = draft.AccountingBasis;
            ApplyManualJournalLifecycleRows(draft);
            RaiseManualJournalDraftComputedProperties();
        }
    }

    private void RememberManualJournalDrafts(IReadOnlyList<ManualJournalEntryDraftDto> drafts)
    {
        _loadedManualDrafts.Clear();
        foreach (var draft in drafts) _loadedManualDrafts[draft.JournalEntryId] = draft;
        if (_selectedDraft is { } selected) _selectedDraft = _loadedManualDrafts.GetValueOrDefault(selected.JournalEntryId);
    }

    private bool CanApplySelectedJournalAction(JournalEntryLifecycleActionDto action)
        => CanEditManualJournalDraft || action is JournalEntryLifecycleActionDto.Validate or JournalEntryLifecycleActionDto.Submit or JournalEntryLifecycleActionDto.Reject;

    private void NotifyAssetAccountingDraftState()
    {
        RaisePropertyChanged(nameof(CanEditManualJournalDraft));
        RaisePropertyChanged(nameof(IsAssetAccountingDraft));
        RaisePropertyChanged(nameof(AssetAccountingDraftRestrictionText));
        RaisePropertyChanged(nameof(AssetAccountingDraftLines));
        RaisePropertyChanged(nameof(ManualJournalEntryHeadingText));
        SaveManualJournalDraftCommand.NotifyCanExecuteChanged();
        ApproveManualJournalCommand.NotifyCanExecuteChanged();
        PostManualJournalCommand.NotifyCanExecuteChanged();
        ReverseManualJournalCommand.NotifyCanExecuteChanged();
        RebookManualJournalCommand.NotifyCanExecuteChanged();
        LockManualJournalAfterCloseCommand.NotifyCanExecuteChanged();
    }

    private ManualJournalEntryDraftDto BuildManualJournalDraft()
    {
        if (_selectedDraft is { AssetAccountingDraft: not null } retained) return retained;
        // Save retries must rebuild identical input. The service supplies the committed update
        // time; client-generated timestamps and a new draft ID stay fixed until a save succeeds.
        var now = _selectedDraft?.UpdatedAtUtc ?? _newManualJournalDraftTimestamp;
        var fundProfileId = _activeFundProfile?.FundProfileId ?? "default-fund";
        var currency = string.IsNullOrWhiteSpace(DraftCurrency) ? "USD" : DraftCurrency.Trim().ToUpperInvariant();
        var ledgerBookId = _configuration?.LedgerBookId ?? _configuration?.LedgerBooks.FirstOrDefault()?.LedgerBookId;
        var journalEntryId = _selectedDraft?.JournalEntryId ?? _newManualJournalDraftId;
        var accountingDate = _selectedDraft?.AccountingDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        var periodId = _selectedDraft?.PeriodId
            ?? _selectedManualJournalPeriod?.PeriodId.ToString("D", CultureInfo.InvariantCulture)
            ?? CreateStableGuid(
                "manual-je-period",
                fundProfileId,
                ledgerBookId?.ToString("D") ?? "fund-level",
                accountingDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToString("D");
        var entityId = (_activeFundProfile?.EntityIds ?? []).FirstOrDefault();
        var dimensions = new LedgerDimensionSetDto(
            FundId: fundProfileId,
            EntityId: entityId,
            SleeveId: (_activeFundProfile?.SleeveIds ?? []).FirstOrDefault());
        return new ManualJournalEntryDraftDto(
            journalEntryId,
            _selectedDraft?.Status ?? ManualJournalEntryStatusDto.Draft,
            fundProfileId,
            ledgerBookId,
            SelectedAccountingBasis,
            accountingDate,
            PeriodId: periodId,
            EntityId: entityId,
            FundNodeId: null,
            currency,
            DraftMemo,
            ResolveActor(),
            _selectedDraft?.CreatedAtUtc ?? now,
            now,
            _selectedDraft?.Version ?? 0,
            [
                new ManualJournalEntryLineDto("debit-line", AccountingTemplateLineSideDto.Debit, DraftAmount, currency, DraftDebitAccountPath, EntityId: entityId, Description: "Desktop debit line", EvidenceLink: NormalizeEvidenceLink(DraftEvidenceLink).FirstOrDefault(), Dimensions: dimensions),
                new ManualJournalEntryLineDto("credit-line", AccountingTemplateLineSideDto.Credit, DraftAmount, currency, DraftCreditAccountPath, EntityId: entityId, Description: "Desktop credit line", EvidenceLink: NormalizeEvidenceLink(DraftEvidenceLink).FirstOrDefault(), Dimensions: dimensions)
            ],
            NormalizeEvidenceLink(DraftEvidenceLink),
            ValidationIssues: [],
            EvidenceAttachments: NormalizeEvidenceLink(DraftEvidenceLink)
                .Select(link => new ManualJournalEntryEvidenceAttachmentDto(
                    "desktop-evidence",
                    "Desktop evidence link",
                    "SourceDocument",
                    link,
                    "WPF",
                    now,
                    ResolveActor()))
                .ToArray(),
            EntryType: SelectedEntryType,
            TreasuryContext: BuildManualJournalTreasuryContext(fundProfileId, SelectedEntryType, accountingDate, journalEntryId),
            Dimensions: dimensions);
    }
}
