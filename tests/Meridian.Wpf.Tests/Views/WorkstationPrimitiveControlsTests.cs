using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Workstation.Controls;
using Meridian.Wpf.Workstation.Models;
using TableActivityLogGridControl = Meridian.Wpf.Workstation.Tables.ActivityLogGridControl;
using TableDenseDataGridControl = Meridian.Wpf.Workstation.Tables.DenseDataGridControl;
using TableKeyValueFactGridControl = Meridian.Wpf.Workstation.Tables.KeyValueFactGridControl;

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
    public void WorkstationTableInspectorControl_ShouldRenderTableInspectorContractAndSingleSelection()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var tableRows = new ObservableCollection<RowFixture>
            {
                new("Polygon.io", "Healthy"),
                new("Alpaca", "Degraded")
            };
            var selectedRows = new ObservableCollection<object>();
            var control = new WorkstationTableInspectorControl
            {
                Table = new WorkstationTableModel<RowFixture>(
                    tableRows,
                    [new("Provider", nameof(RowFixture.Name), 120), new("Status", nameof(RowFixture.Status), 100)],
                    "Provider readiness table"),
                Inspector = new InspectorPanelModel
                {
                    Title = "Provider details",
                    Facts = [new KeyValueFactModel("Credential", "Configured")]
                },
                SelectedItems = selectedRows,
                HeaderText = "Provider Management",
                DetailText = "Select a provider row to inspect readiness.",
                ScopeText = "2 providers",
                TableHeaderContent = new TextBlock { Text = "Filtered providers" },
                EmptyContent = new TextBlock { Text = "No filtered providers" },
                InspectorHeaderContent = new TextBlock { Text = "Readiness summary" },
                GridAutomationId = "TableInspectorGrid",
                EmptyAutomationId = "TableInspectorEmpty",
                InspectorAutomationId = "TableInspectorRail",
                ControlAutomationId = "TableInspectorHost"
            };

            var window = Show(control);
            try
            {
                AutomationProperties.GetAutomationId(control).Should().Be("TableInspectorHost");
                AutomationProperties.GetName(control).Should().Be("Provider Management");

                var denseGrid = control.FindName("TableGrid").Should().BeOfType<DenseDataGridControl>().Subject;
                var rowsList = denseGrid.FindName("RowsList").Should().BeOfType<ListView>().Subject;
                rowsList.SelectionMode.Should().Be(SelectionMode.Single);
                VirtualizingPanel.GetIsVirtualizing(rowsList).Should().BeTrue();
                VirtualizingPanel.GetVirtualizationMode(rowsList).Should().Be(VirtualizationMode.Recycling);
                ScrollViewer.GetCanContentScroll(rowsList).Should().BeTrue();
                AutomationProperties.GetAutomationId(rowsList).Should().Be("TableInspectorGrid");
                denseGrid.EmptyContent.Should().BeAssignableTo<TextBlock>();

                var emptyPanel = denseGrid.FindName("EmptyPanel").Should().BeOfType<Border>().Subject;
                AutomationProperties.GetAutomationId(emptyPanel).Should().Be("TableInspectorEmpty");

                var tableHeader = control.FindName("TableHeaderContentPresenter").Should().BeOfType<ContentPresenter>().Subject;
                tableHeader.Content.Should().BeAssignableTo<TextBlock>();
                var inspectorHeader = control.FindName("InspectorHeaderContentPresenter").Should().BeOfType<ContentPresenter>().Subject;
                inspectorHeader.Content.Should().BeAssignableTo<TextBlock>();

                var inspector = control.FindName("DefaultInspector").Should().BeOfType<InspectorPanelControl>().Subject;
                AutomationProperties.GetAutomationId(inspector).Should().Be("TableInspectorRail");

                rowsList.SelectedItem = tableRows[1];

                control.SelectedItem.Should().BeSameAs(tableRows[1]);
                selectedRows.Should().ContainSingle().Which.Should().BeSameAs(tableRows[1]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DenseDataGridControl_ShouldMirrorExtendedSelectionIntoSelectedItems()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var tableRows = new ObservableCollection<RowFixture>
            {
                new("Polygon.io", "Healthy"),
                new("Alpaca", "Degraded"),
                new("IEX", "Healthy")
            };
            var selectedRows = new ObservableCollection<object>();
            var denseGrid = new DenseDataGridControl
            {
                Table = new WorkstationTableModel<RowFixture>(
                    tableRows,
                    [new("Provider", nameof(RowFixture.Name), 120), new("Status", nameof(RowFixture.Status), 100)],
                    "Provider readiness table"),
                SelectionMode = SelectionMode.Extended,
                SelectedItems = selectedRows
            };

            var window = Show(denseGrid);
            try
            {
                var rowsList = denseGrid.FindName("RowsList").Should().BeOfType<ListView>().Subject;
                rowsList.SelectionMode.Should().Be(SelectionMode.Extended);

                rowsList.SelectedItems.Add(tableRows[0]);
                rowsList.SelectedItems.Add(tableRows[2]);

                selectedRows.Should().ContainInOrder(tableRows[0], tableRows[2]);
                denseGrid.SelectedItem.Should().BeSameAs(tableRows[0]);
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
        xaml.Should().Contain("SelectionMode=\"{Binding ElementName=Root, Path=SelectionMode}\"");
        xaml.Should().Contain("EmptyContentPresenter");
        xaml.Should().Contain("VirtualizingPanel.VirtualizationMode=\"Recycling\"");
        xaml.Should().Contain("VirtualizingPanel.ScrollUnit=\"Item\"");
    }

    [Fact]
    public void LegacyDataGridWrappers_ShouldDefaultToVirtualizedReadOnlyTables()
    {
        WpfTestThread.Run(() =>
        {
            var grids = new DataGrid[]
            {
                new TableDenseDataGridControl(),
                new TableKeyValueFactGridControl(),
                new TableActivityLogGridControl()
            };

            foreach (var grid in grids)
            {
                grid.IsReadOnly.Should().BeTrue();
                grid.EnableRowVirtualization.Should().BeTrue();
                grid.EnableColumnVirtualization.Should().BeTrue();
                ScrollViewer.GetCanContentScroll(grid).Should().BeTrue();
                VirtualizingPanel.GetIsVirtualizing(grid).Should().BeTrue();
                VirtualizingPanel.GetVirtualizationMode(grid).Should().Be(VirtualizationMode.Recycling);
            }

            grids[0].AutoGenerateColumns.Should().BeFalse();
        });
    }

    [Fact]
    public void WorkstationTableInspectorSource_ShouldComposeSharedTableAndInspectorChrome()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Workstation\Controls\WorkstationTableInspectorControl.xaml"));

        xaml.Should().Contain("WorkstationTablePanelStyle");
        xaml.Should().Contain("WorkstationPanelHeaderStyle");
        xaml.Should().Contain("WorkstationInspectorRailStyle");
        xaml.Should().Contain("DenseDataGridControl");
        xaml.Should().Contain("InspectorPanelControl");
        xaml.Should().Contain("TableHeaderContent");
        xaml.Should().Contain("EmptyContent=\"{Binding ElementName=Root, Path=EmptyContent}\"");
        xaml.Should().Contain("InspectorHeaderContent");
        xaml.Should().Contain("SelectedItems=\"{Binding ElementName=Root, Path=SelectedItems}\"");
    }

    [Fact]
    public void ThemeSurfacesSource_ShouldExposeProfessionalWorkstationPanelChrome()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Styles\ThemeSurfaces.xaml"));

        xaml.Should().Contain("WorkstationTablePanelStyle");
        xaml.Should().Contain("WorkstationPanelHeaderStyle");
        xaml.Should().Contain("WorkstationPanelBodyStyle");
        xaml.Should().Contain("WorkstationInspectorRailStyle");
        xaml.Should().Contain("WorkstationDockStripStyle");
        xaml.Should().Contain("<Setter Property=\"CornerRadius\" Value=\"4\" />");
        xaml.Should().Contain("<Setter Property=\"BorderThickness\" Value=\"0,1,0,0\" />");
    }

    [Fact]
    public void DataConfidenceIndicator_CommandParameterDefaultsToTheModelsExplanationRoute()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var indicator = new Meridian.Wpf.Controls.DataConfidenceIndicator
            {
                Model = DataConfidenceIndicatorModel.Unknown() with { ExplanationRoute = "meridian://providers/polygon" }
            };
            var window = Show(indicator);
            try
            {
                // The model's advertised click-through route must reach the command without
                // the caller redundantly copying it into ExplanationCommandParameter.
                indicator.EffectiveExplanationCommandParameter.Should().Be("meridian://providers/polygon");
                var button = indicator.FindName("ExplanationButton").Should().BeOfType<Button>().Which;
                button.CommandParameter.Should().Be("meridian://providers/polygon");

                indicator.ExplanationCommandParameter = "explicit-parameter";
                indicator.EffectiveExplanationCommandParameter.Should().Be(
                    "explicit-parameter", "an explicit parameter overrides the model's route");

                indicator.ExplanationCommandParameter = null;
                indicator.EffectiveExplanationCommandParameter.Should().Be(
                    "meridian://providers/polygon", "clearing the explicit parameter restores the route default");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window Show(FrameworkElement element)
    {
        var window = new Window
        {
            Width = 1000,
            Height = 760,
            Content = element
        };

        window.Show();
        window.UpdateLayout();
        element.UpdateLayout();
        return window;
    }

    private sealed record RowFixture(string Name, string Status);
}
