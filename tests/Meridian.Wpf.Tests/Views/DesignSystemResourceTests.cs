using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Markup;
using System.Xml.Linq;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class DesignSystemResourceTests
{
    [Theory]
    [InlineData("MeridianBodyFontFamily", "IBMPlexSans")]
    [InlineData("MeridianDisplayFontFamily", "IBMPlexSans")]
    [InlineData("MeridianDataFontFamily", "IBMPlexMono")]
    public void EmbeddedFamilies_ShouldResolveEveryAuthoredFaceWithoutInstalledFonts(string resourceKey, string filePrefix)
    {
        WpfTestThread.Run(() =>
        {
            var family = (FontFamily)System.Windows.Application.Current.FindResource(resourceKey);
            var faces = new[]
            {
                (FontStyles.Normal, FontWeights.Normal, "Regular"),
                (FontStyles.Italic, FontWeights.Normal, "Italic"),
                (FontStyles.Normal, FontWeights.Medium, "Medium"),
                (FontStyles.Normal, FontWeights.SemiBold, "SemiBold"),
                (FontStyles.Normal, FontWeights.Bold, "Bold")
            };

            foreach (var (style, weight, name) in faces)
            {
                var typeface = new Typeface(family, style, weight, FontStretches.Normal);
                typeface.TryGetGlyphTypeface(out var glyphs).Should().BeTrue($"{resourceKey} {name} must be embedded");
                glyphs!.FontUri.OriginalString.ToLowerInvariant().Should().EndWith($"{filePrefix}-{name}.ttf".ToLowerInvariant());
                glyphs.Weight.Should().Be(weight);
                glyphs.Style.Should().Be(style);
                glyphs.CharacterToGlyphMap.Should().ContainKeys('0', '9', '$', '\u2212');
            }
        });
    }

    [Fact]
    public void FontResourcesAndLicenses_ShouldMatchTheCanonicalExport()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var family in new[] { "Mono", "Sans" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Medium", "SemiBold", "Bold" })
                {
                    AssertEmbeddedCopyMatchesExport($"IBMPlex{family}-{face}.ttf");
                }

                AssertEmbeddedCopyMatchesExport($"OFL-IBMPlex{family}.txt");
            }
        });
    }

    [Fact]
    public void IndependentPage_ShouldSupplyBodyInheritanceAndPreserveDataAndSymbolFamilies()
    {
        WpfTestThread.Run(() =>
        {
            var body = (FontFamily)System.Windows.Application.Current.FindResource("MeridianBodyFontFamily");
            var data = (FontFamily)System.Windows.Application.Current.FindResource("MeridianDataFontFamily");
            var page = new KeyboardShortcutsPage();
            page.FontFamily.Should().Be(body);
            TextElement.GetFontFamily((DependencyObject)page.Content).Should().Be(body);

            var amount = new TextBlock
            {
                Text = "1,234.56",
                Style = (Style)System.Windows.Application.Current.FindResource("DataValueStyle")
            };
            var legacyMono = new TextBlock
            {
                Text = "1,234.56",
                Style = (Style)System.Windows.Application.Current.FindResource("MonoTextStyle")
            };
            amount.FontFamily.Should().Be(data);
            legacyMono.FontFamily.Should().Be(data);
            ((FontFamily)System.Windows.Application.Current.FindResource("SymbolThemeFontFamily")).Source.Should().Be("Segoe MDL2 Assets");
        });
    }

    [Fact]
    public void PrimaryControlAndEnvironmentRoles_ShouldKeepActionSelectionAndSimulationDistinct()
    {
        WpfTestThread.Run(() =>
        {
            var button = new Button
            {
                Content = "Review",
                Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButtonStyle")
            };
            button.ApplyTemplate();
            var surface = (Border)button.Template.FindName("border", button);
            ((SolidColorBrush)surface.Background).Color.Should().Be(Color.FromRgb(0x29, 0x32, 0x3A));
            ((SolidColorBrush)button.Foreground).Color.Should().Be(Color.FromRgb(0xF5, 0xF3, 0xEE));
            BrushColor("FocusVisualPrimaryBrush").Should().Be(Color.FromRgb(0x2D, 0x6F, 0xB3));
            BrushColor("NavActiveBackgroundBrush").Should().Be(Color.FromRgb(0xE7, 0xED, 0xF2));
            BrushColor("SidebarActiveIndicatorBrush").Should().Be(Color.FromRgb(0x49, 0x6C, 0x8F));
            BrushColor("ModePaperBrush").Should().Be(Color.FromRgb(0x5D, 0x72, 0x86));
            BrushColor("ModeLiveBrush").Should().Be(BrushColor("ErrorColorBrush"));
            BrushColor("ModeFixtureBrush").Should().Be(BrushColor("WarningColorBrush"));
            BrushColor("ChartPrimaryLineBrush").Should().Be(Color.FromRgb(0x29, 0x32, 0x3A));
        });
    }

    [Fact]
    public void QuickstartAction_ShouldKeepReadableTextAcrossPaperHoverAndPressedSurfaces()
    {
        WpfTestThread.Run(() =>
        {
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = XDocument.Load(RunMatUiAutomationFacade.GetRepoFilePath("src/Meridian.Wpf/Views/ScatterAnalysisPage.xaml"));
            var authoredStyle = source.Descendants(presentation + "Style")
                .Single(element => (string?)element.Attribute(xaml + "Key") == "QuickstartItemStyle");
            var dictionarySource = new XElement(presentation + "ResourceDictionary",
                new XAttribute(XNamespace.Xmlns + "x", xaml), new XElement(authoredStyle));
            var resources = (ResourceDictionary)XamlReader.Parse(dictionarySource.ToString());
            var button = new Button { Style = (Style)resources["QuickstartItemStyle"] };
            var labelTemplate = source.Descendants(presentation + "TextBlock")
                .Single(element => (string?)element.Attribute("Text") == "{Binding Name}");
            var label = (TextBlock)XamlReader.Parse(labelTemplate.ToString());
            button.Content = label;
            button.ApplyTemplate();
            button.Measure(new Size(220, 50));
            button.Arrange(new Rect(0, 0, 220, 50));
            button.UpdateLayout();
            var foreground = ((SolidColorBrush)label.Foreground).Color;
            foreground.Should().Be(BrushColor("ConsoleTextBrush"));

            var hover = button.Template.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == Button.IsMouseOverProperty);
            var pressed = button.Template.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == Button.IsPressedProperty);
            var hoverColor = ((SolidColorBrush)hover.Setters.OfType<Setter>().Single().Value).Color;
            var pressedColor = ((SolidColorBrush)pressed.Setters.OfType<Setter>().Single().Value).Color;
            hoverColor.Should().Be(BrushColor("ConsoleBackgroundHoverBrush"));
            pressedColor.Should().Be(BrushColor("ConsoleBackgroundActiveBrush"));
            foreach (var surface in new[] { BrushColor("ChartSurfaceBrush"), hoverColor, pressedColor })
            {
                ContrastRatio(foreground, surface).Should().BeGreaterThanOrEqualTo(4.5);
            }
        });
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color)
            => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static Color BrushColor(string key)
        => ((SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;

    private static void AssertEmbeddedCopyMatchesExport(string filename)
    {
        var expected = File.ReadAllBytes(RunMatUiAutomationFacade.GetRepoFilePath($"Meridian Design System/assets/fonts/{filename}"));
        var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Meridian.Desktop;component/Assets/Fonts/{filename}"));
        resource.Should().NotBeNull($"{filename} must be embedded for offline desktop use");
        using var stream = resource!.Stream;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        bytes.ToArray().Should().Equal(expected);
    }
}
