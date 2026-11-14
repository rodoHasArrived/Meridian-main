using Meridian.Ledger;
using Meridian.Backtesting.Sdk;

namespace Meridian.Strategies.Interfaces;

/// <summary>Retains complete run journals outside the bounded lifecycle snapshot.</summary>
public interface IStrategyRunLedgerArchive
{
    Task<StrategyRunLedgerReference> SaveAsync(string runId, IReadOnlyLedger ledger, CancellationToken ct = default);
    Task<StrategyRunLedgerReference> SaveAsync(string runId, IReadOnlyLedger ledger, PortfolioSnapshot? finalSnapshot, CancellationToken ct = default);
    Task<IReadOnlyLedger> LoadAsync(StrategyRunLedgerReference reference, CancellationToken ct = default);
    Task<StrategyRunLedgerSnapshot> LoadSnapshotAsync(StrategyRunLedgerReference reference, CancellationToken ct = default);
}

/// <summary>Retained journal and final lot evidence from the producing simulation.</summary>
public sealed record StrategyRunLedgerSnapshot(IReadOnlyLedger Ledger, PortfolioSnapshot? FinalSnapshot);

/// <summary>Exact immutable archive identity, bound into the retained run's output metadata.</summary>
public sealed record StrategyRunLedgerReference(string RunId, int JournalEntryCount, string Sha256)
{
    public const string HashMetadataKey = "ledgerArchiveSha256";
    public const string CountMetadataKey = "ledgerArchiveJournalCount";
}
