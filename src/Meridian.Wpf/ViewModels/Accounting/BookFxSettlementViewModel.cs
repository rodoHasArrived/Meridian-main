using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class BookFxSettlementViewModel : BindableBase
{
    private readonly ILedgerReportsApiClient _client;
    private string _setId = string.Empty;
    private string _statusText = string.Empty;
    private BookFxSettlementSetDto? _result;

    public BookFxSettlementViewModel(ILedgerReportsApiClient client)
    {
        _client = client;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => Guid.TryParse(SetId, out _));
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public string SetId
    {
        get => _setId;
        set
        {
            if (!SetProperty(ref _setId, value ?? string.Empty))
                return;
            Result = null;
            StatusText = string.Empty;
            LoadCommand.NotifyCanExecuteChanged();
        }
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public BookFxSettlementSetDto? Result { get => _result; private set => SetProperty(ref _result, value); }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(SetId, out var setId))
            return;
        Result = null;
        StatusText = "Loading settlement evidence…";
        try
        {
            var response = await _client.GetBookFxSettlementSetAsync(setId, ct);
            if (!Guid.TryParse(SetId, out var selected) || selected != setId)
                return;
            if (!response.Success || response.Data is not { } evidence || evidence.Plan.Input.SetId != setId)
            {
                StatusText = response.ErrorMessage ?? "The settlement evidence is unavailable for this posting set.";
                return;
            }
            Result = evidence;
            StatusText = evidence.IsComplete ? "All required journal legs retained."
                : $"Posting set incomplete. {evidence.MissingLegs.Count} missing and {evidence.InvalidatedLegs.Count} require review.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = "Settlement evidence request cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }
}
