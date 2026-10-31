using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FixedIncome;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.SecurityMaster;

/// <summary>
/// Desktop parity for the browser bond migration preview. Compares legacy and candidate payments for
/// explicitly declared conventions, then stages a hash-bound governed draft through the ordinary
/// Security Master workflow. Displayed amounts are never resubmitted; the server recomputes both digests.
/// </summary>
public sealed class BondMigrationPreviewViewModel : BindableBase
{
    public static readonly IReadOnlyList<string> BusinessDayConventions =
        ["Unadjusted", "Following", "ModifiedFollowing", "Preceding", "ModifiedPreceding"];

    private readonly IWorkstationSecurityMasterApiClient _client;
    private long _generation;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private string _status = "Choose the retained coupon unit from source evidence, then preview.";
    private DateTime? _asOf = DateTime.UtcNow.Date;
    private DateTime? _settlementDate;
    private string _couponRateUnit = string.Empty;
    private string _couponCashBasis = "DayCount";
    private string _businessDayConvention = "Unadjusted";
    private string _paymentCalendars = string.Empty;
    private bool _accrualAdjusted;
    private bool _endOfMonthRule;
    private string _justification = string.Empty;
    private BondMigrationPreviewDto? _preview;
    private bool _staged;
    private bool _refreshRequired;

    public BondMigrationPreviewViewModel(IWorkstationSecurityMasterApiClient client, Guid securityId, long version)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        SecurityId = securityId;
        Version = version;
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && !_staged && SecurityId != Guid.Empty);
        StageCommand = new AsyncRelayCommand(StageAsync, () => CanStage);
    }

    public Guid SecurityId { get; }
    public long Version { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand StageCommand { get; }
    public IReadOnlyList<string> CouponRateUnits { get; } = ["Percent", "Fraction"];
    public IReadOnlyList<string> CouponCashBases { get; } = ["DayCount", "Level"];

    public DateTime? AsOf { get => _asOf; set { if (SetProperty(ref _asOf, value)) Invalidate("Inputs changed. Preview again."); } }
    /// <summary>Blank defaults to the preview date, matching the browser panel.</summary>
    public DateTime? SettlementDate { get => _settlementDate; set { if (SetProperty(ref _settlementDate, value)) Invalidate("Inputs changed. Preview again."); } }
    public string CouponRateUnit { get => _couponRateUnit; set { if (SetProperty(ref _couponRateUnit, value)) Invalidate("Inputs changed. Preview again."); } }
    public string CouponCashBasis { get => _couponCashBasis; set { if (SetProperty(ref _couponCashBasis, value)) Invalidate("Inputs changed. Preview again."); } }
    public string BusinessDayConvention { get => _businessDayConvention; set { if (SetProperty(ref _businessDayConvention, value)) Invalidate("Inputs changed. Preview again."); } }
    /// <summary>Comma-separated retained calendar identifiers; availability is checked by the server.</summary>
    public string PaymentCalendars { get => _paymentCalendars; set { if (SetProperty(ref _paymentCalendars, value)) Invalidate("Inputs changed. Preview again."); } }
    public bool AccrualAdjusted { get => _accrualAdjusted; set { if (SetProperty(ref _accrualAdjusted, value)) Invalidate("Inputs changed. Preview again."); } }
    public bool EndOfMonthRule { get => _endOfMonthRule; set { if (SetProperty(ref _endOfMonthRule, value)) Invalidate("Inputs changed. Preview again."); } }
    public string Justification { get => _justification; set { if (SetProperty(ref _justification, value)) StageCommand.NotifyCanExecuteChanged(); } }

    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyCommands(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasPreview => _preview is not null;
    public bool CanStage => !IsBusy && !_staged && !_refreshRequired && _preview is { CanStage: true } && !string.IsNullOrWhiteSpace(Justification);
    public ObservableCollection<BondMigrationDifferenceRow> Differences { get; } = new();
    public ObservableCollection<string> Gaps { get; } = new();
    public string FingerprintText => _preview is { } preview
        ? $"Security SHA-256: {preview.SecurityContentSha256}\nPreview SHA-256: {preview.PreviewSha256}"
        : string.Empty;
    public string StagedText { get; private set; } = string.Empty;

    internal BondMigrationPreviewRequest? TryBuildRequest(out string? problem)
    {
        problem = null;
        if (AsOf is not { } asOf)
        { problem = "Enter the preview date."; return null; }
        if (string.IsNullOrWhiteSpace(CouponRateUnit))
        { problem = "Choose the retained coupon unit from source evidence before previewing."; return null; }
        var calendars = PaymentCalendars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (BusinessDayConvention != "Unadjusted" && calendars.Length == 0)
        { problem = "Name the retained payment calendar for the selected business-day adjustment."; return null; }
        var conventions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["couponRateUnit"] = CouponRateUnit,
            ["couponCashBasis"] = CouponCashBasis,
            ["accrualAdjusted"] = AccrualAdjusted,
            ["businessDayConvention"] = BusinessDayConvention,
            ["endOfMonthRule"] = EndOfMonthRule
        };
        if (calendars.Length > 0)
            conventions["paymentCalendars"] = calendars;
        var settlement = DateOnly.FromDateTime((SettlementDate ?? asOf).Date);
        var context = new BondCalculationContext(new DateTimeOffset(DateTime.SpecifyKind(asOf.Date, DateTimeKind.Utc)), settlement, null, Version);
        return new BondMigrationPreviewRequest(SecurityId, Version, context, JsonSerializer.SerializeToElement(conventions));
    }

    private async Task PreviewAsync()
    {
        Invalidate("Preparing preview…");
        var request = TryBuildRequest(out var problem);
        if (request is null) { StatusText = problem!; return; }
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            var response = await _client.PreviewBondMigrationAsync(request, operation.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null)
            { StatusText = $"Migration preview could not be prepared: {response.ErrorMessage ?? "no response"}."; return; }
            var preview = response.Data;
            if (preview.Request.SecurityId != SecurityId || preview.Request.ExpectedVersion != Version)
                throw new InvalidOperationException("The preview does not match the selected retained terms version. Reload the security.");
            _preview = preview;
            Differences.Clear();
            Gaps.Clear();
            foreach (var row in preview.CashDifferences)
                Differences.Add(new BondMigrationDifferenceRow(row.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Text(row.PreviousPrincipal), Text(row.CandidatePrincipal), Text(row.PreviousInterest), Text(row.CandidateInterest)));
            foreach (var gap in preview.Gaps)
                Gaps.Add(gap);
            StatusText = preview.CanStage
                ? "Preview ready for governed review. Review the changed payments and fingerprints before creating a terms revision."
                : "Migration has unresolved gaps; staging is unavailable until they are resolved.";
            RaisePropertyChanged(nameof(HasPreview));
            RaisePropertyChanged(nameof(FingerprintText));
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = "Preview cancelled."; }
        catch (Exception ex) { if (generation == _generation) StatusText = $"Migration preview could not be prepared: {ex.Message}"; }
        finally { if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private async Task StageAsync()
    {
        if (!CanStage || _preview is not { } preview) return;
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            var response = await _client.StageBondMigrationAsync(
                new BondMigrationSubmitRequest(preview.Request, preview.PreviewSha256, preview.SecurityContentSha256, Justification.Trim()),
                operation.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null)
            {
                var stale = response.StatusCode == 409;
                var uncertain = response.StatusCode == 0 || response.StatusCode >= 500 || response.IsConnectionError;
                _refreshRequired = stale || uncertain;
                StatusText = stale
                    ? "Terms or preview inputs changed. Reload the selected security and prepare a fresh preview before staging."
                    : uncertain
                        ? "The draft outcome is uncertain. Inspect retained revision history and reload the security before trying again."
                        : $"The governed draft could not be created: {response.ErrorMessage ?? "no response"}.";
                return;
            }
            if (response.Data.SecurityId != SecurityId)
                throw new InvalidOperationException("The draft response does not match this security. Inspect retained revision history before retrying.");
            _staged = true;
            StagedText = $"Governed revision created: {response.Data.RevisionId:D} at v{response.Data.NewVersion} ({response.Data.State}). No approval or publication was requested; continue in the Security Master revision workflow.";
            StatusText = "Draft staged. Nothing has been approved or published.";
            RaisePropertyChanged(nameof(StagedText));
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = "Staging cancelled."; }
        catch (Exception ex) { if (generation == _generation) { _refreshRequired = true; StatusText = $"The governed draft could not be created: {ex.Message}"; } }
        finally { if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private void Invalidate(string status)
    {
        Interlocked.Increment(ref _generation);
        _operation?.Cancel();
        _operation = null;
        _preview = null;
        _refreshRequired = false;
        Differences.Clear();
        Gaps.Clear();
        IsBusy = false;
        StatusText = status;
        RaisePropertyChanged(nameof(HasPreview));
        RaisePropertyChanged(nameof(FingerprintText));
    }

    private void NotifyCommands()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        StageCommand.NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(CanStage));
    }

    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed record BondMigrationDifferenceRow(string PaymentDate, string PreviousPrincipal, string CandidatePrincipal, string PreviousInterest, string CandidateInterest);
