using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class QuantScriptPageTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "meridian-wpf-quantscript-page-tests", Guid.NewGuid().ToString("N"));

    private sealed class StubLayoutService : IQuantScriptLayoutService
    {
        public (double ChartHeight, double EditorHeight) LoadRowHeights() => (300, 280);
        public void SaveRowHeights(double chartHeight, double editorHeight) { }
        public int LoadLastActiveTab() => 0;
        public void SaveLastActiveTab(int tabIndex) { }
    }

    [Fact]
    public void Loaded_WhenConstructed_RestoresPersistedRowHeights()
    {
        WpfTestThread.Run(() =>
        {
            Directory.CreateDirectory(_tempDirectory);
            var page = CreatePage();

            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            page.FindName("ChartRow").Should().BeOfType<RowDefinition>().Subject.Height.Value.Should().Be(300);
            page.FindName("EditorRow").Should().BeOfType<RowDefinition>().Subject.Height.Value.Should().Be(280);
        });
    }

    private QuantScriptPage CreatePage()
    {
        var compiler = new Meridian.Wpf.Tests.Support.FakeQuantScriptCompiler();
        var vm = new QuantScriptViewModel(
            new FakeScriptRunner(),
            compiler,
            new NotebookExecutionSession(),
            new QuantScriptNotebookStore(new QuantScriptOptions
            {
                ScriptsDirectory = _tempDirectory,
                NotebookExtension = ".mqnb"
            }),
            new StubLayoutService(),
            Options.Create(new QuantScriptOptions
            {
                ScriptsDirectory = _tempDirectory,
                NotebookExtension = ".mqnb"
            }),
            NullLogger<QuantScriptViewModel>.Instance);

        return new QuantScriptPage(vm);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }
}
