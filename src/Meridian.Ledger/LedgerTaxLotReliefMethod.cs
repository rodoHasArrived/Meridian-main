namespace Meridian.Ledger;

/// <summary>
/// Cost-basis relief method selected for a ledger account.
/// </summary>
public enum LedgerTaxLotReliefMethod
{
    /// <summary>First in, first out; oldest lots are relieved first.</summary>
    Fifo,

    /// <summary>Last in, first out; newest lots are relieved first.</summary>
    Lifo,

    /// <summary>Highest in, first out; highest cost-basis lots are relieved first.</summary>
    Hifo,

    /// <summary>Specific identification; selected lot IDs determine relief order.</summary>
    SpecificId,

    /// <summary>
    /// Average cost; every open lot is pooled into a single average unit cost and each sold share
    /// is relieved at that pooled cost. Lots are still depleted in acquisition order so lot-closing
    /// and holding-period tracking stay deterministic, but the cost basis is the pooled average
    /// rather than any individual lot's recorded unit cost.
    /// </summary>
    AverageCost,
}

