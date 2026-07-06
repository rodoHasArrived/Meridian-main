using Meridian.Contracts.SecurityMaster;

namespace Meridian.TestSupport.CorporateActions;

/// <summary>
/// One golden corporate-action scenario: the event-store contents, the market evidence,
/// the ledger posting context, and the invariants the scenario must satisfy. Loaded from
/// <c>tests/fixtures/corporate-actions/golden/*.json</c> by
/// <see cref="GoldenCorporateActionScenarioLoader"/>; the fixture schema and the
/// known-defect registry are documented in <c>KNOWN-DEFECTS.md</c> beside the fixtures.
/// </summary>
public sealed record GoldenCorporateActionScenario(
    string ScenarioId,
    string Description,
    string Ticker,
    Guid SecurityId,
    IReadOnlyList<CorporateActionDto> Actions,
    IReadOnlyList<GoldenBar> Bars,
    GoldenPostingContext? PostingContext,
    IReadOnlyList<string> InvariantTags,
    IReadOnlyList<string> KnownDefectIds)
{
    public bool HasTag(string tag) => InvariantTags.Contains(tag, StringComparer.Ordinal);
}

/// <summary>Raw (unadjusted) OHLCV session bar carried by a golden scenario.</summary>
public sealed record GoldenBar(
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

/// <summary>
/// Ledger posting context for the scenario; maps onto
/// <c>CorporateActionLedgerPostingContext</c>. A null context on the scenario exercises the
/// bridge's default path (position quantity 0 — see CA-DEF-006).
/// </summary>
public sealed record GoldenPostingContext(
    decimal PositionQuantity,
    decimal WithholdingTaxRate,
    string? FinancialAccountId);

/// <summary>Well-known invariant tag names used by the golden fixtures.</summary>
public static class GoldenInvariantTags
{
    public const string SeriesContinuity = "series-continuity";
    public const string FactorMonotonicity = "factor-monotonicity";
    public const string NotionalConservation = "notional-conservation";
    public const string JournalBalanced = "journal-balanced";
    public const string ReceivableSettled = "receivable-settled";
    public const string IdempotentPosting = "idempotent-posting";
    public const string PositionBasisConserved = "position-basis-conserved";
    public const string SupersedeFold = "supersede-fold";
    public const string UnknownEventType = "unknown-event-type";
}
