namespace Meridian.Core.Subscriptions.Models;

/// <summary>Explicit, retained ownership and date for an opening-balance draft.</summary>
public sealed record PortfolioImportAccountingScope(
    string TenantId, string CompanyId, string FundProfileId,
    Guid FinancialAccountId, Guid LedgerBookId, Guid PeriodId, DateOnly EffectiveDate,
    string Actor, Guid? EntityId = null);

public sealed record PortfolioImportRunRequest(
    Guid RunId, string Broker, PortfolioImportOptions Options,
    PortfolioImportAccountingScope Scope,
    bool PrepareOpeningBalances = false);

public enum PortfolioImportRunStatus { Running, Succeeded, Partial, Failed }

/// <summary>A completed import means the requested draft is queued, never approved or posted.</summary>
public sealed record PortfolioImportRun(
    PortfolioImportRunRequest Request,
    string CommandHash,
    PortfolioImportRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PortfolioSummary? Snapshot = null,
    string? SnapshotHash = null,
    PortfolioImportResult? Subscriptions = null,
    string? WatchlistId = null,
    Guid? OpeningJournalEntryId = null,
    string? Error = null,
    IReadOnlyList<string>? PlannedSymbols = null);
