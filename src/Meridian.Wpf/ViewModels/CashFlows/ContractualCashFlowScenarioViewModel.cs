using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Wpf.Services;

using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels.CashFlows;

/// <summary>One eligible exercise entry with the operator's hypothetical selection.</summary>
public sealed class CashFlowExerciseChoiceViewModel : BindableBase
{
    private bool _include;
    private string _exerciseDate;
    private string _amount = string.Empty;

    public CashFlowExerciseChoiceViewModel(CashFlowExerciseEligibility entry, Action onChanged)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        OnChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        Dates = entry.Dates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
        _exerciseDate = Dates.FirstOrDefault() ?? string.Empty;
    }

    public CashFlowExerciseEligibility Entry { get; }
    private Action OnChanged { get; }
    public IReadOnlyList<string> Dates { get; }
    public string Label => $"{Entry.Side} · {Entry.StreamId} · {Entry.Style} until {Entry.WindowEnd:yyyy-MM-dd}";
    public string CalendarText => Entry.BusinessDayCalendarId is null ? "No notice calendar declared" : $"Notice calendar {Entry.BusinessDayCalendarId}";
    public bool Include { get => _include; set { if (SetProperty(ref _include, value)) OnChanged(); } }
    public string ExerciseDate { get => _exerciseDate; set { if (SetProperty(ref _exerciseDate, value ?? string.Empty)) OnChanged(); } }
    public string Amount { get => _amount; set { if (SetProperty(ref _amount, value ?? string.Empty)) OnChanged(); } }
}

/// <summary>Exact server decimals for one projected event; nothing is recomputed on the desktop.</summary>
public sealed record CashFlowEventRow(string Date, string StreamId, string OpeningBalance, string CashInterest, string ScheduledPrincipal,
    string ForecastPrepayment, string RedeemedPrincipal, string PremiumAndPenalty, string ClosingBalance, string Direction)
{
    public static CashFlowEventRow From(ContractualCashFlowEvent e) => new(
        e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.StreamId, Exact(e.OpeningBalance), Exact(e.CashInterest), Exact(e.ScheduledPrincipal),
        Exact(e.ForecastPrepayment), Exact(e.RedeemedPrincipal), $"{Exact(e.RedemptionPremium)} / {Exact(e.PrepaymentPenalty)}", Exact(e.ClosingBalance), e.Direction.ToString());

    public static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Desktop parity for the browser cash-flow scenario workbench over the shared contractual cash-flow
/// routes: hypothetical valuation date, knowledge cutoff, horizon, rate shock, exercise selections and one
/// prepayment assumption; the result ladder with blockers; retained runs under a client-generated identity;
/// and replay verification of a retained run by output hash. Nothing here changes terms or posts.
/// </summary>
public sealed class ContractualCashFlowScenarioViewModel : BindableBase
{
    private readonly IWorkstationContractualCashFlowApiClient _client;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private string _status = "Preview a scenario to see projected cash flows and exercise eligibility.";
    private DateTime _valuationDate = DateTime.UtcNow.Date;
    private string _knowledgeCutoff = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private DateTime? _horizon;
    private string _rateScenario = nameof(StructuredCashFlowScenario.Base);
    private bool _prepaymentEnabled;
    private string _prepaymentStreamId = string.Empty;
    private DateTime? _prepaymentStart;
    private DateTime? _prepaymentEnd;
    private string _prepaymentMethod = nameof(CashFlowPrepaymentMethod.Cpr);
    private string _prepaymentValue = string.Empty;
    private string _prepaymentPolicy = nameof(CashFlowRecastPolicy.PreservePayments);
    private ContractualCashFlowResult? _result;
    private ContractualCashFlowRun? _run;
    private Guid _runId = Guid.NewGuid();
    private string _runIdText = string.Empty;
    private ContractualCashFlowRun? _loadedRun;
    private string _replayStatus = string.Empty;

    public ContractualCashFlowScenarioViewModel(IWorkstationContractualCashFlowApiClient client, string subjectKind, Guid subjectId, string subjectLabel)
    {
        EventsTable = new WorkstationTableModel<CashFlowEventRow>(
            Events,
            [
                new("Date", nameof(CashFlowEventRow.Date), 95),
                new("Stream", nameof(CashFlowEventRow.StreamId), 110),
                new("Opening balance", nameof(CashFlowEventRow.OpeningBalance), 120),
                new("Cash interest", nameof(CashFlowEventRow.CashInterest), 110),
                new("Scheduled principal", nameof(CashFlowEventRow.ScheduledPrincipal), 130),
                new("Forecast prepayment", nameof(CashFlowEventRow.ForecastPrepayment), 130),
                new("Redeemed principal", nameof(CashFlowEventRow.RedeemedPrincipal), 130),
                new("Premium / penalty", nameof(CashFlowEventRow.PremiumAndPenalty), 120),
                new("Closing balance", nameof(CashFlowEventRow.ClosingBalance), 120),
                new("Direction", nameof(CashFlowEventRow.Direction), 90)
            ],
            "Projected cash flows",
            "No projected events",
            "Preview a scenario to list the projected contractual events.");
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (!ContractualCashFlowSubjectKinds.IsSupported(subjectKind)) throw new ArgumentException("Unsupported contractual cash-flow subject kind.", nameof(subjectKind));
        if (subjectId == Guid.Empty) throw new ArgumentException("A subject identity is required.", nameof(subjectId));
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        SubjectLabel = subjectLabel ?? string.Empty;
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy);
        RetainRunCommand = new AsyncRelayCommand(RetainRunAsync, () => CanRetain);
        LoadRunCommand = new AsyncRelayCommand(LoadRunAsync, () => !IsBusy && Guid.TryParse(RunIdText, out _));
        VerifyReplayCommand = new AsyncRelayCommand(VerifyReplayAsync, () => !IsBusy && _loadedRun is not null);
    }

    public string SubjectKind { get; }
    public Guid SubjectId { get; }
    public string SubjectLabel { get; }
    public IReadOnlyList<string> RateScenarios { get; } = Enum.GetNames<StructuredCashFlowScenario>();
    public IReadOnlyList<string> PrepaymentMethods { get; } = Enum.GetNames<CashFlowPrepaymentMethod>();
    public IReadOnlyList<string> PrepaymentPolicies { get; } = Enum.GetNames<CashFlowRecastPolicy>();
    public ObservableCollection<CashFlowExerciseChoiceViewModel> Exercises { get; } = new();
    public ObservableCollection<CashFlowEventRow> Events { get; } = new();
    /// <summary>Dense-table projection of <see cref="Events"/> for the workstation grid control; rows track the collection.</summary>
    public WorkstationTableModel<CashFlowEventRow> EventsTable { get; }
    public ObservableCollection<string> Blockers { get; } = new();
    public ObservableCollection<string> ExerciseCalculations { get; } = new();
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand RetainRunCommand { get; }
    public IAsyncRelayCommand LoadRunCommand { get; }
    public IAsyncRelayCommand VerifyReplayCommand { get; }

    public DateTime ValuationDate { get => _valuationDate; set => Edit(ref _valuationDate, value); }
    public string KnowledgeCutoff { get => _knowledgeCutoff; set => Edit(ref _knowledgeCutoff, value ?? string.Empty); }
    public DateTime? Horizon { get => _horizon; set => Edit(ref _horizon, value); }
    public string RateScenario { get => _rateScenario; set => Edit(ref _rateScenario, value ?? nameof(StructuredCashFlowScenario.Base)); }
    public bool PrepaymentEnabled { get => _prepaymentEnabled; set => Edit(ref _prepaymentEnabled, value); }
    public string PrepaymentStreamId { get => _prepaymentStreamId; set => Edit(ref _prepaymentStreamId, value ?? string.Empty); }
    public DateTime? PrepaymentStart { get => _prepaymentStart; set => Edit(ref _prepaymentStart, value); }
    public DateTime? PrepaymentEnd { get => _prepaymentEnd; set => Edit(ref _prepaymentEnd, value); }
    public string PrepaymentMethod { get => _prepaymentMethod; set => Edit(ref _prepaymentMethod, value ?? nameof(CashFlowPrepaymentMethod.Cpr)); }
    public string PrepaymentValue { get => _prepaymentValue; set => Edit(ref _prepaymentValue, value ?? string.Empty); }
    public string PrepaymentPolicy { get => _prepaymentPolicy; set => Edit(ref _prepaymentPolicy, value ?? nameof(CashFlowRecastPolicy.PreservePayments)); }
    public string RunIdText { get => _runIdText; set { if (SetProperty(ref _runIdText, value ?? string.Empty)) LoadRunCommand.NotifyCanExecuteChanged(); } }

    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyCommands(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasResult => _result is not null;
    public bool IsBlocked => _result is { IsCalculated: false };
    public bool CanRetain => _result is { IsCalculated: true } && _run is null && !IsBusy;
    public ContractualCashFlowResult? Result => _result;
    public ContractualCashFlowRun? Run => _run;
    public ContractualCashFlowRun? LoadedRun => _loadedRun;
    public Guid PendingRunId => _runId;
    public string ReplayStatusText { get => _replayStatus; private set => SetProperty(ref _replayStatus, value); }

    public string EvidenceText => _result?.Evidence is { } e
        ? $"{e.EngineVersion} · input {e.InputHash} · output {e.OutputHash} · source terms {e.SourceTermsHash} (v{e.SourceVersion})" + (e.Warnings.Count > 0 ? $" · warnings: {string.Join("; ", e.Warnings)}" : string.Empty)
        : string.Empty;
    public string RunText => _run is { } run ? $"Run {run.RunId:D} retained {run.RecordedAt:O} by {run.Actor}. Replay needs no live terms, calendars or curves." : string.Empty;
    public string LoadedRunText => _loadedRun is { } run
        ? $"Run {run.RunId:D} · {run.SubjectKind} · recorded {run.RecordedAt:O} by {run.Actor} · valuation {run.Request.ValuationDate:yyyy-MM-dd} · {run.Request.RateScenario} · {run.Result.Events.Count} events · output {run.Result.Evidence.OutputHash}"
        : string.Empty;

    /// <summary>Builds the hypothetical request, or returns the reason it cannot be built.</summary>
    public ContractualCashFlowRequest? TryBuildRequest(out string? refusal)
    {
        refusal = null;
        if (!DateTimeOffset.TryParse(KnowledgeCutoff, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var cutoff))
        { refusal = "Enter the knowledge cutoff as an ISO-8601 instant."; return null; }
        if (!Enum.TryParse<StructuredCashFlowScenario>(RateScenario, out var scenario))
        { refusal = "Choose a supported rate scenario."; return null; }
        var exercises = new List<SelectedCashFlowExercise>();
        foreach (var choice in Exercises.Where(c => c.Include))
        {
            if (!DateOnly.TryParseExact(choice.ExerciseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exerciseDate)
                || !decimal.TryParse(choice.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            { refusal = $"Choose an exercise date and a decimal amount for the {choice.Entry.Side} on {choice.Entry.StreamId}."; return null; }
            exercises.Add(new SelectedCashFlowExercise(choice.Entry.Side, choice.Entry.EntryStartDate, choice.Entry.StreamId, exerciseDate, exerciseDate, amount));
        }
        IReadOnlyList<CashFlowPrepaymentAssumption>? prepayments = null;
        if (PrepaymentEnabled)
        {
            if (string.IsNullOrWhiteSpace(PrepaymentStreamId) || PrepaymentStart is null || PrepaymentEnd is null
                || !decimal.TryParse(PrepaymentValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                || !Enum.TryParse<CashFlowPrepaymentMethod>(PrepaymentMethod, out var method) || !Enum.TryParse<CashFlowRecastPolicy>(PrepaymentPolicy, out var policy))
            { refusal = "A prepayment assumption needs a stream, a [start, end) interval and a decimal value."; return null; }
            prepayments = [new CashFlowPrepaymentAssumption(PrepaymentStreamId.Trim(), DateOnly.FromDateTime(PrepaymentStart.Value), DateOnly.FromDateTime(PrepaymentEnd.Value), method, value, null, policy)];
        }
        return new ContractualCashFlowRequest(DateOnly.FromDateTime(ValuationDate), cutoff, Horizon is { } h ? DateOnly.FromDateTime(h) : null, scenario,
            exercises.Count > 0 ? exercises : null, prepayments);
    }

    private async Task PreviewAsync()
    {
        var request = TryBuildRequest(out var refusal);
        if (request is null) { StatusText = refusal!; return; }
        var token = Begin("Previewing scenario…");
        try
        {
            var response = await _client.PreviewAsync(SubjectKind, SubjectId, request, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("Cash-flow scenario could not be previewed", response.StatusCode, response.ErrorMessage); return; }
            _run = null;
            Apply(response.Data, rebuildExercises: true);
            StatusText = response.Data.IsCalculated
                ? $"{response.Data.Events.Count} projected events. Retain the run to keep a replayable packet; nothing is posted."
                : $"Blocked by {response.Data.Blockers.Count} condition(s); resolve them before retaining.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Cash-flow scenario could not be previewed: {ex.Message}"; }
        finally { End(token); }
    }

    private async Task RetainRunAsync()
    {
        var request = TryBuildRequest(out var refusal);
        if (request is null) { StatusText = refusal!; return; }
        var token = Begin("Retaining run…");
        try
        {
            var response = await _client.RetainRunAsync(SubjectKind, SubjectId, new CreateContractualCashFlowRunRequest(_runId, request), token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("Cash-flow run could not be retained", response.StatusCode, response.ErrorMessage); return; }
            if (response.Data.RunId == Guid.Empty || response.Data.Result is null)
            { StatusText = "The projection became blocked before it could be retained; preview again and review the blockers."; return; }
            if (response.Data.SubjectId != SubjectId) throw new InvalidOperationException("The retained run names another subject. Refresh the selected record.");
            _run = response.Data;
            Apply(response.Data.Result, rebuildExercises: false);
            RaisePropertyChanged(nameof(Run));
            RaisePropertyChanged(nameof(RunText));
            StatusText = $"Run {response.Data.RunId:D} retained by {response.Data.Actor}.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Cash-flow run could not be retained: {ex.Message}"; }
        finally { End(token); }
    }

    private async Task LoadRunAsync()
    {
        if (!Guid.TryParse(RunIdText, out var runId)) { StatusText = "Enter a retained run identity."; return; }
        var token = Begin("Loading retained run…");
        try
        {
            var response = await _client.GetRunAsync(SubjectKind, SubjectId, runId, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("Retained cash-flow run could not be loaded", response.StatusCode, response.ErrorMessage); return; }
            _loadedRun = response.Data;
            ReplayStatusText = string.Empty;
            RaisePropertyChanged(nameof(LoadedRun));
            RaisePropertyChanged(nameof(LoadedRunText));
            StatusText = "Retained run loaded. Verify replay to compare its output hash with a fresh preview of its request.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Retained cash-flow run could not be loaded: {ex.Message}"; }
        finally { End(token); }
    }

    private async Task VerifyReplayAsync()
    {
        if (_loadedRun is not { } run) return;
        var token = Begin("Verifying replay…");
        try
        {
            var response = await _client.PreviewAsync(SubjectKind, SubjectId, run.Request, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("Replay verification could not run", response.StatusCode, response.ErrorMessage); return; }
            var fresh = response.Data;
            var retained = run.Result.Evidence;
            ReplayStatusText = !fresh.IsCalculated
                ? $"Replay is blocked now ({string.Join(", ", fresh.Blockers.Select(b => b.Code))}); the retained packet stands on its own evidence."
                : string.Equals(fresh.Evidence.OutputHash, retained.OutputHash, StringComparison.Ordinal)
                    ? $"Replay matches: output hash {retained.OutputHash} reproduced from current terms, calendars and curves."
                    : $"Replay differs: retained output {retained.OutputHash}, current {fresh.Evidence.OutputHash}. Source terms {(string.Equals(retained.SourceTermsHash, fresh.Evidence.SourceTermsHash, StringComparison.Ordinal) ? "unchanged" : "changed")}.";
            StatusText = "Replay verification complete.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Replay verification could not run: {ex.Message}"; }
        finally { End(token); }
    }

    private void Apply(ContractualCashFlowResult result, bool rebuildExercises)
    {
        _result = result;
        Events.Clear();
        foreach (var e in result.Events) Events.Add(CashFlowEventRow.From(e));
        Blockers.Clear();
        foreach (var b in result.Blockers) Blockers.Add($"{b.Code}{(b.StreamId is null ? string.Empty : $" ({b.StreamId})")}: {b.Message}");
        ExerciseCalculations.Clear();
        foreach (var c in result.ExerciseCalculations)
            ExerciseCalculations.Add($"{c.Selection.Side} {c.Selection.StreamId} on {c.Selection.ExerciseDate:yyyy-MM-dd}: dirty consideration {CashFlowEventRow.Exact(c.DirtyConsideration)} · premium {CashFlowEventRow.Exact(c.Premium)} · accrued {CashFlowEventRow.Exact(c.AccruedInterest)} · clean floor {CashFlowEventRow.Exact(c.CleanFloor)}");
        if (rebuildExercises)
        {
            var previous = Exercises.ToDictionary(c => Key(c.Entry), c => c);
            Exercises.Clear();
            foreach (var entry in result.ExerciseEligibility)
            {
                var choice = new CashFlowExerciseChoiceViewModel(entry, OnExerciseEdited);
                if (previous.TryGetValue(Key(entry), out var old)) { choice.Include = old.Include; choice.ExerciseDate = old.ExerciseDate; choice.Amount = old.Amount; }
                Exercises.Add(choice);
            }
        }
        RaisePropertyChanged(nameof(HasResult));
        RaisePropertyChanged(nameof(IsBlocked));
        RaisePropertyChanged(nameof(Result));
        RaisePropertyChanged(nameof(EvidenceText));
        NotifyCommands();
    }

    private static string Key(CashFlowExerciseEligibility entry) => $"{entry.Side}:{entry.StreamId}:{entry.EntryStartDate:yyyy-MM-dd}";

    /// <summary>Any edit invalidates the previewed result and the run identity bound to the previous request.</summary>
    private void Edit<T>(ref T field, T value)
    {
        if (!SetProperty(ref field, value)) return;
        Invalidate();
    }

    private void OnExerciseEdited() => Invalidate();

    private void Invalidate()
    {
        _operation?.Cancel();
        _operation = null;
        _busy = false;
        RaisePropertyChanged(nameof(IsBusy));
        _result = null;
        _run = null;
        _runId = Guid.NewGuid();
        Events.Clear();
        Blockers.Clear();
        ExerciseCalculations.Clear();
        RaisePropertyChanged(nameof(HasResult));
        RaisePropertyChanged(nameof(IsBlocked));
        RaisePropertyChanged(nameof(Result));
        RaisePropertyChanged(nameof(Run));
        RaisePropertyChanged(nameof(RunText));
        RaisePropertyChanged(nameof(EvidenceText));
        RaisePropertyChanged(nameof(PendingRunId));
        NotifyCommands();
    }

    private static string Refused(string prefix, int statusCode, string? error) => statusCode switch
    {
        403 => $"{prefix}: this session lacks the view or modify permission for the subject.",
        404 => $"{prefix}: the subject or retained run was not found.",
        409 => $"{prefix}: {error ?? "the run identity conflicts with retained data"}.",
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
        PreviewCommand.NotifyCanExecuteChanged();
        RetainRunCommand.NotifyCanExecuteChanged();
        LoadRunCommand.NotifyCanExecuteChanged();
        VerifyReplayCommand.NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(CanRetain));
    }
}
