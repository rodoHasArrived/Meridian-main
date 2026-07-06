using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Accounting cash-flow helpers for the workstation API surface: workspace and per-run
/// cash-flow summaries and ledger cash balance / variance calculations. Split out of the
/// WorkstationEndpoints core partial.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static WorkstationAccountingCashFlowSummaryPayload BuildAccountingWorkspaceCashFlowSummary(IReadOnlyList<StrategyRunDetail?> details)
    {
        var totalCash = details.Sum(static detail => detail?.Portfolio?.Cash ?? 0m);
        var totalLedgerCash = details.Sum(static detail => GetLedgerCashBalance(detail?.Ledger) ?? 0m);
        var totalFinancing = details.Sum(static detail => detail?.Portfolio?.Financing ?? 0m);
        var runsWithCashSignals = details.Count(static detail => detail?.Portfolio is not null || detail?.Ledger is not null);
        var runsWithCashVariance = details.Count(static detail => Math.Abs(GetCashVariance(detail)) > 0.01m);
        var netVariance = totalLedgerCash - totalCash;

        return new WorkstationAccountingCashFlowSummaryPayload(
            TotalCash: totalCash,
            TotalLedgerCash: totalLedgerCash,
            NetVariance: netVariance,
            TotalFinancing: totalFinancing,
            RunsWithCashSignals: runsWithCashSignals,
            RunsWithCashVariance: runsWithCashVariance,
            Tone: runsWithCashVariance > 0 ? "warning" : runsWithCashSignals > 0 ? "success" : "default",
            Summary: runsWithCashSignals == 0
                ? "Cash-flow coverage is not yet available."
                : runsWithCashVariance > 0
                    ? $"Cash-flow coverage is available for {runsWithCashSignals} runs; {runsWithCashVariance} run needs variance review."
                    : $"Cash-flow coverage is aligned across {runsWithCashSignals} runs.");
    }

    private static WorkstationAccountingRunCashFlowPayload BuildAccountingRunCashFlowSummary(StrategyRunDetail? detail)
    {
        var cashBalance = detail?.Portfolio?.Cash ?? 0m;
        var ledgerCashBalance = GetLedgerCashBalance(detail?.Ledger) ?? 0m;
        var cashVariance = ledgerCashBalance - cashBalance;
        var financing = detail?.Portfolio?.Financing ?? 0m;
        var realizedPnl = detail?.Portfolio?.RealizedPnl ?? 0m;
        var unrealizedPnl = detail?.Portfolio?.UnrealizedPnl ?? 0m;
        var journalEntryCount = detail?.Ledger?.JournalEntryCount ?? 0;
        var hasSignals = detail?.Portfolio is not null || detail?.Ledger is not null;

        return new WorkstationAccountingRunCashFlowPayload(
            CashBalance: cashBalance,
            LedgerCashBalance: ledgerCashBalance,
            CashVariance: cashVariance,
            Financing: financing,
            RealizedPnl: realizedPnl,
            UnrealizedPnl: unrealizedPnl,
            JournalEntryCount: journalEntryCount,
            Tone: !hasSignals ? "default" : Math.Abs(cashVariance) > 0.01m ? "warning" : "success",
            Summary: !hasSignals
                ? "Cash-flow coverage is not yet available."
                : Math.Abs(cashVariance) > 0.01m
                    ? "Cash and ledger balances diverge and should be reviewed."
                    : "Cash and ledger balances are aligned.");
    }

    private static decimal? GetLedgerCashBalance(LedgerSummary? ledger)
        => ledger?.TrialBalance.FirstOrDefault(static line =>
            string.Equals(line.AccountName, "Cash", StringComparison.OrdinalIgnoreCase))?.Balance;

    private static decimal GetCashVariance(StrategyRunDetail? detail)
    {
        var portfolioCash = detail?.Portfolio?.Cash;
        var ledgerCash = GetLedgerCashBalance(detail?.Ledger);
        if (!portfolioCash.HasValue || !ledgerCash.HasValue)
        {
            return 0m;
        }

        return ledgerCash.Value - portfolioCash.Value;
    }
}
