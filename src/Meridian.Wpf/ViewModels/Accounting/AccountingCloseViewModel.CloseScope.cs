using System.Runtime.CompilerServices;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingCloseViewModel
{
    private string _closeScopeFundProfileId = string.Empty;
    private string _closeScopeLedgerBookIdText = string.Empty;
    private string _closeScopeFundAccountIdText = string.Empty;
    private string _closeScopeEntityId = string.Empty;
    private string _closeScopePeriodId = string.Empty;

    public string CloseScopeFundProfileId
    {
        get => _closeScopeFundProfileId;
        set => SetCloseScopeField(ref _closeScopeFundProfileId, value);
    }

    public string CloseScopeLedgerBookIdText
    {
        get => _closeScopeLedgerBookIdText;
        set => SetCloseScopeField(ref _closeScopeLedgerBookIdText, value);
    }

    public string CloseScopeFundAccountIdText
    {
        get => _closeScopeFundAccountIdText;
        set => SetCloseScopeField(ref _closeScopeFundAccountIdText, value);
    }

    public string CloseScopeEntityId
    {
        get => _closeScopeEntityId;
        set => SetCloseScopeField(ref _closeScopeEntityId, value);
    }

    public string CloseScopePeriodId
    {
        get => _closeScopePeriodId;
        set => SetCloseScopeField(ref _closeScopePeriodId, value);
    }

    public string CloseScopeStatusText => TryGetDeclaredCloseScope(out _, out var reason)
        ? "Close scope declared. Shared readiness will verify this fund, book, account, entity, and period before locking."
        : reason;

    /// <summary>Receives the operator's explicit subject selection from navigation or the scope inputs.</summary>
    public void ApplyCloseScope(CloseReadinessScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        CloseScopeFundProfileId = scope.FundProfileId ?? string.Empty;
        CloseScopeLedgerBookIdText = scope.LedgerBookId?.ToString("D") ?? string.Empty;
        CloseScopeFundAccountIdText = scope.FundAccountId?.ToString("D") ?? string.Empty;
        CloseScopeEntityId = scope.EntityId ?? string.Empty;
        CloseScopePeriodId = scope.PeriodId ?? string.Empty;
    }

    private void SetCloseScopeField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value ?? string.Empty, propertyName))
        {
            return;
        }

        InvalidatePendingCloseScopeResponses();
        RaisePropertyChanged(nameof(CloseScopeStatusText));
        if (_closePlan is { IsPeriodLocked: false } closePlan)
        {
            ClosePeriodLockStatusText = ResolveClosePeriodLockStatus(closePlan);
        }

        LockClosePeriodCommand.NotifyCanExecuteChanged();
        RefreshCloseWorkflowSteps();
    }

    private bool TryGetDeclaredCloseScope(out CloseReadinessScopeDto? scope, out string reason)
    {
        scope = null;
        reason = "Declare the fund profile, ledger book, fund account, entity, and period before locking the close.";
        if (string.IsNullOrWhiteSpace(CloseScopeFundProfileId) ||
            string.IsNullOrWhiteSpace(CloseScopeEntityId) ||
            string.IsNullOrWhiteSpace(CloseScopePeriodId) ||
            string.IsNullOrWhiteSpace(CloseScopeLedgerBookIdText) ||
            string.IsNullOrWhiteSpace(CloseScopeFundAccountIdText))
        {
            return false;
        }

        if (!Guid.TryParse(CloseScopeLedgerBookIdText, out var bookId) || bookId == Guid.Empty ||
            !Guid.TryParse(CloseScopeFundAccountIdText, out var accountId) || accountId == Guid.Empty)
        {
            reason = "Enter valid ledger book and fund account identifiers before locking the close.";
            return false;
        }

        if (_closePlan?.LedgerBookId is { } planBookId && planBookId != bookId)
        {
            reason = "The declared ledger book does not match the loaded close plan. Correct the scope or load the matching workflow.";
            return false;
        }

        if (_closePlan is { } plan && !string.Equals(plan.PeriodId, CloseScopePeriodId.Trim(), StringComparison.Ordinal))
        {
            reason = "The declared period does not match the loaded close plan. Correct the scope or load the matching workflow.";
            return false;
        }

        // The legacy plan field may contain the account GUID. It cannot establish the fund profile.
        if (_closePlan is { } accountPlan && Guid.TryParse(accountPlan.FundProfileId, out var planAccountId) &&
            planAccountId != accountId)
        {
            reason = "The declared fund account does not match the loaded close plan. Correct the scope or load the matching workflow.";
            return false;
        }

        scope = new CloseReadinessScopeDto(
            CloseScopeFundProfileId.Trim(), bookId, accountId, CloseScopeEntityId.Trim(), CloseScopePeriodId.Trim());
        reason = string.Empty;
        return true;
    }
}
