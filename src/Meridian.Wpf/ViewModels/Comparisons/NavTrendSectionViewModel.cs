using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Services.Comparisons;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels.Comparisons;

/// <summary>One NAV trend point, formatted for the table beside the charts.</summary>
public sealed record NavTrendRow(string AsOf, string Nav, string Long, string Short, string Gross, string Net);

/// <summary>
/// Ledger NAV and securities exposure at recent month-ends (UX Phase E parity), from the shared
/// <see cref="FundNavTrendDto"/>. The charts are ScottPlot requests, and the same values are listed
/// in a table, as in the browser.
/// </summary>
public sealed class NavTrendSectionViewModel : BindableBase
{
    private Func<CancellationToken, Task<FundNavTrendDto>>? _source;
    private CancellationTokenSource? _loading;
    private PlotRequest? _navPlot;
    private PlotRequest? _exposurePlot;
    private bool _hasTrend;
    private bool _hasExposure;
    private bool _hasFailed;
    private string _message = "Choose a fund profile to see its NAV trend.";

    public NavTrendSectionViewModel()
    {
        Table = new WorkstationTableModel<NavTrendRow>(
            Rows,
            [
                new("As of", nameof(NavTrendRow.AsOf), 110),
                new("NAV", nameof(NavTrendRow.Nav), 150),
                new("Long", nameof(NavTrendRow.Long), 140),
                new("Short", nameof(NavTrendRow.Short), 140),
                new("Gross", nameof(NavTrendRow.Gross), 140),
                new("Net", nameof(NavTrendRow.Net), 140)
            ],
            "NAV and exposure by month-end",
            "No NAV trend",
            "At least two dates with ledger NAV are needed for a trend.");
        RetryCommand = new AsyncRelayCommand(ReloadAsync, () => _source is not null);
    }

    public ObservableCollection<NavTrendRow> Rows { get; } = [];

    public ObservableCollection<string> Notes { get; } = [];

    public WorkstationTableModel<NavTrendRow> Table { get; }

    public IAsyncRelayCommand RetryCommand { get; }

    public PlotRequest? NavPlot
    {
        get => _navPlot;
        private set => SetProperty(ref _navPlot, value);
    }

    public PlotRequest? ExposurePlot
    {
        get => _exposurePlot;
        private set => SetProperty(ref _exposurePlot, value);
    }

    /// <summary>True when there are at least two dated points to draw.</summary>
    public bool HasTrend
    {
        get => _hasTrend;
        private set
        {
            if (SetProperty(ref _hasTrend, value))
            {
                RaisePropertyChanged(nameof(IsMessageVisible));
            }
        }
    }

    /// <summary>True when some date holds securities, so the exposure chart is drawn.</summary>
    public bool HasExposure
    {
        get => _hasExposure;
        private set => SetProperty(ref _hasExposure, value);
    }

    public bool HasFailed
    {
        get => _hasFailed;
        private set => SetProperty(ref _hasFailed, value);
    }

    public bool IsMessageVisible => !HasTrend;

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public Task LoadAsync(Func<CancellationToken, Task<FundNavTrendDto>> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        RetryCommand.NotifyCanExecuteChanged();
        return ReloadAsync();
    }

    public void Reset(string message)
    {
        _loading?.Cancel();
        _source = null;
        RetryCommand.NotifyCanExecuteChanged();
        Apply(null, message);
        HasFailed = false;
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
        HasFailed = false;
        Apply(null, "Loading the NAV trend…");
        try
        {
            var trend = await _source(loading.Token);
            if (!loading.IsCancellationRequested)
            {
                Apply(trend, "At least two dates with ledger NAV are needed for a trend.");
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
                Apply(null, failure.Message);
                HasFailed = true;
            }
        }
    }

    private void Apply(FundNavTrendDto? trend, string message)
    {
        var points = trend?.Points ?? [];
        Rows.Clear();
        foreach (var point in points)
        {
            Rows.Add(new NavTrendRow(
                point.AsOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                PeriodComparisonProjection.FormatAmount(point.TotalNav),
                PeriodComparisonProjection.FormatAmount(point.LongExposure),
                PeriodComparisonProjection.FormatAmount(point.ShortExposure),
                PeriodComparisonProjection.FormatAmount(point.GrossExposure),
                PeriodComparisonProjection.FormatAmount(point.NetExposure)));
        }

        Notes.Clear();
        foreach (var note in trend?.Notes ?? [])
        {
            Notes.Add(note);
        }

        var drawable = points.Count >= 2;
        var currency = trend?.Currency ?? string.Empty;
        NavPlot = drawable
            ? new PlotRequest($"Ledger NAV · {currency}", PlotType.Line,
                Series: points.Select(static point => (point.AsOf, (double)point.TotalNav)).ToArray())
            : null;
        HasExposure = drawable && points.Any(static point => point.GrossExposure != 0m);
        ExposurePlot = HasExposure
            ? new PlotRequest($"Securities exposure · {currency}", PlotType.MultiLine,
                MultiSeries:
                [
                    ("Long", points.Select(static point => (point.AsOf, (double)point.LongExposure)).ToArray()),
                    ("Short", points.Select(static point => (point.AsOf, (double)point.ShortExposure)).ToArray()),
                    ("Gross", points.Select(static point => (point.AsOf, (double)point.GrossExposure)).ToArray())
                ])
            : null;
        Message = drawable ? string.Empty : message;
        HasTrend = drawable;
    }
}
