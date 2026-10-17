using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Ratchet against new hardcoded asset-class lists. <see cref="SecurityAssetClassCatalog"/> is the one
/// place that may enumerate the Security Master asset classes; every other surface should read a
/// descriptor fact or be one of the keyed registries below, each of which is held to the catalog by a
/// parity test. A source file that names five or more distinct catalog classes as string literals is
/// treated as a dispatch table and must appear in <see cref="KnownDispatchSites"/>.
/// <para>
/// The list may only shrink: a site that stops dispatching on asset class must be removed, and no
/// site may be added without a parity guard of its own. Thresholding on distinct literals per file
/// keeps the scan cheap and immune to formatting; the known sites were measured at 6-26 literals.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SecurityAssetClassLiteralRatchetTests
{
    private const int Threshold = 5;

    private const string SourceOfTruth = "src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs";

    // Repository-relative, forward-slash paths. Each entry dispatches on asset class by design and
    // is held to the catalog elsewhere (SecurityAssetClassRegistryCoverageTests,
    // SecurityAssetClassParityGuardTests, SecurityAssetClassSurfaceParityTests, SecurityKindMappingTests).
    private static readonly string[] KnownDispatchSites =
    [
        "src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs",
        "src/Meridian.Application/SecurityMaster/SecurityMasterOperationalReadinessService.cs",
        "src/Meridian.Application/SecurityMaster/SecurityMasterService.cs",
        "src/Meridian.Application/SecurityMaster/Validation/AssetClassValidatorRegistry.cs",
        "src/Meridian.Contracts/SecurityMaster/CorporateActionTypeDescriptorCatalog.cs",
        "src/Meridian.Contracts/SecurityMaster/InstrumentTypeDescriptorCatalog.cs",
        "src/Meridian.Contracts/SecurityMaster/SecurityAssetSpecificTermsUpcasterChain.cs",
        "src/Meridian.Contracts/SecurityMaster/SecurityAssetTermsSchema.cs",
        "src/Meridian.Contracts/SecurityMaster/StructuredCashFlowTermsResolver.cs",
        "src/Meridian.FSharp/Domain/SecurityClassification.fs",
        "src/Meridian.FSharp/Domain/SecurityMaster.fs",
        "src/Meridian.Instruments/AssetOperations/AssetObligationProjectionService.cs",
        "src/Meridian.ReferenceData/SecurityMaster/SecurityKindMapping.cs",
        "src/Meridian.Storage/SecurityMaster/SecurityTermsProjectionRegistry.cs",
        "src/Meridian.Ui.Shared/Services/SecurityMasterSecurityReferenceLookup.cs",
    ];

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/", "/node_modules/", "/wwwroot/"];

    [Fact]
    public void NoNewSourceFile_HardcodesAnAssetClassList()
    {
        var counts = CountDistinctAssetClassLiterals();

        var offenders = counts
            .Where(static entry => entry.Value >= Threshold && entry.Key != SourceOfTruth)
            .Select(static entry => entry.Key)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeSubsetOf(
            KnownDispatchSites,
            $"a file naming {Threshold}+ asset classes is a new dispatch table; read the fact from "
            + "SecurityAssetClassDescriptor instead, or add a parity guard and list the site here");
    }

    [Fact]
    public void KnownDispatchSites_StillDispatchOnAssetClass_OrMustLeaveTheList()
    {
        var counts = CountDistinctAssetClassLiterals();

        foreach (var site in KnownDispatchSites)
        {
            counts.TryGetValue(site, out var distinctLiterals);
            distinctLiterals.Should().BeGreaterThanOrEqualTo(
                Threshold,
                $"{site} no longer hardcodes an asset-class list; remove it from KnownDispatchSites so the ratchet only ever shrinks");
        }
    }

    [Fact]
    public void KnownDispatchSites_AreSortedAndDistinct()
    {
        KnownDispatchSites.Should().OnlyHaveUniqueItems();
        KnownDispatchSites.Should().BeInAscendingOrder(StringComparer.Ordinal);
        KnownDispatchSites.Should().NotContain(SourceOfTruth, "the catalog is the source, not a dispatch site");
    }

    private static IReadOnlyDictionary<string, int> CountDistinctAssetClassLiterals()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var literal = new Regex(
            "\"(" + string.Join("|", SecurityAssetClassCatalog.AssetClasses.Select(Regex.Escape)) + ")\"",
            RegexOptions.CultureInvariant);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".fs"))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (SkippedPathSegments.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
            {
                continue;
            }

            var distinct = literal.Matches(File.ReadAllText(file))
                .Select(static match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (distinct > 0)
            {
                counts[relative] = distinct;
            }
        }

        return counts;
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
