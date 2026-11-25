namespace Meridian.Contracts.Ledger;

/// <summary>Optional operational accounting provenance, separate from native legal agreement references.</summary>
public static class LedgerAccountingArrangementDimensions
{
    /// <summary>Preserves existing report and accumulator signatures when operational provenance is absent.</summary>
    public static string AppendSignature(string signature, LedgerDimensionSetDto dimensions, string separator = "|")
    {
        foreach (var (key, value) in new[]
        {
            ("accountingArrangementId", dimensions.AccountingArrangementId),
            ("accountingArrangementRevisionId", dimensions.AccountingArrangementRevisionId),
            ("chartAccountNodeId", dimensions.ChartAccountNodeId),
            ("accountBindingRevisionId", dimensions.AccountBindingRevisionId),
            ("intercompanyTransactionId", dimensions.IntercompanyTransactionId)
        })
            if (!string.IsNullOrWhiteSpace(value))
                signature += separator + key + "=" + Uri.EscapeDataString(value.Trim());
        return signature;
    }
}
