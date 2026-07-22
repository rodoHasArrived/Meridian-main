using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public static class EmptyStateMessages
{
    public static readonly EmptyStateMessage NoProviderConfigured = new(
        "No provider configured",
        "Connect a market data provider before running imports, live views, or backfills.",
        "Setup",
        WorkspaceTone.Warning);

    public static readonly EmptyStateMessage NoRecordsImported = new(
        "No records imported",
        "Import records or load fixture data before reviewing this grid.",
        "Empty",
        WorkspaceTone.Neutral);

    public static readonly EmptyStateMessage NoSelectedAccountOrPortfolio = new(
        "No account or portfolio selected",
        "Select an account or portfolio to show retained positions, balances, and workflow actions.",
        "Selection needed",
        WorkspaceTone.Warning);

    public static readonly EmptyStateMessage StaleData = new(
        "Data may be stale",
        "Refresh this view before making operational decisions.",
        "Stale",
        WorkspaceTone.Warning);

    public static readonly EmptyStateMessage NoReconciliationRun = new(
        "No reconciliation run yet",
        "Run reconciliation to compare source evidence with retained accounting records.",
        "Action needed",
        WorkspaceTone.Warning);

    public static readonly EmptyStateMessage NoReportsGenerated = new(
        "No reports generated",
        "Generate a governed report pack before reviewing report outputs or distribution evidence.",
        "Empty",
        WorkspaceTone.Neutral);

    public static readonly EmptyStateMessage FixtureDataAvailable = new(
        "Fixture data is available",
        "Load demo data to explore the workflow without connecting production providers.",
        "Demo available",
        WorkspaceTone.Info);
}

public sealed record EmptyStateMessage(string Title, string Description, string Severity = "", string SeverityTone = WorkspaceTone.Neutral);

public partial class EmptyStatePanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(EmptyStatePanel),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyStatePanel), new PropertyMetadata(null));

    public static readonly DependencyProperty SecondaryActionTextProperty =
        DependencyProperty.Register(nameof(SecondaryActionText), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryActionCommandProperty =
        DependencyProperty.Register(nameof(SecondaryActionCommand), typeof(ICommand), typeof(EmptyStatePanel), new PropertyMetadata(null));

    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SeverityToneProperty =
        DependencyProperty.Register(nameof(SeverityTone), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(WorkspaceTone.Neutral));

    public static readonly DependencyProperty PanelAutomationIdProperty =
        DependencyProperty.Register(
            nameof(PanelAutomationId),
            typeof(string),
            typeof(EmptyStatePanel),
            new PropertyMetadata("EmptyStatePanel", OnPanelAutomationIdChanged));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateIcon"));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateTitle"));

    public static readonly DependencyProperty DescriptionAutomationIdProperty =
        DependencyProperty.Register(nameof(DescriptionAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateDescription"));

    public static readonly DependencyProperty ActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(ActionButtonAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateAction"));

    public static readonly DependencyProperty SecondaryActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(SecondaryActionButtonAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateSecondaryAction"));

    public static readonly DependencyProperty SeverityAutomationIdProperty =
        DependencyProperty.Register(nameof(SeverityAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateSeverity"));

    public EmptyStatePanel()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, PanelAutomationId);
        AutomationProperties.SetName(this, Title);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public string SecondaryActionText
    {
        get => (string)GetValue(SecondaryActionTextProperty);
        set => SetValue(SecondaryActionTextProperty, value);
    }

    public ICommand? SecondaryActionCommand
    {
        get => (ICommand?)GetValue(SecondaryActionCommandProperty);
        set => SetValue(SecondaryActionCommandProperty, value);
    }

    public string Severity
    {
        get => (string)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string SeverityTone
    {
        get => (string)GetValue(SeverityToneProperty);
        set => SetValue(SeverityToneProperty, value);
    }

    public string PanelAutomationId
    {
        get => (string)GetValue(PanelAutomationIdProperty);
        set => SetValue(PanelAutomationIdProperty, value);
    }

    public string IconAutomationId
    {
        get => (string)GetValue(IconAutomationIdProperty);
        set => SetValue(IconAutomationIdProperty, value);
    }

    public string TitleAutomationId
    {
        get => (string)GetValue(TitleAutomationIdProperty);
        set => SetValue(TitleAutomationIdProperty, value);
    }

    public string DescriptionAutomationId
    {
        get => (string)GetValue(DescriptionAutomationIdProperty);
        set => SetValue(DescriptionAutomationIdProperty, value);
    }

    public string ActionButtonAutomationId
    {
        get => (string)GetValue(ActionButtonAutomationIdProperty);
        set => SetValue(ActionButtonAutomationIdProperty, value);
    }

    public string SecondaryActionButtonAutomationId
    {
        get => (string)GetValue(SecondaryActionButtonAutomationIdProperty);
        set => SetValue(SecondaryActionButtonAutomationIdProperty, value);
    }

    public string SeverityAutomationId
    {
        get => (string)GetValue(SeverityAutomationIdProperty);
        set => SetValue(SeverityAutomationIdProperty, value);
    }

    private static void OnPanelAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyStatePanel control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyStatePanel control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }
}
