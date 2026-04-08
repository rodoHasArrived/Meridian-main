namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Exposes whether Security Master-backed query workflows are currently available.
/// This lets workstation surfaces degrade gracefully instead of treating missing
/// infrastructure as an unknown lookup miss.
/// </summary>
public interface ISecurityMasterRuntimeStatus
{
    bool IsAvailable { get; }

    string AvailabilityDescription { get; }
}
