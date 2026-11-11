using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Contracts.UnitTests.SecurityMaster;

/// <summary>
/// Listing identity consistency (extensibility review D8): the primary listing market, a listing
/// identifier and a venue MIC are three concepts, checked against each other only where one names
/// the other.
/// </summary>
public sealed class SecurityListingIdentityTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("""{"primaryListingMic":"XNAS"}""", "XNAS")]
    [InlineData("""{"primaryListingMic":" xnas "}""", "XNAS")]
    [InlineData("""{"primaryListingMic":"XNAS","exchange":"NYSE"}""", "XNAS")]
    [InlineData("""{"exchange":"NASDAQ"}""", "XNAS")]
    [InlineData("""{"primaryListingMic":"NASDAQ","exchange":"NYSE"}""", "XNYS")]
    [InlineData("""{"primaryListingMic":"NASDAQ"}""", null)]
    [InlineData("""{}""", null)]
    public void PrimaryListingMarket_IsAWellFormedMic_ElseTheResolvedExchangeLabel(string commonTerms, string? expected)
        => SecurityListingIdentity.ResolvePrimaryListingMic(Json(commonTerms)).Should().Be(expected);

    [Fact]
    public void ConsistentRecord_HasNoFindings()
        => SecurityListingIdentity.Evaluate(
                Json("""{"primaryListingMic":"XNAS","exchange":"NASDAQ"}"""),
                [Listing("AAPL:XNAS", primary: true), Listing("AAPL:XLON", primary: false)],
                AsOf)
            .Should().BeEmpty("a secondary listing on another market is not an inconsistency");

    [Fact]
    public void MalformedPrimaryListingMic_IsReported_AndTheExchangeLabelStillResolves()
    {
        var findings = SecurityListingIdentity.Evaluate(Json("""{"primaryListingMic":"NASDAQ","exchange":"NASDAQ"}"""), [], AsOf);

        findings.Should().ContainSingle().Which.Should().Be(new SecurityListingIdentityFinding(
            SecurityListingIdentity.MalformedPrimaryListingMicCode,
            "commonTerms.primaryListingMic",
            "primaryListingMic 'NASDAQ' is not a four-character ISO 10383 market identifier code."));
    }

    [Fact]
    public void ExchangeLabelThatResolvesToAnotherMarket_IsReported()
    {
        var findings = SecurityListingIdentity.Evaluate(Json("""{"primaryListingMic":"XNAS","exchange":"NYSE"}"""), [], AsOf);

        var finding = findings.Should().ContainSingle().Subject;
        finding.Code.Should().Be(SecurityListingIdentity.ExchangeLabelDisagreesCode);
        finding.FieldPath.Should().Be("commonTerms.exchange");
        finding.Message.Should().Be("exchange 'NYSE' resolves to XNYS, but primaryListingMic is XNAS.");
    }

    [Fact]
    public void UnresolvableExchangeLabel_IsNotADisagreement()
        => SecurityListingIdentity.Evaluate(Json("""{"primaryListingMic":"XNAS","exchange":"Some Regional Board"}"""), [], AsOf)
            .Should().BeEmpty("a label the registry does not know says nothing about the market");

    [Fact]
    public void ActivePrimaryListingIdentifierOnAnotherMarket_IsReported()
    {
        var findings = SecurityListingIdentity.Evaluate(
            Json("""{"primaryListingMic":"XNAS"}"""), [Listing("AAPL:XNYS", primary: true)], AsOf);

        var finding = findings.Should().ContainSingle().Subject;
        finding.Code.Should().Be(SecurityListingIdentity.PrimaryListingIdentifierDisagreesCode);
        finding.Message.Should().Be("The primary listing identifier 'AAPL:XNYS' names XNYS, but the record's primary listing market is XNAS.");
    }

    [Fact]
    public void ListingIdentifiersThatAreNotInForceOrNotPrimary_AreNotCompared()
    {
        SecurityIdentifierDto[] identifiers =
        [
            Listing("AAPL:XNYS", primary: false),
            Listing("AAPL:XNYS", primary: true, validTo: AsOf),
            Listing("AAPL:XNYS", primary: true, validFrom: AsOf.AddDays(1)),
            new(SecurityIdentifierKind.Ticker, "AAPL:XNYS", true, AsOf.AddYears(-1))
        ];

        SecurityListingIdentity.Evaluate(Json("""{"primaryListingMic":"XNAS"}"""), identifiers, AsOf).Should().BeEmpty();
    }

    [Fact]
    public void RecordWithNoResolvableListingMarket_DoesNotJudgeItsIdentifiers()
        => SecurityListingIdentity.Evaluate(Json("""{}"""), [Listing("AAPL:XNYS", primary: true)], AsOf)
            .Should().BeEmpty("there is no stated market for the identifier to disagree with");

    private static SecurityIdentifierDto Listing(string value, bool primary, DateTimeOffset? validFrom = null, DateTimeOffset? validTo = null)
        => new(SecurityIdentifierKind.ListingId, value, primary, validFrom ?? AsOf.AddYears(-1), validTo);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
