using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Holds the generated class-plumbing regions to <see cref="SecurityAssetClassDeclarations"/>
/// (<c>W11-PACK-002</c>, <c>DEC-PACK-CODEGEN-001</c>). Each region is ordinary committed source
/// between <c>&lt;generated:id&gt;</c> markers. These tests regenerate every region and fail when the
/// committed text differs, so the declaration is the only place a class's plumbing is edited. Set
/// <c>MERIDIAN_ACCEPT_GENERATED_CODEC=1</c> to write the regenerated regions instead of comparing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityClassPlumbingGenerationTests
{
    [Theory]
    [MemberData(nameof(RegionIds))]
    public void TheCommittedRegion_IsWhatTheDeclarationGenerates(string regionId)
    {
        var region = SecurityClassPlumbingGenerator.GenerateAll().Single(r => r.Id == regionId);
        var path = Path.Combine(FindRepositoryRoot(), region.TargetPath);
        var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n').ToList();
        var (start, end) = FindRegion(lines, region);

        if (Environment.GetEnvironmentVariable("MERIDIAN_ACCEPT_GENERATED_CODEC") == "1")
        {
            lines.RemoveRange(start + 1, end - start - 1);
            lines.InsertRange(start + 1, region.Lines);
            File.WriteAllText(path, string.Join('\n', lines));
            return;
        }

        lines.GetRange(start + 1, end - start - 1).Should().Equal(region.Lines,
            $"the <generated:{region.Id}> region of {region.TargetPath} must be regenerated from SecurityAssetClassDeclarations, not edited");
    }

    [Fact]
    public void TheDeclaration_ListsEveryCatalogClassOnce()
    {
        // As a set only. The catalog's row order is its public presentation order (CustomAsset
        // follows OtherSecurity) and is not generated; the declaration follows the union order.
        SecurityAssetClassDeclarations.All.Select(static c => c.Name)
            .Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses,
                "the declaration states each catalog class exactly once");
    }

    [Fact]
    public void TheClassModules_AreOneForEachDeclaredClass()
    {
        // The catalog rows live in the per-class asset modules (#3744); each declared class needs
        // exactly one module, and a hand-written writer arm is exactly the opaque profile codec.
        var modules = SecurityAssetClassModules.All;

        modules.Select(static module => module.Descriptor.AssetClass)
            .Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(SecurityAssetClassDeclarations.All.Select(static c => c.Name),
                "every declared class needs a class module, and every module a declared class");
        foreach (var declaration in SecurityAssetClassDeclarations.All)
        {
            modules.Single(module => module.Descriptor.AssetClass == declaration.Name).UsesOpaqueProfileCodec
                .Should().Be(declaration.HandWrittenWriterArm,
                    $"{declaration.Name}'s writer arm is hand-written exactly when its module keeps the opaque profile codec");
        }
    }

    [Fact]
    public void TheCompiledSecurityKindUnion_FollowsTheDeclaration()
    {
        // Case names only. Terms type names are held by the generated region, textually, because
        // reflection sees through abbreviations (CustomAssetTerms is ProfileEnvelopeTerms).
        FSharpType.GetUnionCases(typeof(SecurityKind), null)
            .Select(static unionCase => unionCase.Name)
            .Should().Equal(SecurityAssetClassDeclarations.All.Select(static c => c.Name));
    }

    [Fact]
    public void OnlyClassesWithoutACodecBinding_DeclareAHandWrittenWriterArm()
    {
        foreach (var declaration in SecurityAssetClassDeclarations.All)
        {
            var bound = SecurityAssetTermsCodecBindings.ByAssetClass.ContainsKey(declaration.Name);
            declaration.HandWrittenWriterArm.Should().Be(!bound,
                $"{declaration.Name}'s writer arm is generated exactly when the binding table lists it");
        }
    }

    [Fact]
    public void AddingAClass_AddsItsArmToEveryGeneratedRegion()
    {
        // A hypothetical class with an ordinary flat codec: one declaration row and one binding row
        // produce every arm below.
        var example = new SecurityAssetClassDeclaration("ExampleAsset", "Other", "Example", "ExampleAsset");
        var binding = new SecurityClassCodecBinding("ExampleAsset", ReadsResolvedTerms: false, Members: []);
        var classes = SecurityAssetClassDeclarations.All.Append(example).ToArray();
        var bindings = SecurityAssetTermsCodecBindings.ByAssetClass
            .Append(new KeyValuePair<string, SecurityClassCodecBinding>("ExampleAsset", binding))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);

        var baseline = SecurityClassPlumbingGenerator.GenerateAll();
        var extended = SecurityClassPlumbingGenerator.GenerateAll(classes, bindings);

        extended.Should().HaveSameCount(baseline);
        foreach (var region in extended)
        {
            var before = baseline.Single(r => r.Id == region.Id);
            var added = region.Lines.Count - before.Lines.Count;
            added.Should().BePositive($"{region.Id} gains the new class's arm");
            region.Lines.Count(static line => line.Contains("ExampleAsset", StringComparison.Ordinal))
                .Should().Be(added, $"every line {region.Id} gains belongs to the new class");
        }

        var text = string.Join('\n', extended.SelectMany(static r => r.Lines));
        text.Should().Contain("    | ExampleAsset of ExampleAssetTerms")
            .And.Contain("Family = Some (AssetFamily.OtherFamily \"Example\")")
            .And.Contain("GeneratedFlatWriter.writeExampleAsset schemaVersion terms");
    }

    public static TheoryData<string> RegionIds()
    {
        var data = new TheoryData<string>();
        foreach (var region in SecurityClassPlumbingGenerator.GenerateAll())
        {
            data.Add(region.Id);
        }

        return data;
    }

    private static (int Start, int End) FindRegion(List<string> lines, SecurityClassPlumbingRegion region)
    {
        var open = $"<generated:{region.Id}>";
        var close = $"</generated:{region.Id}>";
        var starts = lines.Select((line, index) => (line, index)).Where(p => p.line.Contains(open, StringComparison.Ordinal)).ToArray();
        var ends = lines.Select((line, index) => (line, index)).Where(p => p.line.Contains(close, StringComparison.Ordinal)).ToArray();

        starts.Should().ContainSingle($"{region.TargetPath} must mark the <generated:{region.Id}> region exactly once");
        ends.Should().ContainSingle($"{region.TargetPath} must close the <generated:{region.Id}> region exactly once");
        ends[0].index.Should().BeGreaterThan(starts[0].index);
        return (starts[0].index, ends[0].index);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the repository root from the test output directory.");
    }
}
