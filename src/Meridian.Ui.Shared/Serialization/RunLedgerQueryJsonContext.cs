using System.Text.Json.Serialization;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(JournalEntry[]))]
[JsonSerializable(typeof(LedgerAccountSummary[]))]
[JsonSerializable(typeof(LedgerBalancePoint[]))]
[JsonSerializable(typeof(RunLedgerExportRequest))]
internal partial class RunLedgerQueryJsonContext : JsonSerializerContext;

public sealed record RunLedgerExportRequest(
    string Format = "csv",
    IReadOnlyDictionary<string, string>? AccountMapping = null);
