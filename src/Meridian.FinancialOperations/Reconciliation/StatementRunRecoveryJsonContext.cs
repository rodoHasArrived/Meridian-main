using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// ADR-014 source-generated metadata for statement-run checkpoints and their immutable hash payloads.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StatementRunRecoveryCheckpoint))]
[JsonSerializable(typeof(BrokerStatementImportResult))]
[JsonSerializable(typeof(StatementRunMatchArtifact))]
[JsonSerializable(typeof(StatementRunProjectionAudit))]
[JsonSerializable(typeof(ReconciliationBreakRecord))]
[JsonSerializable(typeof(ReconciliationCase))]
[JsonSerializable(typeof(StatementRunBreakArtifactPayload))]
[JsonSerializable(typeof(StatementRunCaseArtifactPayload))]
[JsonSerializable(typeof(StatementRunRequestFingerprint))]
[JsonSerializable(typeof(StatementRunInputFingerprint))]
internal sealed partial class StatementRunRecoveryJsonContext : JsonSerializerContext;
