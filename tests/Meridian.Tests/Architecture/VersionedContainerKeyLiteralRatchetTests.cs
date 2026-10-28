using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Ratchet against hardcoded versioned-container keys. The schema
/// (<see cref="SecurityAssetTermsSchema"/>) declares the <c>scheduleConventions</c> block and the
/// <c>callSchedule</c> and <c>putSchedule</c> arrays, and <see cref="SecurityVersionedTerms"/> names
/// their keys once; every other C# source under <c>src/</c> reads the constants, so a reader cannot
/// spell a key differently from the predicate and the shared reader that decide what a row declares.
/// <para>
/// The allowed set may only shrink. Increment 5a moved OpenLotAmortization.cs onto the constants and
/// aliased the mapping's private keys to them; the later increments use the constants.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public sealed class VersionedContainerKeyLiteralRatchetTests
{
    // Repository-relative, forward-slash paths of the only C# sources that may spell a key.
    private static readonly string[] AllowedSites =
    [
        "src/Meridian.Contracts/SecurityMaster/SecurityAssetTermsSchema.cs",
        "src/Meridian.Contracts/SecurityMaster/SecurityVersionedTerms.cs",
    ];

    private static readonly string[] Keys =
    [
        SecurityVersionedTerms.ScheduleConventionsKey,
        SecurityVersionedTerms.CallScheduleKey,
        SecurityVersionedTerms.PutScheduleKey,
    ];

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/", "/node_modules/"];

    [Fact]
    public void TheSourcesSpellingAVersionedContainerKey_AreExactlyTheAllowedSites()
    {
        var sites = FindKeyLiteralSites();

        sites.Except(AllowedSites, StringComparer.Ordinal).Should().BeEmpty(
            "a versioned-container key is read through SecurityVersionedTerms.ScheduleConventionsKey, CallScheduleKey "
            + "or PutScheduleKey, never spelled as a literal outside the schema and SecurityVersionedTerms");
        AllowedSites.Except(sites, StringComparer.Ordinal).Should().BeEmpty(
            "an allowed site that no longer spells a key must leave AllowedSites so the ratchet only ever shrinks");
    }

    [Fact]
    public void AllowedSites_AreSortedAndDistinct()
    {
        AllowedSites.Should().OnlyHaveUniqueItems();
        AllowedSites.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void EachKey_NamesAVersionedContainerTheSchemaDeclares()
    {
        foreach (var key in Keys)
        {
            SecurityAssetTermsSchema.AssetClasses
                .Select(assetClass => SecurityAssetTermsSchema.Field(assetClass, key))
                .Where(static field => field is not null)
                .Should().NotBeEmpty($"'{key}' must be a top-level term of at least one class")
                .And.OnlyContain(field => field!.Key == key && SecurityVersionedTerms.IsVersionedContainer(field!),
                    $"'{key}' is declared with exactly this spelling, as a versioned container");
        }
    }

    private static IReadOnlyList<string> FindKeyLiteralSites()
    {
        var root = FindRepositoryRoot();
        var literal = new Regex(
            "\"(" + string.Join("|", Keys.Select(Regex.Escape)) + ")\"",
            RegexOptions.CultureInvariant);

        var sites = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (SkippedPathSegments.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
            {
                continue;
            }

            if (literal.IsMatch(File.ReadAllText(file)))
            {
                sites.Add(relative);
            }
        }

        sites.Should().NotBeEmpty("the scan must find the schema's declarations; an empty scan means the source root moved");
        sites.Sort(StringComparer.Ordinal);
        return sites;
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

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }
}
