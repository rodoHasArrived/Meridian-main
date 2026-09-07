using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using System.Text.Json;

namespace Meridian.Tests.Application.UI;

[Collection("Sequential")]
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _originalCurrentDirectory = Environment.CurrentDirectory;
    private readonly Func<string> _originalPathResolver = ConfigStore.DefaultPathResolver;
    private readonly List<string> _tempDirectories = [];

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task ConnectionMutation_RequiresReadableConfigurationAndPreservesRejectedState(string? body)
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        if (body is not null)
            await File.WriteAllTextAsync(path, body);
        var service = new ProviderConnectionService(new ConfigStore(path));
        Func<Task>[] mutations = [
            () => service.UpsertForTenantAsync(new CreateProviderConnectionRequest("owned", "alpaca", "Owned", ExternalAccountId: "account-a"), "tenant-a", "paper"),
            () => service.DeleteForTenantAsync("owned", "tenant-a")];
        foreach (var mutation in mutations)
        {
            var error = await Record.ExceptionAsync(mutation);
            error.Should().NotBeNull();
            if (body is null)
            {
                error.Should().BeOfType<FileNotFoundException>();
                File.Exists(path).Should().BeFalse();
            }
            else
            {
                error!.GetType().Should().Be(body == "null" ? typeof(InvalidDataException) : typeof(JsonException));
                (await File.ReadAllTextAsync(path)).Should().Be(body);
            }
        }
    }

    [Fact]
    public void DefaultConstructor_UsesAncestorConfigDirectoryWhenPresent()
    {
        var repositoryRoot = CreateTempDirectory();
        var configDirectory = Path.Combine(repositoryRoot, "config");
        Directory.CreateDirectory(configDirectory);

        File.WriteAllText(Path.Combine(configDirectory, "appsettings.json"), "{}");

        var nestedWorkingDirectory = Path.Combine(repositoryRoot, "src", "Meridian.Ui");
        Directory.CreateDirectory(nestedWorkingDirectory);
        Environment.CurrentDirectory = nestedWorkingDirectory;
        var expectedPath = Path.Combine(
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "..")),
            "config",
            "appsettings.json");

        var store = new ConfigStore();

        store.ConfigPath.Should().Be(expectedPath);
    }

    [Fact]
    public void LoadConfig_UsesConfigRelativeDefaultDataRootWhenFileIsMissing()
    {
        var repositoryRoot = CreateTempDirectory();
        var configDirectory = Path.Combine(repositoryRoot, "config");
        Directory.CreateDirectory(configDirectory);
        var missingConfigPath = Path.Combine(configDirectory, "appsettings.json");

        var config = ConfigStore.LoadConfig(missingConfigPath);

        config.DataRoot.Should().Be(Path.Combine(repositoryRoot, "data"));
    }

    [Fact]
    public void LoadConfig_MigratesLegacyStorageBaseDirectoryToResolvedDataRoot()
    {
        var repositoryRoot = CreateTempDirectory();
        var configDirectory = Path.Combine(repositoryRoot, "config");
        Directory.CreateDirectory(configDirectory);

        var configPath = Path.Combine(configDirectory, "appsettings.json");
        File.WriteAllText(configPath, """
            {
              "storage": {
                "baseDirectory": "archive-data"
              }
            }
            """);

        var config = ConfigStore.LoadConfig(configPath);

        config.DataRoot.Should().Be(Path.Combine(repositoryRoot, "archive-data"));
    }

    public void Dispose()
    {
        ConfigStore.DefaultPathResolver = _originalPathResolver;
        Environment.CurrentDirectory = _originalCurrentDirectory;

        foreach (var path in _tempDirectories.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-config-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
