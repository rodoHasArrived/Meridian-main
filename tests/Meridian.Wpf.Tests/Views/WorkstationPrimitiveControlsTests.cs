using System.Collections.ObjectModel;
using System.Windows;
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
                    new WorkstationStatePanelControl
                    {
                        State = WorkstationStateModel.Ready("Provider posture ready", "Coverage is available.")
                    },
                    new MetricTileControl
                    {
                        Metric = new WorkstationMetricModel("Connected", "1", "Streaming ready", "\uE73E", WorkspaceTone.Success)
                    },
                    new HealthBadgeControl
                    {
                        Badge = new WorkstationBadgeModel("Health", "Healthy", "\uE946", WorkspaceTone.Success)
                    },
                    new DenseDataGridControl
                    {
                        Table = new WorkstationTableModel<RowFixture>(
                            tableRows,
                            [new("Provider", nameof(RowFixture.Name), 120), new("Status", nameof(RowFixture.Status), 100)])
                    },
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
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed record RowFixture(string Name, string Status);
}
