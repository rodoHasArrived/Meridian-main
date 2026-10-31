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

/// <summary>One retained bond reference row shown exactly as served.</summary>
public sealed record BondReferenceLadderRow(Guid SecurityId, string DisplayName, string Identifier, string Issuer, string Currency, string Maturity, string Callable, long Version)
{
    public static BondReferenceLadderRow From(BondReferenceDto dto) => new(dto.SecurityId, dto.DisplayName, dto.PrimaryIdentifier, dto.IssuerName ?? "Unknown", dto.Currency,
        dto.Lifecycle?.MaturityDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown",
        dto.Lifecycle is null ? "Unknown" : dto.Lifecycle.IsCallable ? (dto.Lifecycle.CallDate is { } call ? $"Yes ({call:yyyy-MM-dd})" : "Yes") : "No",
        dto.Version);
}

/// <summary>
/// Desktop parity for the browser bond reference ladders over the bounded keyset routes: rows accumulate page
/// by page behind an explicit "Load more", the opaque cursor stays bound to the query that produced it, and a
/// changed query discards the accumulated rows instead of mixing pages.
/// </summary>
public sealed class BondReferenceLadderViewModel : BindableBase
{
    public const int PageSize = 100;
    private readonly IWorkstationSecurityMasterApiClient _client;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private string _mode = "Issuer";
    private string _issuerName = string.Empty;
    private DateTime? _from;
    private DateTime? _to;
    private string? _cursor;
    private bool _loaded;
    private string _status = "Choose an issuer or a maturity window and load the ladder.";

    public BondReferenceLadderViewModel(IWorkstationSecurityMasterApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        LoadCommand = new AsyncRelayCommand(() => LoadAsync(first: true), () => !IsBusy);
        LoadMoreCommand = new AsyncRelayCommand(() => LoadAsync(first: false), () => !IsBusy && _cursor is not null);
    }

    public IReadOnlyList<string> Modes { get; } = ["Issuer", "Maturity window"];
    public ObservableCollection<BondReferenceLadderRow> Rows { get; } = new();
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand LoadMoreCommand { get; }

    public string Mode { get => _mode; set => Edit(ref _mode, value ?? "Issuer"); }
    public bool IsIssuerMode => _mode == "Issuer";
    public string IssuerName { get => _issuerName; set => Edit(ref _issuerName, value ?? string.Empty); }
    public DateTime? From { get => _from; set => Edit(ref _from, value); }
    public DateTime? To { get => _to; set => Edit(ref _to, value); }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyCommands(); } }
    public bool HasMore => _cursor is not null;
    public bool IsLoaded => _loaded;
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }

    private async Task LoadAsync(bool first)
    {
        if (IsIssuerMode ? string.IsNullOrWhiteSpace(IssuerName) : From is null || To is null)
        {
            StatusText = IsIssuerMode ? "Enter an issuer name." : "Enter the maturity window start and end dates.";
            return;
        }
        if (!first && _cursor is null) return;
        var cursor = first ? null : _cursor;
        var token = Begin(first ? "Loading ladder…" : "Loading next page…");
        try
        {
            var response = IsIssuerMode
                ? await _client.GetBondIssuerLadderPageAsync(IssuerName.Trim(), PageSize, cursor, token).ConfigureAwait(true)
                : await _client.GetBondMaturityLadderPageAsync(DateOnly.FromDateTime(From!.Value), DateOnly.FromDateTime(To!.Value), PageSize, cursor, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (!response.Success || response.Data is null)
            {
                StatusText = $"Bond reference ladder could not be loaded: {response.ErrorMessage ?? "no response"}.";
                return;
            }
            if (first) Rows.Clear();
            var seen = new HashSet<Guid>(Rows.Select(row => row.SecurityId));
            foreach (var item in response.Data.Items.Where(item => seen.Add(item.SecurityId)))
                Rows.Add(BondReferenceLadderRow.From(item));
            _cursor = response.Data.NextCursor;
            _loaded = true;
            RaisePropertyChanged(nameof(HasMore));
            RaisePropertyChanged(nameof(IsLoaded));
            StatusText = $"{Rows.Count} row(s) loaded{(_cursor is null ? "; end of ladder." : "; more pages available.")}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) StatusText = $"Bond reference ladder could not be loaded: {ex.Message}"; }
        finally { End(token); }
    }

    /// <summary>A changed query discards accumulated rows; pages from different queries are never mixed.</summary>
    private void Edit<T>(ref T field, T value)
    {
        if (!SetProperty(ref field, value)) return;
        _operation?.Cancel();
        _operation = null;
        _busy = false;
        RaisePropertyChanged(nameof(IsBusy));
        Rows.Clear();
        _cursor = null;
        _loaded = false;
        RaisePropertyChanged(nameof(IsIssuerMode));
        RaisePropertyChanged(nameof(HasMore));
        RaisePropertyChanged(nameof(IsLoaded));
        StatusText = "Load the ladder for the new query.";
        NotifyCommands();
    }

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
        LoadMoreCommand.NotifyCanExecuteChanged();
    }
}
