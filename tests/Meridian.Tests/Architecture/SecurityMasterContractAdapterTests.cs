using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using LegacyAdjustment = Meridian.Application.SecurityMaster.CorporateActions.PositionCorporateActionAdjustment;
using LegacyAdjuster = Meridian.Application.SecurityMaster.CorporateActions.ILivePositionCorporateActionAdjuster;
using LegacyResolver = Meridian.Application.SecurityMaster.ISecurityResolver;

namespace Meridian.Tests.Architecture;

#pragma warning disable CS0618 // These tests deliberately exercise compatibility contracts.
public sealed class SecurityMasterContractAdapterTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset OpenedAt = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ResolverOnlyHost_DoesNotAdvertiseUnavailableQueryContracts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecurityResolver>(new RecordingResolver());

        services.AddSecurityMasterContractAdapters();

        services.Select(descriptor => descriptor.ServiceType).Should().NotContain(
            [typeof(ISecurityIdentityQueryService), typeof(ISecurityTradingParametersQueryService), typeof(ISecurityCorporateActionsQueryService)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolverRegistration_BridgesBothContractGenerations(bool registerLegacy)
    {
        var services = new ServiceCollection();
        var resolver = new RecordingResolver();
        if (registerLegacy)
            services.AddSingleton<LegacyResolver>(new LegacyResolverImplementation(resolver));
        else
            services.AddSingleton<ISecurityResolver>(resolver);
        services.AddSecurityMasterContractAdapters();
        services.AddSecurityMasterContractAdapters();
        using var provider = services.BuildServiceProvider();
        var request = new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, "BASIS", null, OpenedAt);
        using var cancellation = new CancellationTokenSource();

        var canonicalResult = await provider.GetRequiredService<ISecurityResolver>().ResolveAsync(request, cancellation.Token);
        var legacyResult = await provider.GetRequiredService<LegacyResolver>().ResolveAsync(request, cancellation.Token);

        canonicalResult.Should().Be(SecurityId);
        legacyResult.Should().Be(canonicalResult);
        resolver.Request.Should().BeSameAs(request);
        resolver.CancellationToken.Should().Be(cancellation.Token);
        provider.GetServices<ISecurityResolver>().Should().ContainSingle();
        provider.GetServices<LegacyResolver>().Should().ContainSingle();
        if (registerLegacy)
            provider.GetRequiredService<ISecurityResolver>().Should().BeSameAs(provider.GetRequiredService<LegacyResolver>());
        else
            provider.GetRequiredService<ISecurityResolver>().Should().BeSameAs(resolver);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalAdjusterRegistration_PreservesBothOverloadsThroughLegacyAdapter(bool supplyTotalBasis)
    {
        var services = new ServiceCollection();
        var adjuster = new RecordingAdjuster();
        services.AddSingleton<ILivePositionCorporateActionAdjuster>(adjuster);
        services.AddSecurityMasterContractAdapters();
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var legacy = provider.GetRequiredService<LegacyAdjuster>();

        var result = supplyTotalBasis
            ? await legacy.AdjustPositionAsync("BASIS", 3m, 1m / 3m, 1m, OpenedAt, cancellation.Token)
            : await legacy.AdjustPositionAsync("BASIS", 3m, 1m / 3m, OpenedAt, cancellation.Token);

        result.AdjustedQuantity.Should().Be(6m);
        result.AdjustedCostBasis.Should().Be(1m / 6m);
        result.AdjustedTotalCostBasis.Should().Be(supplyTotalBasis ? 1m : null);
        adjuster.ReceivedTotalBasis.Should().Be(supplyTotalBasis ? 1m : null);
        adjuster.CancellationToken.Should().Be(cancellation.Token);
        provider.GetRequiredService<ILivePositionCorporateActionAdjuster>().Should().BeSameAs(adjuster);
    }

    [Fact]
    public async Task LegacyAdjusterRegistration_RetainsLegacyFallbackThroughCanonicalTotalBasisOverload()
    {
        var services = new ServiceCollection();
        var adjuster = new LegacyQuoteOnlyAdjuster();
        services.AddSingleton<LegacyAdjuster>(adjuster);
        services.AddSecurityMasterContractAdapters();
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();

        var result = await provider.GetRequiredService<ILivePositionCorporateActionAdjuster>()
            .AdjustPositionAsync("BASIS", 3m, 1m / 3m, 1m, OpenedAt, cancellation.Token);

        result.AdjustedQuantity.Should().Be(6m);
        result.AdjustedTotalCostBasis.Should().BeNull();
        adjuster.CancellationToken.Should().Be(cancellation.Token);
        provider.GetRequiredService<ILivePositionCorporateActionAdjuster>().Should().BeSameAs(adjuster);
    }

    [Fact]
    public async Task LegacyAdjusterRegistration_PreservesImplementedTotalBasisOverload()
    {
        var services = new ServiceCollection();
        var adjuster = new LegacyBasisAwareAdjuster();
        services.AddSingleton<LegacyAdjuster>(adjuster);
        services.AddSecurityMasterContractAdapters();
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();

        var result = await provider.GetRequiredService<ILivePositionCorporateActionAdjuster>()
            .AdjustPositionAsync("BASIS", 3m, 1m / 3m, 1m, OpenedAt, cancellation.Token);

        result.AdjustedTotalCostBasis.Should().Be(1m);
        adjuster.CancellationToken.Should().Be(cancellation.Token);
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

    private sealed class LegacyResolverImplementation(RecordingResolver resolver) : LegacyResolver
    {
        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => resolver.ResolveAsync(request, ct);
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

    private sealed class LegacyQuoteOnlyAdjuster : LegacyAdjuster
    {
        public CancellationToken CancellationToken { get; private set; }

        public Task<LegacyAdjustment> AdjustPositionAsync(
            string ticker, decimal quantity, decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default)
        {
            CancellationToken = ct;
            return Task.FromResult(new LegacyAdjustment(ticker, quantity, quantity * 2m, costBasis, costBasis / 2m, 1));
        }
    }

    private sealed class LegacyBasisAwareAdjuster : LegacyAdjuster
    {
        public CancellationToken CancellationToken { get; private set; }

        public Task<LegacyAdjustment> AdjustPositionAsync(
            string ticker, decimal quantity, decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default)
            => throw new InvalidOperationException("The retained total-basis overload must be preserved.");

        public Task<LegacyAdjustment> AdjustPositionAsync(
            string ticker, decimal quantity, decimal costBasis, decimal totalCostBasis,
            DateTimeOffset positionOpenedAt, CancellationToken ct = default)
        {
            CancellationToken = ct;
            return Task.FromResult(new LegacyAdjustment(ticker, quantity, quantity * 2m, costBasis, costBasis / 2m, 1)
            {
                AdjustedTotalCostBasis = totalCostBasis
            });
        }
    }
}
#pragma warning restore CS0618
