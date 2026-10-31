using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FixedIncome;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Settings;

/// <summary>One active bond in the cutover inventory with the operator's reviewed block reason.</summary>
public sealed class BondCutoverRowViewModel : BindableBase
{
    private string _blockReason = string.Empty;

    public BondCutoverRowViewModel(BondMigrationInventoryRowDto row, Action onReasonChanged)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        OnReasonChanged = onReasonChanged ?? throw new ArgumentNullException(nameof(onReasonChanged));
    }

    public BondMigrationInventoryRowDto Row { get; }
    private Action OnReasonChanged { get; }
    public string DisplayName => Row.DisplayName;
    public long Version => Row.Version;
    public string ContentHash => Row.SecurityContentSha256;
    public bool IsUndeclared => !Row.HasDeclaredConventions;
    public string DeclarationText => Row.HasDeclaredConventions ? "Declared" : Row.BlockedReason ?? "Undeclared";
    public string BlockPrompt => Row.HasDeclaredConventions ? "Optional explicit block" : "Required before cutover";

    public string BlockReason
    {
        get => _blockReason;
        set { if (SetProperty(ref _blockReason, value ?? string.Empty)) OnReasonChanged(); }
    }

    public bool HasBlock => !string.IsNullOrWhiteSpace(_blockReason);

    public BondMigrationBlockDto ToBlock() => new(Row.SecurityId, Row.Version, Row.SecurityContentSha256, _blockReason.Trim());
}

/// <summary>One batch preview result row; amounts are never resubmitted.</summary>
public sealed record BondCutoverBatchPreviewRow(string DisplayName, long Version, int ChangedPayments, string Gaps, bool CanStage, string PreviewHash);

/// <summary>
/// Desktop parity for the browser bond calculation cutover console (bond-capability-expansion steps 5–6):
/// read the complete active inventory, require a reviewed block for every bond without supported declared
/// conventions, and submit the fresh inventory hash. Amendments echo the current receipt hash so a receipt
/// that changed underneath the operator is refused rather than overwritten. Nothing is read until asked.
/// </summary>
public sealed class BondCutoverConsoleViewModel : BindableBase
{
    private readonly IWorkstationBondCutoverApiClient _client;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private bool _reviewed;
    private string _status = "Load the complete active bond inventory to begin a reviewed cutover.";
    private BondMigrationInventoryDto? _inventory;
    private BondMigrationCutoverReceiptDto? _receipt;

    public BondCutoverConsoleViewModel(IWorkstationBondCutoverApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => CanSubmit);
        PreviewBatchCommand = new AsyncRelayCommand(PreviewBatchAsync, () => !IsBusy && UndeclaredCount > 0);
    }

    public IAsyncRelayCommand PreviewBatchCommand { get; }
    public IReadOnlyList<string> CouponRateUnits { get; } = ["Percent", "Fraction"];
    public IReadOnlyList<string> CouponCashBases { get; } = ["Level", "DayCount"];
    public IReadOnlyList<string> BusinessDayConventions { get; } = ["Unadjusted", "Following", "ModifiedFollowing", "Preceding", "ModifiedPreceding"];
    private string _batchCouponRateUnit = "Percent";
    private string _batchCouponCashBasis = "Level";
    private string _batchBusinessDayConvention = "Unadjusted";
    private bool _batchAccrualAdjusted;
    private bool _batchEndOfMonthRule;
    public string BatchCouponRateUnit { get => _batchCouponRateUnit; set => SetProperty(ref _batchCouponRateUnit, value ?? "Percent"); }
    public string BatchCouponCashBasis { get => _batchCouponCashBasis; set => SetProperty(ref _batchCouponCashBasis, value ?? "Level"); }
    public string BatchBusinessDayConvention { get => _batchBusinessDayConvention; set => SetProperty(ref _batchBusinessDayConvention, value ?? "Unadjusted"); }
    public bool BatchAccrualAdjusted { get => _batchAccrualAdjusted; set => SetProperty(ref _batchAccrualAdjusted, value); }
    public bool BatchEndOfMonthRule { get => _batchEndOfMonthRule; set => SetProperty(ref _batchEndOfMonthRule, value); }
    public ObservableCollection<BondCutoverBatchPreviewRow> BatchPreviews { get; } = new();
    private string _batchSummary = string.Empty;
    public string BatchSummaryText { get => _batchSummary; private set => SetProperty(ref _batchSummary, value); }

    /// <summary>Read-only comparison of one candidate convention set across up to 100 undeclared bonds; staging stays per bond.</summary>
    private async Task PreviewBatchAsync()
    {
        if (_inventory is not { } inventory) return;
        var candidates = inventory.Bonds.Where(row => !row.HasDeclaredConventions).Take(100).ToArray();
        if (candidates.Length == 0) { StatusText = "Every active bond already declares supported conventions; nothing to preview."; return; }
        var asOf = DateTimeOffset.UtcNow;
        var conventions = JsonSerializer.SerializeToElement(new
        {
            couponRateUnit = BatchCouponRateUnit, couponCashBasis = BatchCouponCashBasis, accrualAdjusted = BatchAccrualAdjusted,
            businessDayConvention = BatchBusinessDayConvention, endOfMonthRule = BatchEndOfMonthRule
        });
        var request = new BondMigrationBatchPreviewRequest(candidates
            .Select(row => new BondMigrationPreviewRequest(row.SecurityId, row.Version, new BondCalculationContext(asOf, DateOnly.FromDateTime(asOf.UtcDateTime), null, row.Version), conventions))
            .ToArray());
        var token = Begin($"Previewing candidate conventions for {candidates.Length} undeclared bond(s)…");
        try
        {
            var response = await _client.PreviewMigrationBatchAsync(request, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null) { Refused("Batch migration preview was refused", response.StatusCode, response.ErrorMessage); return; }
            BatchPreviews.Clear();
            var names = inventory.Bonds.ToDictionary(row => row.SecurityId, row => row.DisplayName);
            foreach (var preview in response.Data)
                BatchPreviews.Add(new(names.TryGetValue(preview.Request.SecurityId, out var name) ? name : preview.Request.SecurityId.ToString("D"),
                    preview.Request.ExpectedVersion, preview.CashDifferences.Count, preview.Gaps.Count == 0 ? "None" : string.Join("; ", preview.Gaps), preview.CanStage, preview.PreviewSha256));
            BatchSummaryText = $"{response.Data.Count(p => p.CanStage)} of {response.Data.Length} can be staged with these conventions; the rest report gaps. Staging happens per bond from its passport.";
            StatusText = "Batch preview complete; nothing was staged.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Batch migration preview was refused: {ex.Message}"; }
        finally { End(token); }
    }

    public ObservableCollection<BondCutoverRowViewModel> Rows { get; } = new();
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand SubmitCommand { get; }

    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyCommands(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasInventory => _inventory is not null;
    public string InventoryHash => _inventory?.InventorySha256 ?? string.Empty;
    public string CurrentReceiptHash => _inventory?.CurrentReceiptSha256 ?? "None";
    public bool CutoverEnabled => _inventory?.CutoverEnabled ?? false;
    public string GateText => _inventory is null ? string.Empty
        : _inventory.CutoverEnabled ? "Cutover enabled"
        : _inventory.RequiresLegacyCalculations ? "Legacy calculations still required"
        : "Not enabled; every bond declares conventions";
    public string SubmitLabel => CutoverEnabled ? "Amend cutover" : "Enable cutover";
    public int UndeclaredCount => Rows.Count(row => row.IsUndeclared);
    public int MissingReasonCount => Rows.Count(row => row.IsUndeclared && !row.HasBlock);
    public int BlockCount => Rows.Count(row => row.HasBlock);

    public bool Reviewed
    {
        get => _reviewed;
        set { if (SetProperty(ref _reviewed, value)) { NotifyCommands(); UpdateReadiness(); } }
    }

    public bool CanSubmit => _inventory is not null && !IsBusy && Reviewed && MissingReasonCount == 0;
    public BondMigrationCutoverReceiptDto? Receipt => _receipt;
    public string ReceiptText => _receipt is { } receipt
        ? $"Receipt recorded {receipt.RecordedAt:O} by {receipt.Reviewer} · inventory {receipt.InventorySha256} · {receipt.ExplicitBlocks.Count} explicit block(s) · {receipt.ModelVersion}"
          + (receipt.PreviousReceiptSha256 is null ? " · first cutover" : $" · amends {receipt.PreviousReceiptSha256}")
        : string.Empty;

    private async Task LoadAsync()
    {
        var token = Begin("Loading the complete active bond inventory…");
        try
        {
            var response = await _client.GetReadinessAsync(token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null) { Refused("Bond cutover inventory is unavailable", response.StatusCode, response.ErrorMessage); return; }
            Apply(response.Data);
            StatusText = Rows.Count == 0 ? "No active bond carries a coupon structure; nothing to cut over." : "Review every bond, then confirm the review to enable submission.";
            UpdateReadiness();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Bond cutover inventory is unavailable: {ex.Message}"; }
        finally { End(token); }
    }

    private async Task SubmitAsync()
    {
        if (_inventory is not { } inventory || !CanSubmit) return;
        var blocks = Rows.Where(row => row.HasBlock).Select(row => row.ToBlock()).ToArray();
        // The reviewer is replaced server-side with the authenticated actor; it is never trusted from the body.
        var request = new BondMigrationCutoverRequest(inventory.InventorySha256, "workstation", blocks, inventory.CurrentReceiptSha256);
        var token = Begin(inventory.CutoverEnabled ? "Submitting cutover amendment…" : "Enabling bond calculation cutover…");
        try
        {
            var response = await _client.CutoverAsync(request, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null) { Refused("Bond calculation cutover was refused", response.StatusCode, response.ErrorMessage); return; }
            _receipt = response.Data;
            RaisePropertyChanged(nameof(Receipt));
            RaisePropertyChanged(nameof(ReceiptText));
            var refreshed = await _client.GetReadinessAsync(token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (refreshed.Success && refreshed.Data is not null) Apply(refreshed.Data);
            StatusText = $"Cutover receipt retained by {response.Data.Reviewer}. Later undeclared or explicitly blocked bonds cannot silently use legacy calculations.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Bond calculation cutover was refused: {ex.Message}"; }
        finally { End(token); }
    }

    private void Apply(BondMigrationInventoryDto inventory)
    {
        var sameInventory = _inventory?.InventorySha256 == inventory.InventorySha256;
        var reasons = sameInventory ? Rows.ToDictionary(row => row.Row.SecurityId, row => row.BlockReason) : null;
        _inventory = inventory;
        Rows.Clear();
        foreach (var row in inventory.Bonds)
        {
            var view = new BondCutoverRowViewModel(row, UpdateReadiness);
            if (reasons is not null && reasons.TryGetValue(row.SecurityId, out var reason)) view.BlockReason = reason;
            Rows.Add(view);
        }
        // Reasons belong to one inventory snapshot; a changed hash restarts the review.
        _reviewed = false;
        RaisePropertyChanged(nameof(Reviewed));
        RaisePropertyChanged(nameof(HasInventory));
        RaisePropertyChanged(nameof(InventoryHash));
        RaisePropertyChanged(nameof(CurrentReceiptHash));
        RaisePropertyChanged(nameof(CutoverEnabled));
        RaisePropertyChanged(nameof(GateText));
        RaisePropertyChanged(nameof(SubmitLabel));
        UpdateReadiness();
    }

    private void UpdateReadiness()
    {
        RaisePropertyChanged(nameof(UndeclaredCount));
        RaisePropertyChanged(nameof(MissingReasonCount));
        RaisePropertyChanged(nameof(BlockCount));
        RaisePropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
        if (_inventory is null || IsBusy) return;
        StatusText = MissingReasonCount > 0
            ? $"{MissingReasonCount} undeclared bond(s) still need a reviewed block reason."
            : Reviewed
                ? $"Ready to submit {BlockCount} explicit block(s) against inventory {_inventory.InventorySha256}."
                : "Confirm the review to enable submission.";
    }

    private void Refused(string prefix, int statusCode, string? error)
        => StatusText = statusCode switch
        {
            403 => $"{prefix}: bond calculation cutover governs every tenant's live calculations and is limited to operators with AdminMaintenance.",
            409 => $"{prefix}: {error ?? "the active bond inventory or the retained receipt changed after review"}. Reload the inventory and review again.",
            _ => $"{prefix}: {error ?? "no response"}."
        };

    private CancellationToken Begin(string status)
    {
        _operation?.Cancel();
        _operation = new CancellationTokenSource();
        StatusText = status;
        IsBusy = true;
        return _operation.Token;
    }

    private void End(CancellationToken token)
    {
        if (_operation is null || token != _operation.Token) return;
        _operation = null;
        IsBusy = false;
    }

    private void NotifyCommands()
    {
        LoadCommand.NotifyCanExecuteChanged();
        SubmitCommand.NotifyCanExecuteChanged();
        PreviewBatchCommand.NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(CanSubmit));
    }
}
