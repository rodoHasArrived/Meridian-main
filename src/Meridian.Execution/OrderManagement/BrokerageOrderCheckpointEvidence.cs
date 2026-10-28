using Meridian.Execution.Sdk;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Read-only evidence sampled at the recovery checkpoint's lock boundary. The pending flag
/// remains separate from its snapshot because another owner may resolve the amendment between
/// those reads; the checkpoint preserves the uncertainty observed by the initial flag.
/// </summary>
internal sealed record BrokerageOrderCheckpointEvidence(
    string? BrokerOrderId,
    string[] BrokerOrderAliases,
    bool HasPendingAmendment,
    PendingOrderAmendment? PendingAmendment);
