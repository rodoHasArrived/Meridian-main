using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>Explicit daily scope configuration; no account or authority is discovered implicitly.</summary>
public sealed record ThreeWayReconciliationSchedule(
    string TenantId,
    string CompanyId,
    Guid FinancialAccountId,
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    bool Enabled = false);

public sealed class ThreeWayReconciliationOptions
{
    public const string SectionKey = "Reconciliation:ThreeWay";
    public const string SupportedBasisConvention = "GrossTradeValueFeesExpensed";
    public string RootDirectory { get; set; } = string.Empty;
    public TimeSpan MaximumSourceAge { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan MaximumSourceDrift { get; set; } = TimeSpan.FromMinutes(5);
    // Provider average-entry cost is not assumed to use Meridian's fee treatment.
    public string BrokerCostBasisConvention { get; set; } = "Unknown";
    public string CashAccountPath { get; set; } = string.Empty;
    public string FeeExpenseAccountPath { get; set; } = string.Empty;
    public TimeOnly DailyRunAfterUtc { get; set; } = new(0, 15);

    /// <summary>
    /// When set, each scheduled account's broker projection is re-synced at most this often before
    /// sources are captured, so the daily comparison reads fresh broker evidence. Null keeps the
    /// broker projection operator-refreshed only.
    /// </summary>
    public TimeSpan? BrokerRefreshInterval { get; set; }

    /// <summary>Per-share price difference tolerated between matched broker and ledger fills.</summary>
    public decimal FillPriceTolerance { get; set; } = 0.0001m;

    /// <summary>Commission difference tolerated between matched broker and ledger fills.</summary>
    public decimal FillFeeTolerance { get; set; } = 0.01m;
    public IReadOnlyList<ThreeWayReconciliationSchedule> Schedules { get; set; } = [];
}

public interface IThreeWayReconciliationSourceReader
{
    Task<ThreeWayReconciliationScopeDto> AuthorizeAsync(Guid accountId, ReconciliationBreakQueueScope accessScope,
        Guid ledgerBookId, Guid periodId, CancellationToken ct = default);
    Task CaptureAsync(ThreeWayReconciliationScopeDto scope, CancellationToken ct = default);
    Task<IReadOnlyList<ThreeWaySourceSnapshotDto>> ReadAsync(ThreeWayReconciliationScopeDto scope,
        DateTimeOffset cutoffUtc, CancellationToken ct = default);
}
