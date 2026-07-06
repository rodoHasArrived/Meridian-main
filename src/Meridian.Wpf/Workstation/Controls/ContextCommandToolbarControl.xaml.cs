using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Commands;

namespace Meridian.Wpf.Workstation.Controls;

public partial class ContextCommandToolbarControl : UserControl
{
    public static readonly DependencyProperty CommandsProperty =
        DependencyProperty.Register(
            nameof(Commands),
            typeof(ReadOnlyObservableCollection<ContextCommandDescriptor>),
            typeof(ContextCommandToolbarControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolbarAutomationIdProperty =
        DependencyProperty.Register(
            nameof(ToolbarAutomationId),
            typeof(string),
            typeof(ContextCommandToolbarControl),
            new PropertyMetadata("WorkspaceContextCommandToolbar"));

    public ContextCommandToolbarControl()
    {
        InitializeComponent();
    }

    public ReadOnlyObservableCollection<ContextCommandDescriptor>? Commands
    {
        get => (ReadOnlyObservableCollection<ContextCommandDescriptor>?)GetValue(CommandsProperty);
        set => SetValue(CommandsProperty, value);
    }

    public string ToolbarAutomationId
    {
        get => (string)GetValue(ToolbarAutomationIdProperty);
        set => SetValue(ToolbarAutomationIdProperty, value);
    }
}
