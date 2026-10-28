using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The underlying graph derived from an instrument's terms: resolved links for term keys that carry
/// a security id, unresolved references for the names the terms carry, one link per role-marked
/// field (including array elements and nested objects), deterministic link ids, and the
/// profile-backed fallback. <see cref="SecurityUnderlyingRoleCoverageTests"/> holds every role the
/// schema declares to the same rules.
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
    public void ProfileBackedTerms_LinkAnUnderlyingSecurityIdUnderProfileFields()
    {
        // underlyingSecurityId is declared only inside an element shape (an equity's conversion
        // terms); a profile field with that spelling still links.
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("CustomAsset", new
        {
            customProfileId = "equity-linked-note",
            profileVersion = 1,
            profileFields = new { underlyingSecurityId = Underlying }
        }));

        links.Should().ContainSingle().Which.UnderlyingSecurityId.Should().Be(Underlying);
    }

    [Fact]
    public void Swap_LinksEveryLegsFloatingIndex_UnderAnyDeclaredSpelling()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Swap", new
        {
            effectiveDate = "2026-01-15",
            maturityDate = "2031-01-15",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 0.04m, exchangesPrincipal = false },
                new { legType = "Floating", currency = "USD", index = "SOFR", exchangesPrincipal = false },
                new { legType = "Floating", currency = "EUR", indexName = "ESTR", exchangesPrincipal = false }
            }
        }));

        links.Should().HaveCount(2);
        links.Should().OnlyContain(static l => l.Role == SecurityUnderlyingRole.Index && !l.IsResolved);
        links.Select(static l => l.Reference).Should().BeEquivalentTo("SOFR", "ESTR");
    }

    [Fact]
    public void Swap_LegsUnderAContainerAlias_AreRead_AndARepeatedIndexLinksOnce()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Swap", new
        {
            swapLegs = new object[]
            {
                new { legType = "Floating", currency = "USD", referenceIndex = "SOFR" },
                new { legType = "Floating", currency = "USD", index = "sofr" }
            }
        }));

        links.Should().ContainSingle(static l => l.Role == SecurityUnderlyingRole.Index)
            .Which.Reference.Should().Be("SOFR", "reference text is one edge regardless of case");
    }

    [Fact]
    public void CommitmentGuarantee_LinksItsCollateral()
    {
        var collateralId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        SecurityUnderlyingLinkDeriver.Derive(Record("CommitmentGuarantee", new { counterparty = "Bank", collateral = "Pledged receivables" }))
            .Should().ContainSingle(static l => l.Role == SecurityUnderlyingRole.Collateral && l.Reference == "Pledged receivables");
        SecurityUnderlyingLinkDeriver.Derive(Record("CommitmentGuarantee", new { counterparty = "Bank", collateral = collateralId.ToString() }))
            .Should().ContainSingle().Which.UnderlyingSecurityId.Should().Be(collateralId);
    }

    [Fact]
    public void Bond_WithAFloatingAndAnInflationIndex_LinksBoth()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Bond", new
        {
            maturity = "2031-01-01",
            couponType = "Floating",
            floatingIndex = "SOFR",
            inflationIndex = "CPI-U"
        }));

        links.Should().HaveCount(2);
        links.Should().OnlyContain(static l => l.Role == SecurityUnderlyingRole.Index);
        links.Select(static l => l.Reference).Should().BeEquivalentTo("SOFR", "CPI-U");
    }

    [Fact]
    public void ConvertibleEquity_LinksTheSecurityItConvertsInto()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Equity", new
        {
            classification = "Convertible",
            convertibleTerms = new { underlyingSecurityId = Underlying, conversionRatio = 2.5m }
        }));

        var link = links.Should().ContainSingle().Subject;
        link.Role.Should().Be(SecurityUnderlyingRole.Underlying);
        link.UnderlyingSecurityId.Should().Be(Underlying);
    }

    [Fact]
    public void Fields_AreReadForTheRecordsClassOnly()
    {
        // referenceEntity is a CDS term; an option that happens to carry the key states no CDS edge.
        SecurityUnderlyingLinkDeriver.Derive(Record("Option", new { underlyingId = Underlying, referenceEntity = "Acme Corp" }))
            .Should().ContainSingle().Which.Role.Should().Be(SecurityUnderlyingRole.Underlying);
    }

    [Fact]
    public void AGuidField_ThatCarriesText_IsNotRecordedAsAReference()
    {
        SecurityUnderlyingLinkDeriver.Derive(Record("Warrant", new { underlyingId = "ACME", warrantType = "Call" }))
            .Should().BeEmpty("an underlying id is a security id or nothing");
    }

    [Fact]
    public void SelfReferenceAndBlankReferences_ProduceNoLinks()
    {
        SecurityUnderlyingLinkDeriver.Derive(Record("Option", new { underlyingId = Security })).Should().BeEmpty();
        SecurityUnderlyingLinkDeriver.Derive(Record("StructuredNote", new { underlyingDescription = Security.ToString() })).Should().BeEmpty();
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
