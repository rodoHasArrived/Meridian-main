using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
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

    [Theory]
    [InlineData("FullVoting")]
    [InlineData("LimitedVoting")]
    [InlineData("NonVoting")]
    [InlineData("DualClass")]
    [InlineData("SuperVoting")]
    // Unrecognized tokens are carried verbatim by VotingRightsCat.OtherVotingRights, so a vendor's
    // own spelling must survive the loop exactly as the five canonical cases do.
    [InlineData("Restricted")]
    public void EquityTerms_SerializeDeserialize_PreservesVotingRightsCat(string votingRightsCat)
    {
        var terms = RoundTripAssetSpecificTerms("Equity", new
        {
            shareClass = "A",
            votingRightsCat,
            classification = "Common"
        });

        terms.GetProperty("shareClass").GetString().Should().Be("A");
        terms.GetProperty("classification").GetString().Should().Be("Common");

        // Asserted via TryGetProperty rather than GetProperty so a dropped field reports as a
        // readable expectation failure instead of a bare KeyNotFoundException.
        //
        // Note the byte-stability check inside RoundTripAssetSpecificTerms cannot catch this on its
        // own: a field the serializer never emits is stably absent from both passes, so first and
        // second pass agree. Only comparing against the *input* value surfaces the loss.
        terms.TryGetProperty("votingRightsCat", out var emitted).Should().BeTrue(
            "the F# snapshot serializer must emit votingRightsCat - SecurityMasterMapping.ToSecurityKind "
            + "reads it back into EquityTerms and PostgresSecurityMasterStore projects it into the "
            + "voting_rights_cat column, so dropping it on write silently nulls that column");
        emitted.GetString().Should().Be(votingRightsCat);
    }

    [Fact]
    public void ToCreateCommand_EquityVotingRightsCat_IsReadIntoTheDomain()
    {
        // Localizes the round-trip expectation above. The read side already parses votingRightsCat
        // into EquityTerms, so if the serialized payload lacks the key the gap is on the write side
        // (Interop.SecurityMaster.fs) rather than the field being unsupported end to end.
        var now = DateTimeOffset.UtcNow;
        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Equity",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Voting rights read-side check",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                shareClass = "B",
                votingRightsCat = "SuperVoting",
                classification = "Common"
            }),
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "VOTE", true, now)
            ],
            EffectiveFrom: now,
            SourceSystem: "interop-tests",
            UpdatedBy: "interop-tests",
            SourceRecordId: "equity-voting-rights-read",
            Reason: "Voting rights must reach the domain from the request payload");

        var command = SecurityMasterMapping.ToCreateCommand(request);

        var equity = command.Kind.Should().BeOfType<SecurityKind.Equity>().Subject;
        equity.Item.ShareClass.Should().NotBeNull();
        equity.Item.ShareClass!.Value.Should().Be("B");
        equity.Item.VotingRightsCat.Should().NotBeNull(
            "the deserializer maps the votingRightsCat token onto the VotingRightsCat DU");
        equity.Item.VotingRightsCat!.Value.IsSuperVoting.Should().BeTrue();
    }

    [Fact]
    public void ToCreateCommand_UnknownAssetClass_DegradesToOtherSecurityPreservingRawClass()
    {
        // A class this node has no deserializer for must not fail the read: it degrades to
        // OtherSecurity with the raw class retained as the category. Before this fallback, an
        // unknown class threw on every load of the row (the InvestmentFund incident above).
        var now = DateTimeOffset.UtcNow;
        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: "EsotericBasketCertificate",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Unknown-class security",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                subType = "Basket",
                issuerName = "Structured Issuer AG",
                maturity = "2032-06-30"
            }),
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "UNKNOWN-1", true, now)
            ],
            EffectiveFrom: now,
            SourceSystem: "interop-tests",
            UpdatedBy: "interop-tests",
            SourceRecordId: "unknown-class-fallback",
            Reason: "Unknown asset classes must degrade instead of throwing");

        var command = SecurityMasterMapping.ToCreateCommand(request);

        var otherSecurity = command.Kind.Should().BeOfType<SecurityKind.OtherSecurity>().Subject;
        otherSecurity.Item.Category.Should().Be("EsotericBasketCertificate");
        otherSecurity.Item.SubType.Value.Should().Be("Basket");
        otherSecurity.Item.IssuerName.Value.Should().Be("Structured Issuer AG");
        otherSecurity.Item.Maturity.Value.Should().Be(new DateOnly(2032, 6, 30));
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
