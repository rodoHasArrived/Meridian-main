using System.Windows.Input;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.Commands;

public sealed class ContextCommandDescriptor : BindableBase
{
    private string _label = string.Empty;
    private string _iconGlyph = string.Empty;
    private ICommand? _command;
    private object? _commandParameter;
    private string _disabledReason = string.Empty;
    private ContextCommandImportance _importance = ContextCommandImportance.Secondary;
    private string _automationId = string.Empty;
    private bool _isVisible = true;
    private bool _isBusy;
    private string _busyText = string.Empty;

    public string Label
    {
        get => _label;
        set
        {
            if (SetProperty(ref _label, value))
            {
                OnPropertyChanged(nameof(EffectiveAutomationId));
                OnPropertyChanged(nameof(EffectiveToolTip));
            }
        }
    }

    public string IconGlyph
    {
        get => _iconGlyph;
        set => SetProperty(ref _iconGlyph, value);
    }

    public ICommand? Command
    {
        get => _command;
        set => SetProperty(ref _command, value);
    }

    public object? CommandParameter
    {
        get => _commandParameter;
        set => SetProperty(ref _commandParameter, value);
    }

    public string DisabledReason
    {
        get => _disabledReason;
        set
        {
            if (SetProperty(ref _disabledReason, value))
            {
                OnPropertyChanged(nameof(EffectiveToolTip));
            }
        }
    }

    public ContextCommandImportance Importance
    {
        get => _importance;
        set => SetProperty(ref _importance, value);
    }

    public string AutomationId
    {
        get => _automationId;
        set
        {
            if (SetProperty(ref _automationId, value))
            {
                OnPropertyChanged(nameof(EffectiveAutomationId));
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(EffectiveToolTip));
            }
        }
    }

    public string BusyText
    {
        get => _busyText;
        set
        {
            if (SetProperty(ref _busyText, value))
            {
                OnPropertyChanged(nameof(EffectiveToolTip));
            }
        }
    }

    public string EffectiveAutomationId => string.IsNullOrWhiteSpace(AutomationId)
        ? $"ContextCommand{NormalizeAutomationId(Label)}"
        : AutomationId;

    public string EffectiveToolTip
    {
        get
        {
            if (IsBusy)
            {
                return string.IsNullOrWhiteSpace(BusyText) ? "Working…" : BusyText;
            }

            return string.IsNullOrWhiteSpace(DisabledReason) ? Label : DisabledReason;
        }
    }

    private static string NormalizeAutomationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Action";
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
