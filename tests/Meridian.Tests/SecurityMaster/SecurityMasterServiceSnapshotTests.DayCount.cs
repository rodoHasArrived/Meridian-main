using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterServiceSnapshotTests
{
    [Fact]
    public async Task CommonOnlyAmendment_RefusesToEraseInvalidHistoricalDayCount()
    {
        var securityId = Guid.NewGuid();
        var events = Substitute.For<ISecurityMasterEventStore>();
        var snapshots = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = CreateDurableProjectionStore();
        var projection = CreateProjection(securityId, "Bond", SecurityStatusDto.Active, "Legacy day count", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                maturity = "2030-01-01",
                couponType = "Fixed",
                couponRate = 0.05m,
                dayCount = "lunar"
            })
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);
        var service = new SecurityMasterService(events, snapshots, store,
            new SecurityMasterAggregateRebuilder(events, snapshots), new SecurityMasterOptions(),
            NullLogger<SecurityMasterService>.Instance);

        SecurityMasterMapping.ToRecord(projection).Kind.Should().BeOfType<Meridian.FSharp.Domain.SecurityKind.Bond>();
        var act = () => service.AmendTermsAsync(TermsAmendRequest(securityId, null));
        await act.Should().ThrowAsync<SecurityMasterWriteRefusedException>().WithMessage("*day-count*complete asset-terms*");
    }
}
