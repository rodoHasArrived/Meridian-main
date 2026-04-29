using Meridian.Application.Logging;
using Meridian.FSharp.Ledger;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Manages the governance exception (break) queue: ingests new breaks from
/// reconciliation runs, enriches them with severity/classification metadata,
/// supports status transitions, and provides dashboard-ready projections.
/// </summary>
public sealed class GovernanceExceptionService
{
    private readonly ILogger _log = LoggingSetup.ForContext<GovernanceExceptionService>();

    // In-memory store keyed by BreakId.  In production this would be
    // backed by a persistent store injected via a repository interface.
    private readonly Dictionary<Guid, GovernanceException> _exceptions = [];

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Ingests reconciliation results from a completed engine run and adds new
    /// <see cref="GovernanceException"/> entries for each open break.
    /// </summary>
    public IReadOnlyList<GovernanceException> IngestBreaks(
        Guid runId,
        string portfolioId,
        PortfolioLedgerCheckResultDto[] breaks,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(breaks);

        var created = new List<GovernanceException>(breaks.Length);

        foreach (var breakResult in breaks)
        {
            if (breakResult.IsMatch)
                continue;

            var severity = ClassifySeverity(breakResult);
            var exception = new GovernanceException(
                ExceptionId: Guid.NewGuid(),
                RunId: runId,
                PortfolioId: portfolioId,
                CheckId: breakResult.CheckId,
                Label: breakResult.Label,
                Category: breakResult.Category,
                Severity: severity,
                Status: GovernanceExceptionStatus.Open,
                ExpectedAmount: breakResult.HasExpectedAmount ? breakResult.ExpectedAmount : null,
                ActualAmount: breakResult.HasActualAmount ? breakResult.ActualAmount : null,
                Variance: breakResult.Variance,
                Reason: breakResult.Reason,
                AsOf: asOf,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                ResolvedAt: null,
                Notes: null);

            _exceptions[exception.ExceptionId] = exception;
            created.Add(exception);

            _log.Debug(
                "Governance exception {ExceptionId} created for break {CheckId} [{Severity}]",
                exception.ExceptionId, breakResult.CheckId, severity);
        }

        _log.Information(
            "Ingested {Count} governance exceptions from run {RunId} for portfolio {PortfolioId}",
            created.Count, runId, portfolioId);

        return created;
    }

    /// <summary>Returns all open exceptions, optionally filtered by portfolio or severity.</summary>
    public IReadOnlyList<GovernanceException> GetOpenExceptions(
        string? portfolioId = null,
        GovernanceExceptionSeverity? minSeverity = null)
        => _exceptions.Values
            .Where(e => e.Status == GovernanceExceptionStatus.Open)
            .Where(e => portfolioId is null ||
                        string.Equals(e.PortfolioId, portfolioId, StringComparison.OrdinalIgnoreCase))
            .Where(e => minSeverity is null || e.Severity >= minSeverity.Value)
            .OrderByDescending(e => e.Severity)
            .ThenBy(e => e.CreatedAt)
            .ToList();

    /// <summary>Returns a summary of exception counts by severity for a governance dashboard.</summary>
    public GovernanceExceptionDashboard GetDashboard(string? portfolioId = null)
    {
        var filtered = _exceptions.Values
            .Where(e => portfolioId is null ||
                        string.Equals(e.PortfolioId, portfolioId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new GovernanceExceptionDashboard(
            TotalOpen: filtered.Count(e => e.Status == GovernanceExceptionStatus.Open),
            TotalResolved: filtered.Count(e => e.Status == GovernanceExceptionStatus.Resolved),
            Critical: filtered.Count(e => e.Status == GovernanceExceptionStatus.Open && e.Severity == GovernanceExceptionSeverity.Critical),
            High: filtered.Count(e => e.Status == GovernanceExceptionStatus.Open && e.Severity == GovernanceExceptionSeverity.High),
            Medium: filtered.Count(e => e.Status == GovernanceExceptionStatus.Open && e.Severity == GovernanceExceptionSeverity.Medium),
            Low: filtered.Count(e => e.Status == GovernanceExceptionStatus.Open && e.Severity == GovernanceExceptionSeverity.Low),
            AsOf: DateTimeOffset.UtcNow);
    }

    /// <summary>Transitions an exception to <see cref="GovernanceExceptionStatus.Investigating"/>.</summary>
    public bool MarkInvestigating(Guid exceptionId, string? notes = null)
        => Transition(exceptionId, GovernanceExceptionStatus.Investigating, notes);

    /// <summary>Marks an exception as resolved with an optional closing note.</summary>
    public bool Resolve(Guid exceptionId, string? notes = null)
    {
        if (!_exceptions.TryGetValue(exceptionId, out var ex))
            return false;

        _exceptions[exceptionId] = ex with
        {
            Status = GovernanceExceptionStatus.Resolved,
            ResolvedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Notes = notes ?? ex.Notes
        };

        _log.Information("Governance exception {ExceptionId} resolved", exceptionId);
        return true;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private bool Transition(Guid id, GovernanceExceptionStatus status, string? notes)
    {
        if (!_exceptions.TryGetValue(id, out var ex))
            return false;

        _exceptions[id] = ex with
        {
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow,
            Notes = notes ?? ex.Notes
        };
        return true;
    }

    private static GovernanceExceptionSeverity ClassifySeverity(PortfolioLedgerCheckResultDto result)
    {
        if (TryMapSeverity(result.Severity, out var severity))
        {
            return severity;
        }

        var abs = Math.Abs(result.Variance);

        return result.Category switch
        {
            "currency_mismatch" or "missing_ledger_coverage" when abs > 100_000m => GovernanceExceptionSeverity.Critical,
            "missing_ledger_coverage" or "missing_portfolio_coverage" => GovernanceExceptionSeverity.High,
            "amount_mismatch" when abs > 50_000m => GovernanceExceptionSeverity.Critical,
            "amount_mismatch" when abs > 10_000m => GovernanceExceptionSeverity.High,
            "amount_mismatch" => GovernanceExceptionSeverity.Medium,
            "classification_gap" => GovernanceExceptionSeverity.High,
            "timing_mismatch" => GovernanceExceptionSeverity.Low,
            _ => GovernanceExceptionSeverity.Medium
        };
    }

    private static bool TryMapSeverity(string severity, out GovernanceExceptionSeverity mapped)
    {
        mapped = severity switch
        {
            "Critical" => GovernanceExceptionSeverity.Critical,
            "High" => GovernanceExceptionSeverity.High,
            "Medium" => GovernanceExceptionSeverity.Medium,
            "Low" => GovernanceExceptionSeverity.Low,
            "Info" => GovernanceExceptionSeverity.Info,
            _ => GovernanceExceptionSeverity.Info
        };

        return !string.IsNullOrWhiteSpace(severity);
    }
}

// ── Domain types ───────────────────────────────────────────────────────────────

/// <summary>Severity ordering for governance exceptions (higher value = more severe).</summary>
public enum GovernanceExceptionSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>Workflow status for a governance exception.</summary>
public enum GovernanceExceptionStatus
{
    Open = 0,
    Investigating = 1,
    Resolved = 2
}

/// <summary>An individual governance exception derived from a reconciliation break.</summary>
public sealed record GovernanceException(
    Guid ExceptionId,
    Guid RunId,
    string PortfolioId,
    string CheckId,
    string Label,
    string Category,
    GovernanceExceptionSeverity Severity,
    GovernanceExceptionStatus Status,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal Variance,
    string Reason,
    DateTimeOffset AsOf,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    string? Notes);

/// <summary>Dashboard-ready summary of open governance exceptions.</summary>
public sealed record GovernanceExceptionDashboard(
    int TotalOpen,
    int TotalResolved,
    int Critical,
    int High,
    int Medium,
    int Low,
    DateTimeOffset AsOf);
