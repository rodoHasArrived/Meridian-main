using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Workstation.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkstationPrimitiveControlsTests
{
    [Fact]
    public void SharedWorkstationControls_ShouldRenderWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var tableRows = new ObservableCollection<RowFixture>
            {
                new("Polygon.io", "Healthy")
            };
            var timeline = new AuditTimelineModel
            {
                Title = "Connection History",
                EmptyText = "No connection events recorded."
            };
            timeline.Entries.Add(new AuditTimelineEntryModel("Connected", "Polygon.io", "Just now", WorkspaceTone.Success));

            var denseGrid = new DenseDataGridControl
            {
                Table = new WorkstationTableModel<RowFixture>(
                    tableRows,
                    [new("Provider", nameof(RowFixture.Name), 120), new("Status", nameof(RowFixture.Status), 100)],
                    "Provider readiness table"),
                GridAutomationId = "TestDenseGrid",
                EmptyAutomationId = "TestDenseGridEmpty"
            };
            var statePanel = new WorkstationStatePanelControl
            {
                State = WorkstationStateModel.Ready("Provider posture ready", "Coverage is available.", evidenceText: "Provider health evidence."),
                TitleAutomationId = "TestStateTitle",
                DetailAutomationId = "TestStateDetail",
                EvidenceAutomationId = "TestStateEvidence"
            };

            var host = new StackPanel
            {
                Children =
                {
                    new WorkstationCommandBarControl
                    {
                        CommandGroup = new WorkstationCommandGroupModel
                        {
                            PrimaryCommands =
                            [
                                new("Refresh", "Refresh", "Refresh provider health.", "\uE72C")
                            ]
                        }
                    },
                    statePanel,
                    new MetricTileControl
                    {
                        Metric = new WorkstationMetricModel("Connected", "1", "Streaming ready", "\uE73E", WorkspaceTone.Success)
                    },
                    new HealthBadgeControl
                    {
                        Badge = new WorkstationBadgeModel("Health", "Healthy", "\uE946", WorkspaceTone.Success)
                    },
                    denseGrid,
                    new InspectorPanelControl
                    {
                        Panel = new InspectorPanelModel
                        {
                            Title = "Polygon.io",
                            Facts = [new KeyValueFactModel("Credential", "Configured")]
                        }
                    },
                    new DiagnosticsChecklistControl
                    {
                        Checklist = new DiagnosticsChecklistModel
                        {
                            Items = [new DiagnosticsChecklistItemModel("Credential presence", "Pass", "Configured", WorkspaceTone.Success)]
                        }
                    },
                    new RoutingMatrixControl
                    {
                        Matrix = new RoutingMatrixModel
                        {
                            Rows = [new RoutingMatrixRowModel("Backfill", "Polygon.io", "Healthy", "Ready", WorkspaceTone.Success)]
                        }
                    },
                    new ActivityLogGridControl
                    {
                        Timeline = timeline
                    }
                }
            };

            var window = new Window
            {
                Width = 1000,
                Height = 900,
                Content = new ScrollViewer { Content = host }
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                host.UpdateLayout();

                var rowsList = denseGrid.FindName("RowsList").Should().BeOfType<ListView>().Subject;
                VirtualizingPanel.GetIsVirtualizing(rowsList).Should().BeTrue();
                VirtualizingPanel.GetVirtualizationMode(rowsList).Should().Be(VirtualizationMode.Recycling);
                ScrollViewer.GetCanContentScroll(rowsList).Should().BeTrue();
                AutomationProperties.GetAutomationId(rowsList).Should().Be("TestDenseGrid");
                AutomationProperties.GetName(rowsList).Should().Be("Provider readiness table");

                var emptyPanel = denseGrid.FindName("EmptyPanel").Should().BeOfType<Border>().Subject;
                AutomationProperties.GetAutomationId(emptyPanel).Should().Be("TestDenseGridEmpty");

                var stateTitle = statePanel.FindName("StateTitleText").Should().BeOfType<TextBlock>().Subject;
                var stateDetail = statePanel.FindName("StateDetailText").Should().BeOfType<TextBlock>().Subject;
                var stateEvidence = statePanel.FindName("StateEvidenceText").Should().BeOfType<TextBlock>().Subject;
                AutomationProperties.GetAutomationId(stateTitle).Should().Be("TestStateTitle");
                AutomationProperties.GetAutomationId(stateDetail).Should().Be("TestStateDetail");
                AutomationProperties.GetAutomationId(stateEvidence).Should().Be("TestStateEvidence");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DenseDataGridSource_ShouldUseCompactInstitutionalTableChrome()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Workstation\Controls\DenseDataGridControl.xaml"));

        xaml.Should().Contain("DenseGridColumnHeaderStyle");
        xaml.Should().Contain("<Setter Property=\"FontSize\" Value=\"10\" />");
        xaml.Should().Contain("<Setter Property=\"MinHeight\" Value=\"26\" />");
        xaml.Should().Contain("FontSize=\"11\"");
        xaml.Should().Contain("VirtualizingPanel.VirtualizationMode=\"Recycling\"");
        xaml.Should().Contain("VirtualizingPanel.ScrollUnit=\"Item\"");
    }

    private sealed record RowFixture(string Name, string Status);
}
