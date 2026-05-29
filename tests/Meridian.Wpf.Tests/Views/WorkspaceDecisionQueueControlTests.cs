using System.IO;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceDecisionQueueControlTests
{
    [Fact]
    public void WorkspaceDecisionQueueControlSource_ShouldExposeSharedCockpitDecisionTemplate()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\WorkspaceDecisionQueueControl.xaml"));

        xaml.Should().Contain("DataType=\"{x:Type models:WorkspaceQueueItem}\"");
        xaml.Should().Contain("WorkspaceToneQueueCardStyle");
        xaml.Should().Contain("WorkspaceToneBadgeStyle");
        xaml.Should().Contain("WorkspaceToneBadgeTextStyle");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"{Binding ElementName=Root, Path=QueueAutomationId}\"");
        xaml.Should().Contain("AutomationProperties.Name=\"{Binding AutomationName}\"");
        xaml.Should().Contain("UniformGrid Columns=\"{Binding Tag.Columns, RelativeSource={RelativeSource AncestorType={x:Type ItemsControl}}}\"");
        xaml.Should().Contain("Click=\"OnPrimaryActionClick\"");
        xaml.Should().Contain("Click=\"OnSecondaryActionClick\"");
        xaml.Should().Contain("SecondaryButtonStyle");
        xaml.Should().Contain("GhostButtonStyle");
        xaml.Should().Contain("SecondaryActionId");
        xaml.Should().Contain("SecondaryActionLabel");
        xaml.Should().Contain("IsBlocked");
    }

    [Fact]
    public void WorkspaceDecisionQueueControlSource_ShouldRoutePrimaryAndSecondaryActionIds()
    {
        var code = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\WorkspaceDecisionQueueControl.xaml.cs"));

        code.Should().Contain("public enum WorkspaceDecisionActionKind");
        code.Should().Contain("Primary,");
        code.Should().Contain("Secondary");
        code.Should().Contain("public static readonly DependencyProperty ColumnsProperty");
        code.Should().Contain("new PropertyMetadata(2)");
        code.Should().Contain("public string ActionId { get; }");
        code.Should().Contain("Decision.SecondaryActionId");
        code.Should().Contain("Decision.PrimaryActionId");
        code.Should().Contain("public event EventHandler<WorkspaceDecisionInvokedEventArgs>? DecisionInvoked;");
        code.Should().Contain("RaiseDecisionInvoked(sender, WorkspaceDecisionActionKind.Primary)");
        code.Should().Contain("RaiseDecisionInvoked(sender, WorkspaceDecisionActionKind.Secondary)");
        code.Should().Contain("Tag: WorkspaceQueueItem decision");
        code.Should().Contain("if (!string.IsNullOrWhiteSpace(args.ActionId))");
        code.Should().Contain("DecisionInvoked?.Invoke(this, args);");
    }
}
