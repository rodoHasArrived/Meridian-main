using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

public abstract class TableViewModel<TRow> : Meridian.Wpf.ViewModels.BindableBase
{
    private readonly BatchedObservableCollection<TRow> _rows = [];
    private bool _hasRows;

    protected TableViewModel()
    {
        Rows.CollectionChanged += OnRowsCollectionChanged;
        UpdateHasRows();
    }

    public ObservableCollection<TRow> Rows => _rows;

    public bool HasRows
    {
        get => _hasRows;
        private set => SetProperty(ref _hasRows, value);
    }

    public virtual void ReplaceRows(IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows.ReplaceAll(rows);
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHasRows();

    private void UpdateHasRows() => HasRows = Rows.Count > 0;
}
