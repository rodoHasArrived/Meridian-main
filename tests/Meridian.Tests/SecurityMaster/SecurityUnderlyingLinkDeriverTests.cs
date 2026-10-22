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
    public void ConvertibleEquity_LinksTheUnderlyingInItsNestedTermBlock()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Equity", new
        {
            classification = "ConvertiblePreferred",
            convertibleTerms = new { underlyingSecurityId = Underlying, conversionRatio = 4.2m }
        }));

        var link = links.Should().ContainSingle().Subject;
        link.Role.Should().Be(SecurityUnderlyingRole.Underlying);
        link.UnderlyingSecurityId.Should().Be(Underlying);
    }

    [Theory]
    [MemberData(nameof(DeclaredReferenceFields))]
    public void EverySchemaDeclaredReferenceField_ProducesItsDeclaredRole(
        string assetClass,
        string fieldPath,
        string termsJson,
        SecurityUnderlyingRole expectedRole,
        bool resolved)
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record(assetClass, JsonSerializer.Deserialize<JsonElement>(termsJson)));

        var link = links.Should().ContainSingle("the schema declares {0}.{1} as a relationship", assetClass, fieldPath).Subject;
        link.Role.Should().Be(expectedRole);
        link.UnderlyingSecurityId.Should().Be(resolved ? Underlying : null);
        link.Reference.Should().Be(resolved ? null : "Schema reference");
    }

    public static IEnumerable<object[]> DeclaredReferenceFields()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                if (field.UnderlyingRole is { } role)
                {
                    yield return new object[]
                    {
                        assetClass,
                        field.Key,
                        JsonSerializer.Serialize(new Dictionary<string, object> { [field.Key] = ReferenceValue(field) }),
                        role,
                        field.Type == SecurityAssetTermFieldType.Guid
                    };
                }

                foreach (var elementField in SecurityAssetTermsSchema.ElementFields(assetClass, field.Key))
                {
                    if (elementField.UnderlyingRole is not { } elementRole)
                    {
                        continue;
                    }

                    var element = new Dictionary<string, object> { [elementField.Key] = ReferenceValue(elementField) };
                    object container = field.Type == SecurityAssetTermFieldType.Array ? new object[] { element } : element;
                    yield return new object[]
                    {
                        assetClass,
                        field.Key + "." + elementField.Key,
                        JsonSerializer.Serialize(new Dictionary<string, object> { [field.Key] = container }),
                        elementRole,
                        elementField.Type == SecurityAssetTermFieldType.Guid
                    };
                }
            }
        }

        static object ReferenceValue(SecurityAssetTermField field)
            => field.Type == SecurityAssetTermFieldType.Guid ? Underlying.ToString() : "Schema reference";
    }

    [Fact]
    public void Swap_RecordsEveryDistinctLegIndex_UsingElementFieldAliases()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Swap", new
        {
            legs = new object[]
            {
                new { legType = "Fixed", fixedRate = 0.04m },
                new { legType = "Floating", index = "SOFR" },
                new { legType = "Floating", indexName = "EURIBOR" },
                new { legType = "Floating", referenceIndex = " sofr " }
            }
        }));

        links.Should().HaveCount(2);
        links.Should().OnlyContain(static link => link.Role == SecurityUnderlyingRole.Index);
        links.Select(static link => link.Reference).Should().Equal("SOFR", "EURIBOR");
        links.Select(static link => link.LinkId).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("swapLegs")]
    [InlineData("cashFlowLegs")]
    public void Swap_UsesSchemaDeclaredArrayAliases_AndProfileFallback(string arrayKey)
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Swap", new
        {
            profileFields = new Dictionary<string, object>
            {
                [arrayKey] = new object[] { new { referenceIndex = "SONIA" }, new { index = "SOFR" } }
            }
        }));

        links.Select(static link => link.Reference).Should().Equal("SONIA", "SOFR");
        links.Should().OnlyContain(static link => link.Role == SecurityUnderlyingRole.Index);
    }

    [Fact]
    public void Swap_CanonicalElementFieldWinsOverItsAliases()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Swap", new
        {
            legs = new[] { new { index = "SOFR", indexName = "superseded alias", referenceIndex = "another alias" } }
        }));

        links.Should().ContainSingle().Which.Reference.Should().Be("SOFR");
    }

    [Fact]
    public void Bond_RecordsMultipleFieldsWithTheSameRole()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Bond", new
        {
            floatingIndex = "SOFR",
            inflationIndex = "US CPI"
        }));

        links.Should().HaveCount(2);
        links.Should().OnlyContain(static link => link.Role == SecurityUnderlyingRole.Index);
        links.Select(static link => link.Reference).Should().Equal("SOFR", "US CPI");
    }

    [Fact]
    public void CommitmentGuarantee_RecordsItsCollateralReference()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("CommitmentGuarantee", new
        {
            collateral = "Pledged Treasury portfolio"
        }));

        var link = links.Should().ContainSingle().Subject;
        link.Role.Should().Be(SecurityUnderlyingRole.Collateral);
        link.Reference.Should().Be("Pledged Treasury portfolio");
        link.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void CommitmentGuarantee_ResolvesCollateralThatNamesASecurityId()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("CommitmentGuarantee", new { collateral = Underlying.ToString() }));

        var link = links.Should().ContainSingle().Subject;
        link.Role.Should().Be(SecurityUnderlyingRole.Collateral);
        link.UnderlyingSecurityId.Should().Be(Underlying);
        link.Reference.Should().BeNull();
    }

    [Fact]
    public void Option_PreservesLegacyUnderlyingSecurityIdAlias()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("Option", new { underlyingSecurityId = Underlying }));

        links.Should().ContainSingle().Which.UnderlyingSecurityId.Should().Be(Underlying);
    }

    [Fact]
    public void ProfileBackedTerms_PreserveMultipleSchemaDeclaredReferenceRoles()
    {
        var links = SecurityUnderlyingLinkDeriver.Derive(Record("CustomAsset", new
        {
            customProfileId = "collateralized-note",
            profileVersion = 1,
            profileFields = new
            {
                underlyingSecurityId = Underlying,
                collateral = "Treasury bills",
                referenceEntity = "Acme Corp"
            }
        }));

        links.Should().Contain(link => link.Role == SecurityUnderlyingRole.Underlying && link.UnderlyingSecurityId == Underlying);
        links.Should().Contain(link => link.Role == SecurityUnderlyingRole.Collateral && link.Reference == "Treasury bills");
        links.Should().Contain(link => link.Role == SecurityUnderlyingRole.ReferenceEntity && link.Reference == "Acme Corp");
    }

    [Fact]
    public void UndeclaredFields_DoNotCreateRelationshipsForAnotherAssetClass()
    {
        SecurityUnderlyingLinkDeriver.Derive(Record("Equity", new
        {
            shareClass = "Common",
            floatingIndex = "not an equity term",
            collateral = "not an equity term"
        })).Should().BeEmpty();
    }

    [Fact]
    public void MalformedElementReferences_AndSelfReferences_DoNotCreateLinks()
    {
        SecurityUnderlyingLinkDeriver.Derive(Record("Swap", new
        {
            legs = new object[]
            {
                new { index = 123 },
                new { index = true },
                new { index = " " },
                new { index = Security.ToString() },
                new { index = Guid.Empty.ToString() }
            }
        })).Should().BeEmpty();
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
