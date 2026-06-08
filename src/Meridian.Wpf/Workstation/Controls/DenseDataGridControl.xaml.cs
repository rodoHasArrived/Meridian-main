using System.Collections;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
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

    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.Register(
            nameof(SelectedItems),
            typeof(ObservableCollection<object>),
            typeof(DenseDataGridControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(
            nameof(SelectionMode),
            typeof(System.Windows.Controls.SelectionMode),
            typeof(DenseDataGridControl),
            new PropertyMetadata(System.Windows.Controls.SelectionMode.Single));

    public static readonly DependencyProperty EmptyContentProperty =
        DependencyProperty.Register(
            nameof(EmptyContent),
            typeof(object),
            typeof(DenseDataGridControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty GridAutomationIdProperty =
        DependencyProperty.Register(
            nameof(GridAutomationId),
            typeof(string),
            typeof(DenseDataGridControl),
            new PropertyMetadata("DenseDataGridRows"));

    public static readonly DependencyProperty EmptyAutomationIdProperty =
        DependencyProperty.Register(
            nameof(EmptyAutomationId),
            typeof(string),
            typeof(DenseDataGridControl),
            new PropertyMetadata("DenseDataGridEmptyState"));

    private INotifyCollectionChanged? _observedRows;

    public DenseDataGridControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateEmptyState();
        Unloaded += (_, _) => DetachRowsCollection();
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

    public ObservableCollection<object>? SelectedItems
    {
        get => (ObservableCollection<object>?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public System.Windows.Controls.SelectionMode SelectionMode
    {
        get => (System.Windows.Controls.SelectionMode)GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public object? EmptyContent
    {
        get => GetValue(EmptyContentProperty);
        set => SetValue(EmptyContentProperty, value);
    }

    public string GridAutomationId
    {
        get => (string)GetValue(GridAutomationIdProperty);
        set => SetValue(GridAutomationIdProperty, value);
    }

    public string EmptyAutomationId
    {
        get => (string)GetValue(EmptyAutomationIdProperty);
        set => SetValue(EmptyAutomationIdProperty, value);
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
        var gridView = new GridView
        {
            ColumnHeaderContainerStyle = TryFindResource("DenseGridColumnHeaderStyle") as Style
        };
        foreach (var column in Table?.Columns ?? Array.Empty<WorkstationTableColumnModel>())
        {
            var binding = new Binding(column.BindingPath);
            if (!string.IsNullOrWhiteSpace(column.StringFormat))
            {
                binding.StringFormat = column.StringFormat;
            }

            gridView.Columns.Add(new GridViewColumn
            {
                Header = column.Header,
                Width = column.Width,
                DisplayMemberBinding = binding
            });
        }

        RowsList.View = gridView;
    }

    private void AttachRowsCollection()
    {
        DetachRowsCollection();

        _observedRows = Table?.Rows as INotifyCollectionChanged;
        if (_observedRows is not null)
        {
            _observedRows.CollectionChanged += OnRowsChanged;
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
        MirrorSelectedRows();
    }

    private void DetachRowsCollection()
    {
        if (_observedRows is null)
        {
            return;
        }

        _observedRows.CollectionChanged -= OnRowsChanged;
        _observedRows = null;
    }

    private void UpdateEmptyState()
    {
        if (RowsList is null || EmptyPanel is null)
        {
            return;
        }

        var hasRows = Table?.Rows is IEnumerable rows && rows.Cast<object>().Any();
        EmptyPanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => MirrorSelectedRows();

    private void MirrorSelectedRows()
    {
        var selectedItems = SelectedItems;
        if (RowsList is null || selectedItems is null)
        {
            return;
        }

        selectedItems.Clear();
        foreach (var selectedItem in RowsList.SelectedItems.Cast<object>())
        {
            selectedItems.Add(selectedItem);
        }
    }
}
