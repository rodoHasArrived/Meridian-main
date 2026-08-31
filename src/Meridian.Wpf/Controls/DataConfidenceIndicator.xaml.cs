using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class DataConfidenceIndicator : UserControl
{
    // A binding can push null during async loading or a data-context transition; coerce it
    // to the unknown model so the change callback never dereferences a missing model.
    private static readonly DataConfidenceIndicatorModel UnknownModel = DataConfidenceIndicatorModel.Unknown();

    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(DataConfidenceIndicatorModel),
            typeof(DataConfidenceIndicator),
            new PropertyMetadata(
                UnknownModel,
                OnModelChanged,
                static (_, value) => value ?? UnknownModel));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(DataConfidenceIndicator), new PropertyMetadata(WorkspaceTone.Neutral));

    public static readonly DependencyProperty ExplanationCommandProperty =
        DependencyProperty.Register(nameof(ExplanationCommand), typeof(ICommand), typeof(DataConfidenceIndicator), new PropertyMetadata(null));

    public static readonly DependencyProperty ExplanationCommandParameterProperty =
        DependencyProperty.Register(
            nameof(ExplanationCommandParameter),
            typeof(object),
            typeof(DataConfidenceIndicator),
            new PropertyMetadata(null, OnExplanationCommandParameterChanged));

    // The command receives the model's advertised ExplanationRoute unless the caller supplies
    // an explicit parameter — otherwise the route would be dead data the command never sees.
    private static readonly DependencyPropertyKey EffectiveExplanationCommandParameterPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(EffectiveExplanationCommandParameter),
            typeof(object),
            typeof(DataConfidenceIndicator),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EffectiveExplanationCommandParameterProperty =
        EffectiveExplanationCommandParameterPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsClickThroughEnabledProperty =
        DependencyProperty.Register(nameof(IsClickThroughEnabled), typeof(bool), typeof(DataConfidenceIndicator), new PropertyMetadata(true));

    public static readonly DependencyProperty IndicatorAutomationIdProperty =
        DependencyProperty.Register(
            nameof(IndicatorAutomationId),
            typeof(string),
            typeof(DataConfidenceIndicator),
            new PropertyMetadata("DataConfidenceIndicator", OnIndicatorAutomationIdChanged));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(DataConfidenceIndicator), new PropertyMetadata("DataConfidenceIndicatorIcon"));

    public static readonly DependencyProperty ConfidenceAutomationIdProperty =
        DependencyProperty.Register(nameof(ConfidenceAutomationId), typeof(string), typeof(DataConfidenceIndicator), new PropertyMetadata("DataConfidenceIndicatorConfidence"));

    public static readonly DependencyProperty ReconciliationAutomationIdProperty =
        DependencyProperty.Register(nameof(ReconciliationAutomationId), typeof(string), typeof(DataConfidenceIndicator), new PropertyMetadata("DataConfidenceIndicatorReconciliation"));

    public static readonly DependencyProperty SourceAutomationIdProperty =
        DependencyProperty.Register(nameof(SourceAutomationId), typeof(string), typeof(DataConfidenceIndicator), new PropertyMetadata("DataConfidenceIndicatorSource"));

    public static readonly DependencyProperty FreshnessAutomationIdProperty =
        DependencyProperty.Register(nameof(FreshnessAutomationId), typeof(string), typeof(DataConfidenceIndicator), new PropertyMetadata("DataConfidenceIndicatorFreshness"));

    public static readonly DependencyProperty NotesAutomationIdProperty =
        DependencyProperty.Register(nameof(NotesAutomationId), typeof(string), typeof(DataConfidenceIndicator), new PropertyMetadata("DataConfidenceIndicatorNotes"));

    public DataConfidenceIndicator()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, IndicatorAutomationId);
        UpdateAutomationName();
        UpdateEffectiveExplanationCommandParameter();
    }

    public DataConfidenceIndicatorModel Model
    {
        get => (DataConfidenceIndicatorModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        private set => SetValue(ToneProperty, value);
    }

    public ICommand? ExplanationCommand
    {
        get => (ICommand?)GetValue(ExplanationCommandProperty);
        set => SetValue(ExplanationCommandProperty, value);
    }

    public object? ExplanationCommandParameter
    {
        get => GetValue(ExplanationCommandParameterProperty);
        set => SetValue(ExplanationCommandParameterProperty, value);
    }

    public object? EffectiveExplanationCommandParameter
    {
        get => GetValue(EffectiveExplanationCommandParameterProperty);
        private set => SetValue(EffectiveExplanationCommandParameterPropertyKey, value);
    }

    public bool IsClickThroughEnabled
    {
        get => (bool)GetValue(IsClickThroughEnabledProperty);
        set => SetValue(IsClickThroughEnabledProperty, value);
    }

    public string IndicatorAutomationId
    {
        get => (string)GetValue(IndicatorAutomationIdProperty);
        set => SetValue(IndicatorAutomationIdProperty, value);
    }

    public string IconAutomationId
    {
        get => (string)GetValue(IconAutomationIdProperty);
        set => SetValue(IconAutomationIdProperty, value);
    }

    public string ConfidenceAutomationId
    {
        get => (string)GetValue(ConfidenceAutomationIdProperty);
        set => SetValue(ConfidenceAutomationIdProperty, value);
    }

    public string ReconciliationAutomationId
    {
        get => (string)GetValue(ReconciliationAutomationIdProperty);
        set => SetValue(ReconciliationAutomationIdProperty, value);
    }

    public string SourceAutomationId
    {
        get => (string)GetValue(SourceAutomationIdProperty);
        set => SetValue(SourceAutomationIdProperty, value);
    }

    public string FreshnessAutomationId
    {
        get => (string)GetValue(FreshnessAutomationIdProperty);
        set => SetValue(FreshnessAutomationIdProperty, value);
    }

    public string NotesAutomationId
    {
        get => (string)GetValue(NotesAutomationIdProperty);
        set => SetValue(NotesAutomationIdProperty, value);
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataConfidenceIndicator control)
        {
            control.Tone = control.Model.Tone;
            control.UpdateAutomationName();
            control.UpdateEffectiveExplanationCommandParameter();
        }
    }

    private static void OnExplanationCommandParameterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataConfidenceIndicator control)
        {
            control.UpdateEffectiveExplanationCommandParameter();
        }
    }

    private void UpdateEffectiveExplanationCommandParameter()
    {
        EffectiveExplanationCommandParameter = ExplanationCommandParameter ?? Model.ExplanationRoute;
    }

    private static void OnIndicatorAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataConfidenceIndicator control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private void UpdateAutomationName()
    {
        AutomationProperties.SetName(this, Model.AccessibleExplanation);
    }
}
