namespace Meridian.Storage.SecurityMaster;

/// <summary>Aggregate usage of current CustomAsset reference records in this store/database.</summary>
public interface ISecurityAssetProfileUsageStore
{
    Task<IReadOnlyList<SecurityAssetProfileUsageCount>> GetProfileUsageCountsAsync(CancellationToken ct = default);
    /// <summary>Counts usage and approved-required-field presence in the same source statement.</summary>
    Task<IReadOnlyList<SecurityAssetProfileUsageCount>> GetProfileUsageSnapshotAsync(
        IReadOnlyList<SecurityAssetProfileUsageRequirements> requirements, CancellationToken ct = default)
        => GetProfileUsageCountsAsync(ct);
}

public sealed record SecurityAssetProfileUsageRequirements(string ProfileId, int ProfileVersion, IReadOnlyList<string> RequiredFieldKeys);

public sealed record SecurityAssetProfileUsageCount(
    string? ProfileId, int? ProfileVersion, string? EnvelopeIssue,
    long ActiveRecordCount, long InactiveRecordCount, long UnknownStatusRecordCount)
{
    public long? CompleteActiveRecordCount { get; init; }
    public long? IncompleteActiveRecordCount { get; init; }
    public long? UnknownActiveRecordCount { get; init; }
    public IReadOnlyDictionary<string, long>? MissingRequiredFieldCounts { get; init; }
}
