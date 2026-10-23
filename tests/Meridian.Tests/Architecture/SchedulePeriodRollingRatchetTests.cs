using System.Text.RegularExpressions;
using FluentAssertions;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Ratchet against rolling schedule periods outside the contractual schedule kernel
/// (<c>Meridian.Contracts.SecurityMaster.Schedules</c>). Every <c>.AddMonths(</c> or
/// <c>.AddYears(</c> (or F# <c>.AddMonths n</c>) in the schedule-owning source roots fails here unless
/// the file is listed with the number of calls it still carries: in
/// <see cref="LegacyPeriodRollSites"/> while its generator awaits retirement onto the kernel, or in
/// <see cref="SingleOffsetSites"/> when the call is a single date offset rather than a period roll.
/// <para>
/// Both lists may only shrink: a file that drops a call must lower or remove its entry, and no new
/// file or higher count may be added. <see cref="LegacyPeriodRollSites"/> ends empty as each
/// generator moves onto <c>ScheduleGenerator</c>. A genuine new single offset is added to
/// <see cref="SingleOffsetSites"/> with its reason and reviewed like any ratchet entry.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SchedulePeriodRollingRatchetTests
{
    private const string KernelRoot = "src/Meridian.Contracts/SecurityMaster/Schedules/";

    // Repository-relative, forward-slash roots (folders end with '/') the scan covers: the schedule
    // generators' owners, not unrelated date math such as reporting windows or staleness cut-offs.
    private static readonly string[] ScannedRoots =
    [
        "src/Meridian.Contracts/SecurityMaster/",
        "src/Meridian.Application/SecurityMaster/",
        "src/Meridian.Application/DirectLending/",
        "src/Meridian.Application/FundStructure/",
        "src/Meridian.Instruments/AssetOperations/",
        "src/Meridian.Instruments/FixedIncome/",
        "src/Meridian.FSharp/Domain/",
        "src/Meridian.FSharp.DirectLending.Aggregates/",
        "src/Meridian.Strategies/Services/SecurityMasterAccountingEventService.cs",
    ];

    // Repository-relative path → period-roll calls still carried by a legacy generator. Each entry
    // is a generator whose frozen copy in LegacyScheduleOracles is parity-proven against the kernel.
    private static readonly IReadOnlyDictionary<string, int> LegacyPeriodRollSites = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // RegularCouponDates and BackwardCouponDates (constant-yield amortization).
        ["src/Meridian.Contracts/SecurityMaster/FaceValueLot.cs"] = 2,
        // The bond coupon roll in BuildBondProjectedCashFlows.
        ["src/Meridian.Instruments/AssetOperations/AssetOperationsReadService.cs"] = 1,
        // NextMonthlyDate, NextQuarterDate, NextFrequencyDate and AdvanceAfter.
        ["src/Meridian.Instruments/AssetOperations/AssetObligationProjectionService.cs"] = 4,
        // The projection roll in BuildProjectedFlows.
        ["src/Meridian.Application/DirectLending/PostgresDirectLendingCommandService.cs"] = 1,
        // The monthly projection roll in BuildFlows.
        ["src/Meridian.Application/DirectLending/InMemoryDirectLendingService.Workflows.cs"] = 1,
        // TryAdvanceByFrequency (monthly, quarterly, semiannual and annual).
        ["src/Meridian.Application/FundStructure/InMemoryFundStructureService.cs"] = 4,
    };

    // Repository-relative path → single date offsets that are not period rolls and stay by design.
    private static readonly IReadOnlyDictionary<string, int> SingleOffsetSites = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // isInterestOnlyPeriod: the interest-only end date is origination plus the interest-only
        // months, a single contractual offset that no schedule generates.
        ["src/Meridian.FSharp/Domain/DirectLending.fs"] = 1,
    };

    private static readonly Regex PeriodRoll = new(@"\.Add(Months|Years)\s*[( ]", RegexOptions.CultureInvariant);

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/"];

    [Fact]
    public void NoScannedFile_RollsPeriodsBeyondItsAllowance()
    {
        var counts = CountPeriodRolls();

        var offenders = counts
            .Where(entry => entry.Value > Allowance(entry.Key))
            .Select(entry => $"{entry.Key} ({entry.Value})")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "schedule periods are rolled by Meridian.Contracts.SecurityMaster.Schedules.ScheduleGenerator; "
            + "build a ScheduleSpec (or use ScheduleGenerator.RollDates) instead of stepping AddMonths/AddYears, "
            + "or list a genuine single offset in SingleOffsetSites with its reason");
    }

    [Fact]
    public void ListedSites_StillCarryTheirCalls_OrMustShrink()
    {
        var counts = CountPeriodRolls();

        foreach (var (site, allowed) in LegacyPeriodRollSites.Concat(SingleOffsetSites))
        {
            counts.TryGetValue(site, out var actual);
            actual.Should().Be(allowed, $"{site} carries {actual} AddMonths/AddYears call(s); the ratchet entry must match so it only ever shrinks");
        }
    }

    [Fact]
    public void Lists_AreDisjoint_InsideTheScan_AndTheKernelIsUnlisted()
    {
        LegacyPeriodRollSites.Keys.Should().NotIntersectWith(SingleOffsetSites.Keys, "a file is either a legacy period roll or a single offset");

        var listed = LegacyPeriodRollSites.Keys.Concat(SingleOffsetSites.Keys).ToArray();
        listed.Should().OnlyContain(path => IsScanned(path), "an entry outside the scanned roots could never shrink");
        listed.Should().NotContain(path => path.StartsWith(KernelRoot, StringComparison.Ordinal), "the kernel is the roll's owner, not a site");
        ScannedRoots.Should().NotContain(KernelRoot);
    }

    private static int Allowance(string path)
        => LegacyPeriodRollSites.TryGetValue(path, out var legacy) ? legacy
            : SingleOffsetSites.TryGetValue(path, out var single) ? single
            : 0;

    private static bool IsScanned(string relative)
        => !relative.StartsWith(KernelRoot, StringComparison.Ordinal)
           && ScannedRoots.Any(root => root.EndsWith('/')
               ? relative.StartsWith(root, StringComparison.Ordinal)
               : string.Equals(relative, root, StringComparison.Ordinal));

    private static IReadOnlyDictionary<string, int> CountPeriodRolls()
    {
        var root = FindRepositoryRoot();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = ScannedRoots.SelectMany(scanned => scanned.EndsWith('/')
            ? Directory.EnumerateFiles(Path.Combine(root, scanned), "*.*", SearchOption.AllDirectories)
            : new[] { Path.Combine(root, scanned) });
        foreach (var file in files.Where(File.Exists))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".fs"))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!IsScanned(relative)
                || SkippedPathSegments.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
            {
                continue;
            }

            var matches = PeriodRoll.Matches(File.ReadAllText(file)).Count;
            if (matches > 0)
            {
                counts[relative] = matches;
            }
        }

        counts.Should().NotBeEmpty("the scan must find the legacy period-roll sites; an empty scan means the roots moved");
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
