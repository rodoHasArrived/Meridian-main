using System.Text.Json;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Live.Designer;

/// <summary>Defines the executable identity shared by a Designer backtest and its paper run.</summary>
public static class DesignerStrategyVersion
{
    public const string VersionKey = "strategyVersionSha256";
    public const string DraftRevisionKey = "designerDraftRevision";
    public const string RealismKey = "designerRealismSha256";
    public const string DataSnapshotKey = "dataSnapshotSha256";
    public const string DataProvenanceKey = "designerReplayProvenance";
    public const string TenantKey = "workstationTenantId";
    public const string CompanyKey = "workstationCompanyId";

    public static string? Read(StrategyRunEntry run) =>
        run.ParameterSet?.GetValueOrDefault(VersionKey);

    public static bool HasRetainedProvenance(StrategyRunEntry run) =>
        !string.IsNullOrWhiteSpace(run.DataProvenanceToken) &&
        string.Equals(run.DataProvenanceToken, run.ParameterSet?.GetValueOrDefault(DataProvenanceKey), StringComparison.Ordinal);

    public static string ComputeRealismHash(ExecutionRealismDescriptor realism) =>
        Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(realism));

    public static string ComputeStrategyId(StrategyRunRepositoryScope scope, string documentId)
    {
        if (!StrategyRunRepositoryVisibility.TryCreateScopeKey(scope, out _))
            throw new ArgumentException("An authenticated tenant and company are required.", nameof(scope));
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        // Preserve the identity already used by retained scoped Designer runs.
        return "strategy-design-" + Sha256Digest.ComputeUtf8(
            JsonSerializer.Serialize(new[] { scope.TenantId, scope.CompanyId, documentId }));
    }

    public static string Compute(StrategyDesignDocument document, string realismHash)
    {
        // Use a structured encoding so authored values cannot collide with record delimiters.
        var inputs = new
        {
            Schema = "meridian.designer.strategy-version.v1",
            document.DocumentId,
            document.Version,
            document.DatasetReference,
            document.Universe,
            Cells = document.Cells.Select(static cell => new
            {
                cell.CellId,
                cell.Kind,
                cell.Purpose,
                cell.Source,
                cell.FieldRefs,
                Parameters = cell.Parameters?.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            }),
            Transitions = document.Transitions.Select(static transition => new
            {
                transition.TransitionId,
                transition.FromCellId,
                transition.ToCellId,
                transition.Kind,
                transition.Condition,
                transition.MaxIterations
            }),
            Realism = realismHash
        };
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(inputs));
    }

    public static bool Matches(StrategyDesignDocument document, IReadOnlyDictionary<string, string> parameters) =>
        parameters.TryGetValue(RealismKey, out var realismHash) && Sha256Digest.IsCanonical(realismHash) &&
        parameters.TryGetValue(VersionKey, out var version) && Sha256Digest.FixedEquals(version, Compute(document, realismHash));
}
