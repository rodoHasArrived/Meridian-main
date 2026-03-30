using System;
using System.Windows;

namespace Meridian.Wpf.Views;

/// <summary>
/// Dialog for entering or updating an API key for a provider.
/// Replaces the former imperative <c>ApiKeyDialog</c> Window class
/// that was embedded in <c>BackfillPage.xaml.cs</c>.
/// </summary>
public partial class ApiKeyDialog : Window
{
    /// <summary>Gets the API key entered by the user.</summary>
    public string ApiKey => ApiKeyBox.Text;

    public ApiKeyDialog(string providerName, string envVarName, bool isOptional = false)
    {
        InitializeComponent();

        Title = $"Configure {providerName} API Key";
        DescriptionText.Text = $"Enter your {providerName} API key{(isOptional ? " (optional)" : "")}:";
        HintText.Text = $"Environment variable: {envVarName}";

        var existingValue = Environment.GetEnvironmentVariable(envVarName, EnvironmentVariableTarget.User);
        if (!string.IsNullOrEmpty(existingValue))
            ApiKeyBox.Text = existingValue;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
