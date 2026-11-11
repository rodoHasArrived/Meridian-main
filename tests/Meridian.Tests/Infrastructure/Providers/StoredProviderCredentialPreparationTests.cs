using FluentAssertions;
using Meridian.Application.Services;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Contracts;
using Meridian.Tests.Ui;
using Moq;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider construction is synchronous, so an asynchronous caller reads the credentials first
/// with <see cref="IProviderCredentialResolver.PrepareAsync"/> and constructs inside the prepared
/// scope. These pin that the scope is what the construction reads from, that it is a
/// point-in-time read which ends with the scope, and that ownership rules are unchanged.
/// </summary>
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class StoredProviderCredentialPreparationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "meridian-credential-preparation", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ProviderCredentialSaveRequest Alpaca(string key, string secret)
        => new("alpaca", new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = secret }, "paper");

    private static string? AlpacaKey(IProviderCredentialResolver resolver)
        => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID");

    [Fact]
    public async Task PreparedScope_ServesConstructionFromTheReadTakenByPrepareAsync()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Alpaca("prepared-key", "prepared-secret"));
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());

        var prepared = await resolver.PrepareAsync();

        // Rotate the vault after the read. Inside the scope the construction must see what was
        // prepared, which shows it reads the prepared record rather than the vault.
        await store.SaveAsync(Alpaca("rotated-key", "rotated-secret"));
        using (prepared.Activate())
        {
            var context = resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
            context.Get("ALPACA_KEY_ID").Should().Be("prepared-key");
            context.Get("ALPACA_SECRET_KEY").Should().Be("prepared-secret");
            context.CredentialEnvironment.Should().Be("paper");
        }

        // The prepared read ends with its scope: the next construction reads the vault again.
        AlpacaKey(resolver).Should().Be("rotated-key");
    }

    [Fact]
    public async Task PreparedScope_IsNotVisibleBeforeActivateOrAfterDispose()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Alpaca("first-key", "first-secret"));
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());
        var prepared = await resolver.PrepareAsync();
        await store.SaveAsync(Alpaca("second-key", "second-secret"));

        AlpacaKey(resolver).Should().Be("second-key", "a preparation that was never activated is not in effect");
        var scope = prepared.Activate();
        AlpacaKey(resolver).Should().Be("first-key");
        scope.Dispose();
        scope.Dispose();
        AlpacaKey(resolver).Should().Be("second-key");
    }

    [Fact]
    public async Task PreparedScope_WorkStartedInsideItStopsSeeingThePreparedReadOnceItEnds()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Alpaca("prepared-key", "prepared-secret"));
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());
        var prepared = await resolver.PrepareAsync();
        var scopeEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // A background task started inside the scope captures its execution flow, as a timer or
        // a long-running worker created during provider construction would.
        Task<string?> background;
        using (prepared.Activate())
        {
            background = Task.Run(async () =>
            {
                await scopeEnded.Task;
                return AlpacaKey(resolver);
            });
        }

        await store.SaveAsync(Alpaca("rotated-key", "rotated-secret"));
        scopeEnded.SetResult();

        (await background).Should().Be("rotated-key",
            "work that outlives the scope must read the vault, not credentials read at startup");
    }

    [Fact]
    public async Task PreparedScope_OnlyAppliesToTheResolverThatPreparedIt()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Alpaca("first-key", "first-secret"));
        var preparing = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());
        var other = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());
        var prepared = await preparing.PrepareAsync();
        await store.SaveAsync(Alpaca("second-key", "second-secret"));

        using (prepared.Activate())
        {
            AlpacaKey(preparing).Should().Be("first-key");
            AlpacaKey(other).Should().Be("second-key", "another resolver must never be served this resolver's read");
        }
    }

    [Fact]
    public async Task PreparedScope_MissingRecordStaysMissingAndIsNotRefilledFromConfiguredValues()
    {
        var store = new FileProviderCredentialStore(_root);
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());

        var prepared = await resolver.PrepareAsync();
        using (prepared.Activate())
        {
            var context = resolver.CreateContext(
                typeof(AlpacaHistoricalDataProvider),
                new Dictionary<string, string?> { ["ALPACA_KEY_ID"] = "configured-key" });

            context.Get("ALPACA_KEY_ID").Should().BeNull("the store owns the record; a missing field stays missing");
        }
    }

    [Fact]
    public async Task PrepareAsync_WithProviderTypes_ReadsOnlyThoseProviders()
    {
        var store = new Mock<IProviderCredentialStore>();
        store.Setup(s => s.ReadForProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderCredentialReadResult?)null);
        var resolver = new StoredProviderCredentialResolver(store.Object, new EnvironmentCredentialResolver());

        await resolver.PrepareAsync([typeof(AlpacaHistoricalDataProvider), typeof(AlpacaMarketDataClient)]);

        store.Verify(s => s.ReadForProviderAsync("alpaca", It.IsAny<CancellationToken>()), Times.Once);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreparedScope_ConstructionDoesNotReadTheStoreAgain()
    {
        var store = new Mock<IProviderCredentialStore>();
        store.Setup(s => s.ReadForProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderCredentialReadResult?)null);
        var resolver = new StoredProviderCredentialResolver(store.Object, new EnvironmentCredentialResolver());

        var prepared = await resolver.PrepareAsync([typeof(AlpacaHistoricalDataProvider)]);
        store.Invocations.Clear();
        using (prepared.Activate())
        {
            resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
            resolver.CreateContext(typeof(AlpacaMarketDataClient));
        }

        store.Invocations.Should().BeEmpty("inside the prepared scope construction performs no storage I/O");
    }

    [Fact]
    public async Task PrepareAsync_WhenOneRecordFailsToRead_OnlyThatProvidersConstructionFails()
    {
        var store = new Mock<IProviderCredentialStore>();
        store.Setup(s => s.ReadForProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderCredentialReadResult?)null);
        store.Setup(s => s.ReadForProviderAsync("alpaca", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("vault record is unreadable"));
        var resolver = new StoredProviderCredentialResolver(store.Object, new EnvironmentCredentialResolver());

        var prepared = await resolver.PrepareAsync();

        using (prepared.Activate())
        {
            var alpaca = () => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
            alpaca.Should().Throw<IOException>("a storage failure must propagate, not switch credential ownership")
                .WithMessage("vault record is unreadable");

            var polygon = () => resolver.CreateContext(typeof(PolygonHistoricalDataProvider));
            polygon.Should().NotThrow("one unreadable record must not take down other providers");
        }
    }

    [Fact]
    public async Task PrepareAsync_HonoursCancellation()
    {
        var store = new Mock<IProviderCredentialStore>();
        var resolver = new StoredProviderCredentialResolver(store.Object, new EnvironmentCredentialResolver());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await resolver.PrepareAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ScopedResolver_PreparesTheScopedRecordAndNeverTheProviderWideOne()
    {
        var scope = new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "paper");
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Alpaca("global-key", "global-secret"));
        await store.SaveScopedAsync(Alpaca("scoped-key", "scoped-secret"), scope);
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), scope);

        var prepared = await resolver.PrepareAsync();
        using (prepared.Activate())
        {
            AlpacaKey(resolver).Should().Be("scoped-key");
        }
    }

    [Fact]
    public async Task EnvironmentResolver_PrepareIsANoOp()
    {
        IProviderCredentialResolver resolver = new EnvironmentCredentialResolver();

        var prepared = await resolver.PrepareAsync();

        prepared.Should().BeSameAs(PreparedProviderCredentials.None);
        using (prepared.Activate())
        {
            resolver.CreateContext(
                    typeof(AlpacaHistoricalDataProvider),
                    new Dictionary<string, string?> { ["ALPACA_KEY_ID"] = "configured-key" })
                .Get("ALPACA_KEY_ID").Should().Be("configured-key");
        }
    }
}
