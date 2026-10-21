using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The underlying graph derived from an instrument's terms: resolved links for term keys that carry
/// a security id, unresolved references for the names the terms carry, deterministic link ids, and
/// the profile-backed fallback.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityUnderlyingLinkDeriverTests
{
    private static readonly Guid Security = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Underlying = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Option_LinksItsUnderlyingSecurity()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Option", new { underlyingId = Underlying, putCall = "Call", strike = 100m }));

        var link = links.Should().ContainSingle().Subject;
        link.Role.Should().Be(SecurityUnderlyingRole.Underlying);
        link.UnderlyingSecurityId.Should().Be(Underlying);
        link.IsResolved.Should().BeTrue();
        link.Source.Should().Be(SecurityUnderlyingLinkSource.Derived);
        link.ValidFrom.Should().Be(From);
        link.LinkId.Should().Be(SecurityUnderlyingLinkDeriver.LinkId(Security, SecurityUnderlyingRole.Underlying, Underlying, null, SecurityUnderlyingLinkSource.Derived));
    }

    [Fact]
    public void CreditDefaultSwap_RecordsReferenceEntityObligationAndIndexAsUnresolvedReferences()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("CreditDefaultSwap", new
        {
            referenceEntity = "Acme Corp",
            referenceObligation = "ACME 5.25% 2031",
            indexName = "CDX.NA.IG.41"
        }));

        links.Should().HaveCount(3);
        links.Should().OnlyContain(static l => !l.IsResolved);
        links.Should().Contain(l => l.Role == SecurityUnderlyingRole.ReferenceEntity && l.Reference == "Acme Corp");
        links.Should().Contain(l => l.Role == SecurityUnderlyingRole.ReferenceObligation && l.Reference == "ACME 5.25% 2031");
        links.Should().Contain(l => l.Role == SecurityUnderlyingRole.Index && l.Reference == "CDX.NA.IG.41");
    }

    [Fact]
    public void Bond_LinksItsFloatingIndexByName()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Bond", new { maturity = "2031-01-01", couponType = "Floating", floatingIndex = "SOFR" }));

        links.Should().ContainSingle(static l => l.Role == SecurityUnderlyingRole.Index && l.Reference == "SOFR");
    }

    [Fact]
    public void StructuredNote_RecordsItsFreeTextUnderlyingAsAGap()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("StructuredNote", new { underlyingDescription = "S&P 500 Index", issuerName = "Bank" }));

        var link = links.Should().ContainSingle().Subject;
        link.Role.Should().Be(SecurityUnderlyingRole.Underlying);
        link.IsResolved.Should().BeFalse();
        link.Reference.Should().Be("S&P 500 Index");
    }

    [Fact]
    public void AReferenceThatIsASecurityId_Resolves()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("StructuredNote", new { underlyingDescription = Underlying.ToString() }));

        links.Should().ContainSingle().Which.UnderlyingSecurityId.Should().Be(Underlying);
    }

    [Fact]
    public void ProfileBackedTerms_AreReadUnderProfileFields()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("CustomAsset", new
        {
            customProfileId = "equity-linked-note",
            profileVersion = 1,
            profileFields = new { underlyingId = Underlying }
        }));

        links.Should().ContainSingle().Which.UnderlyingSecurityId.Should().Be(Underlying);
    }

    [Fact]
    public void SelfReferenceAndBlankReferences_ProduceNoLinks()
    {
        SecurityUnderlyingLinkDeriver.Derive(Record("Option", new { underlyingId = Security })).Should().BeEmpty();
        SecurityUnderlyingLinkDeriver.Derive(Record("Bond", new { floatingIndex = "  " })).Should().BeEmpty();
        SecurityUnderlyingLinkDeriver.Derive(Record("Equity", new { shareClass = "Common" })).Should().BeEmpty();
    }

    [Fact]
    public void LinkIds_AreDeterministic_AndDistinguishSourceRoleAndTarget()
    {
        var a = SecurityUnderlyingLinkDeriver.LinkId(Security, SecurityUnderlyingRole.Underlying, Underlying, null, SecurityUnderlyingLinkSource.Derived);
        var again = SecurityUnderlyingLinkDeriver.LinkId(Security, SecurityUnderlyingRole.Underlying, Underlying, null, SecurityUnderlyingLinkSource.Derived);
        var declared = SecurityUnderlyingLinkDeriver.LinkId(Security, SecurityUnderlyingRole.Underlying, Underlying, null, SecurityUnderlyingLinkSource.Declared);
        var otherRole = SecurityUnderlyingLinkDeriver.LinkId(Security, SecurityUnderlyingRole.Collateral, Underlying, null, SecurityUnderlyingLinkSource.Derived);
        var byText = SecurityUnderlyingLinkDeriver.LinkId(Security, SecurityUnderlyingRole.Index, null, "sofr", SecurityUnderlyingLinkSource.Derived);
        var byTextUpper = SecurityUnderlyingLinkDeriver.LinkId(Security, SecurityUnderlyingRole.Index, null, " SOFR ", SecurityUnderlyingLinkSource.Derived);

        a.Should().Be(again);
        a.Should().NotBe(declared);
        a.Should().NotBe(otherRole);
        byText.Should().Be(byTextUpper, "reference text is compared case- and whitespace-insensitively");
    }

    private static SecurityProjectionRecord Record(string assetClass, object terms)
        => new(
            Security,
            assetClass,
            SecurityStatusDto.Active,
            "Test",
            "USD",
            "Internal",
            "TEST",
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(terms),
            JsonSerializer.SerializeToElement(new { }),
            1,
            From,
            null,
            [],
            []);
}
