using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Supplies Security Master-specific SLA policies for reconciliation cases, honoring the
/// per-category windows in <see cref="SecurityMasterExceptionSlaConfig"/>. The configured
/// calendar-day windows are expressed as business-day equivalents so they integrate with the
/// shared <see cref="ReconciliationSlaCalculator"/> business-hour model (weekends excluded),
/// consistent with the rest of the reconciliation SLA system.
/// </summary>
public sealed class SecurityMasterReconciliationSlaPolicyProvider : IReconciliationSlaPolicyProvider
{
    internal const string SecurityMasterTeam = "Security Master";
    internal const string ConflictPolicyId = "security-master-conflict-sla";
    private const int BusinessHoursPerDay = 8;

    private readonly SecurityMasterExceptionSlaConfig _config;

    public SecurityMasterReconciliationSlaPolicyProvider(SecurityMasterExceptionSlaConfig? config = null)
        => _config = config ?? new SecurityMasterExceptionSlaConfig();

    public ReconciliationSlaPolicy? ResolvePolicy(ReconciliationBreakQueueItem item)
    {
        if (item is null
            || !string.Equals(item.Team, SecurityMasterTeam, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var dueHours = Math.Max(1, ResolveDays(item) * BusinessHoursPerDay);

        return new ReconciliationSlaPolicy(
            PolicyId: ConflictPolicyId,
            FundId: item.FundAccountId,
            AccountId: item.ExternalAccountId,
            BreakType: item.Category.ToString(),
            Severity: item.Severity,
            Priority: item.Priority,
            TimeZoneId: "UTC",
            BusinessDayStart: new TimeOnly(9, 0),
            BusinessDayEnd: new TimeOnly(17, 0),
            DueBusinessHours: dueHours,
            WarningBusinessHours: Math.Max(1, dueHours / 2),
            StopOnResolved: true,
            StopOnSignedOff: false,
            PauseAwaitingEvidence: true);
    }

    private int ResolveDays(ReconciliationBreakQueueItem item) => item.RunId switch
    {
        "security-master-conflicts" => _config.IdentifierConflictDays,
        "security-master-incomplete" => _config.IncompleteRecordDays,
        "security-master-new" => _config.NewSecurityUnresolvedDays,
        _ => _config.IdentifierConflictDays
    };
}
