using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class QuantScriptPageTests
{
    private sealed class StubLayoutService : IQuantScriptLayoutService
    {
        public (double ChartHeight, double EditorHeight) LoadRowHeights() => (300, 400);
        public void SaveRowHeights(double chartHeight, double editorHeight) { }
        public int LoadLastActiveTab() => 0;
        public void SaveLastActiveTab(int tabIndex) { }
    }

    [Fact]
    public void Loaded_WhenConstructed_RestoresPersistedRowHeights()
    {
        WpfTestThread.Run(() =>
        {
            var page = CreatePage();

            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            page.FindName("ChartRow").Should().BeOfType<RowDefinition>().Subject.Height.Value.Should().Be(300);
            page.FindName("EditorRow").Should().BeOfType<RowDefinition>().Subject.Height.Value.Should().Be(400);
        });
    }

    private static QuantScriptPage CreatePage()
    {
        var vm = new QuantScriptViewModel(
            new FakeScriptRunner(),
            new Meridian.Wpf.Tests.Support.FakeQuantScriptCompiler(),
            new PlotQueue(),
            new StubLayoutService(),
            new QuantScriptNotebookStore(new QuantScriptOptions { ScriptsDirectory = Path.GetTempPath() }),
            new QuantScriptTemplateCatalogService(NullLogger<QuantScriptTemplateCatalogService>.Instance),
            new QuantScriptExecutionHistoryService(
                ConfigService.Instance,
                new StrategyRunWorkspaceService(new StrategyRunStore(), new PortfolioReadService(), new LedgerReadService()),
                NullLogger<QuantScriptExecutionHistoryService>.Instance),
            NavigationService.Instance,
            Options.Create(new QuantScriptOptions { ScriptsDirectory = Path.GetTempPath() }),
            NullLogger<QuantScriptViewModel>.Instance);

        return new QuantScriptPage(vm);
    }
}
