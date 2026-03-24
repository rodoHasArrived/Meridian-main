using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services.Services;
using ValidationResult = Meridian.Ui.Services.Services.ValidationResult;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for form validation with inline error display.
/// Provides consistent validation across all forms in the WPF application.
/// Delegates validation rules to shared FormValidationRules class.
/// </summary>
public sealed class FormValidationService
{
    private static readonly Lazy<FormValidationService> _instance = new(() => new FormValidationService());
    public static FormValidationService Instance => _instance.Value;

    private FormValidationService() { }

    #region Validation Rules (Delegates to Shared Rules)

    public static ValidationResult ValidateRequired(string? value, string fieldName = "This field")
        => FormValidationRules.ValidateRequired(value, fieldName);

    public static ValidationResult ValidateSymbol(string? value)
        => FormValidationRules.ValidateSymbol(value);

    public static ValidationResult ValidateSymbolList(string? value, int maxSymbols = 100)
        => FormValidationRules.ValidateSymbolList(value, maxSymbols);

    public static ValidationResult ValidateDateRange(DateTimeOffset? fromDate, DateTimeOffset? toDate)
        => FormValidationRules.ValidateDateRange(fromDate, toDate);

    public static ValidationResult ValidateApiKey(string? value)
        => FormValidationRules.ValidateApiKey(value);

    public static ValidationResult ValidateUrl(string? value, string fieldName = "URL")
        => FormValidationRules.ValidateUrl(value, fieldName);

    public static ValidationResult ValidatePort(int? value)
        => FormValidationRules.ValidatePort(value);

    public static ValidationResult ValidateFilePath(string? value)
        => FormValidationRules.ValidateFilePath(value);

    public static ValidationResult ValidateNumericRange(double? value, double min, double max, string fieldName = "Value")
        => FormValidationRules.ValidateNumericRange(value, min, max, fieldName);

    #endregion

    #region UI Helpers

    /// <summary>
    /// Shows validation result in a TextBlock status area.
    /// </summary>
    public void ShowValidationResult(TextBlock statusBlock, ValidationResult result)
    {
        if (result.IsValid && !result.IsWarning)
        {
            statusBlock.Visibility = Visibility.Collapsed;
            return;
        }

        statusBlock.Text = result.IsWarning ? $"Warning: {result.Message}" : $"Error: {result.Message}";
        statusBlock.Foreground = result.IsWarning
            ? new SolidColorBrush(Color.FromArgb(255, 210, 153, 34))
            : new SolidColorBrush(Color.FromArgb(255, 248, 81, 73));
        statusBlock.Visibility = Visibility.Visible;
    }

    public ValidationResult ValidateTextBox(TextBox textBox, Func<string?, ValidationResult> validator, TextBlock? errorTextBlock = null)
    {
        var result = validator(textBox.Text);
        ApplyValidationStyle(textBox, result, errorTextBlock);
        return result;
    }

    private void ApplyValidationStyle(TextBox textBox, ValidationResult result, TextBlock? errorTextBlock)
    {
        if (!result.IsValid && !result.IsWarning)
            textBox.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 248, 81, 73));
        else if (result.IsWarning)
            textBox.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 210, 153, 34));
        else
            textBox.ClearValue(TextBox.BorderBrushProperty);

        if (errorTextBlock != null)
            ShowErrorText(errorTextBlock, result);
    }

    private void ShowErrorText(TextBlock errorTextBlock, ValidationResult result)
    {
        if (!result.IsValid)
        {
            errorTextBlock.Text = result.Message;
            errorTextBlock.Foreground = result.IsWarning
                ? new SolidColorBrush(Color.FromArgb(255, 210, 153, 34))
                : new SolidColorBrush(Color.FromArgb(255, 248, 81, 73));
            errorTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            errorTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    public void ClearValidation(TextBox textBox, TextBlock? errorTextBlock = null)
    {
        textBox.ClearValue(TextBox.BorderBrushProperty);
        if (errorTextBlock != null)
            errorTextBlock.Visibility = Visibility.Collapsed;
    }

    #endregion
}
