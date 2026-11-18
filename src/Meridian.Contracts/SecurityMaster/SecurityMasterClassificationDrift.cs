using System.Collections.Generic;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// One group of active securities whose latest retained event recorded a classification that the
/// current asset-class taxonomy now derives differently (for example a Swap recorded under the
/// <c>ListedDerivative</c> family before the taxonomy moved it to <c>OtcDerivative</c>).
/// </summary>
/// <param name="TypeName">Canonical asset-class name of the securities in this group.</param>
/// <param name="StoredAssetClass">Asset class the latest retained event recorded.</param>
/// <param name="StoredAssetFamily">Asset family the latest retained event recorded, if any.</param>
/// <param name="CurrentAssetClass">Asset class the current taxonomy derives.</param>
/// <param name="CurrentAssetFamily">Asset family the current taxonomy derives, if any.</param>
/// <param name="SecurityCount">Number of active securities in the group.</param>
/// <param name="SecurityIds">The securities in the group, ordered by id.</param>
public sealed record SecurityClassificationDriftEntryDto(
    string TypeName,
    string StoredAssetClass,
    string? StoredAssetFamily,
    string CurrentAssetClass,
    string? CurrentAssetFamily,
    int SecurityCount,
    IReadOnlyList<Guid> SecurityIds);

/// <summary>
/// Report of active securities whose retained classification differs from the one the current
/// taxonomy derives. Classification is derived on read, so every current projection, snapshot and
/// export already shows the current labels; only retained events keep the old ones, and they are
/// append-only. Building the report changes nothing.
/// </summary>
public sealed record SecurityClassificationDriftReportDto(
    DateTimeOffset GeneratedAt,
    int SecuritiesScanned,
    int UnreadableCount,
    int WithoutRetainedClassificationCount,
    int DriftedSecurityCount,
    IReadOnlyList<SecurityClassificationDriftEntryDto> Entries);

/// <summary>Builds the classification drift report for the active Security Master population.</summary>
public interface ISecurityMasterClassificationDriftReportService
{
    /// <summary>Scans active securities and compares each one's retained classification with the current taxonomy.</summary>
    Task<SecurityClassificationDriftReportDto> BuildReportAsync(CancellationToken ct = default);
}
