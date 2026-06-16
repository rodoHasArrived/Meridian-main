using System.Collections;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Reflection;
using System.Text;
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

    public static readonly DependencyProperty FilterTargetProperty =
        DependencyProperty.Register(
            nameof(FilterTarget),
            typeof(UIElement),
            typeof(DenseDataGridControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OpenSelectedDetailsCommandProperty =
        DependencyProperty.Register(
            nameof(OpenSelectedDetailsCommand),
            typeof(ICommand),
            typeof(DenseDataGridControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CloseDetailsCommandProperty =
        DependencyProperty.Register(
            nameof(CloseDetailsCommand),
            typeof(ICommand),
            typeof(DenseDataGridControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ClearFiltersCommandProperty =
        DependencyProperty.Register(
            nameof(ClearFiltersCommand),
            typeof(ICommand),
            typeof(DenseDataGridControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty JumpToRelatedRecordsCommandProperty =
        DependencyProperty.Register(
            nameof(JumpToRelatedRecordsCommand),
            typeof(ICommand),
            typeof(DenseDataGridControl),
            new PropertyMetadata(null));

    private INotifyCollectionChanged? _observedRows;

    public DenseDataGridControl()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.FocusFilter, ExecuteFocusFilter, CanExecuteFocusFilter));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.OpenSelectedDetails, ExecuteOpenSelectedDetails, CanExecuteOpenSelectedDetails));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.CloseDetails, ExecuteCloseDetails, CanExecuteCloseDetails));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, ExecuteCopySelection, CanExecuteCopySelection));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.ClearFilters, ExecuteClearFilters, CanExecuteClearFilters));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.JumpToRelatedRecords, ExecuteJumpToRelatedRecords, CanExecuteJumpToRelatedRecords));
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

    public UIElement? FilterTarget
    {
        get => (UIElement?)GetValue(FilterTargetProperty);
        set => SetValue(FilterTargetProperty, value);
    }

    public ICommand? OpenSelectedDetailsCommand
    {
        get => (ICommand?)GetValue(OpenSelectedDetailsCommandProperty);
        set => SetValue(OpenSelectedDetailsCommandProperty, value);
    }

    public ICommand? CloseDetailsCommand
    {
        get => (ICommand?)GetValue(CloseDetailsCommandProperty);
        set => SetValue(CloseDetailsCommandProperty, value);
    }

    public ICommand? ClearFiltersCommand
    {
        get => (ICommand?)GetValue(ClearFiltersCommandProperty);
        set => SetValue(ClearFiltersCommandProperty, value);
    }

    public ICommand? JumpToRelatedRecordsCommand
    {
        get => (ICommand?)GetValue(JumpToRelatedRecordsCommandProperty);
        set => SetValue(JumpToRelatedRecordsCommandProperty, value);
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

    private void CanExecuteFocusFilter(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = FilterTarget is not null;
        e.Handled = true;
    }

    private void ExecuteFocusFilter(object sender, ExecutedRoutedEventArgs e)
    {
        FilterTarget?.Focus();
        e.Handled = true;
    }

    private void CanExecuteOpenSelectedDetails(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanExecute(OpenSelectedDetailsCommand, SelectedItem);
        e.Handled = true;
    }

    private void ExecuteOpenSelectedDetails(object sender, ExecutedRoutedEventArgs e)
    {
        Execute(OpenSelectedDetailsCommand, SelectedItem);
        e.Handled = true;
    }

    private void CanExecuteCloseDetails(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanExecute(CloseDetailsCommand, SelectedItem);
        e.Handled = true;
    }

    private void ExecuteCloseDetails(object sender, ExecutedRoutedEventArgs e)
    {
        Execute(CloseDetailsCommand, SelectedItem);
        e.Handled = true;
    }

    private void CanExecuteClearFilters(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanExecute(ClearFiltersCommand, null);
        e.Handled = true;
    }

    private void ExecuteClearFilters(object sender, ExecutedRoutedEventArgs e)
    {
        Execute(ClearFiltersCommand, null);
        e.Handled = true;
    }

    private void CanExecuteJumpToRelatedRecords(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanExecute(JumpToRelatedRecordsCommand, SelectedItem);
        e.Handled = true;
    }

    private void ExecuteJumpToRelatedRecords(object sender, ExecutedRoutedEventArgs e)
    {
        Execute(JumpToRelatedRecordsCommand, SelectedItem);
        e.Handled = true;
    }

    private void CanExecuteCopySelection(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = RowsList?.SelectedItems.Count > 0;
        e.Handled = true;
    }

    private void ExecuteCopySelection(object sender, ExecutedRoutedEventArgs e)
    {
        var text = FormatSelectedRowsForClipboard();
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
        }

        e.Handled = true;
    }

    internal string FormatSelectedRowsForClipboard()
    {
        if (RowsList is null || RowsList.SelectedItems.Count == 0)
        {
            return string.Empty;
        }

        var columns = Table?.Columns ?? Array.Empty<WorkstationTableColumnModel>();
        var builder = new StringBuilder();
        if (columns.Count > 0)
        {
            builder.AppendLine(string.Join("\t", columns.Select(column => column.Header)));
        }

        foreach (var selectedItem in RowsList.SelectedItems.Cast<object>())
        {
            builder.AppendLine(columns.Count == 0
                ? Convert.ToString(selectedItem, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty
                : string.Join("\t", columns.Select(column => FormatCellValue(selectedItem, column.BindingPath))));
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static string FormatCellValue(object row, string bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return string.Empty;
        }

        var current = row;
        foreach (var segment in bindingPath.Split('.'))
        {
            var property = current.GetType().GetProperty(segment, BindingFlags.Instance | BindingFlags.Public);
            if (property is null)
            {
                return string.Empty;
            }

            var value = property.GetValue(current);
            if (value is null)
            {
                return string.Empty;
            }

            current = value;
        }

        return Convert.ToString(current, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private static bool CanExecute(ICommand? command, object? parameter)
        => command is not null && command.CanExecute(parameter);

    private static void Execute(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

}
