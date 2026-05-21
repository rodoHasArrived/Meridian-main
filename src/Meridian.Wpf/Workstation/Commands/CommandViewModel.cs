using System.Windows.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.Commands;

public class CommandViewModel : BindableBase
{
    private string _id = string.Empty;
    private string _label = string.Empty;
    private string _iconGlyph = string.Empty;
    private string _tone = WorkspaceTone.Secondary;
    private string _tooltip = string.Empty;
    private bool _isEnabled = true;
    private ICommand? _command;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string IconGlyph
    {
        get => _iconGlyph;
        set => SetProperty(ref _iconGlyph, value);
    }

    public string Tone
    {
        get => _tone;
        set => SetProperty(ref _tone, value);
    }

    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public ICommand? Command
    {
        get => _command;
        set => SetProperty(ref _command, value);
    }
}
