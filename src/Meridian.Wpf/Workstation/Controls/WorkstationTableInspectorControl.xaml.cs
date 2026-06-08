using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class WorkstationTableInspectorControl : UserControl
{
    public static readonly DependencyProperty TableProperty =
        DependencyProperty.Register(
            nameof(Table),
            typeof(WorkstationTableModel),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(WorkstationTableInspectorControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.Register(
            nameof(SelectedItems),
            typeof(ObservableCollection<object>),
            typeof(WorkstationTableInspectorControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(
            nameof(SelectionMode),
            typeof(System.Windows.Controls.SelectionMode),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(System.Windows.Controls.SelectionMode.Single));

    public static readonly DependencyProperty InspectorProperty =
        DependencyProperty.Register(
            nameof(Inspector),
            typeof(InspectorPanelModel),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(
            nameof(HeaderText),
            typeof(string),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DetailTextProperty =
        DependencyProperty.Register(
            nameof(DetailText),
            typeof(string),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ScopeTextProperty =
        DependencyProperty.Register(
            nameof(ScopeText),
            typeof(string),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToolbarContentProperty =
        DependencyProperty.Register(
            nameof(ToolbarContent),
            typeof(object),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty InspectorContentProperty =
        DependencyProperty.Register(
            nameof(InspectorContent),
            typeof(object),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FooterContentProperty =
        DependencyProperty.Register(
            nameof(FooterContent),
            typeof(object),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty GridAutomationIdProperty =
        DependencyProperty.Register(
            nameof(GridAutomationId),
            typeof(string),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata("WorkstationTableInspectorGrid"));

    public static readonly DependencyProperty EmptyAutomationIdProperty =
        DependencyProperty.Register(
            nameof(EmptyAutomationId),
            typeof(string),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata("WorkstationTableInspectorEmpty"));

    public static readonly DependencyProperty InspectorAutomationIdProperty =
        DependencyProperty.Register(
            nameof(InspectorAutomationId),
            typeof(string),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata("WorkstationTableInspectorRail"));

    public static readonly DependencyProperty ControlAutomationIdProperty =
        DependencyProperty.Register(
            nameof(ControlAutomationId),
            typeof(string),
            typeof(WorkstationTableInspectorControl),
            new PropertyMetadata("WorkstationTableInspector"));

    public WorkstationTableInspectorControl()
    {
        InitializeComponent();
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

    public InspectorPanelModel? Inspector
    {
        get => (InspectorPanelModel?)GetValue(InspectorProperty);
        set => SetValue(InspectorProperty, value);
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public string DetailText
    {
        get => (string)GetValue(DetailTextProperty);
        set => SetValue(DetailTextProperty, value);
    }

    public string ScopeText
    {
        get => (string)GetValue(ScopeTextProperty);
        set => SetValue(ScopeTextProperty, value);
    }

    public object? ToolbarContent
    {
        get => GetValue(ToolbarContentProperty);
        set => SetValue(ToolbarContentProperty, value);
    }

    public object? InspectorContent
    {
        get => GetValue(InspectorContentProperty);
        set => SetValue(InspectorContentProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
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

    public string InspectorAutomationId
    {
        get => (string)GetValue(InspectorAutomationIdProperty);
        set => SetValue(InspectorAutomationIdProperty, value);
    }

    public string ControlAutomationId
    {
        get => (string)GetValue(ControlAutomationIdProperty);
        set => SetValue(ControlAutomationIdProperty, value);
    }
}
