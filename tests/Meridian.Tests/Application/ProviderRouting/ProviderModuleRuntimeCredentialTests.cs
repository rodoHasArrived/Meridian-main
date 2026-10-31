using System.Text.Json;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;
using ModuleCredentialStore = Meridian.Core.Contracts.IProviderCredentialStore;

namespace Meridian.Tests.Application.ProviderRouting;

/// <summary>Real workstation composition must contain provider-module compatibility credential access.</summary>
public sealed class ProviderModuleRuntimeCredentialTests : IDisposable
{
    private static readonly AsyncLocal<ProbeObservation?> ActiveProbe = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-module-ownership", Guid.NewGuid().ToString("N"));
    private readonly string _keyVariable = "MERIDIAN_MODULE_KEY_" + Guid.NewGuid().ToString("N");
    private readonly string _secretVariable = "MERIDIAN_MODULE_SECRET_" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(15));
    private CoreConfigStore Config => new(Path.Combine(_root, "appsettings.json"));
    private string SidecarPath => Path.Combine(_root, "provider-credentials.json");

    [Theory]
    [InlineData("owned", true)]
    [InlineData("owned", false)]
    [InlineData("selected", true)]
    [InlineData("rollback", true)]
    [InlineData("dangling", true)]
    public async Task WorkstationModule_OwnedStateCannotConfigureOrProbeWithLegacyCredentials(string state, bool savedCredentials)
    {
        var store = await SetupAsync(savedCredentials);
        if (state == "dangling")
        {
            await Config.UpdateAsync(config => config with
            {
                ProviderConnections = new ProviderConnectionsConfig
                {
                    RuntimeCredentials = [new("ALPACA", "tenant-a", "removed-owner")]
                }
            }, _timeout.Token);
        }
        else
        {
            await AddOwnershipAsync(_timeout.Token);
            if (state is "selected" or "rollback")
            {
                var commands = new ProviderRuntimeCredentialService(Config, store);
                var preview = await commands.GetMigrationPreviewAsync("alpaca", "owned", "tenant-a", _timeout.Token);
                await commands.TransferAsync("alpaca", "owned", "tenant-a", "synthetic-transfer",
                    preview.AuditMetadata["credentialGeneration"], "operator", false, _timeout.Token);
                await commands.BindAsync("alpaca", "owned", "tenant-a", _timeout.Token);
                if (state == "rollback")
                    await commands.TransferAsync("alpaca", "owned", "tenant-a", "synthetic-transfer", null,
                        "operator", true, _timeout.Token);
            }
        }

        var vault = ObserveReads(store);
        using var services = CreateServices(vault.Object);
        using var observation = new ProbeObservation();
        var modules = services.GetRequiredService<IProviderModuleSetupService>();

        var result = await modules.TestModuleAsync(SyntheticModule.Id, _timeout.Token);

        result.Success.Should().BeFalse();
        result.CredentialConfigValid.Should().BeFalse();
        result.ConnectionVerified.Should().BeFalse();
        result.FailureReason.Should().Contain("tenant-aware");
        var status = (await modules.GetConfiguredModulesAsync(_timeout.Token)).Single();
        status.CredentialStatus.Should().NotBeEmpty().And.OnlyContain(key => !key.HasValue);
        status.LastTestResult.Should().Contain("tenant-aware");
        observation.AssertUnused();
        vault.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkstationModule_UnreadableConfigurationCannotReachVaultOrProbe(bool corrupt)
    {
        var store = await SetupAsync(savedCredentials: true);
        var vault = ObserveReads(store);
        using var services = CreateServices(vault.Object);
        var modules = services.GetRequiredService<IProviderModuleSetupService>();
        using var observation = new ProbeObservation();
        if (corrupt)
            await File.WriteAllTextAsync(Config.ConfigPath, "{ broken-authority", _timeout.Token);
        else
            File.Delete(Config.ConfigPath);

        Func<Task> test = () => modules.TestModuleAsync(SyntheticModule.Id, _timeout.Token);
        Func<Task> list = () => modules.GetConfiguredModulesAsync(_timeout.Token);
        if (corrupt)
        {
            await test.Should().ThrowAsync<JsonException>();
            await list.Should().ThrowAsync<JsonException>();
        }
        else
        {
            await test.Should().ThrowAsync<IOException>();
            await list.Should().ThrowAsync<IOException>();
        }
        observation.AssertUnused();
        vault.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkstationModule_EntirelyLegacyConfigurationRetainsWholeCredentialSelection(bool savedCredentials)
    {
        var store = await SetupAsync(savedCredentials);
        using var services = CreateServices(store);
        using var observation = new ProbeObservation();
        var modules = services.GetRequiredService<IProviderModuleSetupService>();

        var result = await modules.TestModuleAsync(SyntheticModule.Id, _timeout.Token);

        result.Success.Should().BeTrue();
        result.ConnectionVerified.Should().BeTrue("the test module uses a synthetic, in-process probe");
        observation.ConfigureCalls.Should().Be(1);
        observation.ValidateCalls.Should().Be(1);
        observation.ProbeCalls.Should().Be(1);
        observation.Key.Should().Be(savedCredentials ? "vault-key" : "mapped-key");
        var status = (await modules.GetConfiguredModulesAsync(_timeout.Token)).Single();
        status.CredentialStatus.Should().OnlyContain(key => key.HasValue);
        status.LastTestResult.Should().BeNull();
    }

    [Fact]
    public async Task WorkstationModule_ListKeepsOtherLegacyProvidersVisibleWhenOneProviderRequiresOwnership()
    {
        var store = await SetupAsync(savedCredentials: true);
        await AddOwnershipAsync(_timeout.Token);
        await store.SaveAsync(new("polygon", new Dictionary<string, string?> { ["ApiKey"] = "polygon-key" }), _timeout.Token);
        await Config.UpdateAsync(config => config with
        {
            ProviderModules = new ProviderModulesConfig(new Dictionary<string, ProviderModuleSettings>(config.ProviderModules!.Modules)
            {
                ["polygon"] = new()
            })
        }, _timeout.Token);
        using var services = CreateServices(store);
        using var observation = new ProbeObservation();

        var statuses = await services.GetRequiredService<IProviderModuleSetupService>().GetConfiguredModulesAsync(_timeout.Token);

        statuses.Single(status => status.ModuleId == SyntheticModule.Id).LastTestResult.Should().Contain("tenant-aware");
        var legacy = statuses.Single(status => status.ModuleId == "polygon");
        legacy.CredentialStatus.Single(key => key.Key == "ApiKey").HasValue.Should().BeTrue();
        legacy.LastTestResult.Should().BeNull();
        observation.AssertUnused();
    }

    [Fact]
    public async Task WorkstationModule_AllLegacySidecarImportsAtomicallyAndRemovesItsPlaintextSource()
    {
        var store = await SetupAsync(savedCredentials: false);
        await WriteSidecarAsync();
        using var services = CreateServices(store);
        var credentials = services.GetRequiredService<ModuleCredentialStore>();

        var selected = await credentials.GetCredentialsAsync(SyntheticModule.Id, _timeout.Token);

        selected["KeyId"].Should().Be("sidecar-key");
        (await store.ReadForProviderAsync("polygon", _timeout.Token))!.Get("ApiKey").Should().Be("sidecar-polygon");
        File.Exists(SidecarPath).Should().BeFalse();
        File.Exists(SidecarPath + ".migrated").Should().BeFalse();
    }

    [Fact]
    public async Task WorkstationModule_MixedOwnedSidecarDoesNotImportAnyProviderOrEraseSource()
    {
        var store = await SetupAsync(savedCredentials: false);
        await AddOwnershipAsync(_timeout.Token);
        await WriteSidecarAsync();
        var source = await File.ReadAllBytesAsync(SidecarPath, _timeout.Token);
        using var services = CreateServices(store);
        var credentials = services.GetRequiredService<ModuleCredentialStore>();

        Func<Task> import = () => credentials.GetCredentialsAsync("polygon", _timeout.Token);

        await import.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        (await File.ReadAllBytesAsync(SidecarPath, _timeout.Token)).Should().Equal(source);
        File.Exists(SidecarPath + ".migrated").Should().BeFalse();
        (await store.ReadForProviderAsync("polygon", _timeout.Token)).Should().BeNull();
        (await store.ReadForProviderAsync("alpaca", _timeout.Token)).Should().BeNull();
    }

    [Fact]
    public async Task WorkstationModule_SidecarImportHoldsOwnershipLeaseUntilDurableImportCompletes()
    {
        var store = await SetupAsync(savedCredentials: false);
        await WriteSidecarAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vault = ObserveReads(store);
        var importer = vault.As<ILegacyProviderCredentialImporter>();
        importer.Setup(value => value.ImportLegacyAsync(It.IsAny<IReadOnlyList<ProviderCredentialSaveRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<ProviderCredentialSaveRequest> requests, CancellationToken ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
                await store.ImportLegacyAsync(requests, ct);
            });
        using var services = CreateServices(vault.Object);
        var credentials = services.GetRequiredService<ModuleCredentialStore>();
        var import = credentials.GetCredentialsAsync(SyntheticModule.Id, _timeout.Token);
        await entered.Task.WaitAsync(_timeout.Token);
        try
        {
            using var blockedWriter = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            Func<Task> ownershipChange = () => AddOwnershipAsync(blockedWriter.Token);
            await ownershipChange.Should().ThrowAsync<OperationCanceledException>();
            Config.LoadRequired().ProviderConnections?.Connections.Should().BeNullOrEmpty();
        }
        finally
        {
            release.TrySetResult();
            await import;
        }

        await AddOwnershipAsync(_timeout.Token);
        Func<Task> laterRead = () => credentials.GetCredentialsAsync(SyntheticModule.Id, _timeout.Token);
        await laterRead.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        importer.Verify(value => value.ImportLegacyAsync(It.IsAny<IReadOnlyList<ProviderCredentialSaveRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
        vault.Verify(value => value.ReadForProviderAsync(SyntheticModule.Id, It.IsAny<CancellationToken>()), Times.Once);
        vault.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkstationModule_ExistingCompatibilityAdapterRejectsLateLegacyWrites(bool delete)
    {
        var store = await SetupAsync(savedCredentials: true);
        using var services = CreateServices(store);
        var credentials = services.GetRequiredService<ModuleCredentialStore>();
        (await credentials.GetCredentialsAsync(SyntheticModule.Id, _timeout.Token))["KeyId"].Should().Be("vault-key");
        var before = await File.ReadAllBytesAsync(store.VaultPath, _timeout.Token);
        await AddOwnershipAsync(_timeout.Token);

        Func<Task> write = delete
            ? () => credentials.DeleteCredentialsAsync(SyntheticModule.Id, _timeout.Token)
            : () => credentials.SaveCredentialsAsync(SyntheticModule.Id,
                new Dictionary<string, string> { ["KeyId"] = "late-key", ["SecretKey"] = "late-secret" }, _timeout.Token);

        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        (await File.ReadAllBytesAsync(store.VaultPath, _timeout.Token)).Should().Equal(before);
    }

    private async Task<FileProviderCredentialStore> SetupAsync(bool savedCredentials)
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(_keyVariable, "mapped-key");
        Environment.SetEnvironmentVariable(_secretVariable, "mapped-secret");
        await Config.SaveAsync(new AppConfig(DataRoot: _root, ProviderModules: new ProviderModulesConfig(
            new Dictionary<string, ProviderModuleSettings>
            {
                [SyntheticModule.Id] = new(Credentials: new Dictionary<string, string?>
                {
                    ["KeyId"] = _keyVariable,
                    ["SecretKey"] = _secretVariable
                })
            })), _timeout.Token);
        var store = new FileProviderCredentialStore(_root, _ => null);
        if (savedCredentials)
            await store.SaveAsync(new("alpaca", new Dictionary<string, string?>
            {
                ["KeyId"] = "vault-key",
                ["SecretKey"] = "vault-secret"
            }, "paper", "synthetic-operator"), _timeout.Token);
        return store;
    }

    private Task AddOwnershipAsync(CancellationToken ct) =>
        new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
            "owned", "alpaca", "Synthetic owned connection", ExternalAccountId: "account-a"), "tenant-a", "paper", ct);

    private Task WriteSidecarAsync() => File.WriteAllTextAsync(SidecarPath, JsonSerializer.Serialize(
        new Dictionary<string, Dictionary<string, string>>
        {
            ["polygon"] = new() { ["ApiKey"] = "sidecar-polygon" },
            [SyntheticModule.Id] = new() { ["KeyId"] = "sidecar-key", ["SecretKey"] = "sidecar-secret" }
        }), _timeout.Token);

    private static Mock<IProviderCredentialStore> ObserveReads(FileProviderCredentialStore store)
    {
        var result = new Mock<IProviderCredentialStore>(MockBehavior.Strict);
        result.Setup(value => value.ReadForProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string providerId, CancellationToken ct) => store.ReadForProviderAsync(providerId, ct));
        return result;
    }

    private ServiceProvider CreateServices(IProviderCredentialStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Config);
        services.AddSingleton(store);
        services.AddWorkstationSharedServices(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    public sealed class SyntheticModule : IProviderModule, IProviderModuleCredentialHints, IProviderModuleConnectionProbe
    {
        // A real catalog alias without a production module of its own exercises canonical ownership.
        public const string Id = "alpaca-api";
        private ProviderModuleContext? _context;
        public string ModuleId => Id;
        public bool IsProductionProvider => false;
        public IReadOnlyList<string> CredentialKeyHints => ["KeyId", "SecretKey"];
        public void Configure(ProviderModuleContext context)
        {
            _context = context;
            if (ActiveProbe.Value is { } observation)
            {
                observation.ConfigureCalls++;
                observation.Key = context.GetCredential("KeyId");
            }
        }
        public ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken ct = default)
        {
            if (ActiveProbe.Value is { } observation)
                observation.ValidateCalls++;
            return ValueTask.FromResult(_context is { CredentialsAreAuthoritative: true }
                && !string.IsNullOrWhiteSpace(_context.GetCredential("KeyId"))
                && !string.IsNullOrWhiteSpace(_context.GetCredential("SecretKey"))
                    ? ModuleValidationResult.Valid : ModuleValidationResult.Failure("Synthetic credentials are incomplete."));
        }
        public Task<ModuleProbeResult> ProbeConnectionAsync(CancellationToken ct = default)
        {
            if (ActiveProbe.Value is { } observation)
                observation.ProbeCalls++;
            return Task.FromResult(ModuleProbeResult.Success(0, "Synthetic probe; no provider contacted."));
        }
        public void Register(IServiceCollection services, DataSourceRegistry registry) { }
    }

    private sealed class ProbeObservation : IDisposable
    {
        private readonly ProbeObservation? _previous = ActiveProbe.Value;
        public ProbeObservation() => ActiveProbe.Value = this;
        public int ConfigureCalls { get; set; }
        public int ValidateCalls { get; set; }
        public int ProbeCalls { get; set; }
        public string? Key { get; set; }
        public void AssertUnused()
        {
            ConfigureCalls.Should().Be(0);
            ValidateCalls.Should().Be(0);
            ProbeCalls.Should().Be(0);
            Key.Should().BeNull();
        }
        public void Dispose() => ActiveProbe.Value = _previous;
    }

    public void Dispose()
    {
        _timeout.Dispose();
        Environment.SetEnvironmentVariable(_keyVariable, null);
        Environment.SetEnvironmentVariable(_secretVariable, null);
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
