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

    public DateTime? AsOf { get => _asOf; set { if (SetProperty(ref _asOf, value)) Invalidate("Inputs changed. Calculate again."); } }
    public DateTime? SettlementDate { get => _settlementDate; set { if (SetProperty(ref _settlementDate, value)) Invalidate("Inputs changed. Calculate again."); } }
    /// <summary>Optional recorded-information cutoff as an ISO 8601 UTC instant.</summary>
    public string KnownAt { get => _knownAt; set { if (SetProperty(ref _knownAt, value)) Invalidate("Inputs changed. Calculate again."); } }
    /// <summary>False prices from a clean price per 100 face; true prices from an annual yield fraction.</summary>
    public bool UseYield { get => _useYield; set { if (SetProperty(ref _useYield, value)) { Invalidate("Inputs changed. Calculate again."); RaisePropertyChanged(nameof(ValueLabel)); } } }
    public string Value { get => _value; set { if (SetProperty(ref _value, value)) Invalidate("Inputs changed. Calculate again."); } }
    public BondYieldConvention Convention { get => _convention; set { if (SetProperty(ref _convention, value)) Invalidate("Inputs changed. Calculate again."); } }
    public string ValueLabel => UseYield ? "Annual yield (0.05 = 5%)" : "Clean price per 100 face";

    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) CalculateCommand.NotifyCanExecuteChanged(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasResult => _result is { IsBlocked: false };
    public bool IsBlocked => _result is { IsBlocked: true };
    public ObservableCollection<BondMetricRow> Metrics { get; } = new();
    public ObservableCollection<BondCashFlowRow> CashFlows { get; } = new();
    public ObservableCollection<string> Blockers { get; } = new();
    public ObservableCollection<string> Diagnostics { get; } = new();
    public ObservableCollection<string> ExerciseScenarios { get; } = new();
    public string ProvenanceText => _result is { } result
        ? $"Model {result.ModelVersion} · {result.Currency} · settlement {Day(result.Context.SettlementDate)} · yield convention {result.YieldConvention?.ToString() ?? "Unspecified"}"
        : string.Empty;

    /// <summary>Exact server decimal text; null means the server did not return the measure.</summary>
    public static string DecimalText(decimal? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "Unavailable";

    internal BondAnalyticsRequest? TryBuildRequest(out string? problem)
    {
        problem = null;
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
        return new BondAnalyticsRequest(SecurityId, context, UseYield ? null : amount, UseYield ? amount : null, Convention);
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
        foreach (var blocker in result.Blockers)
            Blockers.Add($"{blocker.Code}: {blocker.Reason}");
        foreach (var diagnostic in result.Diagnostics)
            Diagnostics.Add(diagnostic);
        if (!result.IsBlocked)
        {
            foreach (var (label, value) in new (string, decimal?)[]
            {
                ("Clean price / 100", result.CleanPrice), ("Dirty price / 100", result.DirtyPrice),
                ("Accrued interest / 100", result.AccruedInterest), ("YTM (annual fraction)", result.YieldToMaturity),
                ("Current yield (annual fraction)", result.CurrentYield), ("Yield to worst (annual fraction)", result.YieldToWorst),
                ("Macaulay duration (years)", result.MacaulayDuration), ("Modified duration (years)", result.ModifiedDuration),
                ("Convexity", result.Convexity), ("DV01 / 100", result.Dv01)
            })
                Metrics.Add(new BondMetricRow(label, DecimalText(value)));
            foreach (var flow in result.CashFlows)
                CashFlows.Add(new BondCashFlowRow(Day(flow.PaymentDate), $"{Day(flow.AccrualStart)} – {Day(flow.AccrualEnd)}",
                    DecimalText(flow.Interest), DecimalText(flow.Principal)));
            foreach (var exercise in result.ExerciseYields)
                ExerciseScenarios.Add($"{exercise.Kind} · {Day(exercise.ExerciseDate)} · redemption {DecimalText(exercise.RedemptionPrice)} / 100 · annual yield {DecimalText(exercise.Yield)} · latest notice {(exercise.LatestNoticeDate is { } notice ? Day(notice) : "Not specified")}");
        }
        StatusText = result.IsBlocked ? "Calculation blocked. Review the reasons below." : "Calculation ready. Analytical values grant no exercise, payment, posting or trading authority.";
        RaisePropertyChanged(nameof(HasResult));
        RaisePropertyChanged(nameof(IsBlocked));
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
        IsBusy = false;
        StatusText = status;
        RaisePropertyChanged(nameof(HasResult));
        RaisePropertyChanged(nameof(IsBlocked));
        RaisePropertyChanged(nameof(ProvenanceText));
    }

    private static string Day(DateOnly? value) => value is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Unavailable";
}

public sealed record BondMetricRow(string Label, string Value);
public sealed record BondCashFlowRow(string PaymentDate, string AccrualPeriod, string Interest, string Principal);
