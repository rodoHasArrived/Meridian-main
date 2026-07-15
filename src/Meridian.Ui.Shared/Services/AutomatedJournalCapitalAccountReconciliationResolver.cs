using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Exact server-owned scope used to resolve the capital-account tie-out that supports one
/// fee-accrual execution. The evaluation timestamp is the actual execution/review time, not
/// the schedule's original due timestamp.
/// </summary>
public sealed record AutomatedJournalCapitalAccountReconciliationScope(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string EntityId,
    string PeriodId,
    string Currency,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Server-side source for reviewed capital-account balances, confidence, source version, and
/// retained evidence. Implementations must resolve from authoritative state; request payloads
/// and persisted schedule assertions are never an implementation fallback.
/// </summary>
public interface IAutomatedJournalCapitalAccountReconciliationResolver
{
    Task<AutomatedJournalCapitalAccountReconciliationDto?> ResolveAsync(
        AutomatedJournalCapitalAccountReconciliationScope scope,
        CancellationToken ct = default);
}
