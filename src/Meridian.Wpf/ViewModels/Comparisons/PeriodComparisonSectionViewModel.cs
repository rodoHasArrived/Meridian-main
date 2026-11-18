using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.Comparisons;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels.Comparisons;

/// <summary>
/// One prior-period comparison section (UX Phase E parity): NAV, run attribution or the cash ladder.
/// It loads a shared <see cref="PeriodComparisonDto"/> and shows it through
/// <see cref="PeriodComparisonProjection"/>, so the desktop formats, flags and explains the figures
/// exactly as the browser's comparison panel does. The source is a delegate, so each surface can read
/// its in-process service or the HTTP API.
/// </summary>
public sealed class PeriodComparisonSectionViewModel : BindableBase
{
    private readonly decimal? _materialThreshold;
    private readonly IReadOnlyCollection<string> _countKeys;
    private Func<CancellationToken, Task<PeriodComparisonDto>>? _source;
    private CancellationTokenSource? _loading;
    private bool _isLoading;
    private bool _hasRows;
    private bool _hasFailed;
    private string _heading = string.Empty;
    private string _windows = string.Empty;
    private string _message;

    public PeriodComparisonSectionViewModel(
        string title,
        string description,
        string idleMessage,
        decimal? materialThreshold = PeriodComparisonProjection.PortfolioMaterialAbsoluteVariance,
        IReadOnlyCollection<string>? countKeys = null)
    {
        Title = title;
        Description = description;
        _message = idleMessage;
        _materialThreshold = materialThreshold;
        _countKeys = countKeys ?? [];
        Table = new WorkstationTableModel<PeriodComparisonRow>(
            Rows,
            [
                new("Line", nameof(PeriodComparisonRow.Label), 220),
                new("Now", nameof(PeriodComparisonRow.Current), 140),
                new("Prior", nameof(PeriodComparisonRow.Prior), 140),
                new("Variance", nameof(PeriodComparisonRow.Change), 140),
                new("Variance %", nameof(PeriodComparisonRow.ChangePercent), 170),
                new("Flag", nameof(PeriodComparisonRow.MaterialLabel), 90)
            ],
            title,
            "Nothing to compare",
            idleMessage);
        RetryCommand = new AsyncRelayCommand(ReloadAsync, () => _source is not null && !IsLoading);
    }

    public string Title { get; }

    public string Description { get; }

    public ObservableCollection<PeriodComparisonRow> Rows { get; } = [];

    public ObservableCollection<string> Notes { get; } = [];

    public WorkstationTableModel<PeriodComparisonRow> Table { get; }

    public IAsyncRelayCommand RetryCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RetryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasRows
    {
        get => _hasRows;
        private set
        {
            if (SetProperty(ref _hasRows, value))
            {
                RaisePropertyChanged(nameof(IsMessageVisible));
            }
        }
    }

    /// <summary>True when the last load failed; the panel then offers a retry.</summary>
    public bool HasFailed
    {
        get => _hasFailed;
        private set => SetProperty(ref _hasFailed, value);
    }

    public bool IsMessageVisible => !HasRows;

    public string Heading
    {
        get => _heading;
        private set => SetProperty(ref _heading, value);
    }

    public string Windows
    {
        get => _windows;
        private set => SetProperty(ref _windows, value);
    }

    /// <summary>Loading, why there is nothing to compare, or why the load failed.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Reads the comparison from <paramref name="source"/>; a later call replaces an earlier one still loading.</summary>
    public Task LoadAsync(Func<CancellationToken, Task<PeriodComparisonDto>> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        return ReloadAsync();
    }

    /// <summary>Clears the section and shows <paramref name="message"/>, cancelling a load in progress.</summary>
    public void Reset(string message)
    {
        _loading?.Cancel();
        _source = null;
        Apply(new PeriodComparisonView(false, string.Empty, string.Empty, message, [], []));
        HasFailed = false;
        IsLoading = false;
    }

    private async Task ReloadAsync()
    {
        if (_source is null)
        {
            return;
        }

        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;
        IsLoading = true;
        HasFailed = false;
        Apply(PeriodComparisonProjection.Project(null));
        try
        {
            var comparison = await _source(loading.Token);
            if (!loading.IsCancellationRequested)
            {
                Apply(PeriodComparisonProjection.Project(comparison, _materialThreshold, _countKeys));
            }
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            // Replaced by a newer load or reset.
        }
        catch (Exception failure)
        {
            if (!loading.IsCancellationRequested)
            {
                Apply(new PeriodComparisonView(false, string.Empty, string.Empty, failure.Message, [], []));
                HasFailed = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                IsLoading = false;
            }
        }
    }

    private void Apply(PeriodComparisonView view)
    {
        Heading = view.Heading;
        Windows = view.Windows;
        Message = view.Message ?? string.Empty;
        Rows.Clear();
        foreach (var row in view.Rows)
        {
            Rows.Add(row);
        }

        Notes.Clear();
        foreach (var note in view.Notes)
        {
            Notes.Add(note);
        }

        HasRows = view.IsReady;
    }
}
