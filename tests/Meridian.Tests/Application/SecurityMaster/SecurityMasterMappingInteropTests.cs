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

    [Fact]
    public void OptionTerms_SerializeDeserialize_PreservesAllReadableFields()
    {
        var underlyingId = Guid.NewGuid();
        var terms = RoundTripAssetSpecificTerms("Option", new
        {
            underlyingId,
            putCall = "Call",
            strike = 105.5m,
            expiry = "2026-12-18",
            multiplier = 100m,
            optChainId = "SPY-20261218",
            exerciseStyle = "American",
            settlementType = "Physical",
            isAdjusted = true,
            lastTradingDt = "2026-12-17"
        });

        terms.GetProperty("underlyingId").GetString().Should().Be(underlyingId.ToString());
        terms.GetProperty("putCall").GetString().Should().Be("Call");
        terms.GetProperty("strike").GetDecimal().Should().Be(105.5m);
        terms.GetProperty("expiry").GetString().Should().Be("2026-12-18");
        terms.GetProperty("multiplier").GetDecimal().Should().Be(100m);
        // Fields the C# deserializer reads that were previously dropped by the F# serializer.
        terms.GetProperty("optChainId").GetString().Should().Be("SPY-20261218");
        terms.GetProperty("exerciseStyle").GetString().Should().Be("American");
        terms.GetProperty("settlementType").GetString().Should().Be("Physical");
        terms.GetProperty("isAdjusted").GetBoolean().Should().BeTrue();
        terms.GetProperty("lastTradingDt").GetString().Should().Be("2026-12-17");
    }

    [Fact]
    public void FutureTerms_SerializeDeserialize_PreservesAllReadableFields()
    {
        var terms = RoundTripAssetSpecificTerms("Future", new
        {
            rootSymbol = "ES",
            contractMonth = "2026-12",
            expiry = "2026-12-18",
            multiplier = 50m,
            lastTradingDt = "2026-12-18",
            firstNoticeDt = "2026-11-30",
            deliveryMonthDt = "2026-12-01",
            settlementType = "Cash",
            deliveryLocationCode = "XCME",
            isRollTarget = true,
            rollWindowDays = 5
        });

        terms.GetProperty("rootSymbol").GetString().Should().Be("ES");
        terms.GetProperty("contractMonth").GetString().Should().Be("2026-12");
        terms.GetProperty("multiplier").GetDecimal().Should().Be(50m);
        // Fields the C# deserializer reads that were previously dropped by the F# serializer.
        terms.GetProperty("lastTradingDt").GetString().Should().Be("2026-12-18");
        terms.GetProperty("firstNoticeDt").GetString().Should().Be("2026-11-30");
        terms.GetProperty("deliveryMonthDt").GetString().Should().Be("2026-12-01");
        terms.GetProperty("settlementType").GetString().Should().Be("Cash");
        terms.GetProperty("deliveryLocationCode").GetString().Should().Be("XCME");
        terms.GetProperty("isRollTarget").GetBoolean().Should().BeTrue();
        terms.GetProperty("rollWindowDays").GetInt32().Should().Be(5);
    }

    [Fact]
    public void InvestmentFundTerms_SerializeDeserialize_RoundTripsWithoutThrowing()
    {
        // Before the deserializer gained an InvestmentFund case, this class was serialized by F# but
        // ToSecurityKind threw "Unsupported asset class 'InvestmentFund'" on read.
        var terms = RoundTripAssetSpecificTerms("InvestmentFund", new
        {
            fundType = "ETF",
            fundFamily = "Vanguard",
            navCurrency = "USD",
            distributionPolicy = "Distributing",
            isStableNav = false,
            pricingSource = "Bloomberg"
        });

        terms.GetProperty("fundType").GetString().Should().Be("ETF");
        terms.GetProperty("fundFamily").GetString().Should().Be("Vanguard");
        terms.GetProperty("navCurrency").GetString().Should().Be("USD");
        terms.GetProperty("distributionPolicy").GetString().Should().Be("Distributing");
        terms.GetProperty("isStableNav").GetBoolean().Should().BeFalse();
        terms.GetProperty("pricingSource").GetString().Should().Be("Bloomberg");
    }

    /// <summary>
    /// Serializes a fully populated asset-specific-terms payload through the F# snapshot writer, reads
    /// it back through the C# <see cref="SecurityMasterMapping"/> deserializer, and re-serializes it,
    /// asserting the F# → C# → F# loop is byte-stable. Returns the serialized asset-specific-terms JSON
    /// so callers can assert individual field preservation.
    /// </summary>
    private static JsonElement RoundTripAssetSpecificTerms(string assetClass, object assetSpecificTerms)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = $"{assetClass} round-trip",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "RTRIP", true, now)
            ],
            EffectiveFrom: now,
            SourceSystem: "interop-tests",
            UpdatedBy: "interop-tests",
            SourceRecordId: $"{assetClass}-symmetry",
            Reason: "Validate F# <-> C# asset-specific round-trip symmetry");

        var command = SecurityMasterMapping.ToCreateCommand(request);
        var result = SecurityMasterCommandFacade.Create(command);

        result.IsSuccess.Should().BeTrue(
            string.Join("; ", result.ErrorDetails.Select(error => $"{error.Code}: {error.Message}")));

        var firstPass = result.Snapshot.AssetSpecificTermsJson;

        // Close the loop: F# JSON -> C# SecurityKind -> F# JSON must be stable, proving the deserializer
        // reads back exactly what the serializer wrote (no dropped or renamed fields).
        var projection = SecurityMasterMapping.ToProjection(result.Snapshot);
        var record = SecurityMasterMapping.ToRecord(projection);
        var secondPass = new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson;

        secondPass.Should().Be(firstPass);

        return JsonDocument.Parse(firstPass).RootElement.Clone();
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
