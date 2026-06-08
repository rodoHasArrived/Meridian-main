using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Meridian.Wpf.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class ApplicationPrimitiveControlsTests
{
    [Fact]
    public void SharedApplicationPrimitives_ShouldRenderWithAutomationContracts()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var emptyAction = new TestCommand();
            var queueAction = new TestCommand();
            var iconCommand = new TestCommand();
            var emptyState = new EmptyStatePanel
            {
                Title = "No approvals queued",
                Description = "The current fund has no retained approval work.",
                IconGlyph = "\uE946",
                ActionText = "Refresh approvals",
                ActionCommand = emptyAction,
                PanelAutomationId = "EmptyStateTest",
                IconAutomationId = "EmptyStateIconTest",
                TitleAutomationId = "EmptyStateTitleTest",
                DescriptionAutomationId = "EmptyStateDescriptionTest",
                ActionButtonAutomationId = "EmptyStateActionTest"
            };
            var iconButton = new IconTextButton
            {
                IconGlyph = "\uE72C",
                Text = "Refresh",
                Command = iconCommand,
                CommandParameter = "approvals",
                ButtonAutomationId = "IconTextButtonTest",
                IconAutomationId = "IconTextButtonIconTest",
                TextAutomationId = "IconTextButtonLabelTest"
            };
            var metricCard = new MetricCard
            {
                Title = "Approval SLA",
                Value = "18m",
                SubText = "2 retained records",
                IconGlyph = "\uE916",
                Tone = WorkspaceTone.Warning,
                CardAutomationId = "MetricCardTest",
                TitleAutomationId = "MetricCardTitleTest",
                ValueAutomationId = "MetricCardValueTest",
                SubTextAutomationId = "MetricCardSubTextTest",
                IconAutomationId = "MetricCardIconTest"
            };
            var toneBadge = new ToneBadge
            {
                Text = "Blocked",
                IconGlyph = "\uE783",
                Tone = WorkspaceTone.Danger,
                BadgeAutomationId = "ToneBadgeTest",
                IconAutomationId = "ToneBadgeIconTest",
                TextAutomationId = "ToneBadgeTextTest"
            };
            var queueCard = new WorkspaceQueueCard
            {
                Title = "Approve close packet",
                Description = "Controller sign-off is required before report pack publication.",
                IconGlyph = "\uE8D7",
                Tone = WorkspaceTone.Danger,
                ActionText = "Open review",
                ActionCommand = queueAction,
                CardAutomationId = "QueueCardTest",
                IconAutomationId = "QueueCardIconTest",
                TitleAutomationId = "QueueCardTitleTest",
                DescriptionAutomationId = "QueueCardDescriptionTest",
                ActionButtonAutomationId = "QueueCardActionTest"
            };
            var host = new StackPanel
            {
                Children =
                {
                    emptyState,
                    iconButton,
                    metricCard,
                    toneBadge,
                    queueCard
                }
            };

            var window = Show(host);
            try
            {
                AutomationProperties.GetAutomationId(emptyState).Should().Be("EmptyStateTest");
                AutomationProperties.GetName(emptyState).Should().Be("No approvals queued");
                emptyState.MinHeight.Should().Be(150);
                AutomationProperties.GetAutomationId(Get<TextBlock>(emptyState, "IconText")).Should().Be("EmptyStateIconTest");
                AutomationProperties.GetAutomationId(Get<TextBlock>(emptyState, "TitleText")).Should().Be("EmptyStateTitleTest");
                AutomationProperties.GetAutomationId(Get<TextBlock>(emptyState, "DescriptionText")).Should().Be("EmptyStateDescriptionTest");
                AutomationProperties.GetAutomationId(Get<Button>(emptyState, "ActionButton")).Should().Be("EmptyStateActionTest");
                Get<TextBlock>(emptyState, "TitleText").Text.Should().Be("No approvals queued");
                Get<Button>(emptyState, "ActionButton").Command.Should().BeSameAs(emptyAction);

                AutomationProperties.GetAutomationId(iconButton).Should().Be("IconTextButtonTest");
                AutomationProperties.GetName(iconButton).Should().Be("Refresh");
                iconButton.MinHeight.Should().Be(32);
                AutomationProperties.GetAutomationId(Get<Button>(iconButton, "InternalButton")).Should().Be("IconTextButtonTest");
                AutomationProperties.GetAutomationId(Get<TextBlock>(iconButton, "IconText")).Should().Be("IconTextButtonIconTest");
                AutomationProperties.GetAutomationId(Get<TextBlock>(iconButton, "LabelText")).Should().Be("IconTextButtonLabelTest");
                Get<Button>(iconButton, "InternalButton").Command.Should().BeSameAs(iconCommand);
                Get<Button>(iconButton, "InternalButton").CommandParameter.Should().Be("approvals");

                AutomationProperties.GetAutomationId(metricCard).Should().Be("MetricCardTest");
                AutomationProperties.GetName(metricCard).Should().Be("Approval SLA: 18m");
                metricCard.MinWidth.Should().Be(170);
                metricCard.MinHeight.Should().Be(96);
                metricCard.Title.Should().Be("Approval SLA");
                metricCard.Value.Should().Be("18m");
                metricCard.SubText.Should().Be("2 retained records");
                metricCard.IconGlyph.Should().Be("\uE916");
                metricCard.Tone.Should().Be(WorkspaceTone.Warning);

                AutomationProperties.GetAutomationId(toneBadge).Should().Be("ToneBadgeTest");
                AutomationProperties.GetName(toneBadge).Should().Be("Blocked");
                toneBadge.MinHeight.Should().Be(24);
                GetByAutomationId<Border>(toneBadge, "ToneBadgeTest").Should().NotBeNull();
                GetByAutomationId<TextBlock>(toneBadge, "ToneBadgeIconTest").Text.Should().Be("\uE783");
                GetByAutomationId<TextBlock>(toneBadge, "ToneBadgeTextTest").Text.Should().Be("Blocked");

                AutomationProperties.GetAutomationId(queueCard).Should().Be("QueueCardTest");
                AutomationProperties.GetName(queueCard).Should().Be("Approve close packet");
                queueCard.MinWidth.Should().Be(240);
                queueCard.MinHeight.Should().Be(118);
                GetByAutomationId<Border>(queueCard, "QueueCardTest").BorderThickness.Left.Should().Be(4);
                GetByAutomationId<TextBlock>(queueCard, "QueueCardIconTest").Text.Should().Be("\uE8D7");
                GetByAutomationId<TextBlock>(queueCard, "QueueCardTitleTest").Text.Should().Be("Approve close packet");
                GetByAutomationId<TextBlock>(queueCard, "QueueCardDescriptionTest").Text.Should().Be("Controller sign-off is required before report pack publication.");
                GetByAutomationId<Button>(queueCard, "QueueCardActionTest").Command.Should().BeSameAs(queueAction);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OptionalPrimitiveActions_ShouldCollapseWhenCommandOrTextIsMissing()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var emptyWithoutCommand = new EmptyStatePanel
            {
                Title = "No records",
                Description = "Nothing to review.",
                ActionText = "Refresh"
            };
            var emptyWithoutText = new EmptyStatePanel
            {
                Title = "No records",
                Description = "Nothing to review.",
                ActionCommand = new TestCommand()
            };
            var queueWithoutCommand = new WorkspaceQueueCard
            {
                Title = "No action",
                Description = "The queue is informational.",
                ActionText = "Open"
            };
            var queueWithoutText = new WorkspaceQueueCard
            {
                Title = "No action",
                Description = "The queue is informational.",
                ActionCommand = new TestCommand()
            };
            var host = new StackPanel
            {
                Children =
                {
                    emptyWithoutCommand,
                    emptyWithoutText,
                    queueWithoutCommand,
                    queueWithoutText
                }
            };

            var window = Show(host);
            try
            {
                Get<Button>(emptyWithoutCommand, "ActionButton").Visibility.Should().Be(Visibility.Collapsed);
                Get<Button>(emptyWithoutText, "ActionButton").Visibility.Should().Be(Visibility.Collapsed);
                Get<Button>(queueWithoutCommand, "ActionButton").Visibility.Should().Be(Visibility.Collapsed);
                Get<Button>(queueWithoutText, "ActionButton").Visibility.Should().Be(Visibility.Collapsed);
                Get<Border>(queueWithoutCommand, "IconContainer").Visibility.Should().Be(Visibility.Collapsed);
                Get<Border>(queueWithoutText, "IconContainer").Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void IconTextButton_ClickEvent_ShouldRaiseOnceForInternalButtonClick()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var clickCount = 0;
            var control = new IconTextButton
            {
                IconGlyph = "\uE72C",
                Text = "Refresh"
            };
            control.Click += (_, _) => clickCount++;

            var window = Show(control);
            try
            {
                Get<Button>(control, "InternalButton").RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                clickCount.Should().Be(1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static T Get<T>(Control control, string name)
        where T : FrameworkElement
        => control.FindName(name).Should().BeOfType<T>($"{name} should be part of the primitive contract").Subject;

    private static T GetByAutomationId<T>(DependencyObject root, string automationId)
        where T : FrameworkElement
    {
        var match = FindByAutomationId<T>(root, automationId);
        match.Should().NotBeNull($"{automationId} should be part of the primitive automation contract");
        return match!;
    }

    private static T? FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : FrameworkElement
    {
        if (root is T element && AutomationProperties.GetAutomationId(element) == automationId)
        {
            return element;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var match = FindByAutomationId<T>(child, automationId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static Window Show(FrameworkElement element)
    {
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = element
        };

        window.Show();
        window.UpdateLayout();
        element.UpdateLayout();
        return window;
    }

    private sealed class TestCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
