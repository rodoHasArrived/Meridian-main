using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using DayCountConvention = Meridian.Contracts.SecurityMaster.DayCountConvention;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityDayCountTermTests
{
    public static IEnumerable<object[]> DayCountTerms()
    {
        yield return ["Bond", """{"maturity":"2030-01-01","couponType":"Fixed","couponRate":0.05,"dayCount":"VALUE"}""", "dayCount"];
        yield return ["Deposit", """{"depositType":"TimeDeposit","institutionName":"Bank","dayCount":"VALUE"}""", "dayCount"];
        yield return ["CertificateOfDeposit", """{"issuerName":"Bank","maturity":"2030-01-01","dayCount":"VALUE"}""", "dayCount"];
        yield return ["CommercialPaper", """{"issuerName":"Bank","maturity":"2030-01-01","dayCount":"VALUE"}""", "dayCount"];
        yield return ["Swap", """{"swapType":"InterestRate","effectiveDate":"2026-01-01","maturityDate":"2030-01-01","notional":1000000,"legs":[{"legType":"Fixed","currency":"USD","dayCount":"VALUE"}]}""", "legs"];
        yield return ["CreditDefaultSwap", """{"referenceEntity":"Issuer","protectionSide":"Buy","notional":1000000,"fixedCouponBps":100,"effectiveDate":"2026-01-01","maturityDate":"2030-01-01","dayCount":"VALUE"}""", "dayCount"];
        yield return ["Swaption", """{"payerReceiver":"Payer","strikeRate":0.05,"expiry":"2027-01-01","underlyingMaturityDate":"2030-01-01","notional":1000000,"fixedLegDayCount":"VALUE"}""", "fixedLegDayCount"];
        yield return ["CapFloor", """{"capFloorType":"Cap","referenceIndex":"SOFR","capStrike":0.05,"notional":1000000,"effectiveDate":"2026-01-01","maturityDate":"2030-01-01","dayCount":"VALUE"}""", "dayCount"];
    }

    [Theory]
    [MemberData(nameof(DayCountTerms))]
    public void WritesRejectInvalidDayCountBeforeAnyCommandIsSaved(string assetClass, string terms, string field)
    {
        foreach (var invalid in new[] { "lunar", "ACT/999", "garbage360", "Unknown" })
        {
            var act = () => SecurityMasterMapping.ToCreateCommand(Request(assetClass, terms.Replace("VALUE", invalid)));
            act.Should().Throw<InvalidOperationException>().WithMessage("*day*count*", "{0} must reject {1}", field, invalid);
        }
    }

    [Theory]
    [MemberData(nameof(DayCountTerms))]
    public void EveryDefinedConventionSurvivesFlatTermsCodec(string assetClass, string terms, string field)
    {
        foreach (var convention in Enum.GetValues<DayCountConvention>().Where(value => value != DayCountConvention.Unknown))
        {
            var command = SecurityMasterMapping.ToCreateCommand(Request(assetClass, terms.Replace("VALUE", convention.ToString())));
            var result = SecurityMasterCommandFacade.Create(command);
            result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
            var projection = SecurityMasterMapping.ToProjection(result.Snapshot);
            var dayCount = field == "legs"
                ? projection.AssetSpecificTerms.GetProperty("legs")[0].GetProperty("dayCount")
                : projection.AssetSpecificTerms.GetProperty(field);
            dayCount.GetString().Should().Be(DayCountConventions.TermLabel(convention));
            var restored = SecurityMasterMapping.ToRecord(projection);
            new SecurityMasterSnapshotWrapper(restored).AssetSpecificTermsJson.Should().Be(result.Snapshot.AssetSpecificTermsJson);
        }
    }

    [Theory]
    [InlineData("dayCount", null)]
    [InlineData("dayCount", "")]
    [InlineData("dayCount", "  ")]
    [InlineData("dayCountConvention", null)]
    [InlineData("dayCountConvention", "")]
    [InlineData("dayCountConvention", "  ")]
    [InlineData("dayCountBasis", null)]
    [InlineData("dayCountBasis", "")]
    [InlineData("dayCountBasis", "  ")]
    public void SwapOptionalDayCount_BlankAndNullAliasesRemainUnset(string key, string? value)
    {
        var leg = new Dictionary<string, object?> { ["legType"] = "Fixed", ["currency"] = "USD", [key] = value };
        var terms = JsonSerializer.Serialize(new
        {
            effectiveDate = "2026-01-01",
            maturityDate = "2030-01-01",
            legs = new[] { leg }
        });
        var command = SecurityMasterMapping.ToCreateCommand(Request("Swap", terms));
        var swap = command.Kind.Should().BeOfType<SecurityKind.Swap>().Subject;
        swap.Item.Legs.Single().DayCount.Should().BeNull();
    }

    [Fact]
    public void FSharpTermFieldsUseTheSharedEnum()
    {
        var fields = typeof(BondTerms).Assembly.GetTypes()
            .SelectMany(type => type.GetProperties())
            .Where(property => property.DeclaringType?.Namespace == typeof(BondTerms).Namespace
                && (property.Name.Equals("dayCount", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("fixedLegDayCount", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        fields.Should().NotBeEmpty();
        fields.Should().OnlyContain(property => property.PropertyType == typeof(Microsoft.FSharp.Core.FSharpOption<DayCountConvention>));
    }

    private static CreateSecurityRequest Request(string assetClass, string terms)
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        return new CreateSecurityRequest(Guid.NewGuid(), assetClass,
            JsonSerializer.SerializeToElement(new { displayName = "Day-count test", currency = "USD" }),
            JsonDocument.Parse(terms).RootElement.Clone(),
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "DAYCOUNT", true, now)],
            now, "test", "test", null, "Validate typed day-count terms");
    }
}
