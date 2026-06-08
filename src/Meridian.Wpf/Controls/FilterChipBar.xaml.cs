using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Meridian.Wpf.Controls;

public partial class FilterChipBar : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(FilterChipBar), new PropertyMetadata(Array.Empty<object>()));

    public static readonly DependencyProperty ResultSummaryProperty =
        DependencyProperty.Register(
            nameof(ResultSummary),
            typeof(string),
            typeof(FilterChipBar),
            new PropertyMetadata(string.Empty, OnResultSummaryChanged));

    public static readonly DependencyProperty ClearAllTextProperty =
        DependencyProperty.Register(nameof(ClearAllText), typeof(string), typeof(FilterChipBar), new PropertyMetadata("Clear filters"));

    public static readonly DependencyProperty ClearAllCommandProperty =
        DependencyProperty.Register(nameof(ClearAllCommand), typeof(ICommand), typeof(FilterChipBar), new PropertyMetadata(null));

    public static readonly DependencyProperty RemoveFilterCommandProperty =
        DependencyProperty.Register(nameof(RemoveFilterCommand), typeof(ICommand), typeof(FilterChipBar), new PropertyMetadata(null));

    public static readonly DependencyProperty BarAutomationIdProperty =
        DependencyProperty.Register(
            nameof(BarAutomationId),
            typeof(string),
            typeof(FilterChipBar),
            new PropertyMetadata("FilterChipBar", OnBarAutomationIdChanged));

    public static readonly DependencyProperty ResultSummaryAutomationIdProperty =
        DependencyProperty.Register(nameof(ResultSummaryAutomationId), typeof(string), typeof(FilterChipBar), new PropertyMetadata("FilterChipResultSummary"));

    public static readonly DependencyProperty ItemsAutomationIdProperty =
        DependencyProperty.Register(nameof(ItemsAutomationId), typeof(string), typeof(FilterChipBar), new PropertyMetadata("FilterChipItems"));

    public static readonly DependencyProperty ClearAllButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(ClearAllButtonAutomationId), typeof(string), typeof(FilterChipBar), new PropertyMetadata("FilterChipClearAll"));

    public FilterChipBar()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, BarAutomationId);
        AutomationProperties.SetName(this, ResultSummary);
    }

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string ResultSummary
    {
        get => (string)GetValue(ResultSummaryProperty);
        set => SetValue(ResultSummaryProperty, value);
    }

    public string ClearAllText
    {
        get => (string)GetValue(ClearAllTextProperty);
        set => SetValue(ClearAllTextProperty, value);
    }

    public ICommand? ClearAllCommand
    {
        get => (ICommand?)GetValue(ClearAllCommandProperty);
        set => SetValue(ClearAllCommandProperty, value);
    }

    public ICommand? RemoveFilterCommand
    {
        get => (ICommand?)GetValue(RemoveFilterCommandProperty);
        set => SetValue(RemoveFilterCommandProperty, value);
    }

    public string BarAutomationId
    {
        get => (string)GetValue(BarAutomationIdProperty);
        set => SetValue(BarAutomationIdProperty, value);
    }

    public string ResultSummaryAutomationId
    {
        get => (string)GetValue(ResultSummaryAutomationIdProperty);
        set => SetValue(ResultSummaryAutomationIdProperty, value);
    }

    public string ItemsAutomationId
    {
        get => (string)GetValue(ItemsAutomationIdProperty);
        set => SetValue(ItemsAutomationIdProperty, value);
    }

    public string ClearAllButtonAutomationId
    {
        get => (string)GetValue(ClearAllButtonAutomationIdProperty);
        set => SetValue(ClearAllButtonAutomationIdProperty, value);
    }

    private static void OnBarAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilterChipBar control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnResultSummaryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilterChipBar control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }
}
