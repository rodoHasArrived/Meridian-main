using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Desktop scope picker backed by the shared <c>GET /api/workstation/scope-options</c> seam: the
/// entities, funds and fund accounts the operator may scope to, with a chosen entity narrowing
/// the funds and a chosen entity or fund narrowing the accounts, by the same rule the browser
/// workstation's scope picker applies (<see cref="WorkstationScopeOptionsNarrowing"/>).
/// </summary>
public sealed class WorkstationScopeOptionsViewModel : BindableBase
{
    private static readonly WorkstationScopeOptionsDto EmptyOptions = new(
        Array.Empty<WorkstationScopeEntityOptionDto>(),
        Array.Empty<WorkstationScopeFundOptionDto>(),
        Array.Empty<WorkstationScopeAccountOptionDto>());

    private readonly IWorkstationScopeOptionsApiClient _client;
    private readonly ObservableCollection<WorkstationScopeEntityOptionDto> _entities = [];
    private readonly ObservableCollection<WorkstationScopeFundOptionDto> _funds = [];
    private readonly ObservableCollection<WorkstationScopeAccountOptionDto> _accounts = [];
    private WorkstationScopeOptionsDto _options = EmptyOptions;
    private WorkstationScopeOptionsSource _source = WorkstationScopeOptionsSource.Unavailable;
    private WorkstationScopeEntityOptionDto? _selectedEntity;
    private WorkstationScopeFundOptionDto? _selectedFund;
    private WorkstationScopeAccountOptionDto? _selectedAccount;
    private string _statusText = "Entity, fund and account lists have not been loaded yet.";
    private string _narrowingText = string.Empty;
    private bool _isLoading;
    private bool _hasLoaded;
    private bool _isRebuilding;

    public WorkstationScopeOptionsViewModel(IWorkstationScopeOptionsApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        Entities = new ReadOnlyObservableCollection<WorkstationScopeEntityOptionDto>(_entities);
        Funds = new ReadOnlyObservableCollection<WorkstationScopeFundOptionDto>(_funds);
        Accounts = new ReadOnlyObservableCollection<WorkstationScopeAccountOptionDto>(_accounts);

        ReloadCommand = new AsyncRelayCommand(ReloadAsync, () => !IsLoading);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => HasSelection);
    }

    public ReadOnlyObservableCollection<WorkstationScopeEntityOptionDto> Entities { get; }

    public ReadOnlyObservableCollection<WorkstationScopeFundOptionDto> Funds { get; }

    public ReadOnlyObservableCollection<WorkstationScopeAccountOptionDto> Accounts { get; }

    public IAsyncRelayCommand ReloadCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

    /// <summary>Where the current lists came from.</summary>
    public WorkstationScopeOptionsSource Source
    {
        get => _source;
        private set
        {
            if (SetProperty(ref _source, value))
            {
                RaisePropertyChanged(nameof(IsAvailable));
            }
        }
    }

    /// <summary>True when lists loaded (from the seam or the 404 fallback) and narrowing applies.</summary>
    public bool IsAvailable => Source != WorkstationScopeOptionsSource.Unavailable;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                ReloadCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Why the lists look the way they do: loaded counts, fallback source, or failure reason.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>How the chosen entity/fund narrowed the lists, including accounts left out; empty when nothing is narrowed.</summary>
    public string NarrowingText
    {
        get => _narrowingText;
        private set => SetProperty(ref _narrowingText, value);
    }

    public WorkstationScopeEntityOptionDto? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (_isRebuilding || !SetProperty(ref _selectedEntity, value))
            {
                return;
            }

            if (WorkstationScopeOptionsNarrowing.FundConflictsWithEntity(_options, IsAvailable, _selectedFund?.FundId, value?.EntityId))
            {
                _selectedFund = null;
                RaisePropertyChanged(nameof(SelectedFund));
            }

            OnSelectionChanged();
        }
    }

    public WorkstationScopeFundOptionDto? SelectedFund
    {
        get => _selectedFund;
        set
        {
            if (_isRebuilding || !SetProperty(ref _selectedFund, value))
            {
                return;
            }

            OnSelectionChanged();
        }
    }

    public WorkstationScopeAccountOptionDto? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (_isRebuilding || !SetProperty(ref _selectedAccount, value))
            {
                return;
            }

            RaiseSelectionDerivedProperties();
        }
    }

    public bool HasSelection => SelectedEntity is not null || SelectedFund is not null || SelectedAccount is not null;

    /// <summary>
    /// The narrowest pick as an operating-context key (account, else fund, else entity), in the
    /// <see cref="WorkstationOperatingContext.CreateContextKey"/> form the desktop context catalog
    /// uses; null when nothing is picked.
    /// </summary>
    public string? SelectedScopeContextKey
        => SelectedAccount is not null
            ? WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Account, SelectedAccount.AccountId.ToString("D"))
            : SelectedFund is not null
                ? WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, SelectedFund.FundId.ToString("D"))
                : SelectedEntity is not null
                    ? WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Entity, SelectedEntity.EntityId.ToString("D"))
                    : null;

    /// <summary>Display name of the narrowest pick, for status messages; null when nothing is picked.</summary>
    public string? SelectedScopeDisplayName
        => SelectedAccount?.DisplayName ?? SelectedFund?.Name ?? SelectedEntity?.Name;

    /// <summary>Loads the options once; later calls are no-ops until <see cref="ReloadCommand"/> runs.</summary>
    public Task InitializeAsync(CancellationToken ct = default)
        => _hasLoaded || IsLoading ? Task.CompletedTask : LoadAsync(ct);

    /// <summary>Loads (or reloads) the options from the shared seam and re-applies the current picks.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        StatusText = "Loading entities, funds and accounts…";
        try
        {
            var result = await _client.GetScopeOptionsAsync(ct);
            ApplyResult(result);
            _hasLoaded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = "Loading entities, funds and accounts was cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            // The client reports failures as results; this guards an unexpected throw so the
            // picker still says why it is empty instead of showing nothing silently.
            LoggingService.Instance.LogWarning(
                "Workstation scope options view model failed to load.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
            ApplyResult(new WorkstationScopeOptionsLoadResult(
                WorkstationScopeOptionsSource.Unavailable,
                EmptyOptions,
                "Entity, fund and account lists could not be loaded. Accounts are not narrowed by entity or fund."));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private Task ReloadAsync() => LoadAsync();

    private void ApplyResult(WorkstationScopeOptionsLoadResult result)
    {
        _options = result.Options;
        Source = result.Source;
        StatusText = result.StatusMessage;

        // Keep picks that still exist in the new payload (records compare by value, so look up by id).
        var entityId = _selectedEntity?.EntityId;
        var fundId = _selectedFund?.FundId;
        var accountId = _selectedAccount?.AccountId;
        _selectedEntity = entityId is null
            ? null
            : _options.Entities.FirstOrDefault(entity => entity.EntityId == entityId.Value);
        _selectedFund = fundId is null
            ? null
            : _options.Funds.FirstOrDefault(fund => fund.FundId == fundId.Value);
        _selectedAccount = accountId is null
            ? null
            : _options.Accounts.FirstOrDefault(account => account.AccountId == accountId.Value);

        Rebuild();
    }

    private void ClearSelection()
    {
        _selectedEntity = null;
        _selectedFund = null;
        _selectedAccount = null;
        Rebuild();
    }

    private void OnSelectionChanged()
    {
        if (WorkstationScopeOptionsNarrowing.AccountConflictsWithScope(
                _options,
                IsAvailable,
                _selectedAccount?.AccountId,
                _selectedEntity?.EntityId,
                _selectedFund?.FundId))
        {
            _selectedAccount = null;
        }

        Rebuild();
    }

    /// <summary>
    /// Re-fills the lists for the current picks. Item controls push a null selection back while
    /// their source is cleared, so setter writes are ignored for the duration and the picks are
    /// re-raised afterwards, letting the controls re-select them from the new lists.
    /// </summary>
    private void Rebuild()
    {
        var narrowed = WorkstationScopeOptionsNarrowing.Constrain(
            _options,
            IsAvailable,
            _selectedEntity?.EntityId,
            _selectedFund?.FundId);

        _isRebuilding = true;
        try
        {
            Replace(_entities, narrowed.Entities);
            Replace(_funds, narrowed.Funds);
            Replace(_accounts, narrowed.Accounts);
        }
        finally
        {
            _isRebuilding = false;
        }

        NarrowingText = DescribeNarrowing(narrowed);
        RaisePropertyChanged(nameof(SelectedEntity));
        RaisePropertyChanged(nameof(SelectedFund));
        RaisePropertyChanged(nameof(SelectedAccount));
        RaiseSelectionDerivedProperties();
    }

    private void RaiseSelectionDerivedProperties()
    {
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(SelectedScopeContextKey));
        RaisePropertyChanged(nameof(SelectedScopeDisplayName));
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private string DescribeNarrowing(WorkstationScopeNarrowedOptions narrowed)
    {
        if (!narrowed.FundsConstrained && !narrowed.AccountsConstrained)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (narrowed.FundsConstrained)
        {
            parts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} of {1} funds hold the chosen entity.",
                narrowed.Funds.Count,
                _options.Funds.Count));
        }

        if (narrowed.AccountsConstrained)
        {
            parts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Showing {0} of {1} accounts for the chosen {2}.",
                narrowed.Accounts.Count,
                _options.Accounts.Count,
                _selectedEntity is not null && _selectedFund is not null
                    ? "entity and fund"
                    : _selectedFund is not null ? "fund" : "entity"));
        }

        if (narrowed.AccountsWithoutKnownRelationship > 0)
        {
            parts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} account{1} with no stated fund or entity {2} left out.",
                narrowed.AccountsWithoutKnownRelationship,
                narrowed.AccountsWithoutKnownRelationship == 1 ? string.Empty : "s",
                narrowed.AccountsWithoutKnownRelationship == 1 ? "is" : "are"));
        }

        return string.Join(" ", parts);
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        // Leave an unchanged list alone so the control whose pick triggered the rebuild (entities
        // are never narrowed; funds only by entity) is not reset in the middle of its own change.
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
