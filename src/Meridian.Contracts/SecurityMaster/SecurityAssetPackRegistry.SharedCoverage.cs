namespace Meridian.Contracts.SecurityMaster;

public static partial class SecurityAssetPackRegistry
{
    // Every asset class that more than one built-in pack covers, claimed or planned, is listed here
    // with the packs that share it and why. ValidateAll holds the built-in registry to this list in
    // both directions, so an overlap is either a stated allowance or a reported defect; it is never
    // a side effect of the candidate filter. Pack coverage is many-to-many by contract
    // (FindByAssetClass returns a list), so sharing a class is allowed when it is declared.
    private static readonly IReadOnlyList<AssetPackSharedCoverage> SharedCoverage =
    [
        new(
            "DirectLoan",
            ["private-loan-credit", "mortgage-facility-intercompany"],
            "DirectLoan is the only modeled loan class, so it carries private credit and also the "
            + "mortgages and intercompany loans whose own classes are still planned."),
        new(
            "CreditFacility",
            ["private-loan-credit", "mortgage-facility-intercompany", "commitment-guarantee"],
            "Planned, not modeled. Three packs anticipate it as drawn credit, as a facility and as "
            + "unfunded exposure. No owning pack is chosen; settle that before the class enters the catalog.")
    ];

    /// <summary>
    /// The asset classes deliberately covered by more than one built-in pack, with the reason for each.
    /// <see cref="ValidateAll"/> reports any built-in overlap that is missing from this list.
    /// </summary>
    public static IReadOnlyList<AssetPackSharedCoverage> DeclaredSharedCoverage => SharedCoverage;

    /// <summary>
    /// The candidate-scoped overlap rule on one coverage axis: a candidate pack may not claim (or
    /// plan) a class another pack already claims (or plans). Built-in packs are not judged against
    /// each other here; <see cref="ValidateSharedCoverage"/> does that against the declared list.
    /// </summary>
    private static IEnumerable<AssetPackRegistryValidationIssue> CandidateOverlapIssues(
        IReadOnlyList<SecurityAssetPackDescriptor> packs,
        HashSet<string> candidateIds,
        Func<SecurityAssetPackDescriptor, IReadOnlyList<string>> coverage,
        string code,
        string target,
        string verb)
        => packs
            .SelectMany(pack => coverage(pack).Select(assetClass => (pack.PackId, AssetClass: assetClass)))
            .GroupBy(static row => row.AssetClass, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(static row => row.PackId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Where(group => group.Any(row => candidateIds.Contains(row.PackId)))
            .Select(group => Issue(
                code,
                "Critical",
                target,
                $"Asset class '{group.Key}' is {verb} by multiple asset packs."));

    /// <summary>
    /// Holds built-in packs to the declared shared-coverage list in both directions: a class that
    /// several built-in packs claim or plan must be declared with exactly those packs and a reason,
    /// and a declaration must describe an overlap that still exists.
    /// </summary>
    internal static IReadOnlyList<AssetPackRegistryValidationIssue> ValidateSharedCoverage(
        IReadOnlyList<SecurityAssetPackDescriptor> builtInPacks,
        IReadOnlyList<AssetPackSharedCoverage> declarations)
    {
        var issues = new List<AssetPackRegistryValidationIssue>();
        var shared = builtInPacks
            .SelectMany(static pack => pack.AssetClasses
                .Concat(pack.PlannedAssetClasses)
                .Select(assetClass => (pack.PackId, AssetClass: assetClass)))
            .GroupBy(static row => row.AssetClass, StringComparer.OrdinalIgnoreCase)
            .Select(static group => (
                AssetClass: group.Key,
                PackIds: group.Select(static row => row.PackId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .Where(static entry => entry.PackIds.Length > 1)
            .ToArray();

        foreach (var (assetClass, packIds) in shared)
        {
            var declared = declarations
                .Where(declaration => string.Equals(declaration.AssetClass, assetClass, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (declared.Length == 0)
            {
                issues.Add(Issue(
                    "asset-pack.shared-coverage-undeclared",
                    "Critical",
                    "SharedCoverage",
                    $"Asset class '{assetClass}' is covered by built-in packs {string.Join(", ", packIds)} "
                    + "without a declared shared-coverage allowance."));
                continue;
            }

            if (declared.Length > 1 ||
                !new HashSet<string>(declared[0].PackIds, StringComparer.OrdinalIgnoreCase).SetEquals(packIds))
            {
                issues.Add(Issue(
                    "asset-pack.shared-coverage-mismatch",
                    "Critical",
                    "SharedCoverage",
                    $"The shared-coverage declaration for '{assetClass}' must name exactly the built-in "
                    + $"packs that cover it: {string.Join(", ", packIds)}."));
            }
        }

        foreach (var declaration in declarations)
        {
            RequireText(declaration.Reason, "SharedCoverage.Reason", "asset-pack.shared-coverage-reason-required", issues);
            if (!shared.Any(entry => string.Equals(entry.AssetClass, declaration.AssetClass, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Issue(
                    "asset-pack.shared-coverage-stale",
                    "Critical",
                    "SharedCoverage",
                    $"Shared coverage is declared for '{declaration.AssetClass}', which built-in packs no longer share."));
            }
        }

        return issues;
    }
}

/// <summary>
/// One asset class that several built-in packs cover on purpose, claimed or planned, and why.
/// </summary>
public sealed record AssetPackSharedCoverage(
    string AssetClass,
    IReadOnlyList<string> PackIds,
    string Reason);
