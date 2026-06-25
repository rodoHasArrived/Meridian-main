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

            var window = Render(host);
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
    public void AlertAndEvidencePrimitives_ShouldRenderWithAutomationContracts()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var alertAction = new TestCommand();
            var evidenceAction = new TestCommand();
            var alert = new InlineAlertPanel
            {
                Title = "Provider credentials stale",
                Message = "Polygon.io credentials need review before data can be trusted.",
                Tone = WorkspaceTone.Warning,
                IconGlyph = "\uE783",
                ActionText = "Resolve",
                ActionCommand = alertAction,
                PanelAutomationId = "InlineAlertTest",
                TitleAutomationId = "InlineAlertTitleTest",
                MessageAutomationId = "InlineAlertMessageTest",
                ActionButtonAutomationId = "InlineAlertActionTest"
            };
            var evidence = new EvidenceLinkChip
            {
                Label = "Ledger evidence retained",
                Source = "Accounting",
                TimestampText = "Today 10:24",
                Target = "EvidenceWorkbench:accounting-record/123",
                Detail = "Trial balance and approval history retained.",
                NavigateCommand = evidenceAction,
                CommandParameter = "accounting-record/123",
                ChipAutomationId = "EvidenceChipTest",
                LabelAutomationId = "EvidenceChipLabelTest",
                SourceAutomationId = "EvidenceChipSourceTest",
                TimestampAutomationId = "EvidenceChipTimestampTest",
                TargetAutomationId = "EvidenceChipTargetTest",
                DetailAutomationId = "EvidenceChipDetailTest",
                ActionButtonAutomationId = "EvidenceChipActionTest"
            };
            var host = new StackPanel { Children = { alert, evidence } };

            var window = Render(host);
            try
            {
                AutomationProperties.GetAutomationId(alert).Should().Be("InlineAlertTest");
                AutomationProperties.GetName(alert).Should().Be("Provider credentials stale");
                GetByAutomationId<Border>(alert, "InlineAlertTest").BorderThickness.Left.Should().Be(4);
                GetByAutomationId<TextBlock>(alert, "InlineAlertTitleTest").Text.Should().Be("Provider credentials stale");
                GetByAutomationId<TextBlock>(alert, "InlineAlertMessageTest").Text.Should().Be("Polygon.io credentials need review before data can be trusted.");
                GetByAutomationId<Button>(alert, "InlineAlertActionTest").Command.Should().BeSameAs(alertAction);

                AutomationProperties.GetAutomationId(evidence).Should().Be("EvidenceChipTest");
                AutomationProperties.GetName(evidence).Should().Be("Ledger evidence retained");
                GetByAutomationId<TextBlock>(evidence, "EvidenceChipLabelTest").Text.Should().Be("Ledger evidence retained");
                GetByAutomationId<TextBlock>(evidence, "EvidenceChipSourceTest").Text.Should().Be("Accounting");
                GetByAutomationId<TextBlock>(evidence, "EvidenceChipTimestampTest").Text.Should().Be("Today 10:24");
                GetByAutomationId<TextBlock>(evidence, "EvidenceChipTargetTest").Text.Should().Be("EvidenceWorkbench:accounting-record/123");
                GetByAutomationId<TextBlock>(evidence, "EvidenceChipDetailTest").Text.Should().Be("Trial balance and approval history retained.");
                GetByAutomationId<Button>(evidence, "EvidenceChipActionTest").Command.Should().BeSameAs(evidenceAction);
                GetByAutomationId<Button>(evidence, "EvidenceChipActionTest").CommandParameter.Should().Be("accounting-record/123");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HeaderFilterAndFreshnessPrimitives_ShouldRenderWithAutomationContracts()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var sectionAction = new TestCommand();
            var clearFilters = new TestCommand();
            var removeFilter = new TestCommand();
            var header = new SectionHeaderBar
            {
                Title = "Provider readiness",
                Subtitle = "Health, freshness, and evidence posture for active feeds.",
                BadgeText = "3 blocked",
                BadgeTone = WorkspaceTone.Danger,
                ActionText = "Refresh",
                ActionCommand = sectionAction,
                RightContent = new FreshnessIndicator { Label = "As of", Value = "10:24", Tone = WorkspaceTone.Success },
                HeaderAutomationId = "SectionHeaderTest",
                TitleAutomationId = "SectionHeaderTitleTest",
                SubtitleAutomationId = "SectionHeaderSubtitleTest",
                BadgeAutomationId = "SectionHeaderBadgeTest",
                ActionButtonAutomationId = "SectionHeaderActionTest",
                RightContentAutomationId = "SectionHeaderRightContentTest"
            };
            var filters = new FilterChipBar
            {
                ItemsSource = new[] { "Fund: Demo", "Status: Blocked" },
                ResultSummary = "23 records",
                ClearAllCommand = clearFilters,
                RemoveFilterCommand = removeFilter,
                BarAutomationId = "FilterChipBarTest",
                ResultSummaryAutomationId = "FilterChipSummaryTest",
                ItemsAutomationId = "FilterChipItemsTest",
                ClearAllButtonAutomationId = "FilterChipClearAllTest"
            };
            var freshness = new FreshnessIndicator
            {
                Label = "As of",
                Value = "10:24",
                Detail = "Provider health source",
                Tone = WorkspaceTone.Success,
                IndicatorAutomationId = "FreshnessIndicatorTest",
                IconAutomationId = "FreshnessIconTest",
                LabelAutomationId = "FreshnessLabelTest",
                ValueAutomationId = "FreshnessValueTest",
                DetailAutomationId = "FreshnessDetailTest"
            };
            var host = new StackPanel { Children = { header, filters, freshness } };

            var window = Render(host);
            try
            {
                AutomationProperties.GetAutomationId(header).Should().Be("SectionHeaderTest");
                AutomationProperties.GetName(header).Should().Be("Provider readiness");
                GetByAutomationId<TextBlock>(header, "SectionHeaderTitleTest").Text.Should().Be("Provider readiness");
                GetByAutomationId<TextBlock>(header, "SectionHeaderSubtitleTest").Text.Should().Be("Health, freshness, and evidence posture for active feeds.");
                GetByAutomationId<ToneBadge>(header, "SectionHeaderBadgeTest").Text.Should().Be("3 blocked");
                GetByAutomationId<ContentPresenter>(header, "SectionHeaderRightContentTest").Content.Should().BeAssignableTo<FreshnessIndicator>();
                GetByAutomationId<Button>(header, "SectionHeaderActionTest").Command.Should().BeSameAs(sectionAction);

                AutomationProperties.GetAutomationId(filters).Should().Be("FilterChipBarTest");
                AutomationProperties.GetName(filters).Should().Be("23 records");
                GetByAutomationId<TextBlock>(filters, "FilterChipSummaryTest").Text.Should().Be("23 records");
                GetByAutomationId<ItemsControl>(filters, "FilterChipItemsTest").Items.Count.Should().Be(2);
                GetByAutomationId<Button>(filters, "FilterChipClearAllTest").Command.Should().BeSameAs(clearFilters);

                AutomationProperties.GetAutomationId(freshness).Should().Be("FreshnessIndicatorTest");
                AutomationProperties.GetName(freshness).Should().Be("As of: 10:24");
                GetByAutomationId<TextBlock>(freshness, "FreshnessIconTest").Text.Should().Be("\uE823");
                GetByAutomationId<TextBlock>(freshness, "FreshnessLabelTest").Text.Should().Be("As of");
                GetByAutomationId<TextBlock>(freshness, "FreshnessValueTest").Text.Should().Be("10:24");
                GetByAutomationId<TextBlock>(freshness, "FreshnessDetailTest").Text.Should().Be("Provider health source");
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

            var window = Render(host);
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
    public void OptionalAlertAndEvidenceElements_ShouldCollapseWhenMissing()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var alert = new InlineAlertPanel
            {
                Title = "No warning",
                Message = "Nothing to resolve.",
                ActionText = "Resolve",
                IconGlyph = string.Empty
            };
            var evidence = new EvidenceLinkChip
            {
                Label = "Evidence retained",
                Source = "Accounting",
                Target = "EvidenceWorkbench"
            };
            var host = new StackPanel
            {
                Children =
                {
                    alert,
                    evidence
                }
            };

            var window = Render(host);
            try
            {
                Get<Border>(alert, "IconContainer").Visibility.Should().Be(Visibility.Collapsed);
                Get<Button>(alert, "ActionButton").Visibility.Should().Be(Visibility.Collapsed);
                Get<TextBlock>(evidence, "DetailText").Visibility.Should().Be(Visibility.Collapsed);
                Get<Button>(evidence, "OpenButton").Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OptionalHeaderFilterAndFreshnessElements_ShouldCollapseWhenMissing()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var header = new SectionHeaderBar
            {
                Title = "Provider readiness",
                ActionText = "Refresh"
            };
            var filters = new FilterChipBar
            {
                ItemsSource = new[] { "Fund: Demo" },
                ResultSummary = "1 record"
            };
            var freshness = new FreshnessIndicator
            {
                Label = "As of",
                Value = "Unavailable",
                IconGlyph = string.Empty
            };
            var host = new StackPanel
            {
                Children =
                {
                    header,
                    filters,
                    freshness
                }
            };

            var window = Render(host);
            try
            {
                Get<ToneBadge>(header, "Badge").Visibility.Should().Be(Visibility.Collapsed);
                Get<ContentPresenter>(header, "RightContentPresenter").Visibility.Should().Be(Visibility.Collapsed);
                Get<Button>(header, "ActionButton").Visibility.Should().Be(Visibility.Collapsed);
                Get<Button>(filters, "ClearAllButton").Visibility.Should().Be(Visibility.Collapsed);
                Get<TextBlock>(freshness, "IconText").Visibility.Should().Be(Visibility.Collapsed);
                Get<TextBlock>(freshness, "DetailText").Visibility.Should().Be(Visibility.Collapsed);
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

            var window = Render(control);
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

    private static RenderedElement Render(FrameworkElement element)
    {
        element.Measure(new Size(900, 700));
        element.Arrange(new Rect(0, 0, 900, 700));
        element.UpdateLayout();
        return new RenderedElement();
    }

    private sealed class RenderedElement
    {
        public void Close()
        {
        }
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
