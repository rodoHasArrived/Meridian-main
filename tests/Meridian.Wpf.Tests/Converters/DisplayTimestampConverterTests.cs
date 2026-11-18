using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using Meridian.Wpf.Converters;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Converters;

public sealed class DisplayTimestampConverterTests
{
    [Fact]
    public void Convert_FormatsInstantsAndPassesOtherValuesThrough()
    {
        var converter = new DisplayTimestampConverter();
        var instant = new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

        // The display zone is UTC unless a test sets it, so the label is the plain UTC form.
        converter.Convert(instant, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("2026-07-05 12:00 UTC");
        converter.Convert(instant.UtcDateTime, typeof(string), "HH:mm", CultureInfo.InvariantCulture).Should().Be("12:00 UTC");
        converter.Convert("n/a", typeof(string), null, CultureInfo.InvariantCulture).Should().Be("n/a");
        converter.ConvertBack("x", typeof(DateTimeOffset), null, CultureInfo.InvariantCulture).Should().Be(DependencyProperty.UnsetValue);
    }

    /// <summary>
    /// Pages are also loaded without App.xaml (tests, hosted previews), so each page that uses the
    /// converter declares it in its own resources, like the other page converters.
    /// </summary>
    [Fact]
    public void EveryXamlThatUsesTheConverter_DeclaresItLocally()
    {
        var viewsRoot = RunMatUiAutomationFacade.GetRepoFilePath(Path.Combine("src", "Meridian.Wpf", "Views"));
        var users = Directory.GetFiles(viewsRoot, "*.xaml", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => file.Text.Contains("StaticResource DisplayTimestampConverter", StringComparison.Ordinal))
            .ToList();

        users.Should().NotBeEmpty();
        foreach (var (path, text) in users)
        {
            Regex.IsMatch(text, "<converters:DisplayTimestampConverter\\s+x:Key=\"DisplayTimestampConverter\"")
                .Should().BeTrue($"{Path.GetFileName(path)} uses DisplayTimestampConverter and must declare it in its resources");
        }
    }
}
