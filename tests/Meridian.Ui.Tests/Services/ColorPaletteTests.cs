using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class ColorPaletteTests
{
    [Theory]
    [InlineData(nameof(ColorPalette.Success), "ConsoleAccentGreen")]
    [InlineData(nameof(ColorPalette.Warning), "ConsoleAccentOrange")]
    [InlineData(nameof(ColorPalette.Error), "ConsoleAccentRed")]
    [InlineData(nameof(ColorPalette.Info), "ConsoleAccentBlue")]
    [InlineData(nameof(ColorPalette.Inactive), "ConsoleTextDisabled")]
    [InlineData(nameof(ColorPalette.ChartPrimary), "ChartPrimaryLineColor")]
    [InlineData(nameof(ColorPalette.ChartSecondary), "ChartSecondaryLineColor")]
    [InlineData(nameof(ColorPalette.ChartTertiary), "ChartWarningLineColor")]
    [InlineData(nameof(ColorPalette.ChartPositive), "ChartEquityColor")]
    [InlineData(nameof(ColorPalette.ChartNegative), "ChartDrawdownColor")]
    [InlineData(nameof(ColorPalette.ChartBackground), "ChartSurfaceColor")]
    [InlineData(nameof(ColorPalette.ChartDataBackground), "ChartPlotAreaColor")]
    [InlineData(nameof(ColorPalette.ChartGrid), "ChartGridLineColor")]
    [InlineData(nameof(ColorPalette.ChartAxis), "ChartAxisLabelColor")]
    [InlineData(nameof(ColorPalette.ChartBorder), "ChartBorderColor")]
    [InlineData(nameof(ColorPalette.LightText), "TopBarText")]
    [InlineData(nameof(ColorPalette.SuccessBackground), "ConsoleAccentGreenAlpha20")]
    [InlineData(nameof(ColorPalette.WarningBackground), "ConsoleAccentOrangeAlpha20")]
    [InlineData(nameof(ColorPalette.ErrorBackground), "ConsoleAccentRedAlpha20")]
    [InlineData(nameof(ColorPalette.InfoBackground), "ConsoleAccentBlueAlpha20")]
    public void RuntimeColors_ShouldMatchTheWpfTheme(string paletteField, string xamlKey)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the source-contract test runs from the repository");
        var tokens = XDocument.Load(Path.Combine(directory!.FullName, "src/Meridian.Wpf/Styles/ThemeTokens.xaml"));
        var encoded = tokens.Root!.Elements().Single(element => (string?)element.Attribute(xaml + "Key") == xamlKey).Value.TrimStart('#');
        var argb = Convert.ToUInt32(encoded, 16);
        var expected = new ColorPalette.ArgbColor(encoded.Length == 6 ? (byte)255 : (byte)(argb >> 24),
            (byte)((argb >> 16) & 255), (byte)((argb >> 8) & 255), (byte)(argb & 255));
        var actual = (ColorPalette.ArgbColor)typeof(ColorPalette).GetField(paletteField, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        actual.Should().Be(expected);
    }

    [Fact]
    public void StatusMappings_ShouldRetainFinancialAndOperationalMeanings()
    {
        ColorPalette.GetNotificationColor(NotificationType.Success).Should().Be(ColorPalette.Success);
        ColorPalette.GetNotificationColor(NotificationType.Warning).Should().Be(ColorPalette.Warning);
        ColorPalette.GetNotificationColor(NotificationType.Error).Should().Be(ColorPalette.Error);
        ColorPalette.GetNotificationColor((NotificationType)byte.MaxValue).Should().Be(ColorPalette.Info);
        ColorPalette.GetSeverityColor(IntegritySeverity.Critical).Should().Be(ColorPalette.Error);
        ColorPalette.GetSeverityColor(IntegritySeverity.Warning).Should().Be(ColorPalette.Warning);
        ColorPalette.GetStreamStatusColor(true, true, false).Should().Be(ColorPalette.Success);
        ColorPalette.GetStreamStatusColor(true, true, true).Should().Be(ColorPalette.Warning);
        ColorPalette.GetStreamStatusColor(true, false, false).Should().Be(ColorPalette.Inactive);
        ColorPalette.GetStreamStatusColor(false, true, false).Should().Be(ColorPalette.Inactive);
        ColorPalette.ChartPositive.Should().Be(ColorPalette.Success);
        ColorPalette.ChartNegative.Should().Be(ColorPalette.Error);
        ColorPalette.ChartPrimary.Should().NotBe(ColorPalette.Info);
    }

    [Theory]
    [InlineData(0, "Error")]
    [InlineData(49.9, "Error")]
    [InlineData(50, "Warning")]
    [InlineData(69.9, "Warning")]
    [InlineData(70, "Info")]
    [InlineData(89.9, "Info")]
    [InlineData(90, "Success")]
    [InlineData(100, "Success")]
    public void ProgressMapping_ShouldPreserveThresholds(double percentage, string paletteField)
        => ColorPalette.GetProgressColor(percentage).Should().Be((ColorPalette.ArgbColor)typeof(ColorPalette).GetField(paletteField)!.GetValue(null)!);

    [Theory]
    [InlineData(19, "Success")]
    [InlineData(20, "Warning")]
    [InlineData(49, "Warning")]
    [InlineData(50, "Error")]
    public void LatencyMapping_ShouldPreserveThresholds(int milliseconds, string paletteField)
        => ColorPalette.GetLatencyColor(milliseconds).Should().Be((ColorPalette.ArgbColor)typeof(ColorPalette).GetField(paletteField)!.GetValue(null)!);
}
