namespace Meridian.Storage.Reporting;

/// <summary>
/// Raised when retained reporting distribution state cannot be trusted. Callers must fail closed
/// and must not silently replace or skip the affected grant, delivery job, or receipt.
/// </summary>
public sealed class ReportingDistributionStateCorruptionException : IOException
{
    public ReportingDistributionStateCorruptionException(string entityType, string entityId, string detail)
        : base($"Retained reporting {entityType} '{entityId}' is corrupt: {detail}.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public ReportingDistributionStateCorruptionException(
        string entityType,
        string entityId,
        string detail,
        Exception innerException)
        : base($"Retained reporting {entityType} '{entityId}' is corrupt: {detail}.", innerException)
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public string EntityId { get; }
}
