using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FixedIncome;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.SecurityMaster;

/// <summary>
/// Desktop parity for the browser bond analytics panel. Every number is calculated by the server from
/// retained terms and shown as the exact decimal it returned; the desktop never prices, never writes,
/// and a result grants no posting or trading authority.
/// </summary>
public sealed class BondAnalyticsViewModel : BindableBase
{
    private readonly IWorkstationSecurityMasterApiClient _client;
    private long _generation;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private string _status = "Enter inputs to calculate. No market price is assumed.";
    private DateTime? _asOf = DateTime.UtcNow.Date;
    private DateTime? _settlementDate = DateTime.UtcNow.Date;
    private string _knownAt = string.Empty;
    private bool _useYield;
    private string _value = string.Empty;
    private BondYieldConvention _convention = BondYieldConvention.NominalPeriodic;
    private string _calculationVersion = "bond-analytics-v2";
    private BondAnalyticsResult? _result;

    public BondAnalyticsViewModel(IWorkstationSecurityMasterApiClient client, Guid securityId, long version)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        SecurityId = securityId;
        Version = version;
        CalculateCommand = new AsyncRelayCommand(CalculateAsync, () => !IsBusy && SecurityId != Guid.Empty);
    }

    public Guid SecurityId { get; }
    public long Version { get; }
    public IAsyncRelayCommand CalculateCommand { get; }
    public IReadOnlyList<BondYieldConvention> YieldConventions { get; } = Enum.GetValues<BondYieldConvention>();
    public IReadOnlyList<BondAnalyticsModelOption> CalculationVersions { get; } =
    [
        new("bond-analytics-v2", "bond-analytics-v2 · Fixed / zero coupon"),
        new("bond-analytics-v1", "bond-analytics-v1 · Legacy fixed / zero coupon"),
        new("bond-analytics-v3", "bond-analytics-v3 · Floating with named index")
    ];

    public DateTime? AsOf { get => _asOf; set { if (SetProperty(ref _asOf, value)) Invalidate("Inputs changed. Calculate again."); } }
    public DateTime? SettlementDate { get => _settlementDate; set { if (SetProperty(ref _settlementDate, value)) Invalidate("Inputs changed. Calculate again."); } }
    /// <summary>Optional recorded-information cutoff as an ISO 8601 UTC instant.</summary>
    public string KnownAt { get => _knownAt; set { if (SetProperty(ref _knownAt, value)) Invalidate("Inputs changed. Calculate again."); } }
    /// <summary>False prices from a clean price per 100 face; true prices from an annual yield fraction.</summary>
    public bool UseYield { get => _useYield; set { if (SetProperty(ref _useYield, value)) { Invalidate("Inputs changed. Calculate again."); RaisePropertyChanged(nameof(ValueLabel)); } } }
    public string Value { get => _value; set { if (SetProperty(ref _value, value)) Invalidate("Inputs changed. Calculate again."); } }
    public BondYieldConvention Convention { get => _convention; set { if (SetProperty(ref _convention, value)) Invalidate("Inputs changed. Calculate again."); } }
    public string CalculationVersion
    {
        get => _calculationVersion;
        set
        {
            if (!SetProperty(ref _calculationVersion, value)) return;
            Invalidate("Calculation version changed. Calculate again.");
            RaisePropertyChanged(nameof(ValueLabel));
            RaisePropertyChanged(nameof(ModelDescription));
        }
    }
    public string ValueLabel => UseYield
        ? CalculationVersion == "bond-analytics-v3" ? "Projected-flow annual yield (0.05 = 5%)" : "Annual yield (0.05 = 5%)"
        : "Clean price per 100 face";
    public string ModelDescription => CalculationVersion == "bond-analytics-v3"
        ? "Requires an explicitly typed Floating bond with one named reference-rate index. Yield discounts projected coupons; it is not a discount margin. Missing fixings or forward curves block calculation."
        : "Fixed and zero-coupon bonds use v2; v1 remains available for legacy analysis. The server checks model eligibility; it does not substitute another calculation version.";

    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) CalculateCommand.NotifyCanExecuteChanged(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasResult => _result is { IsBlocked: false };
    public bool IsBlocked => _result is { IsBlocked: true };
    public bool HasFloatingResult => _result is { IsBlocked: false, ModelVersion: "bond-analytics-v3" };
    public ObservableCollection<BondMetricRow> Metrics { get; } = new();
    public ObservableCollection<BondCashFlowRow> CashFlows { get; } = new();
    public ObservableCollection<string> Blockers { get; } = new();
    public ObservableCollection<string> Diagnostics { get; } = new();
    public ObservableCollection<string> ExerciseScenarios { get; } = new();
    public ObservableCollection<string> Inputs { get; } = new();
    public string ProvenanceText => _result is { } result
        ? $"Model {result.ModelVersion} · {result.Currency} · settlement {Day(result.Context.SettlementDate)} · yield convention {result.YieldConvention?.ToString() ?? "Unspecified"}"
        : string.Empty;

    /// <summary>Exact server decimal text; null means the server did not return the measure.</summary>
    public static string DecimalText(decimal? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "Unavailable";

    internal BondAnalyticsRequest? TryBuildRequest(out string? problem)
    {
        problem = null;
        if (!CalculationVersions.Any(option => option.Version == CalculationVersion))
        { problem = "Select a supported calculation version."; return null; }
        if (AsOf is not { } asOf || SettlementDate is not { } settlement)
        { problem = "Enter calculation and settlement dates."; return null; }
        if (!decimal.TryParse(Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        { problem = "Enter a decimal clean price or annual yield."; return null; }
        DateTimeOffset? knownAt = null;
        if (!string.IsNullOrWhiteSpace(KnownAt))
        {
            if (!DateTimeOffset.TryParse(KnownAt.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            { problem = "Enter the recorded cutoff as an ISO 8601 UTC instant, or leave it blank."; return null; }
            knownAt = parsed;
        }
        var context = new BondCalculationContext(
            new DateTimeOffset(DateTime.SpecifyKind(asOf.Date, DateTimeKind.Utc)),
            DateOnly.FromDateTime(settlement.Date), knownAt, Version);
        return new BondAnalyticsRequest(SecurityId, context, UseYield ? null : amount, UseYield ? amount : null, Convention)
        { CalculationVersion = CalculationVersion };
    }

    private async Task CalculateAsync()
    {
        Invalidate("Calculating from retained inputs…");
        var request = TryBuildRequest(out var problem);
        if (request is null) { StatusText = problem!; return; }
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            var response = await _client.GetBondAnalyticsAsync(request, operation.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null)
            {
                StatusText = response.StatusCode == 403
                    ? "Bond analytics requires Security Master view permission and a tenant-scoped session."
                    : $"Bond analytics could not be calculated: {response.ErrorMessage ?? "no response"}.";
                return;
            }
            var result = response.Data;
            if (result.SecurityId != SecurityId)
                throw new InvalidOperationException("The calculation returned a different security. Reload the selected passport.");
            if (!string.Equals(result.ModelVersion, request.CalculationVersion, StringComparison.Ordinal))
                throw new InvalidOperationException("The server returned a different calculation version. No substitute model was accepted.");
            Present(result);
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = "Calculation cancelled."; }
        catch (Exception ex) { if (generation == _generation) StatusText = $"Bond analytics could not be calculated: {ex.Message}"; }
        finally { if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private void Present(BondAnalyticsResult result)
    {
        _result = result;
        Metrics.Clear();
        CashFlows.Clear();
        Blockers.Clear();
        Diagnostics.Clear();
        ExerciseScenarios.Clear();
        Inputs.Clear();
        foreach (var blocker in result.Blockers)
            Blockers.Add($"{blocker.Code}: {blocker.Reason}");
        foreach (var diagnostic in result.Diagnostics)
            Diagnostics.Add(diagnostic);
        foreach (var input in result.Inputs)
            Inputs.Add($"{input.Kind} · {input.Key} · version {input.Version?.ToString(CultureInfo.InvariantCulture) ?? "Unspecified"} · version ID {input.VersionId?.ToString("D") ?? "Unspecified"} · SHA-256 {input.ContentSha256 ?? "Unavailable"} · as of {input.AsOf?.ToString("O", CultureInfo.InvariantCulture) ?? "Unspecified"} · recorded {input.RecordedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "Unspecified"}");
        if (!result.IsBlocked)
        {
            var floating = result.ModelVersion == "bond-analytics-v3";
            foreach (var (label, value) in new (string, decimal?)[]
            {
                ("Clean price / 100", result.CleanPrice), ("Dirty price / 100", result.DirtyPrice),
                ("Accrued interest / 100", result.AccruedInterest),
                (floating ? "Projected-flow yield (annual fraction)" : "YTM (annual fraction)", result.YieldToMaturity),
                ("Current yield (annual fraction)", result.CurrentYield),
                ("Macaulay duration (years)", result.MacaulayDuration), ("Modified duration (years)", result.ModifiedDuration),
                ("Convexity (years²)", result.Convexity), ("DV01 / 100 / bp yield", result.Dv01)
            })
                Metrics.Add(new BondMetricRow(label, DecimalText(value)));
            if (floating)
                Metrics.Add(new BondMetricRow("SpreadDv01 / 100 / +1 bp spread", DecimalText(result.SpreadDv01)));
            else
                Metrics.Add(new BondMetricRow("Yield to worst (annual fraction)", DecimalText(result.YieldToWorst)));
            foreach (var flow in result.CashFlows)
            {
                var coupon = flow.FloatingCoupon;
                CashFlows.Add(new BondCashFlowRow(Day(flow.PaymentDate), $"{Day(flow.AccrualStart)} – {Day(flow.AccrualEnd)}",
                    DecimalText(flow.Interest), DecimalText(flow.Principal), Day(coupon?.ResetDate),
                    coupon is null ? "Unavailable" : coupon.IsObserved ? "Observed fixing" : "Forecast fixing",
                    coupon?.IndexName ?? "Unavailable", DecimalText(coupon?.IndexRate), DecimalText(coupon?.SpreadBps),
                    DecimalText(coupon?.CouponRate), coupon?.Source ?? "Unavailable"));
            }
            foreach (var exercise in result.ExerciseYields)
                ExerciseScenarios.Add($"{exercise.Kind} · {Day(exercise.ExerciseDate)} · redemption {DecimalText(exercise.RedemptionPrice)} / 100 · annual yield {DecimalText(exercise.Yield)} · latest notice {(exercise.LatestNoticeDate is { } notice ? Day(notice) : "Not specified")}");
        }
        StatusText = result.IsBlocked ? "Calculation blocked. Review the reasons below." : "Calculation ready. Analytical values grant no exercise, payment, posting or trading authority.";
        RaisePropertyChanged(nameof(HasResult));
        RaisePropertyChanged(nameof(IsBlocked));
        RaisePropertyChanged(nameof(HasFloatingResult));
        RaisePropertyChanged(nameof(ProvenanceText));
    }

    private void Invalidate(string status)
    {
        Interlocked.Increment(ref _generation);
        _operation?.Cancel();
        _operation = null;
        _result = null;
        Metrics.Clear();
        CashFlows.Clear();
        Blockers.Clear();
        Diagnostics.Clear();
        ExerciseScenarios.Clear();
        Inputs.Clear();
        IsBusy = false;
        StatusText = status;
        RaisePropertyChanged(nameof(HasResult));
        RaisePropertyChanged(nameof(IsBlocked));
        RaisePropertyChanged(nameof(HasFloatingResult));
        RaisePropertyChanged(nameof(ProvenanceText));
    }

    private static string Day(DateOnly? value) => value is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Unavailable";
}

public sealed record BondMetricRow(string Label, string Value);
public sealed record BondAnalyticsModelOption(string Version, string Label);
public sealed record BondCashFlowRow(string PaymentDate, string AccrualPeriod, string Interest, string Principal,
    string ResetDate, string FixingStatus, string IndexName, string IndexRate, string SpreadBps, string CouponRate, string Source);
