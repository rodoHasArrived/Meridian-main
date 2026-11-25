using FluentAssertions;
using Meridian.Contracts.EntityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.EntityMaster;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.SecurityMaster.ValidTime;

/// <summary>
/// ADR-033 consumer: portfolio gross exposure grouped by the issuer each security is linked to in the
/// entity master. Two share classes of one issuer concentrate together; unlinked exposure is reported.
/// </summary>
public sealed class IssuerConcentrationServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 25);
    private static readonly Guid ClassA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid ClassC = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    private static readonly Guid OtherIssuerSecurity = Guid.Parse("d0000000-0000-0000-0000-00000000000d");

    [Fact]
    public async Task ShareClassesOfOneIssuer_ConcentrateTogether_AndUnlinkedExposureIsReported()
    {
        var store = new InMemoryEntityMasterStore();
        var parent = await store.CreateEntityAsync(new CreateLegalEntityRequest("Synthetic Holdings Inc", null, "1111", "US"), "test");
        var issuer = await store.CreateEntityAsync(new CreateLegalEntityRequest("Synthetic Search LLC", null, "2222", "US"), "test");
        var other = await store.CreateEntityAsync(new CreateLegalEntityRequest("Other Issuer Corp", null, "3333", "US"), "test");
        await store.SetParentAsync(issuer.EntityId, parent.EntityId, new DateOnly(2020, 1, 1), "test");
        await store.LinkIssuerAsync(ClassA, issuer.EntityId, new DateOnly(2020, 1, 1), "test");
        await store.LinkIssuerAsync(ClassC, issuer.EntityId, new DateOnly(2020, 1, 1), "test");
        await store.LinkIssuerAsync(OtherIssuerSecurity, other.EntityId, new DateOnly(2020, 1, 1), "test");
        var resolver = new TickerResolver(new Dictionary<string, Guid>
        {
            ["SYNA"] = ClassA,
            ["SYNC"] = ClassC,
            ["OTHR"] = OtherIssuerSecurity,
            ["UNLK"] = Guid.Parse("e0000000-0000-0000-0000-00000000000e")
        });
        var service = new IssuerConcentrationService(store, resolver);

        var report = await service.ComputeAsync(
            [("SYNA", 300m), ("SYNC", 200m), ("OTHR", 400m), ("UNLK", 50m), ("AMBG", 50m)],
            totalGross: 1000m,
            AsOf);

        report.Should().NotBeNull();
        report!.Issuers.Should().HaveCount(2);
        var top = report.Issuers[0];
        top.IssuerEntityId.Should().Be(issuer.EntityId);
        top.GrossExposure.Should().Be(500m, "both share classes belong to the same issuer");
        top.ShareOfGross.Should().Be(0.5m);
        top.UltimateParentEntityId.Should().Be(parent.EntityId);
        top.Symbols.Should().Equal("SYNA", "SYNC");
        report.Issuers[1].GrossExposure.Should().Be(400m);
        report.LinkedGrossExposure.Should().Be(900m);
        report.UnlinkedGrossExposure.Should().Be(100m);
        report.UnlinkedSymbols.Should().Equal("AMBG", "UNLK");
    }

    [Fact]
    public async Task IssuerLinkIsReadAsOfTheReportDate()
    {
        var store = new InMemoryEntityMasterStore();
        var before = await store.CreateEntityAsync(new CreateLegalEntityRequest("Predecessor Corp", null, null, null), "test");
        var after = await store.CreateEntityAsync(new CreateLegalEntityRequest("Successor Corp", null, null, null), "test");
        await store.LinkIssuerAsync(ClassA, before.EntityId, new DateOnly(2020, 1, 1), "test");
        await store.LinkIssuerAsync(ClassA, after.EntityId, new DateOnly(2026, 12, 1), "test");
        var service = new IssuerConcentrationService(store, new TickerResolver(new Dictionary<string, Guid> { ["SYNA"] = ClassA }));

        var report = await service.ComputeAsync([("SYNA", 100m)], 100m, AsOf);

        report!.Issuers.Should().ContainSingle().Which.LegalName.Should().Be("Predecessor Corp");
    }

    [Fact]
    public async Task WithoutResolver_NoConcentrationIsComputed()
    {
        var service = new IssuerConcentrationService(new InMemoryEntityMasterStore(), resolver: null);

        var report = await service.ComputeAsync([("SYNA", 100m)], 100m, AsOf);

        report.Should().BeNull();
    }

    private sealed class TickerResolver : ISecurityResolver
    {
        private readonly IReadOnlyDictionary<string, Guid> _tickers;

        public TickerResolver(IReadOnlyDictionary<string, Guid> tickers) => _tickers = tickers;

        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult<Guid?>(_tickers.TryGetValue(request.IdentifierValue, out var id) ? id : null);

        public Task<SecurityCanonicalResolution> ResolveCanonicalAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult(request.IdentifierValue == "AMBG"
                ? new SecurityCanonicalResolution(SecurityResolutionStatus.Ambiguous, null, [ClassA, ClassC])
                : _tickers.TryGetValue(request.IdentifierValue, out var id)
                    ? SecurityCanonicalResolution.Resolve(id)
                    : SecurityCanonicalResolution.NotFound);
    }
}
