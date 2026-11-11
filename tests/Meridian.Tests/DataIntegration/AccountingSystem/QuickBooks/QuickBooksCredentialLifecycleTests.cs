using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem.QuickBooks;

[Collection("Sequential")]
public sealed class QuickBooksCredentialLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_CancellationAfterTokenExchange_RetainsCompleteRecoverySnapshot(bool providerRotates)
    {
        var root = TemporaryRoot();
        try
        {
            var store = await ConfiguredStoreAsync(root);
            var original = (await store.ReadForProviderAsync("quickbooks"))!;
            using var cancellation = new CancellationTokenSource();
            var client = new StubClient
            {
                Refresh = (_, _) =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(Token(providerRotates ? "rotated-refresh" : null));
                }
            };
            var provider = Provider(store, client);

            await provider.Invoking(p => p.ImportAsync(Request(), cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();

            // No verification write has refreshed recovery storage after the remote rotation.
            await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary");
            var recoveredStore = new FileProviderCredentialStore(root);
            var recovered = (await recoveredStore.ReadForProviderAsync("quickbooks"))!;
            recovered.Get("RefreshToken").Should().Be(providerRotates ? "rotated-refresh" : original.Get("RefreshToken"));
            recovered.Credentials.Where(p => p.Key != "RefreshToken").Should()
                .BeEquivalentTo(original.Credentials.Where(p => p.Key != "RefreshToken"));
            recovered.Environment.Should().Be(original.Environment);
            recovered.CredentialGeneration.Should().NotBe(original.CredentialGeneration);
            (await recoveredStore.GetStatusAsync("quickbooks")).VerificationState.Should().Be(ProviderVerificationStateDto.NotVerified);

            var retryClient = new StubClient
            {
                Refresh = (connection, _) =>
                {
                    connection.RefreshToken.Should().Be(recovered.Get("RefreshToken"));
                    return Task.FromResult(Token("retry-refresh"));
                }
            };
            (await Provider(recoveredStore, retryClient).ImportAsync(Request())).Summary.ProviderId.Should().Be("quickbooks");
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Import_ConcurrentCredentialReplacement_PreservesReplacementAndItsVerificationState(
        bool duringTokenExchange, bool upstreamFails)
    {
        var root = TemporaryRoot();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var store = await ConfiguredStoreAsync(root);
            async Task PauseAsync(CancellationToken ct)
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(ct);
                if (upstreamFails)
                    throw new InvalidOperationException("Synthetic upstream refusal.");
            }
            var client = new StubClient
            {
                Refresh = async (_, ct) =>
                {
                    if (duringTokenExchange)
                        await PauseAsync(ct);
                    return Token("stale-rotated-refresh");
                },
                Read = async (_, ct) =>
                {
                    if (!duringTokenExchange)
                        await PauseAsync(ct);
                    return EmptyEvidence;
                }
            };
            var operation = Provider(store, client).ImportAsync(Request());
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var replacementStore = new FileProviderCredentialStore(root);
            await replacementStore.SaveAsync(new("quickbooks", Fields("replacement"), "production"));
            var replacement = (await replacementStore.ReadForProviderAsync("quickbooks"))!;
            release.TrySetResult();

            await ((Func<Task>)(async () => await operation)).Should().ThrowAsync<InvalidOperationException>();

            var retained = (await replacementStore.ReadForProviderAsync("quickbooks"))!;
            retained.CredentialGeneration.Should().Be(replacement.CredentialGeneration);
            retained.Credentials.Should().BeEquivalentTo(replacement.Credentials);
            retained.Environment.Should().Be(replacement.Environment);
            (await replacementStore.GetStatusAsync("quickbooks")).VerificationState.Should().Be(ProviderVerificationStateDto.NotVerified);
            var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl"));
            audit.Should().NotContain("verify-success").And.NotContain("verify-failure");
        }
        finally
        {
            release.TrySetResult();
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task BoundVerification_WaitsForImportRotation_AndCancelledWaiterDoesNotRefresh()
    {
        var root = TemporaryRoot();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var store = await ConfiguredStoreAsync(root);
            var refreshCalls = 0;
            var client = new StubClient
            {
                Refresh = async (connection, ct) =>
                {
                    if (Interlocked.Increment(ref refreshCalls) == 1)
                    {
                        reached.TrySetResult();
                        await release.Task.WaitAsync(ct);
                        return Token("first-rotated-refresh");
                    }
                    connection.RefreshToken.Should().Be("first-rotated-refresh");
                    return Token("second-rotated-refresh");
                }
            };
            var provider = Provider(store, client);
            var import = provider.ImportAsync(Request());
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var verifier = provider.BindCredentials(store);
            using var cancellation = new CancellationTokenSource();
            var cancelledVerification = verifier.VerifyConnectionAsync(cancellation.Token);
            cancellation.Cancel();

            await ((Func<Task>)(async () => await cancelledVerification)).Should().ThrowAsync<OperationCanceledException>();
            refreshCalls.Should().Be(1);
            release.TrySetResult();
            await import;
            var verified = await verifier.VerifyConnectionAsync();
            verified.Success.Should().BeTrue();
            verified.ExpectedCredentialGeneration.Should().Be((await store.ReadForProviderAsync("quickbooks"))!.CredentialGeneration);
            refreshCalls.Should().Be(2);
        }
        finally
        {
            release.TrySetResult();
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_UnchangedCredentials_RecordsCurrentGenerationOutcome(bool upstreamFails)
    {
        var root = TemporaryRoot();
        try
        {
            var store = await ConfiguredStoreAsync(root);
            var client = new StubClient
            {
                Read = (_, _) => upstreamFails
                    ? Task.FromException<QuickBooksOnlineCompanyEvidence>(new InvalidOperationException("Synthetic provider refusal."))
                    : Task.FromResult(EmptyEvidence)
            };
            var provider = Provider(store, client);
            if (upstreamFails)
                await provider.Invoking(p => p.ImportAsync(Request())).Should().ThrowAsync<InvalidOperationException>();
            else
                await provider.ImportAsync(Request());

            var status = await store.GetStatusAsync("quickbooks");
            status.VerificationState.Should().Be(upstreamFails ? ProviderVerificationStateDto.Failed : ProviderVerificationStateDto.Verified);
            (await store.ReadForProviderAsync("quickbooks"))!.Get("RefreshToken").Should().Be("rotated-refresh");
        }
        finally { DeleteRoot(root); }
    }

    private static string TemporaryRoot()
        => Path.Combine(Path.GetTempPath(), "meridian-qbo-lifecycle", Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static Dictionary<string, string?> Fields(string prefix = "original") => new()
    {
        ["ClientId"] = $"{prefix}-client",
        ["ClientSecret"] = $"{prefix}-secret",
        ["RefreshToken"] = $"{prefix}-refresh",
        ["RealmId"] = $"{prefix}-realm",
        ["CompanyName"] = $"{prefix}-company"
    };

    private static async Task<FileProviderCredentialStore> ConfiguredStoreAsync(string root)
    {
        var store = new FileProviderCredentialStore(root);
        await store.SaveAsync(new("quickbooks", Fields(), "sandbox"));
        return store;
    }

    private static QuickBooksOnlineAccountingProvider Provider(IProviderCredentialStore store, IQuickBooksOnlineClient client)
        => new(new QuickBooksOnlineProviderCredentialConnectionStore(store), client);

    private static AccountingSystemImportRequestDto Request()
        => new("quickbooks", PeriodStart: new DateOnly(2026, 1, 1), PeriodEnd: new DateOnly(2026, 1, 31));

    private static QuickBooksOnlineTokenExchangeResult Token(string? refreshToken)
        => new("synthetic-access", refreshToken, DateTimeOffset.UtcNow.AddHours(1), []);

    private static readonly QuickBooksOnlineCompanyEvidence EmptyEvidence = new([], [], [], [], []);

    private sealed class StubClient : IQuickBooksOnlineClient
    {
        public Func<QuickBooksOnlineConnection, CancellationToken, Task<QuickBooksOnlineTokenExchangeResult>> Refresh { get; init; }
            = (_, _) => Task.FromResult(Token("rotated-refresh"));
        public Func<QuickBooksOnlineConnection, CancellationToken, Task<QuickBooksOnlineCompanyEvidence>> Read { get; init; }
            = (_, _) => Task.FromResult(EmptyEvidence);

        public Task<QuickBooksOnlineTokenExchangeResult> RefreshAccessTokenAsync(QuickBooksOnlineConnection connection, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Refresh(connection, ct);
        }

        public Task<QuickBooksOnlineCompanyEvidence> ReadCompanyEvidenceAsync(QuickBooksOnlineConnection connection,
            string accessToken, AccountingSystemImportRequestDto request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Read(connection, ct);
        }

        public Task VerifyCompanyAsync(QuickBooksOnlineConnection connection, string accessToken, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<QuickBooksOnlineTokenExchangeResult> ExchangeAuthorizationCodeAsync(
            QuickBooksOnlineAuthorizationCodeRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
