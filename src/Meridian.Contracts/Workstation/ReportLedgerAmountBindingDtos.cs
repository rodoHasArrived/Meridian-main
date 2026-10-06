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
}
