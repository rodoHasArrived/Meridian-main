using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class SecretInputControlTests
{
    [Fact]
    public void SecretInputControl_ShouldHideSecretByDefaultAndExposeSafeAutomationNames()
    {
        WpfTestThread.Run(() =>
        {
            var control = CreateControl();
            control.Secret = FixtureSecret(16);

            Arrange(control);

            var hidden = Get<PasswordBox>(control, "HiddenPasswordBox");
            var revealed = Get<TextBox>(control, "RevealedTextBox");
            var toggle = Get<ToggleButton>(control, "RevealToggleButton");

            hidden.Visibility.Should().Be(Visibility.Visible);
            revealed.Visibility.Should().Be(Visibility.Collapsed);
            AutomationProperties.GetName(control).Should().Be("Provider credential");
            AutomationProperties.GetName(hidden).Should().Be("Provider credential");
            AutomationProperties.GetName(revealed).Should().Be("Provider credential");
            AutomationProperties.GetName(toggle).Should().Be("Show or hide provider credential");
            AutomationProperties.GetAutomationId(toggle).Should().Be("ProviderCredentialReveal");
        });
    }

    [Fact]
    public void SecretInputControl_ShouldSynchronizeMaskedAndRevealedInputs()
    {
        WpfTestThread.Run(() =>
        {
            var control = CreateControl();
            Arrange(control);

            var hidden = Get<PasswordBox>(control, "HiddenPasswordBox");
            var revealed = Get<TextBox>(control, "RevealedTextBox");

            hidden.Password = FixtureSecret(14);

            control.Secret.Length.Should().Be(14);
            revealed.Text.Length.Should().Be(14);

            control.IsRevealed = true;
            revealed.Text = FixtureSecret(9);

            control.Secret.Length.Should().Be(9);
            hidden.Password.Length.Should().Be(9);
            hidden.Visibility.Should().Be(Visibility.Collapsed);
            revealed.Visibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void ClearSecret_ShouldClearMaskedAndRevealedValuesAndReturnToHiddenMode()
    {
        WpfTestThread.Run(() =>
        {
            var control = CreateControl();
            control.Secret = FixtureSecret(18);
            control.IsRevealed = true;
            Arrange(control);

            control.ClearSecret();

            Get<PasswordBox>(control, "HiddenPasswordBox").Password.Length.Should().Be(0);
            Get<TextBox>(control, "RevealedTextBox").Text.Length.Should().Be(0);
            control.Secret.Should().BeEmpty();
            control.IsRevealed.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(@"src\Meridian.Wpf\Views\ApiKeyDialog.xaml")]
    [InlineData(@"src\Meridian.Wpf\Views\CredentialManagementPage.xaml")]
    [InlineData(@"src\Meridian.Wpf\Views\DataExportPage.xaml")]
    [InlineData(@"src\Meridian.Wpf\Views\DataSourcesPage.xaml")]
    [InlineData(@"src\Meridian.Wpf\Views\SetupWizardPage.xaml")]
    [InlineData(@"src\Meridian.Wpf\Views\StartupWindow.xaml")]
    public void CredentialEntrySources_ShouldUseSharedSecretInputControl(string relativePath)
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(relativePath));

        xaml.Should().Contain("SecretInputControl");
        xaml.Should().NotContain("<PasswordBox");
    }

    private static SecretInputControl CreateControl()
    {
        RunMatUiAutomationFacade.EnsureApplicationResources();
        return new SecretInputControl
        {
            InputAutomationId = "ProviderCredentialInput",
            RevealAutomationId = "ProviderCredentialReveal",
            InputAutomationName = "Provider credential",
            RevealAutomationName = "Show or hide provider credential",
            RevealToolTip = "Show or hide provider credential"
        };
    }

    private static void Arrange(SecretInputControl control)
    {
        control.Measure(new Size(360, 42));
        control.Arrange(new Rect(0, 0, 360, 42));
        control.UpdateLayout();
    }

    private static T Get<T>(SecretInputControl control, string name)
        where T : FrameworkElement
        => control.FindName(name).Should().BeOfType<T>($"{name} should be part of the shared secret input contract").Subject;

    private static string FixtureSecret(int length)
        => new('x', length);
}
