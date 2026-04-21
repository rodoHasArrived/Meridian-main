using Meridian.Contracts.SecurityMaster;

namespace Meridian.Wpf.Services;

/// <summary>
/// Simple desktop-facing runtime status for Security Master-backed workflows.
/// </summary>
public sealed class SecurityMasterRuntimeStatusService : ISecurityMasterRuntimeStatus
{
    public SecurityMasterRuntimeStatusService(bool isAvailable, string availabilityDescription)
    {
        IsAvailable = isAvailable;
        AvailabilityDescription = availabilityDescription ?? string.Empty;
    }

    public bool IsAvailable { get; }

    public string AvailabilityDescription { get; }
}
