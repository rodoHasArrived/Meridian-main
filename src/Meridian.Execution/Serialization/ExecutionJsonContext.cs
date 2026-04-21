using System.Text.Json.Serialization;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution.Serialization;

/// <summary>
/// Source-generated JSON serialisation context for paper-session persistence types.
/// Conforms to ADR-014: no-reflection serialisation using <c>System.Text.Json</c> source generators.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PersistedSessionRecord))]
[JsonSerializable(typeof(ExecutionReport))]
[JsonSerializable(typeof(OrderState))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(PersistedJournalEntryDto))]
[JsonSerializable(typeof(List<PersistedJournalEntryDto>))]
[JsonSerializable(typeof(PersistedLedgerLineDto))]
[JsonSerializable(typeof(List<PersistedLedgerLineDto>))]
[JsonSerializable(typeof(PersistedLedgerAccountDto))]
// Multi-account position snapshot types (Phase 4)
[JsonSerializable(typeof(ExecutionAccountDetailSnapshot))]
[JsonSerializable(typeof(List<ExecutionAccountDetailSnapshot>))]
[JsonSerializable(typeof(MultiAccountPortfolioSnapshot))]
[JsonSerializable(typeof(ExecutionPosition))]
[JsonSerializable(typeof(List<ExecutionPosition>))]
[JsonSerializable(typeof(PaperSessionSummaryDto))]
[JsonSerializable(typeof(PaperSessionDetailDto))]
[JsonSerializable(typeof(ExecutionPortfolioSnapshotDto))]
[JsonSerializable(typeof(PaperSessionReplayVerificationDto))]
[JsonSerializable(typeof(AccountKind))]
[JsonSerializable(typeof(MarginAccountType))]
[JsonSerializable(typeof(ExecutionAuditEntry))]
[JsonSerializable(typeof(List<ExecutionAuditEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, decimal>))]
[JsonSerializable(typeof(ExecutionCircuitBreakerState))]
[JsonSerializable(typeof(ExecutionManualOverride))]
[JsonSerializable(typeof(List<ExecutionManualOverride>))]
// Operator control and audit trail types
[JsonSerializable(typeof(ExecutionControlSnapshot))]
internal sealed partial class ExecutionJsonContext : JsonSerializerContext
{
}
