using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.Config;
using Meridian.Application.Monitoring.DataQuality;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class PipelineFeatureRegistrationTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task Register_UsesConfiguredDataRootForQualityReportOutput()
    {
        var root = CreateTempDirectory();
        var dataRoot = Path.Combine(root, "persistent-data");
        var configPath = WriteConfig(root, new AppConfig(DataRoot: "persistent-data"));

        var services = new ServiceCollection();
        services.AddLogging();

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var monitoring = provider.GetRequiredService<DataQualityMonitoringService>();

        var outputDirectory = GetReportOutputDirectory(monitoring.ReportGenerator);
        outputDirectory.Should().Be(Path.Combine(dataRoot, "reports"));
        Directory.Exists(outputDirectory).Should().BeTrue();
    }

    private static string GetReportOutputDirectory(DataQualityReportGenerator generator)
    {
        var field = typeof(DataQualityReportGenerator).GetField("_outputDirectory", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(generator).Should().BeOfType<string>().Subject;
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-pipeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private string WriteConfig(string root, AppConfig config)
    {
        var path = Path.Combine(root, "appsettings.json");
        var json = JsonSerializer.Serialize(config, AppConfigJsonOptions.Write);
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
