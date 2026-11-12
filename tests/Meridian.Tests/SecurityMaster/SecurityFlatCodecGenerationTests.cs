using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Holds the committed flat-codec arms to the binding table. Both generated files are ordinary
/// source, compiled with the rest; these tests regenerate them from
/// <see cref="SecurityAssetTermsCodecBindings"/> and fail when the committed text differs, so the
/// table is the only place a generated class's codec is edited. Set
/// <c>MERIDIAN_ACCEPT_GENERATED_CODEC=1</c> to write the regenerated files instead of comparing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityFlatCodecGenerationTests
{
    [Fact]
    public void TheCommittedDecodeArms_AreWhatTheBindingTableGenerates()
        => AssertCommitted(SecurityFlatCodecGenerator.DecodePath, SecurityFlatCodecGenerator.GenerateDecode());

    [Fact]
    public void TheCommittedWriterArms_AreWhatTheBindingTableGenerates()
        => AssertCommitted(SecurityFlatCodecGenerator.WriterPath, SecurityFlatCodecGenerator.GenerateWriter());

    [Fact]
    public void GeneratedClasses_AreDeclaredCatalogClasses_ListedOnce()
    {
        var generated = SecurityAssetTermsCodecBindings.Classes.Select(static binding => binding.AssetClass).ToArray();

        generated.Should().OnlyHaveUniqueItems();
        generated.Should().BeSubsetOf(SecurityAssetTermsSchema.AssetClasses);
        SecurityMasterMapping.GeneratedDecodeClasses.Should().Equal(generated,
            "the decode arms compiled into the mapping are the table's classes");
    }

    [Theory]
    [MemberData(nameof(GeneratedClasses))]
    public void EveryDeclaredKeyOfAGeneratedClass_IsWrittenByExactlyOneRow(string assetClass)
    {
        // rateUnit is written by the shared rate-unit step, after the class's own document.
        var declared = SecurityAssetTermsSchema.Fields(assetClass)
            .Select(static field => field.Key)
            .Where(static key => key != "rateUnit")
            .ToArray();
        var written = SecurityAssetTermsCodecBindings.ByAssetClass[assetClass].Members
            .Where(static row => row.Key is not null && !row.DecodeOnly)
            .Select(static row => row.Key!)
            .ToArray();

        written.Should().OnlyHaveUniqueItems();
        written.Should().BeEquivalentTo(declared, $"the {assetClass} rows must write exactly the keys the schema declares");
    }

    public static TheoryData<string> GeneratedClasses()
    {
        var data = new TheoryData<string>();
        foreach (var binding in SecurityAssetTermsCodecBindings.Classes)
        {
            data.Add(binding.AssetClass);
        }

        return data;
    }

    private static void AssertCommitted(string relativePath, string generated)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath);
        if (Environment.GetEnvironmentVariable("MERIDIAN_ACCEPT_GENERATED_CODEC") == "1")
        {
            File.WriteAllText(path, generated);
            return;
        }

        File.Exists(path).Should().BeTrue($"{relativePath} is generated and committed");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(generated,
            $"{relativePath} must be regenerated from the binding table, not edited");
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
