using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    /// <summary>Compatibility entrypoint for the shared canonical fill identity.</summary>
    internal static Guid CreateDeterministicFillId(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        string? financialAccountId)
    {
        _ = cumulativeFilledQuantity;
        _ = financialAccountId;
        return PaperSessionFillRecord.ComputeCanonicalFillId(fillIncrement);
    }
}
