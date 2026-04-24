using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.Application.SecurityMaster;

public sealed class SecurityMasterMappingInteropTests
{
    [Fact]
    public void SnapshotWrapper_CommonTermsJson_RoundTripsExtendedCommonFields()
    {
        var snapshot = CreateSnapshotWrapper();

        using var json = JsonDocument.Parse(snapshot.CommonTermsJson);
        var common = json.RootElement;

        common.GetProperty("primaryListingMic").GetString().Should().Be("XNYS");
        common.GetProperty("countryOfIncorporation").GetString().Should().Be("US");
        common.GetProperty("settlementCycleDays").GetInt32().Should().Be(2);
        common.GetProperty("holidayCalendarId").GetString().Should().Be("NYSE");
    }

    [Fact]
    public void ToProjection_And_ToRecord_PreserveExtendedCommonFields()
    {
        var snapshot = CreateSnapshotWrapper();

        var projection = SecurityMasterMapping.ToProjection(snapshot);

        projection.CommonTerms.GetProperty("primaryListingMic").GetString().Should().Be("XNYS");
        projection.CommonTerms.GetProperty("countryOfIncorporation").GetString().Should().Be("US");
        projection.CommonTerms.GetProperty("settlementCycleDays").GetInt32().Should().Be(2);
        projection.CommonTerms.GetProperty("holidayCalendarId").GetString().Should().Be("NYSE");

        var record = SecurityMasterMapping.ToRecord(projection);

        record.Common.PrimaryListingMic.Should().NotBeNull();
        record.Common.PrimaryListingMic!.Value.Should().Be("XNYS");
        record.Common.CountryOfIncorporation.Should().NotBeNull();
        record.Common.CountryOfIncorporation!.Value.Should().Be("US");
        record.Common.SettlementCycleDays.Should().NotBeNull();
        record.Common.SettlementCycleDays!.Value.Should().Be(2);
        record.Common.HolidayCalendarId.Should().NotBeNull();
        record.Common.HolidayCalendarId!.Value.Should().Be("NYSE");
    }

    private static SecurityMasterSnapshotWrapper CreateSnapshotWrapper()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Equity",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Interop Common Terms Test",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Interop Issuer",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01,
                primaryListingMic = "xnys",
                countryOfIncorporation = "US",
                settlementCycleDays = 2,
                holidayCalendarId = "NYSE"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { shareClass = "A" }),
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "INTROP", true, now)
            ],
            EffectiveFrom: now,
            SourceSystem: "interop-tests",
            UpdatedBy: "interop-tests",
            SourceRecordId: "interop-extended-common-fields",
            Reason: "Validate F# <-> C# common term round-trip");

        var command = SecurityMasterMapping.ToCreateCommand(request);
        var result = SecurityMasterCommandFacade.Create(command);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        return result.Snapshot;
    }
}
