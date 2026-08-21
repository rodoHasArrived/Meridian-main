namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Which embedded families the caller may see inside the accounting workspace payload, which the
/// governance workspace serves under a second name from the same builder.
/// <para>
/// The workspace admits a deliberately wide set, because it is the screen the reconciliation,
/// direct-lending, Security Master and trading desks all open to see where the period stands. What
/// it may not do is treat that admission as authority for everything it aggregates: the payload
/// carries the break queue, the manual-journal workbench, and the authoritative reporting
/// projection, each of which is served head-on by a route with a strictly narrower gate. A
/// composite's admission is the narrowest of what it carries, not the widest.
/// </para>
/// <para>
/// Each flag mirrors the permissions of the route that serves that family directly, so a caller sees
/// through the workspace exactly what it could fetch on its own and nothing more. An unreadable
/// family is not loaded at all rather than loaded and discarded, so withholding it also costs
/// nothing.
/// </para>
/// <para>
/// Headline counters are deliberately outside this scope. <c>Metrics</c> and the workspace summary
/// are counts - open breaks, timing drift, security gaps, audit-ready runs - and a count is what the
/// workspace exists to show every desk it admits. Withholding the number as well as the records
/// would make the screen blank for the operators it was widened for, without withholding anything
/// the records do not already disclose in far more detail.
/// </para>
/// </summary>
/// <param name="BreakQueue">
/// Reconciliation break records - strategy and run identifiers, variances, reasons, assignees,
/// sign-off history, counterparties and resolution notes. Mirrors
/// <c>GetReconciliationBreakQueue</c>, which admits the direct-lending and Security Master families
/// and AdminMaintenance, and not ViewTrades.
/// </param>
/// <param name="ManualJournal">
/// The manual-journal workbench. Mirrors <c>GetManualJournalEntryWorkbench</c>, which admits only
/// AdminMaintenance and ManageDirectLending.
/// </param>
/// <param name="Reporting">
/// The authoritative reporting projection - profiles, templates, recent runs and report-pack
/// distributions. Mirrors the reporting-authority reads, which admit only ViewReporting and
/// AdminMaintenance. Distribution-access filtering inside the projection narrows which records a
/// principal matches; it is not a permission check and does not stand in for one.
/// </param>
/// <param name="StrategyRuns">
/// The reconciliation queue's run cards and the cash-flow summary derived from them - strategy name,
/// run id, mode and status, audit, ledger and portfolio references, governance evidence, security
/// coverage, the reconciliation detail, and the runs' cash and financing balances. Mirrors the run
/// routes, which admit only ViewStrategies and ManageStrategies.
/// <para>
/// The workspace admits the Security Master and direct-lending desks on the strength of the period
/// they work, and the runs behind that period are not part of that basis. The counts stay - how many
/// runs, how many reconciled, how many audit-ready - because a count is what the screen exists to
/// show every desk it admits; the records and the balances do not.
/// </para>
/// </param>
/// <param name="KernelObservability">
/// Kernel telemetry - domain names, evaluation throughput, latency percentiles, drift, determinism
/// mismatches and alert thresholds. Mirrors <c>GetWorkstationData</c>, which serves the same object
/// and admits only ViewHistoricalData, ViewDiagnostics and ManageStorage.
/// <para>
/// The counts-stay rule above does not reach this one. It holds because an accounting count is what
/// the workspace exists to show every desk it admits; a kernel alert count is platform-operations
/// telemetry that happens to be carried here, and no desk is admitted to this screen on the strength
/// of it. Its headline card is therefore withheld with the projection rather than shown as a zero
/// that would read as an all-clear.
/// </para>
/// </param>
public sealed record AccountingWorkspaceReadScope(
    bool BreakQueue,
    bool ManualJournal,
    bool Reporting,
    bool StrategyRuns,
    bool KernelObservability)
{
    public static readonly AccountingWorkspaceReadScope All =
        new(BreakQueue: true, ManualJournal: true, Reporting: true, StrategyRuns: true, KernelObservability: true);
}
