using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

/// <summary>
/// ADR-014 source-generated metadata for retained statement-run and statement-casework authority.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StatementRunMatchArtifact))]
[JsonSerializable(typeof(StatementRunProjectionAudit))]
[JsonSerializable(typeof(StatementCaseworkCommitEnvelope))]
[JsonSerializable(typeof(StatementCaseworkCompletion))]
[JsonSerializable(typeof(StatementBreakCaseworkUpdate))]
[JsonSerializable(typeof(StatementBreakCaseworkAuditEvent))]
[JsonSerializable(typeof(ReconciliationBreakRecord))]
[JsonSerializable(typeof(ReconciliationCase))]
[JsonSerializable(typeof(ReconciliationCaseAuditEvent))]
internal sealed partial class StatementDurabilityJsonContext : JsonSerializerContext;

/// <summary>
/// Preserves the exact Pascal-case, indented fingerprint representation used by legacy statement
/// casework receipts while still supplying ADR-014 generated metadata.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(StatementBreakCaseworkUpdate))]
[JsonSerializable(typeof(StatementCaseworkLegacyReceipt))]
internal sealed partial class StatementLegacyCaseworkJsonContext : JsonSerializerContext;
