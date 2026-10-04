using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

[Collection("Sequential")]
public sealed class ExternalGlCredentialRecoveryTests
{
    [Theory]
    [InlineData("xero", false)]
    [InlineData("xero", true)]
    [InlineData("netsuite", false)]
    [InlineData("netsuite", true)]
    public async Task Rotation_RetainsCompleteConnectionAndRecoveryGeneration_BeforeCancelledImport(string id, bool environment)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-gl-rotation", Guid.NewGuid().ToString("N"));
        var descriptor = ProviderCredentialCatalog.Find(id)!;
        var values = new ExternalGlTestStore(id).Values;
        var fields = descriptor.RequiredFields.Where(f => f.Required).ToDictionary(f => f.Name, f => (string?)values[f.Name]);
        var variables = descriptor.RequiredFields.Where(f => f.Required).ToDictionary(f => f.EnvironmentNames[0], f => fields[f.Name]);
        variables["MDC_PROVIDER_ALLOW_ENV_FALLBACK"] = "true";
        var previous = variables.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var variable in variables)
                Environment.SetEnvironmentVariable(variable.Key, environment ? variable.Value : null);
            var store = new FileProviderCredentialStore(root);
            if (!environment)
                await store.SaveAsync(new(id, fields));
            using var cancellation = new CancellationTokenSource();
            using var handler = new ExternalGlTestHandler((request, body) =>
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("token", StringComparison.Ordinal))
                    cancellation.Cancel();
                return ExternalGlTestData.Respond(request, body);
            });
            using var client = new HttpClient(handler);
            var provider = id == "xero"
                ? (Meridian.DataIntegration.AccountingSystem.CredentialedAccountingProvider)new Meridian.DataIntegration.AccountingSystem.XeroAccountingProvider(store, client)
                : new Meridian.DataIntegration.AccountingSystem.NetSuiteAccountingProvider(store, client);
            await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id), cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();
            // No later verification write has refreshed the backup; recovery must use the rotated snapshot itself.
            await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary");
            var recoveredStore = new FileProviderCredentialStore(root);
            var recovered = await recoveredStore.ReadForProviderAsync(id);
            recovered!.Source.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
            recovered.Get("RefreshToken").Should().Be("rotated-never-return");
            foreach (var field in fields.Where(p => p.Key != "RefreshToken"))
                recovered.Get(field.Key).Should().Be(field.Value);
            using var retryHandler = new ExternalGlTestHandler((request, body) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("token", StringComparison.Ordinal))
                    body.Should().Contain("refresh_token=rotated-never-return");
                return ExternalGlTestData.Respond(request, body);
            });
            using var retryClient = new HttpClient(retryHandler);
            var retry = id == "xero"
                ? (Meridian.DataIntegration.AccountingSystem.CredentialedAccountingProvider)new Meridian.DataIntegration.AccountingSystem.XeroAccountingProvider(recoveredStore, retryClient)
                : new Meridian.DataIntegration.AccountingSystem.NetSuiteAccountingProvider(recoveredStore, retryClient);
            (await retry.ImportAsync(ExternalGlTestData.Request(id))).TrialBalance.Should().NotBeEmpty();
        }
        finally
        {
            foreach (var variable in previous)
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
