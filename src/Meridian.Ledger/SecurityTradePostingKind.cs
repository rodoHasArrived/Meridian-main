namespace Meridian.Ledger;

/// <summary>The economic leg of an execution, independent of its buy/sell order side.</summary>
public enum SecurityTradePostingKind
{
    OpenLong,
    CloseLong,
    OpenShort,
    CoverShort
}
