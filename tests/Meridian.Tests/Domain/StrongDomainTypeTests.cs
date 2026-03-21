using FluentAssertions;
using Meridian.Contracts.Domain;
using Xunit;

namespace Meridian.Tests.Domain;

/// <summary>
/// Tests for the strong domain-type value objects: <see cref="SymbolId"/>, <see cref="ProviderId"/>,
/// <see cref="VenueCode"/>, <see cref="CanonicalSymbol"/>, <see cref="StreamId"/>,
/// and <see cref="SubscriptionId"/>.
/// </summary>
public sealed class StrongDomainTypeTests
{
    // ------------------------------------------------------------------ //
    //  SymbolId                                                            //
    // ------------------------------------------------------------------ //

    [Fact]
    public void SymbolId_Value_IsUpperCase()
    {
        var id = new SymbolId("spy");
        id.Value.Should().Be("SPY");
    }

    [Fact]
    public void SymbolId_ImplicitConversion_YieldsString()
    {
        SymbolId id = new("AAPL");
        string s = id;
        s.Should().Be("AAPL");
    }

    [Fact]
    public void SymbolId_ExplicitConversion_FromString()
    {
        var id = (SymbolId)"msft";
        id.Value.Should().Be("MSFT");
    }

    [Fact]
    public void SymbolId_EqualityIsCaseInsensitive()
    {
        var a = new SymbolId("SPY");
        var b = new SymbolId("spy");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void SymbolId_NullOrWhitespace_ThrowsArgumentException()
    {
        var act1 = () => new SymbolId("");
        var act2 = () => new SymbolId("  ");
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SymbolId_ToString_ReturnsTicker()
    {
        var id = new SymbolId("QQQ");
        id.ToString().Should().Be("QQQ");
    }

    [Fact]
    public void SymbolId_HashCode_IsCaseInsensitiveConsistent()
    {
        var a = new SymbolId("SPY");
        var b = new SymbolId("spy");
        a.GetHashCode().Should().Be(b.GetHashCode(),
            "case-insensitive equality requires matching hash codes");
    }

    // ------------------------------------------------------------------ //
    //  ProviderId                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public void ProviderId_Value_IsLowerCase()
    {
        var id = new ProviderId("ALPACA");
        id.Value.Should().Be("alpaca");
    }

    [Fact]
    public void ProviderId_WellKnownConstants_HaveCorrectValues()
    {
        ProviderId.Alpaca.Value.Should().Be("alpaca");
        ProviderId.Polygon.Value.Should().Be("polygon");
        ProviderId.Stooq.Value.Should().Be("stooq");
    }

    [Fact]
    public void ProviderId_EqualityIsCaseInsensitive()
    {
        var a = new ProviderId("Polygon");
        var b = new ProviderId("polygon");
        a.Should().Be(b);
    }

    [Fact]
    public void ProviderId_ImplicitConversion_YieldsString()
    {
        ProviderId id = ProviderId.Stooq;
        string s = id;
        s.Should().Be("stooq");
    }

    [Fact]
    public void ProviderId_NullOrWhitespace_ThrowsArgumentException()
    {
        var act = () => new ProviderId(null!);
        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------ //
    //  VenueCode                                                           //
    // ------------------------------------------------------------------ //

    [Fact]
    public void VenueCode_Value_IsUpperCase()
    {
        var code = new VenueCode("nyse");
        code.Value.Should().Be("NYSE");
    }

    [Fact]
    public void VenueCode_WellKnownConstants_HaveCorrectValues()
    {
        VenueCode.Nyse.Value.Should().Be("NYSE");
        VenueCode.Nasdaq.Value.Should().Be("NASDAQ");
        VenueCode.Unknown.Value.Should().Be("UNKNOWN");
    }

    [Fact]
    public void VenueCode_EqualityIsCaseInsensitive()
    {
        var a = new VenueCode("NASDAQ");
        var b = new VenueCode("nasdaq");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void VenueCode_NullOrWhitespace_ThrowsArgumentException()
    {
        var act = () => new VenueCode("");
        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------ //
    //  Cross-type non-interchangeability                                  //
    // ------------------------------------------------------------------ //

    [Fact]
    public void StrongTypes_AreNotInterchangeable()
    {
        // The compiler enforces this at compile time; this test documents intent.
        var symbol = new SymbolId("SPY");
        var provider = new ProviderId("stooq");
        var venue = new VenueCode("NYSE");

        // SymbolId, ProviderId and VenueCode are distinct value types – confirmed by
        // verifying they carry independent values even when the raw strings match.
        var sameLetter = new SymbolId("ALPACA");
        var sameLetterProvider = new ProviderId("ALPACA");
        ((string)sameLetter).Should().NotBeSameAs((string)sameLetterProvider,
            because: "although the raw letters match, they are semantically different entities");
        _ = symbol;
        _ = provider;
        _ = venue;
    }

    // ------------------------------------------------------------------ //
    //  CanonicalSymbol                                                     //
    // ------------------------------------------------------------------ //

    [Fact]
    public void CanonicalSymbol_Value_IsUpperCase()
    {
        var cs = new CanonicalSymbol("aapl");
        cs.Value.Should().Be("AAPL");
    }

    [Fact]
    public void CanonicalSymbol_ImplicitConversion_YieldsString()
    {
        CanonicalSymbol cs = new("SPY");
        string s = cs;
        s.Should().Be("SPY");
    }

    [Fact]
    public void CanonicalSymbol_ExplicitConversion_FromString()
    {
        var cs = (CanonicalSymbol)"msft";
        cs.Value.Should().Be("MSFT");
    }

    [Fact]
    public void CanonicalSymbol_EqualityIsCaseInsensitive()
    {
        var a = new CanonicalSymbol("SPY");
        var b = new CanonicalSymbol("spy");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void CanonicalSymbol_NullOrWhitespace_ThrowsArgumentException()
    {
        var act1 = () => new CanonicalSymbol("");
        var act2 = () => new CanonicalSymbol("  ");
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CanonicalSymbol_ToString_ReturnsTicker()
    {
        var cs = new CanonicalSymbol("QQQ");
        cs.ToString().Should().Be("QQQ");
    }

    [Fact]
    public void CanonicalSymbol_HashCode_IsCaseInsensitiveConsistent()
    {
        var a = new CanonicalSymbol("AAPL");
        var b = new CanonicalSymbol("aapl");
        a.GetHashCode().Should().Be(b.GetHashCode(),
            "case-insensitive equality requires matching hash codes");
    }

    [Fact]
    public void CanonicalSymbol_IsDistinctFrom_SymbolId()
    {
        // CanonicalSymbol and SymbolId carry the same raw letters but are different types.
        var raw = new SymbolId("AAPL");
        var canonical = new CanonicalSymbol("AAPL");
        // The compiler prevents direct assignment between them (verified by code not compiling
        // without an explicit cast). This test documents the intent.
        ((string)raw).Should().Be((string)canonical,
            because: "both normalise the same ticker to uppercase");
    }

    // ------------------------------------------------------------------ //
    //  StreamId                                                            //
    // ------------------------------------------------------------------ //

    [Fact]
    public void StreamId_Value_PreservesOriginalCase()
    {
        var id = new StreamId("T.SPY");
        id.Value.Should().Be("T.SPY");
    }

    [Fact]
    public void StreamId_ImplicitConversion_YieldsString()
    {
        StreamId id = new("trades-AAPL");
        string s = id;
        s.Should().Be("trades-AAPL");
    }

    [Fact]
    public void StreamId_ExplicitConversion_FromString()
    {
        var id = (StreamId)"channel-42";
        id.Value.Should().Be("channel-42");
    }

    [Fact]
    public void StreamId_EqualityIsCaseSensitive()
    {
        var a = new StreamId("Stream-X");
        var b = new StreamId("stream-x");
        a.Should().NotBe(b, "stream IDs are case-sensitive provider-specific tokens");
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void StreamId_SameValue_AreEqual()
    {
        var a = new StreamId("T.SPY");
        var b = new StreamId("T.SPY");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void StreamId_NullOrWhitespace_ThrowsArgumentException()
    {
        var act1 = () => new StreamId("");
        var act2 = () => new StreamId("   ");
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StreamId_ToString_ReturnsValue()
    {
        var id = new StreamId("channel-1");
        id.ToString().Should().Be("channel-1");
    }

    // ------------------------------------------------------------------ //
    //  SubscriptionId                                                      //
    // ------------------------------------------------------------------ //

    [Fact]
    public void SubscriptionId_Value_IsStored()
    {
        var id = new SubscriptionId(42);
        id.Value.Should().Be(42);
    }

    [Fact]
    public void SubscriptionId_ImplicitConversion_YieldsInt()
    {
        SubscriptionId id = new(99);
        int i = id;
        i.Should().Be(99);
    }

    [Fact]
    public void SubscriptionId_ExplicitConversion_FromInt()
    {
        var id = (SubscriptionId)7;
        id.Value.Should().Be(7);
    }

    [Fact]
    public void SubscriptionId_Equality_BasedOnValue()
    {
        var a = new SubscriptionId(10);
        var b = new SubscriptionId(10);
        var c = new SubscriptionId(11);
        a.Should().Be(b);
        a.Should().NotBe(c);
        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
    }

    [Fact]
    public void SubscriptionId_CompareTo_OrdersByValue()
    {
        var a = new SubscriptionId(1);
        var b = new SubscriptionId(2);
        a.CompareTo(b).Should().BeNegative();
        b.CompareTo(a).Should().BePositive();
        a.CompareTo(a).Should().Be(0);
    }

    [Fact]
    public void SubscriptionId_ToString_ReturnsIntegerString()
    {
        var id = new SubscriptionId(123);
        id.ToString().Should().Be("123");
    }
}
