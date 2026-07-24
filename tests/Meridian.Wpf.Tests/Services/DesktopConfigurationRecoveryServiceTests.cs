using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class DesktopConfigurationRecoveryServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-config-recovery-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureReadableConfiguration_WhenMissing_CreatesConfigAndLastKnownGood()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        var service = new DesktopConfigurationRecoveryService(configPath);

        var result = service.EnsureReadableConfiguration();

        result.Outcome.Should().Be(ConfigurationProvisioningResult.CreatedDefault);
        File.Exists(configPath).Should().BeTrue();
        File.Exists(service.LastKnownGoodPath).Should().BeTrue();
        ReadConfiguration(configPath).Should().NotBeNull();
    }

    [Fact]
    public void EnsureReadableConfiguration_WhenCurrentFileIsCorrupt_RestoresLastKnownGoodAndRetainsEvidence()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        var timestamp = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var service = new DesktopConfigurationRecoveryService(configPath, () => timestamp);
        var expected = AppConfigDefaults.CreateDefaultAppConfig();
        expected.DataSource = "RecoveredProvider";
        var validJson = JsonSerializer.Serialize(expected, DesktopJsonOptions.PrettyPrint);
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(service.LastKnownGoodPath, validJson);
        File.WriteAllText(configPath, "{ not-json");

        var result = service.EnsureReadableConfiguration();

        result.Outcome.Should().Be(ConfigurationProvisioningResult.RestoredLastKnownGood);
        ReadConfiguration(configPath).DataSource.Should().Be("RecoveredProvider");
        result.InvalidConfigurationPath.Should().NotBeNull();
        File.ReadAllText(result.InvalidConfigurationPath!).Should().Be("{ not-json");
        result.RecoveryReceiptPath.Should().NotBeNull();
        File.Exists(result.RecoveryReceiptPath!).Should().BeTrue();
        result.RetainedArtifacts.Should().HaveCount(2);
    }

    [Fact]
    public void EnsureReadableConfiguration_WhenNoValidRecoveryPoint_RestoresDefaultsAndRetainsInvalidFile()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(configPath, string.Empty);
        var service = new DesktopConfigurationRecoveryService(configPath);

        var result = service.EnsureReadableConfiguration();

        result.Outcome.Should().Be(ConfigurationProvisioningResult.RepairedInvalid);
        ReadConfiguration(configPath).DataSource.Should().Be("NoOp");
        File.Exists(result.InvalidConfigurationPath!).Should().BeTrue();
        File.Exists(result.RecoveryReceiptPath!).Should().BeTrue();
    }

    [Fact]
    public async Task PersistValidConfigurationAsync_IsRejectedBeforeReplacingCurrentFile()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        var service = new DesktopConfigurationRecoveryService(configPath);
        service.EnsureReadableConfiguration();
        var original = File.ReadAllText(configPath);

        var act = () => service.PersistValidConfigurationAsync("[]");

        await act.Should().ThrowAsync<JsonException>();
        File.ReadAllText(configPath).Should().Be(original);
    }

    private static AppConfigDto ReadConfiguration(string path)
        => JsonSerializer.Deserialize<AppConfigDto>(File.ReadAllText(path), DesktopJsonOptions.PrettyPrint)!;

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
