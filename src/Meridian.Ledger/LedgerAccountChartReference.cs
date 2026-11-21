using System.Text.Json.Serialization;

namespace Meridian.Ledger;

/// <summary>Posting-time chart evidence, independent of the ledger account's balance identity.</summary>
public sealed record LedgerAccountChartReference(
    string NodeId,
    string Revision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AccountCode = null);
