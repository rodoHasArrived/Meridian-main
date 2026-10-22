using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The underlying graph follows the term schema. Every field that declares an underlying role
/// (<see cref="SecurityAssetTermField.UnderlyingRole"/>) links in that role under its key and every
/// alias, whether it is a top-level term, a field of an array element or a field of a nested object;
/// every field whose name says it refers to something either declares a role or is listed in
/// <see cref="NotLinks"/> with the reason it names nothing the instrument depends on; and the
/// deriver carries no term key of its own. A new reference field therefore fails here until the
/// schema says what it links.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityUnderlyingRoleCoverageTests
{
    private static readonly Guid Security = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A key or alias that reads like a reference to an underlying, index or collateral.</summary>
    private static readonly Regex ReferenceShapedName = new(
        "underlying|index|reference|collateral|obligation|basket|constituent",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Reference-shaped fields that are not graph edges, by schema path, with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> NotLinks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Bond.inflationBaseIndexValue"] = "the reference index level at issue, a number",
        ["Bond.inflationIndexRatio"] = "the current index ratio, a number",
        ["Repo.collateralType"] = "the collateral category (general or special), not a pledged asset",
        ["StructuredCredit.collateralType"] = "the pool's collateral category, not a pledged asset",
        ["Cfd.underlyingAssetClass"] = "the class of the reference asset, not the asset",
        ["Swaption.underlyingEffectiveDate"] = "a date of the swap the option is on, which the terms describe inline",
        ["Swaption.underlyingMaturityDate"] = "a date of the swap the option is on, which the terms describe inline",
        ["Swap.legs.currentIndexRate"] = "the latest fixing of the leg's index, a number"
    };

    public static TheoryData<string, string> RoleMarkedSpellings()
    {
        var data = new TheoryData<string, string>();
        foreach (var marked in RoleMarkedFields())
        {
            foreach (var spelling in Spellings(marked.Field))
            {
                data.Add(marked.Path, spelling);
            }
        }

        return data;
    }

    public static TheoryData<string, string> ProfileFieldSpellings()
    {
        var data = new TheoryData<string, string>();
        foreach (var marked in RoleMarkedFields())
        {
            foreach (var spelling in Spellings(marked.Field))
            {
                data.Add(marked.Path, spelling);
            }
        }

        return data;
    }

    [Fact]
    public void TheSchemaDeclaresTheReferencesTheGraphIsBuiltFrom()
    {
        var paths = RoleMarkedFields().Select(static marked => marked.Path).ToArray();

        // The fields the graph missed or truncated before it read the schema: a swap leg's index,
        // a commitment's collateral, a bond's second index and a convertible's conversion target.
        paths.Should().Contain(["Swap.legs.index", "CommitmentGuarantee.collateral", "Bond.floatingIndex", "Bond.inflationIndex", "Equity.convertibleTerms.underlyingSecurityId"]);
        RoleMarkedFields().Select(static marked => marked.Field.UnderlyingRole!.Value).Distinct()
            .Should().Contain([SecurityUnderlyingRole.Underlying, SecurityUnderlyingRole.ReferenceEntity, SecurityUnderlyingRole.ReferenceObligation, SecurityUnderlyingRole.Index, SecurityUnderlyingRole.Collateral]);
    }

    [Theory]
    [MemberData(nameof(RoleMarkedSpellings))]
    public void EveryRoleMarkedField_LinksInItsRole_UnderEverySpelling(string path, string spelling)
    {
        var marked = Resolve(path);
        var value = marked.Field.Type == SecurityAssetTermFieldType.Guid ? Target.ToString() : "REF " + marked.Field.Key;

        var links = SecurityUnderlyingLinkDeriver.Derive(Record(marked.AssetClass, Terms(marked, spelling, value)));

        var link = links.Should().ContainSingle($"{path} spelled '{spelling}' states one edge").Subject;
        link.Role.Should().Be(marked.Field.UnderlyingRole);
        link.Source.Should().Be(SecurityUnderlyingLinkSource.Derived);
        if (marked.Field.Type == SecurityAssetTermFieldType.Guid)
        {
            link.UnderlyingSecurityId.Should().Be(Target);
        }
        else
        {
            link.IsResolved.Should().BeFalse();
            link.Reference.Should().Be(value);
        }
    }

    [Theory]
    [MemberData(nameof(RoleMarkedSpellings))]
    public void EveryRoleMarkedField_ResolvesWhenItCarriesASecurityId(string path, string spelling)
    {
        var marked = Resolve(path);

        var links = SecurityUnderlyingLinkDeriver.Derive(Record(marked.AssetClass, Terms(marked, spelling, Target.ToString())));

        links.Should().ContainSingle().Which.UnderlyingSecurityId.Should().Be(Target);
    }

    /// <summary>Every role-marked spelling, element-shape fields included, links when a profile carries it.</summary>
    [Theory]
    [MemberData(nameof(ProfileFieldSpellings))]
    public void ProfileFields_SpelledLikeADeclaredReference_LinkInTheDeclaredRole(string path, string spelling)
    {
        var marked = Resolve(path);
        var value = marked.Field.Type == SecurityAssetTermFieldType.Guid ? Target.ToString() : "REF " + marked.Field.Key;
        var terms = new JsonObject
        {
            ["customProfileId"] = "profile",
            ["profileVersion"] = 1,
            ["profileFields"] = new JsonObject { [spelling] = value }
        };

        var links = SecurityUnderlyingLinkDeriver.Derive(Record("CustomAsset", terms));

        links.Should().Contain(link => link.Role == marked.Field.UnderlyingRole
                                       && (link.UnderlyingSecurityId == Target || link.Reference == value));
    }

    [Fact]
    public void AnArrayContainer_YieldsOneLinkPerElement()
    {
        foreach (var marked in RoleMarkedFields().Where(static marked => marked.Container?.Type == SecurityAssetTermFieldType.Array))
        {
            var elements = new JsonArray(
                new JsonObject { [marked.Field.Key] = Guid.NewGuid().ToString() },
                new JsonObject { [marked.Field.Key] = Guid.NewGuid().ToString() });

            var links = SecurityUnderlyingLinkDeriver.Derive(Record(marked.AssetClass, new JsonObject { [marked.Container!.Key] = elements }));

            links.Should().HaveCount(2, $"each element of {marked.AssetClass}.{marked.Container.Key} states its own {marked.Field.Key}");
        }
    }

    [Fact]
    public void EveryReferenceShapedField_DeclaresARole_OrSaysWhyItIsNotALink()
    {
        var undeclared = AllFields()
            .Where(static entry => entry.Field.UnderlyingRole is null
                                   && Spellings(entry.Field).Any(static spelling => ReferenceShapedName.IsMatch(spelling)))
            .Select(static entry => entry.Path)
            .Where(static path => !NotLinks.ContainsKey(path))
            .ToArray();

        undeclared.Should().BeEmpty(
            "a term whose name refers to an underlying, index or collateral must declare its role with "
            + "LinksUnderlying, or be listed in NotLinks with the reason it names nothing the instrument depends on");
    }

    [Fact]
    public void NotLinks_NamesOnlyFieldsThatExist_AndDeclareNoRole()
    {
        var fields = AllFields().ToDictionary(static entry => entry.Path, static entry => entry.Field, StringComparer.Ordinal);

        foreach (var path in NotLinks.Keys)
        {
            fields.Should().ContainKey(path, "a NotLinks entry for a field the schema no longer declares must be removed");
            fields[path].UnderlyingRole.Should().BeNull($"{path} declares a role, so it is a link and must leave NotLinks");
        }
    }

    [Fact]
    public void TheDeriver_NamesNoTermKeyOfItsOwn()
    {
        var spellings = AllFields()
            .SelectMany(static entry => Spellings(entry.Field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The profile envelope is the one key the deriver reads by name: it says where dynamic
        // fields live, not what they link.
        spellings.Remove("profileFields");

        // Code only: XML doc attributes (<paramref name="…"/>) quote member names, not term keys.
        var code = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "src", "Meridian.Contracts", "SecurityMaster", "SecurityUnderlyingGraph.cs"))
            .Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));
        var literals = Regex.Matches(string.Join('\n', code), "\"((?:[^\"\\\\]|\\\\.)*)\"")
            .Select(static match => match.Groups[1].Value)
            .Where(spellings.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        literals.Should().BeEmpty("the graph reads the roles the term schema declares, not a second copy of its keys");
    }

    private sealed record MarkedField(string AssetClass, SecurityAssetTermField? Container, SecurityAssetTermField Field)
    {
        public string Path => Container is null ? $"{AssetClass}.{Field.Key}" : $"{AssetClass}.{Container.Key}.{Field.Key}";
    }

    private static IEnumerable<MarkedField> AllFields()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                yield return new MarkedField(assetClass, null, field);
                foreach (var element in SecurityAssetTermsSchema.ElementFields(assetClass, field.Key))
                {
                    yield return new MarkedField(assetClass, field, element);
                }
            }
        }
    }

    private static IEnumerable<MarkedField> RoleMarkedFields()
        => AllFields().Where(static entry => entry.Field.UnderlyingRole is not null);

    private static MarkedField Resolve(string path)
        => RoleMarkedFields().Single(entry => entry.Path == path);

    private static IEnumerable<string> Spellings(SecurityAssetTermField field)
        => new[] { field.Key }.Concat(field.Aliases);

    /// <summary>Terms carrying <paramref name="value"/> under <paramref name="spelling"/>, inside the container when the field is an element field.</summary>
    private static JsonObject Terms(MarkedField marked, string spelling, string value)
    {
        var holder = new JsonObject { [spelling] = value };
        if (marked.Container is null)
        {
            return holder;
        }

        JsonNode container = marked.Container.Type == SecurityAssetTermFieldType.Array ? new JsonArray(holder) : holder;
        return new JsonObject { [marked.Container.Key] = container };
    }

    private static SecurityProjectionRecord Record(string assetClass, JsonObject terms)
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }
}
