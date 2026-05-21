using System.Collections.ObjectModel;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

public class FilterableTableViewModel<TRow> : TableViewModel<TRow>
{
    private readonly ObservableCollection<TRow> _allRows = [];
    private string _filterQuery = string.Empty;

    public string FilterQuery
    {
        get => _filterQuery;
        set
        {
            if (SetProperty(ref _filterQuery, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public Func<TRow, string, bool> MatchFilter { get; set; } = static (_, _) => true;

    public override void ReplaceRows(IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _allRows.Clear();
        foreach (var row in rows)
        {
            _allRows.Add(row);
        }

        ApplyFilter();
    }

    public void ClearFilter() => FilterQuery = string.Empty;

    private void ApplyFilter()
    {
        var query = FilterQuery.Trim();
        var visible = string.IsNullOrWhiteSpace(query)
            ? _allRows
            : _allRows.Where(row => MatchFilter(row, query));

        base.ReplaceRows(visible);
    }
}
