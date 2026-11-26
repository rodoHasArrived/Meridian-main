using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Architecture;

public sealed class SecurityMasterContractAdapterTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void QueryAliases_PreserveHostLifetimesAcrossScopes(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(ISecurityMasterQueryService),
            _ => NSubstitute.Substitute.For<ISecurityMasterQueryService>(), lifetime));
        services.Add(new ServiceDescriptor(typeof(ISecurityResolver), _ => new RecordingResolver(), lifetime));
        services.Add(new ServiceDescriptor(typeof(ILivePositionCorporateActionAdjuster), _ => new RecordingAdjuster(), lifetime));
        services.AddSecurityMasterContractAdapters();
        var aliases = new[]
        {
            typeof(ISecurityIdentityQueryService), typeof(ISecurityTradingParametersQueryService),
            typeof(ISecurityCorporateActionsQueryService), typeof(ISecurityResolver),
            typeof(ILivePositionCorporateActionAdjuster)
        };
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        foreach (var alias in aliases)
        {
            var initial = first.ServiceProvider.GetRequiredService(alias);
            var repeated = first.ServiceProvider.GetRequiredService(alias);
            var otherScope = second.ServiceProvider.GetRequiredService(alias);
            if (lifetime == ServiceLifetime.Transient)
                repeated.Should().NotBeSameAs(initial);
            else
                repeated.Should().BeSameAs(initial);
            if (lifetime == ServiceLifetime.Singleton)
                otherScope.Should().BeSameAs(initial);
            else
                otherScope.Should().NotBeSameAs(initial);
        }
    }

    [Fact]
    public void ResolverOnlyHost_DoesNotAdvertiseUnavailableQueryContracts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecurityResolver>(new RecordingResolver());

        services.AddSecurityMasterContractAdapters();

        services.Select(descriptor => descriptor.ServiceType).Should().NotContain(
            [typeof(ISecurityIdentityQueryService), typeof(ISecurityTradingParametersQueryService), typeof(ISecurityCorporateActionsQueryService)]);
    }

    private sealed class RecordingResolver : ISecurityResolver
    {
        public ResolveSecurityRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
        {
            Request = request;
            CancellationToken = ct;
            return Task.FromResult<Guid?>(SecurityId);
        }
    }

    private sealed class RecordingAdjuster : ILivePositionCorporateActionAdjuster
    {
        public decimal? ReceivedTotalBasis { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
            string ticker, decimal quantity, decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default)
            => Adjust(ticker, quantity, costBasis, null, ct);

        public Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
            string ticker, decimal quantity, decimal costBasis, decimal totalCostBasis,
            DateTimeOffset positionOpenedAt, CancellationToken ct = default)
            => Adjust(ticker, quantity, costBasis, totalCostBasis, ct);

        private Task<PositionCorporateActionAdjustment> Adjust(
            string ticker, decimal quantity, decimal costBasis, decimal? totalCostBasis, CancellationToken ct)
        {
            ReceivedTotalBasis = totalCostBasis;
            CancellationToken = ct;
            return Task.FromResult(new PositionCorporateActionAdjustment(
                ticker, quantity, quantity * 2m, costBasis, 1m / 6m, 1)
            {
                AdjustedTotalCostBasis = totalCostBasis
            });
        }
    }
}
