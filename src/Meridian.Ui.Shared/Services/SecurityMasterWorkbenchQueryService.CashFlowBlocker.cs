using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed partial class SecurityMasterWorkbenchQueryService
{
    private async Task<SecurityMasterScheduleBlockerDto?> ResolveCashFlowBlockerAsync(
        SecurityDetailDto detail, DateTimeOffset asOf, CancellationToken ct)
    {
        if (_cashFlowService is null)
            return null;
        try
        {
            var source = await _cashFlowService.GetCashFlowSourceAsync(detail.SecurityId, ct).ConfigureAwait(false);
            if (source?.SourceKind is not (StructuredCashFlowSourceKind.CalculatedBullet or StructuredCashFlowSourceKind.CalculatedSinker))
                return null;
            // Reuse the selected terms and evaluation time. Rereading today's row could attach a
            // different version's capability decision to this passport; legacy receipt replay is
            // deliberately not consulted by this current-terms read model.
            var projection = await _cashFlowService.GetProjectionAsync(detail, StructuredCashFlowScenario.Base,
                new BondCalculationContext(asOf, SecurityVersion: detail.Version), ct).ConfigureAwait(false);
            if (projection?.Blocker is { } blocker)
                return new(blocker.Code, blocker.Reason);
            return projection?.BlockedReason is { } reason ? new("CashFlowProjectionBlocked", reason) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Cash-flow capability resolution failed for security {SecurityId} version {SecurityVersion}", detail.SecurityId, detail.Version);
            return new("CashFlowCalculationUnavailable", "The cash-flow calculation could not be evaluated on this node; the service log has the cause.");
        }
    }
}
