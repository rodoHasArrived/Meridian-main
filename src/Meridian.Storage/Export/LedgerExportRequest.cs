using Meridian.Ledger;

namespace Meridian.Storage.Export;

/// <summary>Source-backed accounting export. Amounts are encoded as invariant decimal text to
/// preserve all booked digits in spreadsheet and columnar consumers.</summary>
public sealed record LedgerExportRequest(
    IReadOnlyLedger Ledger,
    DateTimeOffset PeriodStart,
    DateTimeOffset AsOf,
    string OutputDirectory,
    ExportFormat Format = ExportFormat.Csv,
    string? FinancialAccountId = null,
    IReadOnlyList<LedgerOpenLotExportRow>? OpenLots = null,
    IReadOnlyList<LedgerRealizedLotExportRow>? RealizedLots = null);

/// <summary>Exact remaining basis and value of a lot at the requested as-of instant.</summary>
public sealed record LedgerOpenLotExportRow(string LotId, string Symbol, string FinancialAccountId,
    DateOnly AcquiredDate, decimal Quantity, decimal CostBasis, decimal MarketValue, DateTimeOffset AsOf,
    bool IsShort = false);

/// <summary>One disposed parcel; adjustments are explicit, rather than inferred tax advice.</summary>
public sealed record LedgerRealizedLotExportRow(string LotId, string Symbol, string FinancialAccountId,
    DateOnly AcquiredDate, DateOnly SoldDate, decimal Quantity, decimal Proceeds, decimal CostBasis,
    string TaxCharacter, decimal Adjustment = 0m, string? AdjustmentCode = null, DateTimeOffset? SoldAt = null,
    bool IsShort = false, string? DisposalId = null);
