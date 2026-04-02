using System.Collections.ObjectModel;
using System.Windows.Media;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// A single cell in the quality calendar, representing one day's data.
/// </summary>
public sealed class QualityCalendarCell
{
    public DateOnly Date { get; init; }
    public double Score { get; init; }
    public string DisplayDate { get; init; } = string.Empty;
    public Brush Background { get; init; } = Brushes.Transparent;
}

/// <summary>
/// ViewModel for displaying a perpetual symbol quality archive as a calendar heatmap.
/// Last 90 days loaded on demand when symbol is selected and Load button is clicked.
/// </summary>
public sealed partial class QualityArchiveViewModel : BindableBase, IDisposable
{
    private readonly IQualityArchiveStore _archiveStore;

    // Simple two-way bindable property — public setter is fine for combo-box selection.
    [ObservableProperty] private string _selectedSymbol = string.Empty;

    // Read-only-from-outside properties — kept as manual properties with private setters.
    private ObservableCollection<QualityCalendarCell> _calendarCells = new();
    public ObservableCollection<QualityCalendarCell> CalendarCells
    {
        get => _calendarCells;
        private set => SetProperty(ref _calendarCells, value);
    }

    private ObservableCollection<string> _symbols = new();
    public ObservableCollection<string> Symbols
    {
        get => _symbols;
        private set => SetProperty(ref _symbols, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private bool _isDisposed;

    public QualityArchiveViewModel(IQualityArchiveStore archiveStore)
    {
        _archiveStore = archiveStore;
        _ = InitializeSymbolsAsync();
    }

    // LoadCommand is generated from [RelayCommand] on LoadAsync (matches XAML binding).
    [RelayCommand]
    private async Task LoadAsync()
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

    private async Task InitializeSymbolsAsync()
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

    private static Brush GetBackgroundBrush(double score, bool hasData)
    {
        // Grey for no data
        if (!hasData)
        {
            return MakeFrozenBrush(Colors.LightGray);
        }

        // Green for > 90%
        if (score > 0.90)
        {
            return MakeFrozenBrush(Color.FromRgb(34, 139, 34)); // ForestGreen
        }

        // Amber for 70-90%
        if (score >= 0.70)
        {
            return MakeFrozenBrush(Color.FromRgb(255, 165, 0)); // Orange
        }

        // Red for < 70%
        return MakeFrozenBrush(Color.FromRgb(178, 34, 34)); // FireBrick
    }

    private static Brush MakeFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _archiveStore?.DisposeAsync().GetAwaiter().GetResult();
    }
}

