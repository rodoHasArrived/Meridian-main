using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Correction after a resolved dispute (DEC-IC-CONFIRM-003), over the browser's route: a balanced draft on this book's
/// mapped intercompany account, retained in the journal workbench for ordinary review. Nothing is posted here.
/// </summary>
public sealed partial class IntercompanyBalanceConfirmationWorkspace
{
    private string _adjustmentRole = nameof(IntercompanyAccountRoleDto.Receivable), _adjustmentAmount = "", _adjustmentOffset = "", _adjustmentDate = "";
    private IAsyncRelayCommand? _draftAdjustment;

    /// <summary>The choices for the intercompany account to correct.</summary>
    public IReadOnlyList<IntercompanyChoiceRow> AdjustmentRoles { get; } =
        [new(nameof(IntercompanyAccountRoleDto.Receivable), "Due from (receivable)"), new(nameof(IntercompanyAccountRoleDto.Payable), "Due to (payable)")];
    public string AdjustmentRole { get => _adjustmentRole; set { if (SetProperty(ref _adjustmentRole, value ?? "")) NotifyCommands(); } }
    /// <summary>Agreement currency, invariant decimal. Positive debits the intercompany account, negative credits it.</summary>
    public string AdjustmentAmount { get => _adjustmentAmount; set { if (SetProperty(ref _adjustmentAmount, value ?? "")) NotifyCommands(); } }
    /// <summary>A non-intercompany chart node of this book.</summary>
    public string AdjustmentOffsetAccount { get => _adjustmentOffset; set { if (SetProperty(ref _adjustmentOffset, value ?? "")) NotifyCommands(); } }
    /// <summary>Accounting date (yyyy-MM-dd); empty uses the confirmation date.</summary>
    public string AdjustmentDate { get => _adjustmentDate; set => SetProperty(ref _adjustmentDate, value ?? ""); }
    public IAsyncRelayCommand DraftAdjustmentCommand => _draftAdjustment ??= new AsyncRelayCommand(DraftAdjustmentAsync,
        () => Actionable && SelectedConfirmation!.Status == IntercompanyBalanceConfirmationStatusDto.Resolved
            && TryAmount(out var amount) && amount != 0m && !string.IsNullOrWhiteSpace(AdjustmentOffsetAccount));

    private Task DraftAdjustmentAsync() => RunAsync(async token =>
    {
        var row = SelectedConfirmation!;
        if (!TryAmount(out var amount) || !Enum.TryParse<IntercompanyAccountRoleDto>(AdjustmentRole, out var role))
            throw new InvalidOperationException("Enter an invariant decimal amount and choose the account to correct.");
        var date = string.IsNullOrWhiteSpace(AdjustmentDate) ? row.Confirmation.AsOf : Date(AdjustmentDate);
        var key = $"adjust|{row.ConfirmationId:D}|{row.Version}|{row.ThisBookParty}|{role}|{amount.ToString(CultureInfo.InvariantCulture)}|{AdjustmentOffsetAccount.Trim()}|{date:yyyy-MM-dd}";
        var request = new CreateIntercompanyConfirmationAdjustmentDraftRequest(Identity(key), row.ThisBookParty, row.Version, row.BalanceDigest,
            role, amount, AdjustmentOffsetAccount.Trim(), date);
        var draft = Require(await _client.CreateConfirmationAdjustmentDraftAsync(row.ConfirmationId, request, token));
        RetireIdentity();
        AdjustmentAmount = "";
        StatusText = $"Adjustment draft {draft.JournalEntryId:D} ({draft.Status}) retained in this book's journal entry review queue. Nothing was submitted or posted; after it posts, pin the balances again.";
    });

    private bool TryAmount(out decimal amount)
        => decimal.TryParse(AdjustmentAmount.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
}
