using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class DataVendorEntitlementServiceTests
{
    private static (DataVendorEntitlementService Service, IDataVendorEntitlementStore Store) BuildSut()
    {
        var store = Substitute.For<IDataVendorEntitlementStore>();
        var service = new DataVendorEntitlementService(store);
        return (service, store);
    }

    [Fact]
    public async Task GetExpiringAsync_DelegatesToStoreWithComputedCutoff()
    {
        var (sut, store) = BuildSut();
        var expiringSoon = MakeEntitlement("CUSIP Global Services", DateTimeOffset.UtcNow.AddDays(10), DataVendorEntitlementStatus.ExpiringSoon);

        store.GetExpiringAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<DataVendorEntitlementDto> { expiringSoon });

        var before = DateTimeOffset.UtcNow.AddDays(30);
        var result = await sut.GetExpiringAsync(withinDays: 30);
        var after = DateTimeOffset.UtcNow.AddDays(30);

        result.Should().HaveCount(1);
        result[0].VendorName.Should().Be("CUSIP Global Services");
        await store.Received(1).GetExpiringAsync(
            Arg.Is<DateTimeOffset>(cutoff => cutoff >= before && cutoff <= after),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_ThrowsArgumentException_WhenVendorNameEmpty()
    {
        var (sut, _) = BuildSut();
        var request = new UpsertDataVendorEntitlementRequest(
            string.Empty, DataVendorDataType.Identifiers, null,
            DateTimeOffset.UtcNow, null, null, false, null, 30, "operator");

        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpsertAsync(request));
    }

    [Fact]
    public async Task UpsertAsync_PersistsEntitlementWithDerivedStatus()
    {
        var (sut, store) = BuildSut();
        var request = new UpsertDataVendorEntitlementRequest(
            "LSEG/Refinitiv", DataVendorDataType.Pricing, "LSEG-2024-001",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(120),
            null, true, "licensing@firm.com", 30, "operator");

        store.UpsertAsync(Arg.Any<DataVendorEntitlementDto>(), Arg.Any<CancellationToken>())
            .Returns(args => (DataVendorEntitlementDto)args[0]);

        var result = await sut.UpsertAsync(request);

        result.VendorName.Should().Be("LSEG/Refinitiv");
        result.DataType.Should().Be(DataVendorDataType.Pricing);
        result.Status.Should().Be(DataVendorEntitlementStatus.Active);
        result.RequiresDirectClientContract.Should().BeTrue();
        await store.Received(1).UpsertAsync(Arg.Any<DataVendorEntitlementDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_ThrowsWhenEntitlementNotFound()
    {
        var (sut, store) = BuildSut();
        store.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DataVendorEntitlementDto?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DeactivateAsync(Guid.NewGuid(), "operator"));
    }

    private static DataVendorEntitlementDto MakeEntitlement(
        string vendorName, DateTimeOffset effectiveTo, DataVendorEntitlementStatus status)
        => new(
            Guid.NewGuid(), vendorName, DataVendorDataType.Pricing, null,
            DateTimeOffset.UtcNow.AddDays(-30), effectiveTo,
            null, false, null, 30, status, "test", DateTimeOffset.UtcNow);
}
