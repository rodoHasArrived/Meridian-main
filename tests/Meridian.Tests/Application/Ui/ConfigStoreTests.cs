using Moq;
using Meridian.ProviderSdk;
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

    [Fact]
    public async Task ConcurrentConnectionMutations_PreserveEveryOwnedConnectionAcrossStoreInstances()
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(async () =>
        {
            var service = new ProviderConnectionService(new ConfigStore(path));
            await service.UpsertForTenantAsync(new CreateProviderConnectionRequest(
                $"owned-{index}", "alpaca", $"Owned {index}", ExternalAccountId: $"account-{index}"), "tenant-a", "paper");
        })));
        var rows = await new ProviderConnectionService(new ConfigStore(path)).GetConnectionsForTenantAsync("tenant-a");
        rows.Select(row => row.ConnectionId).Should().BeEquivalentTo(Enumerable.Range(0, 24).Select(index => $"owned-{index}"));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            new ProviderConnectionService(new ConfigStore(path)).DeleteForTenantAsync($"owned-{index}", "tenant-a"))));
        rows = await new ProviderConnectionService(new ConfigStore(path)).GetConnectionsForTenantAsync("tenant-a");
        rows.Select(row => row.ConnectionId).Should().BeEquivalentTo(Enumerable.Range(12, 12).Select(index => $"owned-{index}"));
    }

    [Fact]
    public async Task ConcurrentBindingAndConnectionMutations_PreserveBothCollections()
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(async () =>
        {
            var store = new ConfigStore(path);
            await new ProviderConnectionService(store).UpsertForTenantAsync(new CreateProviderConnectionRequest(
                $"owned-{index}", "alpaca", $"Owned {index}", ExternalAccountId: $"account-{index}"), "tenant-a", "paper");
            await new ProviderBindingService(store).UpsertAsync(new UpdateProviderBindingRequest(
                $"binding-{index}", "RealtimeMarketData", $"owned-{index}"));
        })));
        var service = new ProviderConnectionService(new ConfigStore(path));
        (await service.GetConnectionsForTenantAsync("tenant-a")).Should().HaveCount(24);
        var bindings = new ProviderBindingService(new ConfigStore(path));
        (await bindings.GetBindingsAsync()).Select(row => row.BindingId).Should()
            .BeEquivalentTo(Enumerable.Range(0, 24).Select(index => $"binding-{index}"));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            new ProviderBindingService(new ConfigStore(path)).DeleteAsync($"binding-{index}"))));
        (await bindings.GetBindingsAsync()).Select(row => row.BindingId).Should()
            .BeEquivalentTo(Enumerable.Range(12, 12).Select(index => $"binding-{index}"));
        (await service.GetConnectionsForTenantAsync("tenant-a")).Should().HaveCount(24);
    }

    [Fact]
    public async Task ConcurrentPresetAndConnectionMutations_PreserveOwnershipAndSelectedPreset()
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        var presetId = (await new ProviderPresetService(new ConfigStore(path)).GetPresetsAsync()).First().PresetId;
        await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(async () =>
        {
            var store = new ConfigStore(path);
            await new ProviderConnectionService(store).UpsertForTenantAsync(new CreateProviderConnectionRequest(
                $"owned-{index}", "alpaca", $"Owned {index}", ExternalAccountId: $"account-{index}"), "tenant-a", "paper");
            (await new ProviderPresetService(store).ApplyAsync(presetId)).Should().NotBeNull();
        })));
        (await new ProviderConnectionService(new ConfigStore(path)).GetConnectionsForTenantAsync("tenant-a"))
            .Select(row => row.ConnectionId).Should().BeEquivalentTo(Enumerable.Range(0, 24).Select(index => $"owned-{index}"));
        (await new ProviderPresetService(new ConfigStore(path)).GetPresetsAsync())
            .Where(preset => preset.IsEnabled).Should().ContainSingle(preset => preset.PresetId == presetId);
        var before = await File.ReadAllTextAsync(path);
        (await new ProviderPresetService(new ConfigStore(path)).ApplyAsync("missing-preset")).Should().BeNull();
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task BindingAndPresetMutations_RefuseUnreadableConfiguration(string body)
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        await File.WriteAllTextAsync(path, body);
        var store = new ConfigStore(path);
        Func<Task>[] mutations = [
            () => new ProviderBindingService(store).UpsertAsync(new UpdateProviderBindingRequest("binding", "RealtimeMarketData", "owned")),
            () => new ProviderBindingService(store).DeleteAsync("binding"),
            () => new ProviderPresetService(store).ApplyAsync("preset")];
        foreach (var mutation in mutations)
        {
            var error = await Record.ExceptionAsync(mutation);
            error.Should().NotBeNull();
            error!.GetType().Should().Be(body == "null" ? typeof(InvalidDataException) : typeof(JsonException));
            (await File.ReadAllTextAsync(path)).Should().Be(body);
        }
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("change")]
    [InlineData("unrelated")]
    [InlineData("wrong-result")]
    public async Task CertificationCommit_RevalidatesConnectionAndPreservesConcurrentChanges(string action)
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        var store = new ConfigStore(path);
        var connections = new ProviderConnectionService(store);
        var request = new CreateProviderConnectionRequest("owned", "alpaca", "Original", ExternalAccountId: "account-a");
        await connections.UpsertForTenantAsync(request, "tenant-a", "paper");
        var adapter = Mock.Of<IProviderFamilyAdapter>();
        var catalog = new Mock<IProviderFamilyCatalogService>();
        catalog.Setup(value => value.GetFamily("alpaca")).Returns(adapter);
        var completed = new TaskCompletionSource<ProviderCertificationRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new Mock<IProviderCertificationRunner>();
        runner.Setup(value => value.RunAsync("owned", adapter, It.IsAny<CancellationToken>())).Returns(completed.Task);
        var service = new ProviderCertificationService(store, catalog.Object, runner.Object);
        var pending = service.RunAsync("owned");
        runner.Verify(value => value.RunAsync("owned", adapter, It.IsAny<CancellationToken>()), Times.Once);
        if (action == "delete")
            await connections.DeleteForTenantAsync("owned", "tenant-a");
        else if (action == "change")
            await connections.UpsertForTenantAsync(request with { DisplayName = "Changed" }, "tenant-a", "paper");
        else if (action == "unrelated")
            await connections.UpsertForTenantAsync(request with { ConnectionId = "other", ExternalAccountId = "account-b" }, "tenant-a", "paper");
        var before = await File.ReadAllTextAsync(path);
        completed.SetResult(new ProviderCertificationRunResult(action == "wrong-result" ? "foreign" : "owned", true, "Passed", [], DateTimeOffset.UtcNow));
        if (action == "unrelated")
        {
            (await pending).Should().NotBeNull();
            (await connections.GetConnectionsForTenantAsync("tenant-a")).Select(row => row.ConnectionId)
                .Should().BeEquivalentTo("owned", "other");
            (await service.GetCertificationsAsync()).Should().ContainSingle(row => row.ConnectionId == "owned" && row.ProductionReady);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            (await File.ReadAllTextAsync(path)).Should().Be(before);
            (await service.GetCertificationsAsync()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ConnectionMutation_RechecksOwnershipAfterWaitingForConfigurationWriter()
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        var service = new ProviderConnectionService(new ConfigStore(path));
        await service.UpsertForTenantAsync(new CreateProviderConnectionRequest("owned", "alpaca", "Original", ExternalAccountId: "account-a"), "tenant-a", "paper");
        Task<bool> pending;
        string changed;
        using (var lease = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            pending = service.DeleteForTenantAsync("owned", "tenant-a");
            pending.IsCompleted.Should().BeFalse();
            changed = (await File.ReadAllTextAsync(path)).Replace("tenant-a", "tenant-b", StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, changed);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        (await File.ReadAllTextAsync(path)).Should().Be(changed);
    }

    [Fact]
    public async Task CancelledConfigurationWriter_DoesNotChangeStateAndAllowsRetry()
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        var service = new ProviderConnectionService(new ConfigStore(path));
        var request = new CreateProviderConnectionRequest("owned", "alpaca", "Owned", ExternalAccountId: "account-a");
        using (var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = service.UpsertForTenantAsync(request, "tenant-a", "paper", cancellation.Token);
            pending.IsCompleted.Should().BeFalse();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            (await File.ReadAllTextAsync(path)).Should().Be("{}");
        }
        await service.UpsertForTenantAsync(request, "tenant-a", "paper");
        (await service.GetConnectionsForTenantAsync("tenant-a")).Should().ContainSingle();
    }

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
