using System.Globalization;
using System.Text;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Live.Designer;
using Meridian.Strategies.Live.Strategies;

namespace Meridian.Strategies.Artifacts;

public enum StrategyAuthoringRoute
{
    Code,
    Designer,
    QuantScript
}

/// <summary>The inputs that determine how one retained strategy revision trades.</summary>
public sealed record StrategyArtifact(
    string StrategyId,
    StrategyAuthoringRoute Route,
    string SourceRevision,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Universe,
    string? DataDependency,
    string? ExecutionRealism,
    IReadOnlyDictionary<string, string>? RiskLimits = null,
    StrategyDesignDocument? DesignerDocument = null,
    string? ScriptSource = null)
{
    public const string IdPrefix = "sa1-";

    public static bool IsValidId(string? value) =>
        value is { Length: 68 } && value.StartsWith(IdPrefix, StringComparison.Ordinal)
        && value.AsSpan(IdPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    public string ComputeId()
    {
        var canonical = new StringBuilder();
        Append(canonical, "meridian.strategy-artifact.v1");
        Append(canonical, StrategyId);
        Append(canonical, Route.ToString());
        Append(canonical, SourceRevision);
        AppendMap(canonical, Parameters);
        foreach (var symbol in Universe.Select(symbol => symbol.Trim().ToUpperInvariant())
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            Append(canonical, symbol);
        Append(canonical, null);
        Append(canonical, DataDependency);
        Append(canonical, ExecutionRealism);
        AppendMap(canonical, RiskLimits);
        Append(canonical, ScriptSource);
        if (DesignerDocument is { } document)
        {
            Append(canonical, document.DocumentId);
            Append(canonical, document.Version);
            Append(canonical, document.DatasetReference);
            foreach (var symbol in document.Universe)
                Append(canonical, symbol);
            Append(canonical, null);
            foreach (var cell in document.Cells)
            {
                Append(canonical, cell.CellId);
                Append(canonical, cell.Kind.ToString());
                Append(canonical, cell.Purpose.ToString());
                Append(canonical, cell.Source);
                Append(canonical, cell.DisabledReason);
                foreach (var field in cell.FieldRefs)
                    Append(canonical, field);
                Append(canonical, null);
                AppendMap(canonical, cell.Parameters);
            }
            Append(canonical, null);
            foreach (var transition in document.Transitions)
            {
                Append(canonical, transition.TransitionId);
                Append(canonical, transition.FromCellId);
                Append(canonical, transition.ToCellId);
                Append(canonical, transition.Kind.ToString());
                Append(canonical, transition.Condition);
                Append(canonical, transition.MaxIterations?.ToString(CultureInfo.InvariantCulture));
            }
        }

        return IdPrefix + Sha256Digest.ComputeUtf8(canonical.ToString());
    }

    public static StrategyArtifact FromCode(
        string strategyId,
        IBacktestStrategy strategy,
        IReadOnlyDictionary<string, string> parameters,
        BacktestRequest request,
        string? dataDependency = null,
        IReadOnlyDictionary<string, string>? riskLimits = null) =>
        new(strategyId, StrategyAuthoringRoute.Code, CodeRevision(strategy.GetType()), CaptureCodeParameters(strategy, parameters),
            request.Symbols ?? [], dataDependency, request.ToRealismDescriptor().ToCanonicalString(), riskLimits);

    public static StrategyArtifact FromDesigner(
        StrategyDesignDocument document,
        IReadOnlyDictionary<string, string>? parameters = null,
        BacktestRequest? request = null) =>
        new(document.DocumentId, StrategyAuthoringRoute.Designer, DesignerDocumentRevision.ComputeHash(document),
            parameters ?? new Dictionary<string, string>(), document.Universe, document.DatasetReference,
            request?.ToRealismDescriptor().ToCanonicalString(), DesignerDocument: document);

    public static string CodeRevision(Type implementation)
    {
        var revision = $"{implementation.FullName}@{implementation.Module.ModuleVersionId:D}";
        if (string.IsNullOrEmpty(implementation.Assembly.Location))
            return revision;
        using var binary = File.OpenRead(implementation.Assembly.Location);
        return $"{revision}:{Sha256Digest.Compute(binary)}";
    }

    internal static IReadOnlyDictionary<string, string> CaptureCodeParameters(
        IBacktestStrategy strategy, IReadOnlyDictionary<string, string> declared)
    {
        var parameters = new Dictionary<string, string>(declared, StringComparer.OrdinalIgnoreCase);
        switch (strategy)
        {
            case BuyAndHoldLiveStrategy buyAndHold:
                parameters["quantity"] = buyAndHold.QuantityPerSymbol.ToString(CultureInfo.InvariantCulture);
                break;
            case MovingAverageCrossoverLiveStrategy crossover:
                var actual = crossover.EffectiveParameters;
                parameters["fastPeriod"] = actual.FastPeriod.ToString(CultureInfo.InvariantCulture);
                parameters["slowPeriod"] = actual.SlowPeriod.ToString(CultureInfo.InvariantCulture);
                parameters["quantity"] = actual.Quantity.ToString(CultureInfo.InvariantCulture);
                parameters["barBased"] = actual.BarBased ? "true" : "false";
                break;
        }
        return parameters;
    }

    private static void AppendMap(StringBuilder builder, IReadOnlyDictionary<string, string>? values)
    {
        foreach (var pair in (values ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Append(builder, pair.Key);
            Append(builder, pair.Value);
        }
        Append(builder, null);
    }

    private static void Append(StringBuilder builder, string? value)
    {
        if (value is null)
            builder.Append("-1:");
        else
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }
}
