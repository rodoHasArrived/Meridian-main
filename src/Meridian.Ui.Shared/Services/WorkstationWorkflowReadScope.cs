using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Which workspace families a caller may read in the shared workflow summary.
///
/// <para>The summary is shell furniture that composes one card per canonical workspace, so its route
/// admits the union of the per-workspace read permissions — otherwise a reporting or strategy reader
/// loses the whole strip. Admission is therefore not authorization: without this projection a caller
/// admitted by any one family receives every family's card, and the strategy and accounting cards
/// carry real record content (candidate strategy names, promotion state and reasons, open break
/// counts, ledger continuity posture).</para>
///
/// <para>Cards backed by no record content — Portfolio, Reporting and Settings, which vary only on
/// whether an operating context is selected — are not gated here; there is nothing in them to
/// withhold.</para>
///
/// <para>There is deliberately no default: every caller of
/// <see cref="WorkstationWorkflowSummaryService.GetAsync"/> states a scope, because the desktop lane
/// reaches that service in process with no endpoint filter in between, and an optional argument
/// there is a grant to whichever caller forgets it. Browser requests resolve theirs through
/// <see cref="ForRequest"/>; the desktop resolves its operator's through its own authenticated
/// session.</para>
/// </summary>
public sealed record WorkstationWorkflowReadScope(
    bool Trading,
    bool Accounting,
    bool Strategy,
    bool Data)
{
    /// <summary>
    /// Every family readable. For a caller whose authority is already established to cover all of
    /// them, and for tests; never a stand-in for a scope that was not resolved.
    /// </summary>
    public static readonly WorkstationWorkflowReadScope All = new(
        Trading: true,
        Accounting: true,
        Strategy: true,
        Data: true);

    /// <summary>
    /// True when any family whose card is built from strategy runs is readable. Trading and
    /// Accounting both read the governed (paper and live) runs and Strategy reads the backtest queue,
    /// so a caller holding none of the three needs no run load at all.
    /// </summary>
    public bool NeedsStrategyRuns => Trading || Accounting || Strategy;

    /// <summary>
    /// The families the current request may read. Each is the permission set already declared by the
    /// route that serves that family's data directly, so the summary card and the surface it
    /// summarises admit the same callers and neither becomes the other's second door:
    /// <list type="bullet">
    /// <item>Trading — <see cref="UserPermission.ViewTrades"/>, as the trading read surface.</item>
    /// <item>Accounting — the reconciliation break queue's set, which the accounting card's open-break
    /// and continuity posture is drawn from.</item>
    /// <item>Strategy — the run-ledger routes' <see cref="UserPermission.ViewStrategies"/> and
    /// <see cref="UserPermission.ManageStrategies"/>.</item>
    /// <item>Data — the Data workspace's own set. The card summarises that workspace and its
    /// next action opens it, so the two admit the same callers; the workspace payload reads the
    /// same provider metrics the card counts, so nothing reaches the card that the workspace
    /// withholds.</item>
    /// </list>
    /// <see cref="UserPermission.AdminMaintenance"/> is not a universal override: it appears only in
    /// the accounting set, because the reconciliation break queue accepts it and the trading, strategy
    /// and data workspaces do not. A profile carrying it alone administers maintenance routines; it is
    /// not thereby entitled to strategy candidates or trading posture.
    /// </summary>
    public static WorkstationWorkflowReadScope ForRequest(HttpContext context) => new(
        Trading: EndpointAuthorization.HasPermission(context, UserPermission.ViewTrades),
        Accounting: EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewTrades,
            UserPermission.ViewDirectLending,
            UserPermission.ManageDirectLending,
            UserPermission.ViewSecurityMaster,
            UserPermission.ModifySecurityMaster,
            UserPermission.AdminMaintenance),
        Strategy: EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewStrategies,
            UserPermission.ManageStrategies),
        Data: EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewHistoricalData,
            UserPermission.ViewDiagnostics,
            UserPermission.ManageStorage));
}
