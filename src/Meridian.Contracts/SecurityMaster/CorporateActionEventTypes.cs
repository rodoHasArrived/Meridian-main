namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical Security Master corporate-action event vocabulary.
/// </summary>
public static class CorporateActionEventTypes
{
    public const string Dividend = "Dividend";
    public const string StockSplit = "StockSplit";
    public const string ReverseStockSplit = "ReverseStockSplit";
    public const string SpinOff = "SpinOff";
    public const string MergerAbsorption = "MergerAbsorption";
    public const string RightsIssue = "RightsIssue";
    public const string PrincipalPaydown = "PrincipalPaydown";
    public const string BondCall = "BondCall";
    public const string FuturesExpiry = "FuturesExpiry";
    public const string OptionContractAdjustment = "OptionContractAdjustment";
    public const string CryptoFork = "CryptoFork";

    public static string Normalize(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return string.Empty;

        return CompactToken(eventType) switch
        {
            "DIVIDEND" or "CASHDIVIDEND" or "DIVIDENDPAYMENT" => Dividend,
            "SPLIT" or "STOCKSPLIT" => StockSplit,
            "REVERSESPLIT" or "REVERSESTOCKSPLIT" => ReverseStockSplit,
            "SPINOFF" => SpinOff,
            "MERGER" or "MERGERABSORPTION" => MergerAbsorption,
            "RIGHTS" or "RIGHTSISSUE" => RightsIssue,
            "PRINCIPALPAYDOWN" or "FACTORPAYDOWN" or "FACTORREDUCTION" => PrincipalPaydown,
            "BONDCALL" or "CALL" => BondCall,
            "FUTURESEXPIRY" or "FUTUREEXPIRY" or "FUTURESEXPIRATION" or "FUTUREEXPIRATION" => FuturesExpiry,
            "OPTIONCONTRACTADJUSTMENT" or "OPTIONADJUSTMENT" => OptionContractAdjustment,
            "CRYPTOFORK" or "FORK" => CryptoFork,
            _ => eventType.Trim()
        };
    }

    public static bool IsKnown(string eventType)
        => Normalize(eventType) is Dividend
            or StockSplit
            or ReverseStockSplit
            or SpinOff
            or MergerAbsorption
            or RightsIssue
            or PrincipalPaydown
            or BondCall
            or FuturesExpiry
            or OptionContractAdjustment
            or CryptoFork;

    private static string CompactToken(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
