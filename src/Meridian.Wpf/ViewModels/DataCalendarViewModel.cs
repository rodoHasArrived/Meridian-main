using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using DataCalendarService = Meridian.Ui.Services.DataCalendarService;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Data Calendar page.
/// Holds year navigation state, summary metrics, monthly data, and all async loading.
/// </summary>
public sealed class DataCalendarViewModel : BindableBase
{
    private readonly DataCalendarService _calendarService;

    private int _currentYear = DateTime.UtcNow.Year;

    // ── Bindable properties ─────────────────────────────────────────────────

    public string CurrentYearText => _currentYear.ToString();

    private string _tradingDaysText = "--";
    public string TradingDaysText { get => _tradingDaysText; private set => SetProperty(ref _tradingDaysText, value); }

    private string _daysWithDataText = "--";
    public string DaysWithDataText { get => _daysWithDataText; private set => SetProperty(ref _daysWithDataText, value); }

    private string _totalGapsText = "--";
    public string TotalGapsText { get => _totalGapsText; private set => SetProperty(ref _totalGapsText, value); }

    private string _completenessText = "--";
    public string CompletenessText { get => _completenessText; private set => SetProperty(ref _completenessText, value); }

    private Brush _completenessForeground = Brushes.White;
    public Brush CompletenessForeground { get => _completenessForeground; private set => SetProperty(ref _completenessForeground, value); }

    private string _monthlyStatusText = "Loading calendar data...";
    public string MonthlyStatusText { get => _monthlyStatusText; private set => SetProperty(ref _monthlyStatusText, value); }

    private Brush _monthlyStatusForeground = Brushes.Gray;
    public Brush MonthlyStatusForeground { get => _monthlyStatusForeground; private set => SetProperty(ref _monthlyStatusForeground, value); }

    private IEnumerable<object>? _monthlyData;
    public IEnumerable<object>? MonthlyData { get => _monthlyData; private set => SetProperty(ref _monthlyData, value); }

    // ── Cached brushes from application resources ───────────────────────────
    private readonly Brush _successBrush;
    private readonly Brush _infoBrush;
    private readonly Brush _warningBrush;
    private readonly Brush _errorBrush;
    private readonly Brush _mutedBrush;

    public DataCalendarViewModel(
        DataCalendarService calendarService,
        Brush successBrush,
        Brush infoBrush,
        Brush warningBrush,
        Brush errorBrush,
        Brush mutedBrush)
    {
        _calendarService = calendarService;
        _successBrush = successBrush;
        _infoBrush = infoBrush;
        _warningBrush = warningBrush;
        _errorBrush = errorBrush;
        _mutedBrush = mutedBrush;
    }

    // ── Public actions ──────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        await LoadCalendarDataAsync();
    }

    public async Task PreviousYearAsync()
    {
        _currentYear--;
        RaisePropertyChanged(nameof(CurrentYearText));
        await LoadCalendarDataAsync();
    }

    public async Task NextYearAsync()
    {
        _currentYear++;
        RaisePropertyChanged(nameof(CurrentYearText));
        await LoadCalendarDataAsync();
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task LoadCalendarDataAsync()
    {
        MonthlyStatusText = "Loading calendar data...";
        MonthlyStatusForeground = _mutedBrush;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var yearData = await _calendarService.GetYearCalendarAsync(_currentYear, ct: cts.Token);

            TradingDaysText = yearData.TotalTradingDays.ToString();
            DaysWithDataText = yearData.DaysWithData.ToString();
            TotalGapsText = yearData.TotalGaps.ToString();
            CompletenessText = $"{yearData.OverallCompleteness:F1}%";

            CompletenessForeground = yearData.OverallCompleteness switch
            {
                >= 95 => _successBrush,
                >= 80 => _infoBrush,
                >= 50 => _warningBrush,
                _     => _errorBrush
            };

            if (yearData.Months.Count > 0)
            {
                MonthlyData = yearData.Months;
                MonthlyStatusText = $"Showing {yearData.Months.Count} months";
                MonthlyStatusForeground = _mutedBrush;
            }
            else
            {
                MonthlyData = null;
                MonthlyStatusText = "No calendar data available.";
                MonthlyStatusForeground = _mutedBrush;
            }
        }
        catch (Exception ex)
        {
            MonthlyStatusText = $"Failed to load calendar data: {ex.Message}";
            MonthlyStatusForeground = _errorBrush;

            TradingDaysText = "--";
            DaysWithDataText = "--";
            TotalGapsText = "--";
            CompletenessText = "--";
            MonthlyData = null;
        }
    }
}
