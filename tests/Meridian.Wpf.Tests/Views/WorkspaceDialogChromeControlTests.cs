using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceDialogChromeControlTests
{
    [Fact]
    public void WorkspaceDialogChrome_ShouldExposeReusableAutomationContract()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            var body = new TextBox
            {
                Text = "credential-input"
            };
            var control = new WorkspaceDialogChromeControl
            {
                DialogAutomationId = "ProviderDialogChrome",
                TitleAutomationId = "ProviderDialogTitle",
                SubtitleAutomationId = "ProviderDialogSubtitle",
                BodyAutomationId = "ProviderDialogBody",
                TitleText = "Configure Provider",
                SubtitleText = "Credential setup",
                Body = body
            };

            control.Measure(new Size(480, 260));
            control.Arrange(new Rect(0, 0, 480, 260));
            control.UpdateLayout();

            AutomationProperties.GetAutomationId(control).Should().Be("ProviderDialogChrome");
            AutomationProperties.GetName(control).Should().Be("Configure Provider");
            Get<TextBlock>(control, "DialogTitleText").Text.Should().Be("Configure Provider");
            AutomationProperties.GetAutomationId(Get<TextBlock>(control, "DialogTitleText")).Should().Be("ProviderDialogTitle");
            Get<TextBlock>(control, "DialogSubtitleText").Text.Should().Be("Credential setup");
            AutomationProperties.GetAutomationId(Get<TextBlock>(control, "DialogSubtitleText")).Should().Be("ProviderDialogSubtitle");

            var presenter = Get<ContentPresenter>(control, "DialogBodyPresenter");
            presenter.Content.Should().BeSameAs(body);
            AutomationProperties.GetAutomationId(presenter).Should().Be("ProviderDialogBody");
        });
    }

    [Fact]
    public void ApiKeyDialogSource_ShouldUseSharedDialogChromeAndStableActionAutomationIds()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\ApiKeyDialog.xaml"));
        var code = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\ApiKeyDialog.xaml.cs"));

        xaml.Should().Contain("WorkspaceDialogChromeControl");
        xaml.Should().Contain("DialogAutomationId=\"ApiKeyDialogChrome\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ApiKeyDialogInput\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ApiKeyDialogCancelButton\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ApiKeyDialogSaveButton\"");
        xaml.Should().NotContain("Background=\"#FF1E1E2E\"");
        xaml.Should().NotContain("Background=\"#FF4CAF50\"");

        code.Should().Contain("DialogChrome.TitleText = Title;");
        code.Should().Contain("DialogChrome.SubtitleText");
    }

    private static T Get<T>(Control control, string name)
        where T : FrameworkElement
        => control.FindName(name).Should().BeOfType<T>($"{name} should be part of the dialog chrome contract").Subject;
}
