using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>An exact account balance and its retained journal-line population in a generated report.</summary>
public sealed record ReportLedgerAmountBindingDto(
    string AmountId,
    string Label,
    decimal Amount,
    string Currency,
    LedgerAmountScopeDto Scope,
    IReadOnlyList<Guid> JournalEntryIds,
    IReadOnlyList<Guid> LedgerEntryIds)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubjectId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceSnapshotHash { get; init; }

    /// <summary>
    /// Server-owned answer to whether the current caller may open this amount's proof. Set only on
    /// API responses; retained bindings leave it null, and clients treat an absent value as denied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProofAvailable { get; init; }
}
