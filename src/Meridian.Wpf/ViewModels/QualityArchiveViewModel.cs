using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Input;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// A single cell in the quality calendar, representing one day's data.
/// </summary>
public sealed class QualityCalendarCell
{
    public DateOnly Date { get; init; }
    public double Score { get; init; }
    public string DisplayDate { get; init; }
    public Brush Background { get; init; }
}

/// <summary>
/// ViewModel for displaying a perpetual symbol quality archive as a calendar heatmap.
/// Last 90 days loaded on demand when symbol is selected and Load button is clicked.
/// </summary>
public sealed class QualityArchiveViewModel : BindableBase, IDisposable
{
    private readonly IQualityArchiveStore _archiveStore;
    private ICommand? _loadCommand;
    private string _selectedSymbol = string.Empty;
    private ObservableCollection<QualityCalendarCell> _calendarCells = new();
    private ObservableCollection<string> _symbols = new();
    private bool _isLoading;
    private string _statusMessage = "Ready";
    private bool _isDisposed;

    public QualityArchiveViewModel(IQualityArchiveStore archiveStore)
    {
        _archiveStore = archiveStore;
        InitializeSymbols();
    }

    public string SelectedSymbol
    {
        get => _selectedSymbol;
        set => SetProperty(ref _selectedSymbol, value);
    }

    public ObservableCollection<QualityCalendarCell> CalendarCells
    {
        get => _calendarCells;
        private set => SetProperty(ref _calendarCells, value);
    }

    public ObservableCollection<string> Symbols
    {
        get => _symbols;
        private set => SetProperty(ref _symbols, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand LoadCommand => _loadCommand ??= new AsyncRelayCommand(OnLoadAsync);

    private async void InitializeSymbols()
    {
        try
        {
            var symbols = await _archiveStore.GetSymbolsAsync(CancellationToken.None).ConfigureAwait(true);
            foreach (var sym in symbols)
            {
                Symbols.Add(sym);
            }

            if (Symbols.Count > 0)
            {
                SelectedSymbol = Symbols[0];
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading symbols: {ex.Message}";
        }
    }

    private async Task OnLoadAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSymbol))
        {
            StatusMessage = "Please select a symbol";
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading data...";

        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var from = today.AddDays(-90);

            var records = await _archiveStore.GetHistoryAsync(SelectedSymbol, from, today, CancellationToken.None)
                .ConfigureAwait(true);

            var cells = new List<QualityCalendarCell>();

            // Create cells for the last 90 days, even if no data exists
            for (var date = from; date <= today; date = date.AddDays(1))
            {
                var record = records.FirstOrDefault(r => r.Date == date);
                var score = record?.CompletenessScore ?? 0;
                var background = GetBackgroundBrush(score, record != null);

                cells.Add(new QualityCalendarCell
                {
                    Date = date,
                    Score = score,
                    DisplayDate = date.ToString("ddd MMM d"),
                    Background = background
                });
            }

            CalendarCells = new ObservableCollection<QualityCalendarCell>(cells);
            StatusMessage = $"Loaded {records.Count} records for {SelectedSymbol}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static Brush GetBackgroundBrush(double score, bool hasData)
    {
        // Grey for no data
        if (!hasData)
        {
            return new SolidColorBrush(Colors.LightGray) { Freeze() };
        }

        // Green for > 90%
        if (score > 0.90)
        {
            return new SolidColorBrush(Color.FromRgb(34, 139, 34)) { Freeze() }; // ForestGreen
        }

        // Amber for 70-90%
        if (score >= 0.70)
        {
            return new SolidColorBrush(Color.FromRgb(255, 165, 0)) { Freeze() }; // Orange
        }

        // Red for < 70%
        return new SolidColorBrush(Color.FromRgb(178, 34, 34)) { Freeze() }; // FireBrick
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _archiveStore?.DisposeAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Simple async relay command implementation for WPF.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<Task> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await _execute().ConfigureAwait(true);
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
