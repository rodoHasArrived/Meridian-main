using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Core.Contracts;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application.UI;

public sealed class ProviderModuleCredentialSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedPartialOrEmptyRecord_CannotBorrowMappedEnvironmentCredentials(bool empty)
    {
        await WithServiceAsync(empty ? new Dictionary<string, string>() : new() { ["key"] = "stored-key" }, async service =>
        {
            var result = await service.TestModuleAsync(ReviewCredentialModule.Id);
            result.CredentialConfigValid.Should().BeFalse();
            var status = (await service.GetConfiguredModulesAsync()).Single();
            status.CredentialStatus.Single(key => key.Key == "secret").HasValue.Should().BeFalse();
        });
    }

    [Fact]
    public async Task ProvenAbsentRecord_CanSelectTheEnvironmentMappedRecord()
    {
        await WithServiceAsync(null, async service =>
        {
            var result = await service.TestModuleAsync(ReviewCredentialModule.Id);
            result.CredentialConfigValid.Should().BeTrue();
            var status = (await service.GetConfiguredModulesAsync()).Single();
            status.CredentialStatus.Should().OnlyContain(key => key.HasValue);
        });
    }

    private static async Task WithServiceAsync(IReadOnlyDictionary<string, string>? record,
        Func<ProviderModuleSetupService, Task> verify)
    {
        var root = Path.Combine(Path.GetTempPath(), "module-record-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var keyVariable = "MERIDIAN_TEST_KEY_" + Guid.NewGuid().ToString("N");
        var secretVariable = "MERIDIAN_TEST_SECRET_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(keyVariable, "environment-key");
        Environment.SetEnvironmentVariable(secretVariable, "environment-secret");
        try
        {
            var path = Path.Combine(root, "appsettings.json");
            await File.WriteAllTextAsync(path, "{}");
            var config = new ConfigStore(path);
            await config.UpdateAsync(value => value with
            {
                ProviderModules = new ProviderModulesConfig(new Dictionary<string, ProviderModuleSettings>
                {
                    [ReviewCredentialModule.Id] = new(Credentials: new Dictionary<string, string?>
                    {
                        ["key"] = keyVariable,
                        ["secret"] = secretVariable
                    })
                })
            });
            await verify(new ProviderModuleSetupService(config, new RecordStore(record),
                NullLogger<ProviderModuleSetupService>.Instance));
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyVariable, null);
            Environment.SetEnvironmentVariable(secretVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    public sealed class ReviewCredentialModule : IProviderModule, IProviderModuleCredentialHints
    {
        public const string Id = "review-record-selection";
        private ProviderModuleContext? _context;
        public string ModuleId => Id;
        public bool IsProductionProvider => false;
        public IReadOnlyList<string> CredentialKeyHints => ["key", "secret"];
        public void Configure(ProviderModuleContext context) => _context = context;
        public ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(_context is { CredentialsAreAuthoritative: true }
                && !string.IsNullOrWhiteSpace(_context.GetCredential("key"))
                && !string.IsNullOrWhiteSpace(_context.GetCredential("secret"))
                    ? ModuleValidationResult.Valid
                    : ModuleValidationResult.Failure("The selected credential record is incomplete."));
        public void Register(IServiceCollection services, DataSourceRegistry registry) { }
    }

    private sealed class RecordStore(IReadOnlyDictionary<string, string>? record) : IProviderCredentialStore
    {
        public Task<IReadOnlyDictionary<string, string>?> ReadCredentialRecordAsync(string moduleId, CancellationToken ct = default) =>
            Task.FromResult(record);
        public Task<IReadOnlyDictionary<string, string>> GetCredentialsAsync(string moduleId, CancellationToken ct = default) =>
            Task.FromResult(record ?? new Dictionary<string, string>());
        public Task<IReadOnlySet<string>> GetStoredKeyNamesAsync(string moduleId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>((record?.Keys ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase));
        public Task SaveCredentialsAsync(string moduleId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task DeleteCredentialsAsync(string moduleId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
