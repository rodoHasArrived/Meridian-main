using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Services;
using Meridian.Core.Config;

namespace Meridian.Tests.Application.Services;

public sealed class QuickSetupAtomicSaveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-quick-setup-{Guid.NewGuid():N}");

    [Fact]
    public void WriteConfiguration_ReplacesFileWithoutChangingAnOpenSnapshotOrSerialization()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, "original");
        using var snapshot = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete));
        var config = new AppConfig { DataSource = DataSourceKind.Alpaca };

        ConfigurationWizard.WriteConfiguration(config, path);

        snapshot.ReadToEnd().Should().Be("original");
        var expected = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        });
        File.ReadAllText(path).Should().Be(expected);
        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void WriteConfiguration_CancelledPublicationPreservesExistingConfiguration()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, "original");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Action save = () => ConfigurationWizard.WriteConfiguration(new AppConfig(), path, cts.Token);

        save.Should().Throw<OperationCanceledException>();
        File.ReadAllText(path).Should().Be("original");
        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
