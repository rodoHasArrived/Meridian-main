using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Per-category SLA configuration for Security Master exceptions.
/// </summary>
public sealed record SecurityMasterExceptionSlaConfig
{
    /// <summary>Days to resolve an identifier conflict before SLA breach.</summary>
    public int IdentifierConflictDays { get; init; } = 5;

    /// <summary>Days to resolve an incomplete security record before SLA breach.</summary>
    public int IncompleteRecordDays { get; init; } = 3;

    /// <summary>Days to create a new unresolved security before SLA breach.</summary>
    public int NewSecurityUnresolvedDays { get; init; } = 1;

    /// <summary>Days to resolve a HardBlock data quality violation before SLA breach.</summary>
    public int QualityHardBlockDays { get; init; } = 1;

    /// <summary>Days to resolve an Error data quality violation before SLA breach.</summary>
    public int QualityErrorDays { get; init; } = 3;

    /// <summary>Days to resolve a Warning data quality violation before SLA breach.</summary>
    public int QualityWarningDays { get; init; } = 5;
}

/// <summary>
/// Reads Security Master-tagged cases from the authoritative reconciliation queue.
/// </summary>
/// <remarks>
/// Security Master conflicts, operator overrides, and quality reports are deployment-global source
/// records and do not carry a canonical tenant/company owner. This service therefore never projects
/// those global records into a caller-owned reconciliation case. A scoped producer that genuinely
/// owns a case may retain it in the shared queue; this service only reads those already-owned rows.
/// </remarks>
public sealed class SecurityMasterExceptionCaseworkService
{
    /// <summary>
    /// Canonical run identifier used by genuinely owner-scoped Security Master quality cases.
    /// </summary>
    public const string QualityRunId = "security-master-quality";

    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;

    public SecurityMasterExceptionCaseworkService(
        IReconciliationBreakQueueRepository? breakQueueRepository,
        ILogger<SecurityMasterExceptionCaseworkService> logger)
    {
        _breakQueueRepository = breakQueueRepository;
        ArgumentNullException.ThrowIfNull(logger);
    }

    /// <summary>
    /// Returns exact-scope open Security Master cases whose SLA deadline has elapsed.
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAgingExceptionsAsync(
        ReconciliationBreakQueueScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_breakQueueRepository is null)
        {
            return [];
        }

        var asOfUtc = DateTimeOffset.UtcNow;
        var open = await _breakQueueRepository
            .GetAllAsync(scope, ReconciliationBreakQueueStatus.Open, ct)
            .ConfigureAwait(false);
        return open
            .Where(item => string.Equals(item.Team, "Security Master", StringComparison.OrdinalIgnoreCase)
                && item.SlaDueAt.HasValue
                && item.SlaDueAt.Value < asOfUtc)
            .ToArray();
    }
}
