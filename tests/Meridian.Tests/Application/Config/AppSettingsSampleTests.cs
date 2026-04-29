using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class AppSettingsSampleTests
{
    [Fact]
    public void AppSettingsSample_LoadsThroughConfigurationBuilder()
    {
        var repositoryRoot = FindRepositoryRoot();
        var samplePath = Path.Combine(repositoryRoot, "config", "appsettings.sample.json");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(samplePath, optional: false, reloadOnChange: false)
            .Build();

        configuration["DataSource"].Should().Be("Synthetic");
        configuration["Backfill:Providers:Robinhood:Enabled"].Should().Be("False");
    }

    private static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
