using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Keeps the Fund Structure workspace scenarios on their explicitly seeded strategy-run ledger
/// source even when the certification process publishes PostgreSQL settings for durable-ledger
/// integration tests. Durable journal persistence and hydration are covered by their owning
/// database fixtures.
/// </summary>
public sealed class FundStructureEndpointTestFixture : IAsyncLifetime
{
    private static readonly ObjectFactory<FundOperationsWorkspaceReadService> WorkspaceFactory =
        ActivatorUtilities.CreateFactory<FundOperationsWorkspaceReadService>(
            [typeof(ILedgerJournalStore)]);

    private readonly EndpointTestFixture _fixture = new(static services =>
    {
        services.RemoveAll<FundOperationsWorkspaceReadService>();
        services.AddSingleton(static provider =>
            WorkspaceFactory(provider, [null]));
    });

    public HttpClient Client => _fixture.Client;

    public IServiceProvider Services => _fixture.Services;

    public HttpClient CreatePermittedClient(params UserPermission[] permissions)
        => _fixture.CreatePermittedClient(permissions);

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();
}
