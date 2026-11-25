using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Equivalence tests for the <c>W11-PACK-002</c> class-plumbing generator prototype. Each test
/// regenerates one hand-written per-class site from <see cref="SecurityClassPlumbingDeclarations"/>
/// and requires the committed source to equal it, line for line. The hand-written code stays the
/// source of truth: nothing here writes to <c>src/</c>, and a failure means the declaration (or the
/// generator) is behind the hand-written code, not that the code is wrong.
/// </summary>
[Trait("Category", "Unit")]
public sealed partial class SecurityClassPlumbingGenerationTests
{
    [Theory]
    [MemberData(nameof(FragmentNames))]
    public void TheHandWrittenSite_IsWhatTheDeclarationGenerates(string fragmentName)
    {
        var fragment = SecurityClassPlumbingGenerator.GenerateAll().Single(f => f.Name == fragmentName);
        var source = ReadSource(fragment.TargetPath);
        var handWritten = ExtractHandWritten(fragmentName, source);

        if (fragment.OrderSensitive)
        {
            handWritten.Should().Equal(fragment.Lines,
                $"{fragment.TargetPath} ({fragmentName}) must match what the class declaration generates");
        }
        else
        {
            handWritten.Should().BeEquivalentTo(fragment.Lines,
                $"{fragment.TargetPath} ({fragmentName}) must carry exactly the generated arms");
        }
    }

    [Fact]
    public void TheDeclaration_ListsEveryCatalogClassOnce()
    {
        // As a set only: the catalog's row order puts CustomAsset after OtherSecurity, while the F#
        // union, the registry and the name constants put it last. The declaration follows the union.
        SecurityClassPlumbingDeclarations.All.Select(static c => c.Name)
            .Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses,
                "the declaration states each catalog class exactly once");
    }

    [Fact]
    public void TheDeclaration_FollowsTheCompiledSecurityKindUnionOrder()
    {
        // Case names only. Terms type names are held by the union-case fragment, textually, because
        // reflection sees through abbreviations (CustomAssetTerms is ProfileEnvelopeTerms).
        FSharpType.GetUnionCases(typeof(SecurityKind), null)
            .Select(static unionCase => unionCase.Name)
            .Should().Equal(SecurityClassPlumbingDeclarations.All.Select(static c => c.Name));
    }

    [Fact]
    public void OnlyClassesWithoutACodecBinding_DeclareHandWrittenCodecArms()
    {
        foreach (var declaration in SecurityClassPlumbingDeclarations.All)
        {
            var bound = SecurityAssetTermsCodecBindings.ByAssetClass.ContainsKey(declaration.Name);
            declaration.HandWrittenWriterArm.Should().Be(!bound,
                $"{declaration.Name}'s writer arm is generated exactly when the binding table lists it");
            (declaration.DecodeExpression is not null).Should().Be(!bound,
                $"{declaration.Name}'s decode arm is a declared expression exactly when it has no binding");
        }
    }

    [Fact]
    public void AddingAClass_AddsItsArmToEveryGeneratedSite()
    {
        // A hypothetical class with an ordinary flat codec. One declaration row and one binding
        // produce every arm below; today each is a separate hand edit.
        var example = new SecurityClassDeclaration("ExampleAsset", "Other", "Example", "ExampleAsset");
        var binding = new SecurityClassCodecBinding("ExampleAsset", ReadsResolvedTerms: false, Members: []);
        var classes = SecurityClassPlumbingDeclarations.All.Append(example).ToArray();
        var bindings = SecurityAssetTermsCodecBindings.ByAssetClass
            .Append(new KeyValuePair<string, SecurityClassCodecBinding>("ExampleAsset", binding))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);

        var baseline = SecurityClassPlumbingGenerator.GenerateAll();
        var extended = SecurityClassPlumbingGenerator.GenerateAll(classes, bindings);

        extended.Should().HaveSameCount(baseline);
        foreach (var fragment in extended)
        {
            var before = baseline.Single(f => f.Name == fragment.Name);
            var added = fragment.Lines.Count - before.Lines.Count;
            added.Should().BePositive($"{fragment.Name} gains the new class's arm");
            fragment.Lines.Count(static line => line.Contains("ExampleAsset", StringComparison.Ordinal))
                .Should().Be(added, $"every line {fragment.Name} gains belongs to the new class");
        }

        SecurityClassPlumbingGenerator.Render(extended).Should()
            .Contain("    | ExampleAsset of ExampleAssetTerms")
            .And.Contain("Family = Some (AssetFamily.OtherFamily \"Example\")")
            .And.Contain("GeneratedFlatWriter.writeExampleAsset schemaVersion terms")
            .And.Contain("\"ExampleAsset\" => DecodeGeneratedExampleAsset(bound, mode, assetClass, json),");
    }

    public static TheoryData<string> FragmentNames()
    {
        var data = new TheoryData<string>();
        foreach (var fragment in SecurityClassPlumbingGenerator.GenerateAll())
        {
            data.Add(fragment.Name);
        }

        return data;
    }

    private static IReadOnlyList<string> ExtractHandWritten(string fragmentName, string[] source)
        => fragmentName switch
        {
            "SecurityKind union cases" => BlockAfter(source, "type SecurityKind =")
                .Where(static line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                .ToArray(),
            "AssetClassRegistry.keyOf arms" => BlockAfter(source, "match kind with",
                startSearchAt: IndexOf(source, "let private keyOf")),
            "AssetClassRegistry.descriptors rows" => BlockAfter(source, "let private descriptors : AssetClassDescriptor list ="),
            "Flat writer dispatch arms" => source.Where(static line => WriterArmLine().IsMatch(line)).ToArray(),
            "ToSecurityKind decode arms" => DecodeSwitchArms(source),
            "SecurityAssetClassNames constants" => BlockAfter(source, "{",
                    startSearchAt: IndexOf(source, "public static class SecurityAssetClassNames"))
                .TakeWhile(static line => line != "}")
                .ToArray(),
            "SecurityAssetClassCatalog row heads" => source.Where(static line => CatalogRowHeadLine().IsMatch(line)).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(fragmentName), fragmentName, "No extractor for this fragment."),
        };

    private static string[] DecodeSwitchArms(string[] source)
    {
        var start = IndexOf(source, "var kind = assetClass switch");
        var end = IndexOf(source, "_ when mode == SecurityKindMappingMode.Write", start);
        return source[(start + 1)..end]
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith('"'))
            .ToArray();
    }

    /// <summary>The lines after the first line (at or after <paramref name="startSearchAt"/>) whose
    /// trimmed text starts with <paramref name="anchor"/>, up to the next blank line.</summary>
    private static string[] BlockAfter(string[] source, string anchor, int startSearchAt = 0)
    {
        var start = IndexOf(source, anchor, startSearchAt);
        return source.Skip(start + 1).TakeWhile(static line => line.Trim().Length > 0).ToArray();
    }

    private static int IndexOf(string[] source, string anchor, int startSearchAt = 0)
    {
        for (var i = startSearchAt; i < source.Length; i++)
        {
            if (source[i].Trim().StartsWith(anchor, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Anchor '{anchor}' was not found; the hand-written site moved.");
    }

    private static string[] ReadSource(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath)).ReplaceLineEndings("\n").Split('\n');

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

    [GeneratedRegex(@"^\s*\| SecurityKind\.\w+ terms -> GeneratedFlatWriter\.")]
    private static partial Regex WriterArmLine();

    [GeneratedRegex(@"^\s*AssetClass: SecurityAssetClassNames\.\w+,\s*$")]
    private static partial Regex CatalogRowHeadLine();
}
