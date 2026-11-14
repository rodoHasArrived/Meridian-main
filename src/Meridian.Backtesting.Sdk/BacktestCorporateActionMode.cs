using Meridian.Contracts.SecurityMaster;

namespace Meridian.Backtesting.Sdk;

/// <summary>Chooses mutually exclusive price-adjustment and accounting-event semantics.</summary>
public enum BacktestCorporateActionMode
{
    Legacy = 0,
    RawPricesWithCanonicalEvents = 1
}

/// <summary>A frozen effective action and the security definition version used to resolve it.</summary>
public sealed record BacktestCanonicalAction(string Symbol, long SecurityVersion, CorporateActionDto Action);

/// <summary>
/// Effective-through action evidence captured before strategy initialization. This is not a
/// transaction-time record of which announcements were historically known.
/// </summary>
public sealed record BacktestCanonicalActionSnapshot(
    DateTimeOffset EffectiveThroughUtc,
    string ContentHash,
    IReadOnlyList<BacktestCanonicalAction> Actions);
