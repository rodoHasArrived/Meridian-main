using System.Collections;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Reflection;
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
            new PropertyMetadata(null, OnFilterTargetChanged));

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
    private readonly List<KeyBinding> _filterTargetBindings = [];
    private UIElement? _filterTargetWithBindings;

    public DenseDataGridControl()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.FocusFilter, ExecuteFocusFilter, CanExecuteFocusFilter));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.OpenSelectedDetails, ExecuteOpenSelectedDetails, CanExecuteOpenSelectedDetails));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.CloseDetails, ExecuteCloseDetails, CanExecuteCloseDetails));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, ExecuteCopySelection, CanExecuteCopySelection));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.ClearFilters, ExecuteClearFilters, CanExecuteClearFilters));
        CommandBindings.Add(new CommandBinding(DenseGridKeyboardCommands.JumpToRelatedRecords, ExecuteJumpToRelatedRecords, CanExecuteJumpToRelatedRecords));
        Loaded += (_, _) =>
        {
            UpdateEmptyState();
            AttachFilterTargetBindings(FilterTarget);
        };
        Unloaded += (_, _) =>
        {
            DetachRowsCollection();
            DetachFilterTargetBindings();
        };
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

    private static void OnFilterTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DenseDataGridControl control)
        {
            control.AttachFilterTargetBindings(e.NewValue as UIElement);
        }
    }

    /// <summary>
    /// The filter target is composed outside this control, so once <see cref="ExecuteFocusFilter"/>
    /// moves keyboard focus there the grid's own input bindings are no longer on the routed-input
    /// path. Mirroring the chrome shortcuts onto the target — with this control as the command
    /// target so the routed commands resolve against its command bindings — keeps Ctrl+F,
    /// Ctrl+Shift+F, Escape and Ctrl+J live while the operator types in the filter. Enter and
    /// Ctrl+C are deliberately not mirrored: they belong to the target's own commit and text-copy
    /// semantics.
    /// </summary>
    private void AttachFilterTargetBindings(UIElement? target)
    {
        DetachFilterTargetBindings();
        if (target is null)
        {
            return;
        }

        _filterTargetWithBindings = target;
        AddFilterTargetBinding(target, DenseGridKeyboardCommands.FocusFilter, Key.F, ModifierKeys.Control);
        AddFilterTargetBinding(target, DenseGridKeyboardCommands.CloseDetails, Key.Escape, ModifierKeys.None);
        AddFilterTargetBinding(target, DenseGridKeyboardCommands.ClearFilters, Key.F, ModifierKeys.Control | ModifierKeys.Shift);
        AddFilterTargetBinding(target, DenseGridKeyboardCommands.JumpToRelatedRecords, Key.J, ModifierKeys.Control);
    }

    private void AddFilterTargetBinding(UIElement target, ICommand command, Key key, ModifierKeys modifiers)
    {
        // Assigning Key/Modifiers (as XAML does) instead of using the KeyGesture constructor,
        // which rejects modifier-less non-function keys such as Escape.
        var binding = new KeyBinding
        {
            Command = command,
            Key = key,
            Modifiers = modifiers,
            CommandTarget = this
        };
        _filterTargetBindings.Add(binding);
        target.InputBindings.Add(binding);
    }

    private void DetachFilterTargetBindings()
    {
        if (_filterTargetWithBindings is not null)
        {
            foreach (var binding in _filterTargetBindings)
            {
                _filterTargetWithBindings.InputBindings.Remove(binding);
            }
        }

        _filterTargetBindings.Clear();
        _filterTargetWithBindings = null;
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
        var lines = new List<string>();
        if (columns.Count > 0)
        {
            lines.Add(string.Join("\t", columns.Select(column => EscapeTsvCell(column.Header))));
        }

        foreach (var selectedItem in RowsList.SelectedItems.Cast<object>())
        {
            lines.Add(columns.Count == 0
                ? EscapeTsvCell(Convert.ToString(selectedItem, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty)
                : string.Join("\t", columns.Select(column => EscapeTsvCell(FormatCellForClipboard(selectedItem, column)))));
        }

        return string.Join("\n", lines);
    }

    private static readonly char[] TsvEscapeTriggers = ['"', '\t', '\r', '\n'];

    /// <summary>
    /// Free-text cells (journal descriptions, system-event messages) can legally contain tabs,
    /// newlines and quotes; quoting those RFC-4180 style — mirroring the browser workstation's
    /// csv.ts — keeps the clipboard table rectangular when pasted into a spreadsheet.
    /// </summary>
    private static string EscapeTsvCell(string value)
    {
        if (value.IndexOfAny(TsvEscapeTriggers) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatCellForClipboard(object row, WorkstationTableColumnModel column)
    {
        var value = ResolveCellValue(row, column.BindingPath);
        if (value is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(column.StringFormat))
        {
            // Match WPF Binding.StringFormat semantics: a format without a placeholder is applied
            // as "{0:format}", so copied cells carry the same currency/percentage/date text the
            // grid displays instead of the raw property value.
            var format = column.StringFormat.Contains('{')
                ? column.StringFormat
                : "{0:" + column.StringFormat + "}";
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, value);
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private static object? ResolveCellValue(object row, string bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return null;
        }

        var current = row;
        foreach (var segment in bindingPath.Split('.'))
        {
            current = ResolvePathSegment(current, segment);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// Resolves one binding-path segment reflectively, including WPF indexer segments such as
    /// <c>Cells[columnId]</c> that dynamic-column tables (e.g. the Financial Record Explorer)
    /// emit as their column binding paths.
    /// </summary>
    private static object? ResolvePathSegment(object current, string segment)
    {
        var name = segment;
        string? indexKey = null;
        var bracket = segment.IndexOf('[');
        if (bracket >= 0 && segment.EndsWith("]", StringComparison.Ordinal))
        {
            name = segment[..bracket];
            indexKey = segment[(bracket + 1)..^1].Trim().Trim('"', '\'');
        }

        var value = current;
        if (name.Length > 0)
        {
            var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property is null)
            {
                return null;
            }

            value = property.GetValue(value);
            if (value is null)
            {
                return null;
            }
        }

        return indexKey is null ? value : ResolveIndexedValue(value, indexKey);
    }

    private static object? ResolveIndexedValue(object value, string key)
    {
        if (value is IDictionary dictionary)
        {
            return dictionary.Contains(key) ? dictionary[key] : null;
        }

        if (value is IList list)
        {
            return int.TryParse(key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < list.Count
                ? list[index]
                : null;
        }

        return null;
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
