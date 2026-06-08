using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Meridian.Wpf.Views;

public partial class SecretInputControl : UserControl
{
    public static readonly DependencyProperty SecretProperty =
        DependencyProperty.Register(
            nameof(Secret),
            typeof(string),
            typeof(SecretInputControl),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSecretChanged));

    public static readonly DependencyProperty IsRevealedProperty =
        DependencyProperty.Register(
            nameof(IsRevealed),
            typeof(bool),
            typeof(SecretInputControl),
            new PropertyMetadata(false, OnIsRevealedChanged));

    public static readonly DependencyProperty InputAutomationIdProperty =
        DependencyProperty.Register(
            nameof(InputAutomationId),
            typeof(string),
            typeof(SecretInputControl),
            new PropertyMetadata("SecretInput"));

    public static readonly DependencyProperty RevealAutomationIdProperty =
        DependencyProperty.Register(
            nameof(RevealAutomationId),
            typeof(string),
            typeof(SecretInputControl),
            new PropertyMetadata("SecretInputRevealButton"));

    public static readonly DependencyProperty InputAutomationNameProperty =
        DependencyProperty.Register(
            nameof(InputAutomationName),
            typeof(string),
            typeof(SecretInputControl),
            new PropertyMetadata("Secret input", OnInputAutomationNameChanged));

    public static readonly DependencyProperty RevealAutomationNameProperty =
        DependencyProperty.Register(
            nameof(RevealAutomationName),
            typeof(string),
            typeof(SecretInputControl),
            new PropertyMetadata("Show or hide secret"));

    public static readonly DependencyProperty RevealToolTipProperty =
        DependencyProperty.Register(
            nameof(RevealToolTip),
            typeof(string),
            typeof(SecretInputControl),
            new PropertyMetadata("Show or hide secret"));

    public static readonly DependencyProperty InputMinHeightProperty =
        DependencyProperty.Register(
            nameof(InputMinHeight),
            typeof(double),
            typeof(SecretInputControl),
            new PropertyMetadata(34d));

    private const string ShowSecretGlyph = "\uE7B3";
    private const string HideSecretGlyph = "\uED1A";
    private bool _isSynchronizing;

    public SecretInputControl()
    {
        InitializeComponent();
        ApplyAutomationName(InputAutomationName);
        SyncVisualState();
    }

    public event EventHandler? SecretChanged;

    public string Secret
    {
        get => (string)GetValue(SecretProperty);
        set => SetValue(SecretProperty, value ?? string.Empty);
    }

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    public string InputAutomationId
    {
        get => (string)GetValue(InputAutomationIdProperty);
        set => SetValue(InputAutomationIdProperty, value);
    }

    public string RevealAutomationId
    {
        get => (string)GetValue(RevealAutomationIdProperty);
        set => SetValue(RevealAutomationIdProperty, value);
    }

    public string InputAutomationName
    {
        get => (string)GetValue(InputAutomationNameProperty);
        set => SetValue(InputAutomationNameProperty, value);
    }

    public string RevealAutomationName
    {
        get => (string)GetValue(RevealAutomationNameProperty);
        set => SetValue(RevealAutomationNameProperty, value);
    }

    public string RevealToolTip
    {
        get => (string)GetValue(RevealToolTipProperty);
        set => SetValue(RevealToolTipProperty, value);
    }

    public double InputMinHeight
    {
        get => (double)GetValue(InputMinHeightProperty);
        set => SetValue(InputMinHeightProperty, value);
    }

    public void ClearSecret()
    {
        IsRevealed = false;
        Secret = string.Empty;
    }

    private static void OnSecretChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SecretInputControl control)
        {
            return;
        }

        control.SyncSecretToInputs(e.NewValue as string ?? string.Empty);
        control.SecretChanged?.Invoke(control, EventArgs.Empty);
    }

    private static void OnIsRevealedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SecretInputControl control)
        {
            control.SyncVisualState();
        }
    }

    private static void OnInputAutomationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SecretInputControl control)
        {
            control.ApplyAutomationName(e.NewValue as string ?? "Secret input");
        }
    }

    private void HiddenPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizing)
        {
            return;
        }

        Secret = HiddenPasswordBox.Password;
    }

    private void RevealedTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSynchronizing)
        {
            return;
        }

        Secret = RevealedTextBox.Text;
    }

    private void SyncSecretToInputs(string value)
    {
        _isSynchronizing = true;
        try
        {
            if (!string.Equals(HiddenPasswordBox.Password, value, StringComparison.Ordinal))
            {
                HiddenPasswordBox.Password = value;
            }

            if (!string.Equals(RevealedTextBox.Text, value, StringComparison.Ordinal))
            {
                RevealedTextBox.Text = value;
            }
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void SyncVisualState()
    {
        var isRevealed = IsRevealed;
        HiddenPasswordBox.Visibility = isRevealed ? Visibility.Collapsed : Visibility.Visible;
        RevealedTextBox.Visibility = isRevealed ? Visibility.Visible : Visibility.Collapsed;
        RevealIconText.Text = isRevealed ? HideSecretGlyph : ShowSecretGlyph;
    }

    private void ApplyAutomationName(string name)
    {
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetName(HiddenPasswordBox, name);
        AutomationProperties.SetName(RevealedTextBox, name);
    }
}
