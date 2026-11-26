using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Phase 0 guardrail for docs/engineering/plans/ui-shared-decomposition-plan.md.
/// <para>
/// <c>Meridian.Ui.Shared</c> is being reduced to an ASP.NET adapter layer, so its
/// <c>Services/</c> and <c>Evidence/</c> folders may not gain new service-like types (names ending
/// in Service, Store, Coordinator, and so on) while domain services move out. Counting distinct type
/// names rather than files or lines keeps bug fixes and partial-class splits unaffected.
/// </para>
/// <para>
/// The ceilings only go down. When a decomposition PR moves types out, lower the ceiling to the new
/// count in the same PR. New domain services belong in their destination project, not here.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public sealed class UiSharedServiceGrowthRatchetTests
{
    // Folder under src/Meridian.Ui.Shared -> most distinct service-like type names it may declare.
    private static readonly IReadOnlyDictionary<string, int> Ceilings = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Services"] = 226,
        ["Evidence"] = 16,
    };

    private static readonly Regex ServiceLikeDeclaration = new(
        @"\b(?:class|record|interface)\s+([A-Z]\w*(?:Service|Store|Coordinator|Runner|Scheduler|Provider|Builder|Projector|Resolver|Authority|Retainer|Gateway|Engine))\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex Comments = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    [Fact]
    public void UiShared_DoesNotGainServiceLikeTypes()
    {
        foreach (var (folder, ceiling) in Ceilings)
        {
            var actual = CountServiceLikeTypes(folder);
            actual.Should().BeLessThanOrEqualTo(
                ceiling,
                $"src/Meridian.Ui.Shared/{folder} may not gain service-like types during the Ui.Shared decomposition; "
                + "add new domain services to their destination project (see docs/engineering/plans/ui-shared-decomposition-plan.md)");
        }
    }

    [Fact]
    public void Ceilings_AreTight_SoTheRatchetOnlyGoesDown()
    {
        foreach (var (folder, ceiling) in Ceilings)
        {
            var actual = CountServiceLikeTypes(folder);
            actual.Should().Be(
                ceiling,
                $"src/Meridian.Ui.Shared/{folder} now declares {actual} service-like types; lower its ceiling to match");
        }
    }

    private static int CountServiceLikeTypes(string folder)
    {
        var root = Path.Combine(FindRepositoryRoot(), "src", "Meridian.Ui.Shared", folder);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var text = Comments.Replace(File.ReadAllText(file), string.Empty);
            foreach (Match match in ServiceLikeDeclaration.Matches(text))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        return names.Count;
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

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
