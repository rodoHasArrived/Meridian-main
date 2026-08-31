using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackDeliveryService
{
    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
        => OperationsOriginGuard.RequireHumanOperator(actionOrigin, action);
}
