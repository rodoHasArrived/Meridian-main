using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// The loaded book's opt-in confirmation close policy (ADR-032), over the same routes as the browser. Without an active
/// policy, confirmation stays advisory; one operator proposes the blocking findings and a different one approves.
/// </summary>
public sealed partial class IntercompanyBalanceConfirmationWorkspace
{
    private const string NoPolicy = "No active close policy: confirmation is advisory and close eligibility is unchanged.";
    private string _closePolicyReason = "", _closePolicyText = "";
    private IntercompanyConfirmationClosePolicyDto? _openPolicy;
    private IAsyncRelayCommand? _proposeClosePolicy, _approveClosePolicy, _rejectClosePolicy, _disableClosePolicy;

    /// <summary>Findings a policy may make blocking; "disputed" is preselected.</summary>
    public ObservableCollection<IntercompanyClosePolicyCodeChoice> BlockingCodeChoices { get; } = [.. new (string Code, string Label)[]
    {
        ("not-proposed", "No confirmation proposed"), ("awaiting-confirmation", "Awaiting the other side"), ("disputed", "Disputed"),
        ("balances-changed", "Balances changed since confirmation"), ("resolved-awaiting-proposal", "Resolved, awaiting a new proposal")
    }.Select(item => new IntercompanyClosePolicyCodeChoice(item.Code, item.Label, item.Code == "disputed"))];
    public string ClosePolicyReason { get => _closePolicyReason; set { if (SetProperty(ref _closePolicyReason, value ?? "")) NotifyClosePolicyCommands(); } }
    /// <summary>The loaded book's policy position in words.</summary>
    public string ClosePolicyText { get => _closePolicyText; private set => SetProperty(ref _closePolicyText, value); }
    /// <summary>The book's proposed or active policy, if any.</summary>
    public IntercompanyConfirmationClosePolicyDto? OpenClosePolicy { get => _openPolicy; private set { if (SetProperty(ref _openPolicy, value)) NotifyClosePolicyCommands(); } }
    public IAsyncRelayCommand ProposeClosePolicyCommand => _proposeClosePolicy ??= new AsyncRelayCommand(ProposeClosePolicyAsync,
        () => !IsBusy && CanManageLedger && Loaded && OpenClosePolicy is null && BlockingCodeChoices.Any(item => item.IsSelected) && !string.IsNullOrWhiteSpace(ClosePolicyReason));
    public IAsyncRelayCommand ApproveClosePolicyCommand => _approveClosePolicy ??= new AsyncRelayCommand(() => DecideClosePolicyAsync(IntercompanyConfirmationClosePolicyActionDto.Approve),
        () => !IsBusy && CanManageLedger && OpenClosePolicy?.Status == IntercompanyConfirmationClosePolicyStatusDto.Proposed);
    public IAsyncRelayCommand RejectClosePolicyCommand => _rejectClosePolicy ??= new AsyncRelayCommand(() => DecideClosePolicyAsync(IntercompanyConfirmationClosePolicyActionDto.Reject),
        () => !IsBusy && CanManageLedger && OpenClosePolicy?.Status == IntercompanyConfirmationClosePolicyStatusDto.Proposed && !string.IsNullOrWhiteSpace(ClosePolicyReason));
    public IAsyncRelayCommand DisableClosePolicyCommand => _disableClosePolicy ??= new AsyncRelayCommand(() => DecideClosePolicyAsync(IntercompanyConfirmationClosePolicyActionDto.Disable),
        () => !IsBusy && CanManageLedger && OpenClosePolicy?.Status == IntercompanyConfirmationClosePolicyStatusDto.Active && !string.IsNullOrWhiteSpace(ClosePolicyReason));

    private async Task LoadClosePolicyAsync(Guid book, CancellationToken token)
    {
        var response = await _client.GetConfirmationClosePoliciesAsync(book, token);
        token.ThrowIfCancellationRequested();
        if (!response.Success || response.Data is null)
        {
            OpenClosePolicy = null;
            ClosePolicyText = $"The book's confirmation close policy could not be read: {response.ErrorMessage ?? "unavailable."} Close readiness reports its own position.";
            return;
        }
        ShowClosePolicy(response.Data.FirstOrDefault(item => item.Status is IntercompanyConfirmationClosePolicyStatusDto.Proposed or IntercompanyConfirmationClosePolicyStatusDto.Active));
    }

    private void ShowClosePolicy(IntercompanyConfirmationClosePolicyDto? policy)
    {
        OpenClosePolicy = policy;
        ClosePolicyText = policy switch
        {
            null => NoPolicy,
            { Status: IntercompanyConfirmationClosePolicyStatusDto.Active } => $"Active close policy v{policy.Version} since {policy.ActiveFrom:yyyy-MM-dd}: "
                + $"{string.Join(", ", policy.BlockingCodes)} block close for this book. Confirmation status that cannot be read also stops close.",
            _ => $"Proposed close policy v{policy.Version} would block {string.Join(", ", policy.BlockingCodes)}. It blocks nothing until a different reviewer approves it."
        };
    }

    private Task ProposeClosePolicyAsync() => RunAsync(async token =>
    {
        var codes = BlockingCodeChoices.Where(item => item.IsSelected).Select(item => item.Code).ToArray();
        var reason = ClosePolicyReason.Trim();
        var request = new ProposeIntercompanyConfirmationClosePolicyRequest(Identity($"close-policy|{_loadedBook:D}|{string.Join(',', codes)}|{reason}"),
            _loadedBook, codes, reason);
        var proposed = Require(await _client.ProposeConfirmationClosePolicyAsync(request, token));
        RetireIdentity();
        ClosePolicyReason = "";
        ShowClosePolicy(proposed);
        StatusText = "Close policy proposed. It blocks nothing until a different reviewer approves it.";
    });

    private Task DecideClosePolicyAsync(IntercompanyConfirmationClosePolicyActionDto action) => RunAsync(async token =>
    {
        var policy = OpenClosePolicy!;
        var reason = action == IntercompanyConfirmationClosePolicyActionDto.Approve ? null : ClosePolicyReason.Trim();
        var request = new DecideIntercompanyConfirmationClosePolicyRequest(Identity($"close-policy|{policy.PolicyId:D}|{policy.Version}|{action}|{reason}"),
            action, policy.Version, reason);
        var decided = Require(await _client.DecideConfirmationClosePolicyAsync(policy.PolicyId, _loadedBook, request, token));
        RetireIdentity();
        ClosePolicyReason = "";
        ShowClosePolicy(decided.Status is IntercompanyConfirmationClosePolicyStatusDto.Proposed or IntercompanyConfirmationClosePolicyStatusDto.Active ? decided : null);
        StatusText = action switch
        {
            IntercompanyConfirmationClosePolicyActionDto.Approve => "Close policy approved. The listed confirmation findings now block close for this book.",
            IntercompanyConfirmationClosePolicyActionDto.Disable => "Close policy disabled. Confirmation is advisory again for this book.",
            _ => "Close policy rejected."
        };
    });

    private void ClearClosePolicy()
    {
        OpenClosePolicy = null;
        ClosePolicyText = "";
    }

    private void NotifyClosePolicyCommands()
    {
        _proposeClosePolicy?.NotifyCanExecuteChanged();
        _approveClosePolicy?.NotifyCanExecuteChanged();
        _rejectClosePolicy?.NotifyCanExecuteChanged();
        _disableClosePolicy?.NotifyCanExecuteChanged();
    }
}

/// <summary>One confirmation finding a close policy can block, with whether this proposal includes it.</summary>
public sealed partial class IntercompanyClosePolicyCodeChoice(string code, string label, bool isSelected) : ObservableObject
{
    private bool _isSelected = isSelected;
    public string Code { get; } = code;
    public string Label { get; } = label;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
