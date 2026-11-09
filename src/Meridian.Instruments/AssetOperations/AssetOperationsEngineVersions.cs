namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// The engine-version stamps asset operations writes on bond projection runs, projected cash flows and
/// the terms and obligations timeline. A stamp names the rules that produced a row; readers pass it
/// through as evidence and never branch on it, so a stored row keeps the stamp it was written with.
/// </summary>
public static class AssetOperationsEngineVersions
{
    /// <summary>
    /// The legacy bond loop's run and flow stamp up to 2026-11-09. It is no longer written and stays
    /// readable on stored evidence. The flow-id derivation changed on 2026-10-29 (commit 4f2e63245), so a
    /// run carrying this stamp from that day on already has the <see cref="LegacyBondCashFlow"/> ids; the
    /// stamp alone does not tell the two apart.
    /// </summary>
    public const string LegacyBondCashFlowV1 = "fixed-income-cash-flow-projection-v1";

    /// <summary>
    /// The legacy bond loop for records that declare no schedule conventions, with flow ids derived
    /// from the schedule period (<c>SchedulePeriodId</c>) instead of the due date and sequence.
    /// </summary>
    public const string LegacyBondCashFlow = "fixed-income-cash-flow-projection-v2";

    /// <summary>
    /// A bond run mapped from the shared Security Master calculation with retained schedule evidence,
    /// and each of its flows that carries rate evidence.
    /// </summary>
    public const string DeclaredObligation = "asset-obligation-declared-v3";

    /// <summary>A flow of a declared run that carries no rate evidence.</summary>
    public const string DeclaredObligationWithoutRateEvidence = "asset-obligation-declared-v2";

    /// <summary>
    /// The terms and obligations timeline built from retained terms scalars and projected flows. A
    /// timeline with a projection run is stamped <c>{run stamp}+{this}</c>.
    /// </summary>
    public const string ObligationTimeline = "asset-terms-obligations-timeline-v1";
}
