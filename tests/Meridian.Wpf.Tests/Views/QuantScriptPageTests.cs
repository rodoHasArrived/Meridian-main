using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class QuantScriptPageTests
{
    private sealed class StubLayoutService : IQuantScriptLayoutService
    {
        public (double LeftWidth, double RightWidth) LoadColumnWidths() => (300, 400);
        public void SaveColumnWidths(double leftWidth, double rightWidth) { }
        public int LoadLastActiveTab() => 0;
        public void SaveLastActiveTab(int tabIndex) { }
    }

    [Fact]
    public void Loaded_WhenConstructed_RestoresPersistedColumnWidths()
    {
        WpfTestThread.Run(() =>
        {
            var page = CreatePage();

            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            page.FindName("LeftColumn").Should().BeOfType<ColumnDefinition>().Subject.Width.Value.Should().Be(300);
            page.FindName("RightColumn").Should().BeOfType<ColumnDefinition>().Subject.Width.Value.Should().Be(400);
        });
    }

    private static QuantScriptPage CreatePage()
    {
        var vm = new QuantScriptViewModel(
            new FakeScriptRunner(),
            new Meridian.Wpf.Tests.Support.FakeQuantScriptCompiler(),
            new PlotQueue(),
            new StubLayoutService(),
            Options.Create(new QuantScriptOptions { ScriptsDirectory = Path.GetTempPath() }),
            NullLogger<QuantScriptViewModel>.Instance);

        return new QuantScriptPage
        {
            DataContext = vm
        };
    }
}
