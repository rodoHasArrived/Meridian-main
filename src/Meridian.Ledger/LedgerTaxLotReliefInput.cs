namespace Meridian.Ledger;

/// <summary>
/// Sale input used to relieve tax lots and project realized gain/loss accounting lines.
/// </summary>
public sealed record LedgerTaxLotReliefInput
{
    public LedgerTaxLotReliefInput(
        LedgerAccount account,
        DateOnly saleDate,
        decimal quantitySold,
        decimal salePrice,
        LedgerTaxLotReliefMethod reliefMethod,
        IReadOnlyList<LedgerTaxLot> openLots,
        string? financialAccountId = null,
        IReadOnlyList<string>? specificLotIds = null,
        IReadOnlyList<LedgerTaxLotBasisAdjustment>? basisAdjustments = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(openLots);
        if (quantitySold <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantitySold), quantitySold, "Quantity sold must be positive.");
        if (salePrice < 0m)
            throw new ArgumentOutOfRangeException(nameof(salePrice), salePrice, "Sale price cannot be negative.");
        if (openLots.Count == 0)
            throw new ArgumentException("At least one open tax lot is required.", nameof(openLots));

        Account = account;
        SaleDate = saleDate;
        QuantitySold = quantitySold;
        SalePrice = salePrice;
        ReliefMethod = reliefMethod;
        OpenLots = openLots;
        FinancialAccountId = string.IsNullOrWhiteSpace(financialAccountId)
            ? account.FinancialAccountId
            : financialAccountId.Trim();
        SpecificLotIds = specificLotIds is null
            ? []
            : specificLotIds
                .Select(static lotId => lotId.Trim())
                .Where(static lotId => lotId.Length > 0)
                .ToArray();
        BasisAdjustments = basisAdjustments is null
            ? []
            : basisAdjustments.ToArray();
    }

    public LedgerAccount Account { get; }

    public DateOnly SaleDate { get; }

    public decimal QuantitySold { get; }

    public decimal SalePrice { get; }

    public LedgerTaxLotReliefMethod ReliefMethod { get; }

    public IReadOnlyList<LedgerTaxLot> OpenLots { get; }

    public string? FinancialAccountId { get; }

    public IReadOnlyList<string> SpecificLotIds { get; }

    /// <summary>
    /// Reference-data-derived cost-basis adjustments (day-count amortization, pool factor, and
    /// corporate-action restatements) applied to <see cref="OpenLots"/> before relief. Empty when
    /// the sale relieves lots at their recorded quantity and unit cost.
    /// </summary>
    public IReadOnlyList<LedgerTaxLotBasisAdjustment> BasisAdjustments { get; }
}
