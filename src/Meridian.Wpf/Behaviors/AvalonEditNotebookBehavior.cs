using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace Meridian.Wpf.Behaviors;

/// <summary>
/// Enables simple MVVM text binding and notebook execution shortcuts for AvalonEdit.
/// </summary>
public static class AvalonEditNotebookBehavior
{
    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(AvalonEditNotebookBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(AvalonEditNotebookBehavior),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public static readonly DependencyProperty RunCommandProperty =
        DependencyProperty.RegisterAttached(
            "RunCommand",
            typeof(ICommand),
            typeof(AvalonEditNotebookBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty RunAndAdvanceCommandProperty =
        DependencyProperty.RegisterAttached(
            "RunAndAdvanceCommand",
            typeof(ICommand),
            typeof(AvalonEditNotebookBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.RegisterAttached(
            "CommandParameter",
            typeof(object),
            typeof(AvalonEditNotebookBehavior),
            new PropertyMetadata(null));

    public static string GetText(DependencyObject obj) =>
        (string)obj.GetValue(TextProperty);

    public static void SetText(DependencyObject obj, string value) =>
        obj.SetValue(TextProperty, value);

    public static ICommand? GetRunCommand(DependencyObject obj) =>
        (ICommand?)obj.GetValue(RunCommandProperty);

    public static void SetRunCommand(DependencyObject obj, ICommand? value) =>
        obj.SetValue(RunCommandProperty, value);

    public static ICommand? GetRunAndAdvanceCommand(DependencyObject obj) =>
        (ICommand?)obj.GetValue(RunAndAdvanceCommandProperty);

    public static void SetRunAndAdvanceCommand(DependencyObject obj, ICommand? value) =>
        obj.SetValue(RunAndAdvanceCommandProperty, value);

    public static object? GetCommandParameter(DependencyObject obj) =>
        obj.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(DependencyObject obj, object? value) =>
        obj.SetValue(CommandParameterProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEditor editor)
            return;

        EnsureHooks(editor);

        if (GetIsUpdating(editor))
            return;

        var nextText = e.NewValue as string ?? string.Empty;
        if (editor.Text == nextText)
            return;

        SetIsUpdating(editor, true);
        editor.Text = nextText;
        SetIsUpdating(editor, false);
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor)
            EnsureHooks(editor);
    }

    private static void EnsureHooks(TextEditor editor)
    {
        editor.TextChanged -= OnEditorTextChanged;
        editor.TextChanged += OnEditorTextChanged;

        editor.PreviewKeyDown -= OnEditorPreviewKeyDown;
        editor.PreviewKeyDown += OnEditorPreviewKeyDown;
    }

    private static void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextEditor editor || GetIsUpdating(editor))
            return;

        SetIsUpdating(editor, true);
        editor.SetCurrentValue(TextProperty, editor.Text);
        SetIsUpdating(editor, false);
    }

    private static void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextEditor editor || e.Key != Key.Enter)
            return;

        var parameter = GetCommandParameter(editor);
        ICommand? command = null;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            command = GetRunAndAdvanceCommand(editor);
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            command = GetRunCommand(editor);

        if (command is null || !command.CanExecute(parameter))
            return;

        command.Execute(parameter);
        e.Handled = true;
    }

    private static bool GetIsUpdating(DependencyObject obj) =>
        (bool)obj.GetValue(IsUpdatingProperty);

    private static void SetIsUpdating(DependencyObject obj, bool value) =>
        obj.SetValue(IsUpdatingProperty, value);
}
