using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.AccountingSystem;
using Meridian.DataIntegration.Credentials;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

[Collection("Sequential")]
public sealed class ExternalGlConnectionLifecycleTests
{
    [Theory]
    [InlineData("xero", true)]
    [InlineData("netsuite", true)]
    [InlineData("xero", false)]
    [InlineData("netsuite", false)]
    public async Task Lifecycle_PersistsOneVerificationWithActor_AndCredentialReplacementClearsIt(string id, bool success)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-gl-verification", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileProviderCredentialStore(root);
            var values = new ExternalGlTestStore(id).Values;
            var fields = ProviderCredentialCatalog.Find(id)!.RequiredFields.Where(field => field.Required)
                .ToDictionary(field => field.Name, field => (string?)values[field.Name]);
            await store.SaveAsync(new(id, fields));
            using var handler = new ExternalGlTestHandler((request, body) =>
                success || request.RequestUri!.AbsolutePath.EndsWith("token", StringComparison.Ordinal)
                    ? ExternalGlTestData.Respond(request, body)
                    : ExternalGlTestHandler.Raw("secret-provider-error", HttpStatusCode.Forbidden));
            using var client = new HttpClient(handler);
            CredentialedAccountingProvider provider = id == "xero"
                ? new XeroAccountingProvider(store, client) : new NetSuiteAccountingProvider(store, client);
            var lifecycle = new ProviderConnectionLifecycleService(store, new ConfigStore(Path.Combine(root, "config.json")),
                NullLogger<ProviderConnectionLifecycleService>.Instance, accountingSystemProviders: [provider]);
            var result = await lifecycle.VerifyAsync(id, actor: "accounting-controller");
            result.Success.Should().Be(success);
            var status = await store.GetStatusAsync(id);
            status.VerificationState.Should().Be(success ? ProviderVerificationStateDto.Verified : ProviderVerificationStateDto.Failed);
            var records = (await File.ReadAllLinesAsync(Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl")))
                .Select(line => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line)!).ToArray();
            var verification = records.Where(record => record["action"].GetString()!.StartsWith("verify-", StringComparison.Ordinal))
                .Should().ContainSingle().Which;
            verification["action"].GetString().Should().Be(success ? "verify-success" : "verify-failure");
            verification["actor"].GetString().Should().Be("accounting-controller");
            await store.SaveAsync(new(id, new Dictionary<string, string?> { ["ClientSecret"] = "replacement-test-secret" }));
            var replaced = await store.GetStatusAsync(id);
            replaced.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
            replaced.VerificationState.Should().Be(ProviderVerificationStateDto.NotVerified);
            replaced.LastSuccessfulAt.Should().Be(status.LastSuccessfulAt);
            replaced.LastVerifiedAt.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
