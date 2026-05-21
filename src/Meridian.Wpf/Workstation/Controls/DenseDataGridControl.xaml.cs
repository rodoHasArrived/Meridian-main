using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class DenseDataGridControl : UserControl
{
    public static readonly DependencyProperty TableProperty =
        DependencyProperty.Register(
            nameof(Table),
            typeof(WorkstationTableModel),
            typeof(DenseDataGridControl),
            new PropertyMetadata(null, OnTableChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(DenseDataGridControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private INotifyCollectionChanged? _observedRows;

    public DenseDataGridControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateEmptyState();
    }

    public WorkstationTableModel? Table
    {
        get => (WorkstationTableModel?)GetValue(TableProperty);
        set => SetValue(TableProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private static void OnTableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DenseDataGridControl control)
        {
            control.RebuildColumns();
            control.AttachRowsCollection();
            control.UpdateEmptyState();
        }
    }

    private void RebuildColumns()
    {
        var gridView = new GridView();
        foreach (var column in Table?.Columns ?? Array.Empty<WorkstationTableColumnModel>())
        {
            gridView.Columns.Add(new GridViewColumn
            {
                Header = column.Header,
                Width = column.Width,
                DisplayMemberBinding = new Binding(column.BindingPath)
            });
        }

        RowsList.View = gridView;
    }

    private void AttachRowsCollection()
    {
        if (_observedRows is not null)
        {
            _observedRows.CollectionChanged -= OnRowsChanged;
        }

        _observedRows = Table?.Rows as INotifyCollectionChanged;
        if (_observedRows is not null)
        {
            _observedRows.CollectionChanged += OnRowsChanged;
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateEmptyState();

    private void UpdateEmptyState()
    {
        if (RowsList is null || EmptyPanel is null)
        {
            return;
        }

        var hasRows = Table?.Rows is IEnumerable rows && rows.Cast<object>().Any();
        EmptyPanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
    }
}
