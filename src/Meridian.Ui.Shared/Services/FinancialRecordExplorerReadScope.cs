namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Which sibling families the caller may see enriched into a financial-record explorer row. The
/// security-instrument explorer's rows are the Security Master references a strategy run touched, so
/// a strategy permission admits it — but each row is then decorated with the instrument passport,
/// AssetOperations detail and readiness, posted journal proofs, report-pack usage, and direct-lending
/// operations detail. None of those is a claim the explorer's own admission establishes.
/// <para>
/// Each flag mirrors the permissions of the route that serves that family directly, so a caller sees
/// through the explorer exactly what it could fetch head-on and nothing more.
/// </para>
/// <para>
/// <see cref="All"/> is for callers whose authority is already established: the legacy in-process
/// overloads and tests. Request-serving callers resolve the scope from the caller's permissions, and
/// an unreadable family is not loaded at all rather than loaded and discarded.
/// </para>
/// </summary>
/// <param name="Reporting">Report-pack line usage. Mirrors the reporting read routes.</param>
/// <param name="DirectLending">Direct-lending loan health. Mirrors the direct-lending read routes.</param>
/// <param name="SecurityMaster">
/// The instrument passport. Mirrors <c>GetSecurityMasterWorkstationInstrumentPassport</c>, which
/// admits only ViewSecurityMaster and ModifySecurityMaster.
/// </param>
/// <param name="AssetOperations">
/// AssetOperations detail, readiness, and the journal proofs derived from them. Mirrors
/// <c>GetWorkstationAssetOperations</c>, which does not admit strategy permissions.
/// </param>
/// <param name="Strategy">
/// The identity of the run a projection was derived from — its id, strategy name and mode, the
/// evidence packet for it, and links into the run ledger and portfolio. Mirrors the run routes, which
/// admit only ViewStrategies and ManageStrategies. The security-instrument explorer admits Security
/// Master callers on its own basis, and run identity is not part of that basis.
/// </param>
public sealed record FinancialRecordExplorerReadScope(
    bool Reporting,
    bool DirectLending,
    bool SecurityMaster,
    bool AssetOperations,
    bool Strategy)
{
    public static readonly FinancialRecordExplorerReadScope All =
        new(Reporting: true, DirectLending: true, SecurityMaster: true, AssetOperations: true, Strategy: true);
}
