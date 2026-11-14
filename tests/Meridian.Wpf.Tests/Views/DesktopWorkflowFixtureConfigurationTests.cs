using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Tests.Views;

public sealed class DesktopWorkflowFixtureConfigurationTests
{
    [Fact]
    public void FixtureSeed_IsAcceptedByDesktopAndStrictHostConfigurationReaders()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? templatePath = null;
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "dev", "desktop-workflow-fixture-config.json");
            if (File.Exists(candidate))
            {
                templatePath = candidate;
                break;
            }

            current = current.Parent;
        }

        templatePath.Should().NotBeNull("the capture fixture must be tested from its actual runner input");
        var json = File.ReadAllText(templatePath!);
        var desktop = JsonSerializer.Deserialize<AppConfigDto>(json, DesktopJsonOptions.PrettyPrint);
        var host = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);

        desktop.Should().NotBeNull();
        desktop!.DataSource.Should().Be("Synthetic");
        host.Should().NotBeNull();
        host!.DataSource.Should().Be(DataSourceKind.Synthetic);
        host.Symbols.Should().BeEmpty();
        host.Synthetic!.Enabled.Should().BeTrue();
        host.Synthetic.UniverseSymbols.Should().BeEmpty();
        host.Backfill!.Enabled.Should().BeFalse();
        host.Backfill.Provider.Should().Be("synthetic");
        host.Backfill.Symbols.Should().BeEmpty();
    }
}
