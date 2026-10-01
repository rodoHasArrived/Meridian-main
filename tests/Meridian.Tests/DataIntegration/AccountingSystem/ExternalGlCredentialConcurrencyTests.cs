using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.AccountingSystem;
using Meridian.DataIntegration.Credentials;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

[Collection("Sequential")]
public sealed class ExternalGlCredentialConcurrencyTests
{
    [Theory]
    [InlineData("xero", false, true)]
    [InlineData("netsuite", false, true)]
    [InlineData("xero", true, true)]
    [InlineData("netsuite", true, true)]
    [InlineData("xero", false, false)]
    [InlineData("netsuite", false, false)]
    [InlineData("xero", true, false)]
    [InlineData("netsuite", true, false)]
    public async Task ConcurrentReplacement_WinsOverTokenRotationAndVerification(
        string id, bool verify, bool duringTokenExchange)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-gl-concurrency", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileProviderCredentialStore(root);
            var values = new ExternalGlTestStore(id).Values;
            var fields = ProviderCredentialCatalog.Find(id)!.RequiredFields.Where(field => field.Required)
                .ToDictionary(field => field.Name, field => (string?)values[field.Name]);
            await store.SaveAsync(new(id, fields));
            using var handler = new PausedResponseHandler(duringTokenExchange);
            using var client = new HttpClient(handler);
            CredentialedAccountingProvider provider = id == "xero"
                ? new XeroAccountingProvider(store, client) : new NetSuiteAccountingProvider(store, client);
            var lifecycle = new ProviderConnectionLifecycleService(store, new ConfigStore(Path.Combine(root, "config.json")),
                NullLogger<ProviderConnectionLifecycleService>.Instance, accountingSystemProviders: [provider]);
            ProviderCredentialVerificationResultDto? result = null;
            async Task RunAsync()
            {
                if (verify)
                    result = await lifecycle.VerifyAsync(id, actor: "controller");
                else
                    await provider.ImportAsync(ExternalGlTestData.Request(id));
            }
            var operation = RunAsync();
            await handler.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var replacementStore = new FileProviderCredentialStore(root);
            var replacement = new Dictionary<string, string?>
            {
                ["ClientId"] = "new-client",
                ["ClientSecret"] = "new-secret",
                ["RefreshToken"] = "new-refresh",
                [id == "xero" ? "TenantId" : "SubsidiaryId"] = id == "xero" ? Guid.NewGuid().ToString() : "99"
            };
            await replacementStore.SaveAsync(new(id, replacement));
            var expected = (await replacementStore.ReadForProviderAsync(id))!;
            handler.Release.TrySetResult();
            if (verify)
            {
                await operation;
                result!.Success.Should().BeFalse();
                result.VerificationState.Should().Be(ProviderVerificationStateDto.NotVerified);
            }
            else
            {
                await ((Func<Task>)(async () => await operation)).Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("Read-only GL import failed.*");
            }
            var retained = (await replacementStore.ReadForProviderAsync(id))!;
            retained.CredentialGeneration.Should().Be(expected.CredentialGeneration);
            retained.Credentials.Should().BeEquivalentTo(expected.Credentials);
            (await replacementStore.GetStatusAsync(id)).VerificationState.Should().Be(ProviderVerificationStateDto.NotVerified);
            var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl"));
            audit.Should().NotContain("verify-success").And.NotContain("verify-failure");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PausedResponseHandler(bool duringTokenExchange) : HttpMessageHandler
    {
        private bool _paused;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var isToken = request.RequestUri!.AbsolutePath.EndsWith("token", StringComparison.Ordinal);
            if (!_paused && isToken == duringTokenExchange)
            {
                _paused = true;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return ExternalGlTestData.Respond(request, body);
        }
    }
}
