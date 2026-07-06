using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.TestSupport.CorporateActions;

/// <summary>
/// Loads and validates the golden corporate-action fixture library under
/// <c>tests/fixtures/corporate-actions/golden/</c>. A malformed fixture is a test failure by
/// design: silent shrinkage of the fixture set is exactly the failure mode the pack exists to
/// prevent, so every validation error names the offending file and reason.
/// </summary>
public static class GoldenCorporateActionScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<IReadOnlyList<GoldenCorporateActionScenario>> Cache = new(LoadFromDisk);

    /// <summary>All scenarios, loaded once per test run.</summary>
    public static IReadOnlyList<GoldenCorporateActionScenario> LoadAll() => Cache.Value;

    public static GoldenCorporateActionScenario Load(string scenarioId)
        => LoadAll().FirstOrDefault(s => string.Equals(s.ScenarioId, scenarioId, StringComparison.Ordinal))
           ?? throw new InvalidOperationException(
               $"Golden corporate-action scenario '{scenarioId}' not found under '{FixtureDirectory()}'.");

    /// <summary>
    /// Scenario ids for theory expansion, optionally filtered to scenarios carrying
    /// <paramref name="invariantTag"/>.
    /// </summary>
    public static TheoryData<string> ScenarioIds(string? invariantTag = null)
    {
        var data = new TheoryData<string>();
        foreach (var scenario in LoadAll())
        {
            if (invariantTag is null || scenario.HasTag(invariantTag))
                data.Add(scenario.ScenarioId);
        }

        return data;
    }

    public static string FixtureDirectory()
        => Path.Combine(FindRepoRoot(), "tests", "fixtures", "corporate-actions", "golden");

    private static IReadOnlyList<GoldenCorporateActionScenario> LoadFromDisk()
    {
        var directory = FixtureDirectory();
        if (!Directory.Exists(directory))
            throw new InvalidOperationException($"Golden fixture directory not found: '{directory}'.");

        var scenarios = new List<GoldenCorporateActionScenario>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(static p => p, StringComparer.Ordinal))
        {
            GoldenCorporateActionScenario? scenario;
            try
            {
                scenario = JsonSerializer.Deserialize<GoldenCorporateActionScenario>(File.ReadAllText(path), JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Golden fixture '{path}' is not valid JSON: {ex.Message}", ex);
            }

            if (scenario is null)
                throw new InvalidOperationException($"Golden fixture '{path}' deserialized to null.");

            Validate(scenario, path);

            if (!seenIds.Add(scenario.ScenarioId))
                throw new InvalidOperationException($"Golden fixture '{path}' duplicates scenarioId '{scenario.ScenarioId}'.");

            scenarios.Add(scenario);
        }

        if (scenarios.Count == 0)
            throw new InvalidOperationException($"No golden fixtures found under '{directory}'.");

        return scenarios;
    }

    private static void Validate(GoldenCorporateActionScenario scenario, string path)
    {
        if (string.IsNullOrWhiteSpace(scenario.ScenarioId))
            throw new InvalidOperationException($"Golden fixture '{path}' has a blank scenarioId.");

        var expectedId = Path.GetFileNameWithoutExtension(path);
        if (!string.Equals(scenario.ScenarioId, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Golden fixture '{path}' declares scenarioId '{scenario.ScenarioId}' but the filename implies '{expectedId}'.");
        }

        if (string.IsNullOrWhiteSpace(scenario.Ticker))
            throw new InvalidOperationException($"Golden fixture '{path}' has a blank ticker.");

        if (scenario.Actions.Count == 0)
            throw new InvalidOperationException($"Golden fixture '{path}' declares no corporate actions.");

        var allowUnknownTypes = scenario.HasTag(GoldenInvariantTags.UnknownEventType);
        var actionIds = scenario.Actions.Select(static a => a.CorpActId).ToHashSet();

        foreach (var action in scenario.Actions)
        {
            if (!CorporateActionLifecycleStates.IsWriteable(action.LifecycleState))
            {
                throw new InvalidOperationException(
                    $"Golden fixture '{path}' action '{action.CorpActId}' has non-writeable lifecycle state '{action.LifecycleState}'.");
            }

            if (action.SupersedesCorpActId is { } superseded && !actionIds.Contains(superseded))
            {
                throw new InvalidOperationException(
                    $"Golden fixture '{path}' action '{action.CorpActId}' supersedes '{superseded}', which is not in the same fixture.");
            }

            if (!CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor))
            {
                if (allowUnknownTypes)
                    continue;

                throw new InvalidOperationException(
                    $"Golden fixture '{path}' action '{action.CorpActId}' has unresolvable event type '{action.EventType}' " +
                    $"and the fixture does not carry the '{GoldenInvariantTags.UnknownEventType}' tag.");
            }

            foreach (var field in descriptor.RequiredFields)
            {
                if (!HasRequiredField(action, field))
                {
                    throw new InvalidOperationException(
                        $"Golden fixture '{path}' action '{action.CorpActId}' ({descriptor.CanonicalName}) is missing required field '{field}'.");
                }
            }
        }
    }

    private static bool HasRequiredField(CorporateActionDto action, CorporateActionField field) => field switch
    {
        CorporateActionField.DividendPerShare => action.DividendPerShare.HasValue,
        CorporateActionField.Currency => !string.IsNullOrWhiteSpace(action.Currency),
        CorporateActionField.SplitRatio => action.SplitRatio.HasValue,
        CorporateActionField.NewSecurityId => action.NewSecurityId.HasValue,
        CorporateActionField.DistributionRatio => action.DistributionRatio.HasValue,
        CorporateActionField.AcquirerSecurityId => action.AcquirerSecurityId.HasValue,
        CorporateActionField.ExchangeRatio => action.ExchangeRatio.HasValue,
        CorporateActionField.SubscriptionPricePerShare => action.SubscriptionPricePerShare.HasValue,
        CorporateActionField.RightsPerShare => action.RightsPerShare.HasValue,
        CorporateActionField.PayDate => action.PayDate.HasValue,
        CorporateActionField.RedemptionPricePercentOfPar => action.RedemptionPricePercentOfPar.HasValue,
        _ => true
    };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root (Meridian.sln) walking up from '{AppContext.BaseDirectory}'.");
    }
}
