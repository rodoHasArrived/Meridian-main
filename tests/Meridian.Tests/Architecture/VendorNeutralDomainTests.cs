using FluentAssertions;

namespace Meridian.Tests.Architecture;

/// <summary>
/// The domain, contracts, storage and shared UI modules describe Meridian's own model; data-vendor
/// methodology lives in adapters (<c>Meridian.ReferenceData</c>, <c>Meridian.Infrastructure</c>) and
/// is wired at the composition root. This guard keeps the first vendor that was pulled out of the
/// domain (Clearwater) from drifting back in through identifiers, comments or operator-facing text.
/// <para>
/// The scan is case-sensitive on purpose: persisted lowercase keys such as the
/// <c>clearwater-corporate-action</c> model key are stored identifiers that must not change, while
/// the capitalised name is what appears in types, prose and UI strings.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public sealed class VendorNeutralDomainTests
{
    private const string VendorName = "Clearwater";

    private static readonly string[] GuardedModules =
    [
        "src/Meridian.Application",
        "src/Meridian.Contracts",
        "src/Meridian.FSharp",
        "src/Meridian.Instruments",
        "src/Meridian.Storage",
        "src/Meridian.Ui.Shared",
    ];

    // Files allowed to name the vendor, each for a stated reason. The list may only shrink.
    private static readonly string[] AllowedMentions =
    [
        // Composition root: this is where the vendor adapter is wired behind ICorporateActionRuleProfile.
        "src/Meridian.Application/Composition/Features/LedgerFeatureRegistration.cs",
        // Formula provenance: the calculations cite the vendor reference guide they were validated against.
        "src/Meridian.FSharp/Calculations/SecurityCalculations.fs",
    ];

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/", "/Migrations/"];

    [Fact]
    public void DomainModules_DoNotNameTheVendor()
    {
        var offenders = FilesNamingTheVendor()
            .Where(static path => !AllowedMentions.Contains(path, StringComparer.Ordinal))
            .ToArray();

        offenders.Should().BeEmpty(
            $"'{VendorName}' belongs in an adapter (Meridian.ReferenceData, Meridian.Infrastructure), not in the domain; "
            + "move the symbol or reword the comment, or add the file to AllowedMentions with a reason");
    }

    [Fact]
    public void AllowedMentions_StillNameTheVendor_OrMustLeaveTheList()
    {
        var naming = FilesNamingTheVendor().ToHashSet(StringComparer.Ordinal);

        foreach (var allowed in AllowedMentions)
        {
            naming.Should().Contain(allowed, $"{allowed} no longer names the vendor; remove it so the allowlist only shrinks");
        }
    }

    private static IEnumerable<string> FilesNamingTheVendor()
    {
        var root = FindRepositoryRoot();
        foreach (var module in GuardedModules)
        {
            var moduleRoot = Path.Combine(root, module.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.EnumerateFiles(moduleRoot, "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is not (".cs" or ".fs"))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (SkippedPathSegments.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains(VendorName, StringComparison.Ordinal))
                {
                    yield return relative;
                }
            }
        }
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
