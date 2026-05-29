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
}

