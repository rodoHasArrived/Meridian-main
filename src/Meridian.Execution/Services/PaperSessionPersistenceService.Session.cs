using Meridian.Execution.Logging;
using System.Collections.Concurrent;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Models;
using Meridian.Execution.Events;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

public sealed partial class PaperSessionPersistenceService
{
    private sealed class PaperSession
    {
        public object SyncRoot { get; } = new();
        public required string SessionId { get; init; }
        public required string StrategyId { get; init; }
        public string? StrategyName { get; init; }
        public decimal InitialCash { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ClosedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public List<string> Symbols { get; init; } = [];
        public string? MatchingModelVersion { get; init; }
        public string? CostModelVersion { get; init; }
        public TradeFillLedgerPostingContext? AccountingContext { get; init; }
        public string? FinancialAccountId { get; init; }
        public string? JournalCurrency { get; init; }
        public bool IsAccountDefault { get; init; }
        public HashSet<string> NativeOrderReservations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyLedger? DurableLedger { get; set; }
        public Dictionary<Guid, PaperSessionJournalReceipt> DurableReceipts { get; set; } = [];
        public List<PaperSessionFillRecord> DurableFills { get; } = [];
        public Guid? PendingDurableEffect { get; set; }
        public string? AccountingUnavailableReason { get; init; }
        public PaperTradingPortfolio? Portfolio { get; set; }
        public List<OrderState> OrderHistory { get; } = [];
        public List<ExecutionReport> FillHistory { get; } = [];
        public List<PaperSessionSettlementRecord> SettlementHistory { get; } = [];
        public List<PaperFinancingCharge> FinancingHistory { get; } = [];
        public Guid? PendingFinancingSourceEventId { get; set; }
        public Dictionary<Guid, string> AppliedFillHashes { get; } = [];
        public Dictionary<Guid, (int Version, string? FinancialAccountId)> FillAccountingProvenance { get; } = [];

        /// <summary>
        /// Ledger reconstructed from persisted JSONL entries on load (closed sessions only).
        /// For active sessions use <c>Portfolio.Ledger</c> instead.
        /// </summary>
        public IReadOnlyLedger? ReconstructedLedger { get; init; }
        public LedgerReconstructionResult Reconstruction { get; init; } = LedgerReconstructionResult.Empty;
    }
}
