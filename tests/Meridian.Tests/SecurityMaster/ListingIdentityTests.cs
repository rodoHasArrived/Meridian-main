using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Domain;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class ListingIdentityTests
{
    [Theory]
    [InlineData("XNAS", true)]
    [InlineData(" xnys ", true)]
    [InlineData("A1B2", true)]
    [InlineData("XNA", false)]
    [InlineData("XNASD", false)]
    [InlineData("XN-S", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Mic_AcceptsOnlyFourLettersOrDigits(string? value, bool valid)
    {
        Mic.TryParse(value, out var mic).Should().Be(valid);
        if (valid)
        {
            mic.Value.Should().Be(value!.Trim().ToUpperInvariant());
        }
    }

    [Fact]
    public void Mic_ReportsWhetherItIsInTheBundledReference()
    {
        Mic.Parse("XNAS").IsRegistered.Should().BeTrue();
        Mic.Parse("ZZZZ").IsRegistered.Should().BeFalse();
        Action parse = () => Mic.Parse("NASDAQ");
        parse.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("XNAS", "XNAS")]
    [InlineData("nasdaq", "XNAS")]
    [InlineData("NYSE", "XNYS")]
    [InlineData(" NYSE Arca ", "ARCX")]
    [InlineData("AMEX", "XASE")]
    [InlineData("TSX", "XTSE")]
    public void MicReference_ResolvesBundledCodesAndKnownExchangeNames(string exchange, string expected)
    {
        MicReference.TryResolve(exchange, out var mic).Should().BeTrue();
        mic.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("SMART")]
    [InlineData("ZZZZ")]
    [InlineData("TRACE")]
    [InlineData("")]
    [InlineData(null)]
    public void MicReference_LeavesUnknownLabelsUnresolved(string? exchange)
    {
        MicReference.TryResolve(exchange, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("AAPL:XNAS", "AAPL", "XNAS")]
    [InlineData("brk.b:xnys", "BRK.B", "XNYS")]
    [InlineData("A:B:XLON", "A:B", "XLON")]
    public void ListingId_ParsesTickerAndMicFromTheCanonicalForm(string value, string ticker, string mic)
    {
        var listing = ListingId.Parse(value);

        listing.Ticker.Value.Should().Be(ticker);
        listing.Mic.Value.Should().Be(mic);
        listing.ToString().Should().Be($"{ticker}:{mic}");
        listing.Should().Be(new ListingId(new SymbolId(ticker), Mic.Parse(mic)));
    }

    [Theory]
    [InlineData("AAPL")]
    [InlineData("AAPL:")]
    [InlineData(":XNAS")]
    [InlineData("AAPL:NASDAQ")]
    [InlineData("AAPL :XNAS")]
    [InlineData("AA PL:XNAS")]
    [InlineData("")]
    public void ListingId_RejectsMalformedValues(string value)
    {
        ListingId.TryParse(value, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("AAPL:XNAS", true)]
    [InlineData(" aapl:xnas ", true)]
    [InlineData("AAPL", false)]
    [InlineData("AAPL:NASDAQ", false)]
    public void ListingIdIdentifier_ValidatesTheTickerMicForm(string value, bool valid)
    {
        SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.ListingId, value, out var message).Should().Be(valid, message);
    }

    [Fact]
    public void ListingIdIdentifier_NormalizesToUpperCaseWithoutStrippingTheSeparator()
    {
        // Matches the SQL normalized-lookup default (upper(btrim(value))) for kinds it does not strip.
        SecurityIdentifierNormalizer.NormalizeValue(SecurityIdentifierKind.ListingId, " brk.b:xnys ").Should().Be("BRK.B:XNYS");
    }

    [Fact]
    public void ListingIdIdentifier_SurvivesTheDomainRoundTrip()
    {
        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var projection = new SecurityProjectionRecord(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Swap",
            Status: SecurityStatusDto.Active,
            DisplayName: "Listing identity test",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "LISTING-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Listing identity test", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                effectiveDate = "2026-01-15",
                maturityDate = "2031-01-15",
                legs = new object[]
                {
                    new { legType = "Fixed", currency = "USD", fixedRate = 0.04m, exchangesPrincipal = false }
                }
            }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "listing-tests", asOf, updatedBy = "listing-tests" }),
            Version: 1,
            EffectiveFrom: asOf,
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "LISTING-1", true, asOf),
                new SecurityIdentifierDto(SecurityIdentifierKind.ListingId, "AAPL:XNAS", false, asOf)
            ],
            Aliases: []);

        var persisted = SecurityMasterMapping.ToProjection(
            new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)));

        persisted.Identifiers.Should().Contain(identifier =>
            identifier.Kind == SecurityIdentifierKind.ListingId && identifier.Value == "AAPL:XNAS");
    }
}
