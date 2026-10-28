using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Accounting;

/// <summary>Review preparation and request a governed candidate. Financial calculations remain server owned.</summary>
public sealed class FactorPaydownViewModel : BindableBase
{
    private readonly IFactorPaydownApiClient? _client;
    private string? _fundProfileId;
    private Guid? _bookId;
    private long _generation;
    private CancellationTokenSource? _operation;
    private FactorPaydownPreparationDto? _preview;
    private PrepareFactorPaydownRequestDto? _reviewedRequest;
    private string _status = "Select a fund and ledger book to prepare a factor paydown.";
    private bool _busy;
    private bool _confirmed;
    private AccountingBasisKindDto _basis;

    public FactorPaydownViewModel(IFactorPaydownApiClient? client)
    {
        _client = client;
        Fields = new([
            new("security", "Security ID"), new("position", "Book position ID"), new("period", "Period ID"),
            new("periodVersion", "Expected period version"), new("date", "Effective date (yyyy-MM-dd)"),
            new("prior", "Prior factor"), new("next", "New factor"), new("domain", "Source domain"),
            new("source", "Source entity ID"), new("hash", "Source SHA-256"),
            new("occurred", "Observation time (UTC, ISO 8601)"), new("cash", "Cash account path"),
            new("carrying", "Carrying account path"), new("gain", "Realized gain account path"),
            new("loss", "Realized loss account path"), new("evidence", "Exact retained factor evidence (JSON array)")]);
        foreach (var field in Fields)
            field.PropertyChanged += (_, _) => Invalidate("Inputs changed. Prepare again to review the current paydown.");
        PrepareCommand = new AsyncRelayCommand(PrepareAsync, () => !IsBusy && _client is not null && _bookId.HasValue);
        CreateDraftCommand = new AsyncRelayCommand(CreateDraftAsync, () => CanCreateDraft);
    }

    public ObservableCollection<FactorPaydownInput> Fields { get; }
    public IReadOnlyList<AccountingBasisKindDto> AccountingBases { get; } = Enum.GetValues<AccountingBasisKindDto>();
    public AccountingBasisKindDto AccountingBasis { get => _basis; set { if (SetProperty(ref _basis, value)) Invalidate("Accounting basis changed. Prepare again."); } }
    public IAsyncRelayCommand PrepareCommand { get; }
    public IAsyncRelayCommand CreateDraftCommand { get; }
    public string ScopeText => _bookId.HasValue ? $"{_fundProfileId} · ledger book {_bookId:D}" : "No selected ledger scope";
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    public bool Confirmed { get => _confirmed; set { SetProperty(ref _confirmed, value); NotifyCommands(); } }
    public bool CanCreateDraft => !IsBusy && Confirmed && _preview is not null;
    public string PreviewText => _preview is { } preview
        ? $"{preview.Instruction.PriorFactor:G} → {preview.Instruction.NewFactor:G}\nPrincipal: {preview.Instruction.PrincipalPaydown:G29} {preview.Instruction.Currency}\n"
          + string.Join("\n", preview.Components.Select(pair => $"{ComponentLabel(pair.Key)}: {pair.Value}"))
          + "\n" + string.Join("\n", preview.Instruction.Lots.Select(lot =>
              $"Lot {lot.ExpectedLot.LotId} · v{lot.ExpectedLot.Version}: principal {lot.Principal:G29}; remaining basis {lot.FunctionalBasisAfter:G29}"))
        : "No current server preparation. Lots, principal and basis relief are calculated from retained records.";

    public void SetScope(string? fundProfileId, Guid? bookId)
    {
        Publish(() =>
        {
            _fundProfileId = fundProfileId;
            _bookId = bookId is { } id && id != Guid.Empty && !string.IsNullOrWhiteSpace(fundProfileId) ? id : null;
            Invalidate("Scope refreshed. Prepare the paydown against the selected fund and book.");
            RaisePropertyChanged(nameof(ScopeText));
        });
    }

    public void Cancel() => Publish(() => Invalidate("Paydown review cancelled. Prepare again before creating a draft."));

    private async Task PrepareAsync()
    {
        Invalidate("Preparing paydown from retained lots…");
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            var request = BuildRequest();
            var preview = await _client!.PrepareAsync(request, operation.Token);
            if (generation != _generation) return;
            var scope = preview.Instruction.Scope;
            if (scope.LedgerBookId != request.LedgerBookId || scope.FundProfileId != request.FundProfileId
                || scope.SecurityId != request.SecurityId || scope.BookPositionId != request.BookPositionId
                || scope.PeriodId != request.PeriodId || scope.AccountingBasis != request.AccountingBasis)
                throw new InvalidOperationException("The preparation does not match the selected scope.");
            _preview = preview;
            _reviewedRequest = request;
            RaisePropertyChanged(nameof(PreviewText));
            StatusText = "Review the calculated amounts and confirm before creating a draft. Nothing has posted.";
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = "Preparation cancelled. Prepare again."; }
        catch (Exception ex) { if (generation == _generation) StatusText = $"Paydown preparation blocked: {ex.Message}"; }
        finally { if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private async Task CreateDraftAsync()
    {
        if (!CanCreateDraft || _preview is not { } preview || _reviewedRequest is not { } request) return;
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            var result = await _client!.CreateDraftAsync(new(request, preview.PreparationFingerprint, "Reviewed factor paydown"), operation.Token);
            if (generation != _generation) return;
            if (result.Spine.Scope.LedgerBookId != request.LedgerBookId || result.Spine.Scope.FundProfileId != request.FundProfileId
                || result.Spine.Scope.SecurityId != request.SecurityId || result.Spine.Scope.BookPositionId != request.BookPositionId
                || result.Spine.Scope.PeriodId != request.PeriodId || result.Spine.Scope.AccountingBasis != request.AccountingBasis)
                throw new InvalidOperationException("The candidate does not match the reviewed paydown scope.");
            _preview = null;
            Confirmed = false;
            RaisePropertyChanged(nameof(PreviewText));
            StatusText = result.Candidate.HasBlockingIssues
                ? "Candidate blocked: " + string.Join("; ", result.Candidate.Issues.Select(issue => issue.Message))
                : $"Retained candidate for event {result.Spine.EventId:D}. Complete the governed approval workflow before posting. Nothing has posted.";
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = "Draft request cancelled. Check retained history before retrying."; }
        catch (Exception ex) { if (generation == _generation) StatusText = $"Draft request blocked: {ex.Message}. Check retained history before retrying."; }
        finally { if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private PrepareFactorPaydownRequestDto BuildRequest()
    {
        string Text(string key) => Fields.Single(field => field.Key == key).Value.Trim();
        Guid Id(string key) => Guid.TryParse(Text(key), out var id) && id != Guid.Empty ? id : throw new FormatException($"{key} requires a valid identifier.");
        decimal Number(string key) => decimal.Parse(Text(key), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var evidence = JsonSerializer.Deserialize<RetainedEvidenceIdentityDto[]>(Text("evidence"), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new FormatException("Exact retained factor evidence is required.");
        if (evidence.Length == 0) throw new FormatException("Exact retained factor evidence is required.");
        var observationTime = Text("occurred");
        if (!observationTime.EndsWith("Z", StringComparison.OrdinalIgnoreCase) && !observationTime.EndsWith("+00:00", StringComparison.Ordinal))
            throw new FormatException("Observation time must state UTC explicitly (Z or +00:00).");
        return new(_bookId!.Value, Id("security"), Id("position"), Id("period"),
            long.Parse(Text("periodVersion"), CultureInfo.InvariantCulture), AccountingBasis, _fundProfileId!,
            DateOnly.ParseExact(Text("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture), Number("prior"), Number("next"),
            Text("domain"), Text("source"), Text("hash"), DateTimeOffset.Parse(observationTime, CultureInfo.InvariantCulture),
            evidence, Text("cash"), Text("carrying"), Text("gain"), Text("loss"));
    }

    private static string ComponentLabel(string key) => key switch
    {
        "lot.principal" => "Principal",
        "lot.basisRelieved" => "Basis relieved",
        "lot.realizedGain" => "Realized gain",
        "lot.realizedLoss" => "Realized loss",
        _ => key
    };

    private void Invalidate(string status)
    {
        if (_busy && _preview is not null)
            status += " A draft request may have reached the server. Check retained history before retrying.";
        Interlocked.Increment(ref _generation);
        _operation?.Cancel();
        _operation = null;
        _preview = null;
        _reviewedRequest = null;
        _confirmed = false;
        _busy = false;
        StatusText = status;
        RaisePropertyChanged(nameof(PreviewText));
        RaisePropertyChanged(nameof(Confirmed));
        RaisePropertyChanged(nameof(IsBusy));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RaisePropertyChanged(nameof(CanCreateDraft));
        PrepareCommand?.NotifyCanExecuteChanged();
        CreateDraftCommand?.NotifyCanExecuteChanged();
    }

    private static void Publish(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(action);
        else action();
    }
}

public sealed class FactorPaydownInput(string key, string label) : BindableBase
{
    private string _value = string.Empty;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}
