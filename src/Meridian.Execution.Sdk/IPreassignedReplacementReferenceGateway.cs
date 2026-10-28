namespace Meridian.Execution.Sdk;

/// <summary>
/// A gateway that implements amendment as cancel-and-replace under a client reference the
/// caller assigns. The caller persists that reference before dispatch, so a crash after the
/// replacement reaches the broker still leaves recovery a reference to look the successor up by.
/// </summary>
public interface IPreassignedReplacementReferenceGateway
{
}
